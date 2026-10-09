using LabOps.Engines.Python;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace LabOps.Engines.Yaml;

/// <summary>
/// Reads YAML as PyYAML's CSafeLoader does (yaml.load(text, Loader=CSafeLoader)): YamlDotNet
/// parses, and this composes and constructs the values with PyYAML's rules, which are YAML 1.1's
/// (see <see cref="YamlResolver"/>): duplicate keys keep the last value in the first key's place,
/// merge keys (`<<`) work, a duplicate anchor is refused, and keys compare as Python's do.
/// </summary>
/// <remarks>
/// Where Python would crash on a value (2026-02-30, `!!int abc`) this reports a problem with the
/// record instead, and a recursive alias is refused rather than built. Both are deliberate
/// differences from project.py; nothing else should differ.
/// </remarks>
public static class YamlLoader
{
    /// <summary>The document's value: a PyDict, List, scalar, or null for an empty stream.</summary>
    /// <exception cref="YamlProblemException">The text is not YAML PyYAML would load.</exception>
    public static object? Load(string text)
    {
        // libyaml skips a byte-order mark at the start of the stream; YamlDotNet would read it as
        // part of the first key.
        if (text.StartsWith((char)0xFEFF))
        {
            text = text[1..];
        }

        var events = new EventReader(new Parser(new StringReader(text)));
        try
        {
            events.Take<StreamStart>();
            if (events.Peek() is StreamEnd)
            {
                return null;
            }

            events.Take<DocumentStart>();
            var root = new Composer(events).Compose();
            events.Take<DocumentEnd>();
            if (events.Peek() is not StreamEnd)
            {
                throw new YamlProblemException("but found another document",
                    Line(events.Peek().Start));
            }

            return new Constructor().Construct(root);
        }
        catch (YamlException ex)
        {
            throw new YamlProblemException(CleanMessage(ex.Message), Line(ProblemMark(ex)));
        }
    }

    private static int Line(Mark mark) => (int)Math.Max(1, mark.Line);

    /// <summary>
    /// YamlDotNet's message as libyaml words it: without the "(Line: 2, Col: 1, Idx: 5) - (...): "
    /// in front, and without the "While parsing a flow sequence, " context (libyaml's messages,
    /// which YamlDotNet's were ported from, are the part after it).
    /// </summary>
    private static string CleanMessage(string message)
    {
        var i = message.IndexOf("): ", StringComparison.Ordinal);
        var text = message.StartsWith("(Line", StringComparison.Ordinal) && i >= 0 ? message[(i + 3)..] : message;
        if (text.StartsWith("While ", StringComparison.Ordinal) && text.IndexOf(", ", StringComparison.Ordinal) is var comma and > 0)
        {
            text = text[(comma + 2)..];
        }

        text = text.TrimEnd('.');
        // Where YamlDotNet words a problem its own way, libyaml's words.
        return text switch
        {
            "found invalid mapping" or "Mapping values are not allowed in this context" => "mapping values are not allowed in this context",
            "found a tab character that violate indentation" => "found a tab character that violates indentation",
            _ when text.Length > 0 && char.IsUpper(text[0]) && !text.StartsWith("YAML", StringComparison.Ordinal) => char.ToLowerInvariant(text[0]) + text[1..],
            _ => text,
        };
    }

    // libyaml marks where these problems end (the end of the stream, the tab), YamlDotNet where they start.
    private static Mark ProblemMark(YamlException ex) =>
        ex.Message.Contains("end of stream", StringComparison.Ordinal) || ex.Message.Contains("tab character", StringComparison.Ordinal)
            ? ex.End : ex.Start;

    private sealed class EventReader(IParser parser)
    {
        private ParsingEvent? _next;

        public ParsingEvent Peek()
        {
            if (_next is null)
            {
                if (!parser.MoveNext() || parser.Current is null)
                {
                    throw new YamlProblemException("found unexpected end of stream", 1);
                }

                _next = parser.Current;
            }

            return _next;
        }

        public ParsingEvent Take()
        {
            var e = Peek();
            _next = null;
            return e;
        }

        public T Take<T>() where T : ParsingEvent =>
            Take() is T e ? e : throw new YamlProblemException($"expected {typeof(T).Name}", 1);
    }

    private abstract class Node(Mark start, string tag)
    {
        public Mark Start { get; } = start;

        public string Tag { get; set; } = tag;
    }

    private sealed class ScalarNode(Mark start, string tag, string value) : Node(start, tag)
    {
        public string Value { get; } = value;
    }

    private sealed class SequenceNode(Mark start, string tag) : Node(start, tag)
    {
        public List<Node> Items { get; } = [];
    }

    private sealed class MappingNode(Mark start, string tag) : Node(start, tag)
    {
        public List<(Node Key, Node Value)> Pairs { get; set; } = [];
    }

    /// <summary>PyYAML's composer: events to nodes, with anchors and aliases.</summary>
    private sealed class Composer(EventReader events)
    {
        private readonly Dictionary<string, Node> _anchors = new(StringComparer.Ordinal);

        public Node Compose()
        {
            var e = events.Take();
            if (e is AnchorAlias alias)
            {
                return _anchors.TryGetValue(alias.Value.Value, out var target)
                    ? target
                    : throw new YamlProblemException("found undefined alias", Line(alias.Start));
            }

            var nodeEvent = e as NodeEvent ?? throw new YamlProblemException("expected a node", Line(e.Start));
            string? tag = nodeEvent.Tag.IsEmpty ? null : nodeEvent.Tag.Value;
            Node node = e switch
            {
                Scalar s => new ScalarNode(s.Start, ScalarTag(tag, s), s.Value),
                SequenceStart => new SequenceNode(e.Start, tag is null or "!" ? YamlTags.Seq : tag),
                MappingStart => new MappingNode(e.Start, tag is null or "!" ? YamlTags.Map : tag),
                _ => throw new YamlProblemException("expected a node", Line(e.Start)),
            };

            if (!nodeEvent.Anchor.IsEmpty)
            {
                var name = nodeEvent.Anchor.Value;
                if (_anchors.ContainsKey(name))
                {
                    throw new YamlProblemException($"found duplicate anchor {name}", Line(e.Start));
                }

                _anchors[name] = node;
            }

            switch (node)
            {
                case SequenceNode seq:
                    while (events.Peek() is not SequenceEnd)
                    {
                        seq.Items.Add(Compose());
                    }

                    events.Take();
                    break;
                case MappingNode map:
                    while (events.Peek() is not MappingEnd)
                    {
                        var key = Compose();
                        map.Pairs.Add((key, Compose()));
                    }

                    events.Take();
                    break;
            }

            return node;
        }

        // libyaml treats the non-specific tag `!` as plain, whatever the quoting: `! '12'` is 12.
        private static string ScalarTag(string? tag, Scalar s) => tag switch
        {
            null => s.Style == ScalarStyle.Plain ? YamlResolver.ResolvePlain(s.Value) : YamlTags.Str,
            "!" => YamlResolver.ResolvePlain(s.Value),
            _ => tag,
        };
    }

    /// <summary>PyYAML's SafeConstructor: nodes to values.</summary>
    private sealed class Constructor
    {
        private readonly Dictionary<Node, object?> _built = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Node> _building = new(ReferenceEqualityComparer.Instance);

        public object? Construct(Node node)
        {
            if (_built.TryGetValue(node, out var done))
            {
                return done;
            }

            if (!_building.Add(node))
            {
                throw new YamlProblemException("found an alias inside the value it names", Line(node.Start));
            }

            object? value;
            try
            {
                value = node switch
                {
                    ScalarNode s => Scalar(s),
                    SequenceNode q when q.Tag == YamlTags.Seq => q.Items.Select(Construct).ToList(),
                    MappingNode m when m.Tag == YamlTags.Map => Mapping(m),
                    _ => throw new YamlProblemException($"could not determine a constructor for the tag '{node.Tag}'", Line(node.Start)),
                };
            }
            finally
            {
                _building.Remove(node);
            }

            _built[node] = value;
            return value;
        }

        private static string Kind(Node node) => node switch
        {
            ScalarNode => "scalar",
            SequenceNode => "sequence",
            _ => "mapping",
        };

        private static object? Scalar(ScalarNode s)
        {
            if (s.Tag is YamlTags.Merge or YamlTags.Value or YamlTags.Yaml)
            {
                throw new YamlProblemException($"could not determine a constructor for the tag '{s.Tag}'", Line(s.Start));
            }

            try
            {
                return YamlResolver.Construct(s.Tag, s.Value);
            }
            catch (FormatException ex)
            {
                throw new YamlProblemException(ex.Message, Line(s.Start));
            }
        }

        private PyDict Mapping(MappingNode node)
        {
            Flatten(node);
            var dict = new PyDict();
            foreach (var (keyNode, valueNode) in node.Pairs)
            {
                var key = Construct(keyNode);
                if (key is PyDict or List<object?>)
                {
                    throw new YamlProblemException("found unhashable key", Line(keyNode.Start));
                }

                dict[key] = Construct(valueNode);
            }

            return dict;
        }

        /// <summary>
        /// SafeConstructor.flatten_mapping: the pairs merged in by `<<` go first (with a list, the
        /// last mapping's first, so the first mapping wins), then the mapping's own, later winning.
        /// </summary>
        private static void Flatten(MappingNode node)
        {
            var merge = new List<(Node, Node)>();
            var index = 0;
            while (index < node.Pairs.Count)
            {
                var (keyNode, valueNode) = node.Pairs[index];
                if (keyNode.Tag == YamlTags.Merge)
                {
                    node.Pairs.RemoveAt(index);
                    switch (valueNode)
                    {
                        case MappingNode m:
                            Flatten(m);
                            merge.AddRange(m.Pairs);
                            break;
                        case SequenceNode seq:
                            var subs = new List<List<(Node, Node)>>();
                            foreach (var sub in seq.Items)
                            {
                                if (sub is not MappingNode subMap)
                                {
                                    throw new YamlProblemException($"expected a mapping for merging, but found {Kind(sub)}", Line(sub.Start));
                                }

                                Flatten(subMap);
                                subs.Add(subMap.Pairs);
                            }

                            subs.Reverse();
                            foreach (var pairs in subs)
                            {
                                merge.AddRange(pairs);
                            }

                            break;
                        default:
                            throw new YamlProblemException($"expected a mapping or list of mappings for merging, but found {Kind(valueNode)}", Line(valueNode.Start));
                    }
                }
                else
                {
                    if (keyNode.Tag == YamlTags.Value)
                    {
                        keyNode.Tag = YamlTags.Str;
                    }

                    index++;
                }
            }

            if (merge.Count > 0)
            {
                node.Pairs = [.. merge, .. node.Pairs];
            }
        }
    }
}
