using System.Globalization;
using System.Numerics;
using System.Text;
using LabOps.Engines.Python;

namespace LabOps.Engines.Yaml;

/// <summary>
/// Writes a value exactly as project.py's yaml_scalar does: PyYAML's yaml.dump with
/// default_flow_style=True, allow_unicode=True, width=10000 and sort_keys=False, stripped of its
/// line end and document marker. Records are edited line by line, so every value written into one
/// goes through here, and the text must match PyYAML's byte for byte or every edit would rewrite
/// lines nobody changed.
/// </summary>
/// <remarks>
/// A port of the parts of PyYAML's representer, serializer and emitter that a flow-style dump
/// reaches (yaml/representer.py, serializer.py, emitter.py): scalar analysis and style choice,
/// the single- and double-quoted writers, flow sequences and mappings, and line wrapping past the
/// width. Python counts code points where .NET counts UTF-16 units, so text is walked by rune.
/// </remarks>
public static class YamlEmitter
{
    private const int BestIndent = 2;
    private const int BestWidth = 10_000;

    /// <summary>project.py's yaml_scalar(v).</summary>
    public static string Scalar(object? value)
    {
        if (value is DateOnly date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (value is PyDateTime dateTime)
        {
            return dateTime.IsoFormat('T');
        }

        var text = Py.Strip(Dump(value));
        if (text.EndsWith("...", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        return Py.Strip(text);
    }

    /// <summary>yaml.dump(value, Dumper=SafeDumper with no aliases, default_flow_style=True, allow_unicode=True, width=10000, sort_keys=False).</summary>
    public static string Dump(object? value)
    {
        var emitter = new Emitter();
        emitter.EmitDocument(Represent(value));
        return emitter.ToString();
    }

    // ---------------------------------------------------------------- representer and serializer

    private abstract record Node;

    private sealed record ScalarNode(string Tag, string Value, bool PlainImplicit, bool QuotedImplicit) : Node;

    private sealed record SequenceNode(List<Node> Items) : Node;

    private sealed record MappingNode(List<(Node Key, Node Value)> Pairs) : Node;

    private static Node Represent(object? value) => value switch
    {
        null => Scalar(YamlTags.Null, "null"),
        bool b => Scalar(YamlTags.Bool, b ? "true" : "false"),
        int i => Scalar(YamlTags.Int, i.ToString(CultureInfo.InvariantCulture)),
        long l => Scalar(YamlTags.Int, l.ToString(CultureInfo.InvariantCulture)),
        BigInteger big => Scalar(YamlTags.Int, big.ToString(CultureInfo.InvariantCulture)),
        double d => Scalar(YamlTags.Float, RepresentFloat(d)),
        string s => Scalar(YamlTags.Str, s),
        DateOnly date => Scalar(YamlTags.Timestamp, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        PyDateTime dt => Scalar(YamlTags.Timestamp, dt.IsoFormat(' ')),
        PyDict dict => new MappingNode([.. dict.Select(p => (Represent(p.Key), Represent(p.Value)))]),
        System.Collections.IDictionary => throw new NotSupportedException("use PyDict for a mapping"),
        System.Collections.IEnumerable list => new SequenceNode([.. list.Cast<object?>().Select(Represent)]),
        _ => throw new NotSupportedException($"no YAML form for {value.GetType().Name}"),
    };

    // The serializer's implicit flags: plain is allowed when the text resolves back to the same
    // tag (`yes` as a str does not, so it is quoted); quoted when the tag is the default, str.
    private static ScalarNode Scalar(string tag, string text) =>
        new(tag, text, YamlResolver.ResolvePlain(text) == tag, tag == YamlTags.Str);

    /// <summary>SafeRepresenter.represent_float: repr, lowercased, with .0 put in 1e+20.</summary>
    private static string RepresentFloat(double d)
    {
        if (double.IsNaN(d))
        {
            return ".nan";
        }

        if (double.IsInfinity(d))
        {
            return d > 0 ? ".inf" : "-.inf";
        }

        var value = Py.FloatRepr(d).ToLowerInvariant();
        if (!value.Contains('.', StringComparison.Ordinal) && value.Contains('e', StringComparison.Ordinal))
        {
            var e = value.IndexOf('e', StringComparison.Ordinal);
            value = value[..e] + ".0" + value[e..];
        }

        return value;
    }

    // ---------------------------------------------------------------- emitter

    private sealed class Analysis
    {
        public required string Scalar { get; init; }

        public bool Empty { get; init; }

        public bool Multiline { get; init; }

        public bool AllowFlowPlain { get; init; }

        public bool AllowBlockPlain { get; init; }

        public bool AllowSingleQuoted { get; init; }
    }

    private sealed class Emitter
    {
        private readonly StringBuilder _out = new();
        private readonly Stack<int?> _indents = new();
        private int? _indent;
        private int _flowLevel;
        private int _column;
        private bool _whitespace = true;
        private bool _indention = true;
        private bool _openEnded;
        private bool _rootContext;
        private bool _simpleKeyContext;

        public override string ToString() => _out.ToString();

        public void EmitDocument(Node root)
        {
            // expect_document_start: the first document is implicit (no ---) unless it is an
            // empty untagged scalar, which a str never is (its tag is never omitted as "implicit").
            ExpectNode(root, root: true);
            // expect_document_end, then the stream end writes "..." after an open-ended scalar.
            WriteIndent();
            if (_openEnded)
            {
                WriteIndicator("...", true);
                WriteIndent();
            }
        }

        private void ExpectNode(Node node, bool root = false, bool simpleKey = false)
        {
            _rootContext = root;
            _simpleKeyContext = simpleKey;
            switch (node)
            {
                case ScalarNode s:
                    ProcessScalar(s);
                    break;
                case SequenceNode seq:
                    WriteIndicator("[", true, whitespace: true);
                    _flowLevel++;
                    IncreaseIndent(flow: true);
                    for (var i = 0; i < seq.Items.Count; i++)
                    {
                        if (i > 0)
                        {
                            WriteIndicator(",", false);
                        }

                        if (_column > BestWidth)
                        {
                            WriteIndent();
                        }

                        ExpectNode(seq.Items[i]);
                    }

                    _indent = _indents.Pop();
                    _flowLevel--;
                    WriteIndicator("]", false);
                    break;
                case MappingNode map:
                    WriteIndicator("{", true, whitespace: true);
                    _flowLevel++;
                    IncreaseIndent(flow: true);
                    for (var i = 0; i < map.Pairs.Count; i++)
                    {
                        if (i > 0)
                        {
                            WriteIndicator(",", false);
                        }

                        if (_column > BestWidth)
                        {
                            WriteIndent();
                        }

                        var (key, value) = map.Pairs[i];
                        if (CheckSimpleKey(key))
                        {
                            ExpectNode(key, simpleKey: true);
                            WriteIndicator(":", false);
                            ExpectNode(value);
                        }
                        else
                        {
                            WriteIndicator("?", true);
                            ExpectNode(key);
                            if (_column > BestWidth)
                            {
                                WriteIndent();
                            }

                            WriteIndicator(":", true);
                            ExpectNode(value);
                        }
                    }

                    _indent = _indents.Pop();
                    _flowLevel--;
                    WriteIndicator("}", false);
                    break;
            }
        }

        // check_simple_key counts the node's tag ("!!str") toward the 128 characters whether or not
        // the tag is written, since PyYAML's events always carry one.
        private static bool CheckSimpleKey(Node key)
        {
            switch (key)
            {
                case ScalarNode s:
                    var analysis = Analyze(s.Value);
                    return Runes(analysis.Scalar) + PrepareTag(s.Tag).Length < 128 && !analysis.Empty && !analysis.Multiline;
                case SequenceNode seq:
                    return PrepareTag(YamlTags.Seq).Length < 128 && seq.Items.Count == 0;
                case MappingNode map:
                    return PrepareTag(YamlTags.Map).Length < 128 && map.Pairs.Count == 0;
                default:
                    return false;
            }
        }

        /// <summary>Emitter.prepare_tag for the YAML core tags: tag:yaml.org,2002:timestamp is !!timestamp.</summary>
        private static string PrepareTag(string tag)
        {
            const string core = "tag:yaml.org,2002:";
            return tag.StartsWith(core, StringComparison.Ordinal) && tag.Length > core.Length
                ? "!!" + tag[core.Length..]
                : "!<" + tag + ">";
        }

        private static int Runes(string s) => s.EnumerateRunes().Count();

        private void IncreaseIndent(bool flow)
        {
            _indents.Push(_indent);
            _indent = _indent is null ? (flow ? BestIndent : 0) : _indent + BestIndent;
        }

        /// <summary>process_tag, then expect_scalar's process_scalar.</summary>
        private void ProcessScalar(ScalarNode s)
        {
            var analysis = Analyze(s.Value);
            var style = ChooseStyle(s, analysis);
            if (!((style == ' ' && s.PlainImplicit) || (style != ' ' && s.QuotedImplicit)))
            {
                // A value that cannot be written plain and is not text, such as a datetime in a
                // flow mapping (its colons need quotes), keeps its type with a tag:
                // {started: !!timestamp '2026-10-01 09:30:00'}.
                WriteIndicator(PrepareTag(s.Tag), true);
            }

            IncreaseIndent(flow: true);
            WriteScalar(style, analysis);
            _indent = _indents.Pop();
        }

        private void WriteScalar(char style, Analysis analysis)
        {
            var split = !_simpleKeyContext;
            switch (style)
            {
                case '"':
                    WriteDoubleQuoted(analysis.Scalar, split);
                    break;
                case '\'':
                    WriteSingleQuoted(analysis.Scalar, split);
                    break;
                default:
                    WritePlain(analysis.Scalar, split);
                    break;
            }
        }

        /// <summary>Emitter.choose_scalar_style for an event with no style of its own: ' ' is plain.</summary>
        private char ChooseStyle(ScalarNode s, Analysis a)
        {
            if (s.PlainImplicit
                && !(_simpleKeyContext && (a.Empty || a.Multiline))
                && ((_flowLevel > 0 && a.AllowFlowPlain) || (_flowLevel == 0 && a.AllowBlockPlain)))
            {
                return ' ';
            }

            if (a.AllowSingleQuoted && !(_simpleKeyContext && a.Multiline))
            {
                return '\'';
            }

            return '"';
        }

        private static bool IsBreak(int c) => c is '\n' or 0x85 or 0x2028 or 0x2029;

        private static bool IsSpaceOrBreakOrNull(int c) => c is '\0' or ' ' or '\t' or '\r' || IsBreak(c);

        /// <summary>Emitter.analyze_scalar.</summary>
        private static Analysis Analyze(string scalar)
        {
            if (scalar.Length == 0)
            {
                return new Analysis
                {
                    Scalar = scalar, Empty = true, Multiline = false, AllowFlowPlain = false, AllowBlockPlain = true,
                    AllowSingleQuoted = true,
                };
            }

            var chars = scalar.EnumerateRunes().Select(r => r.Value).ToArray();
            bool blockIndicators = false, flowIndicators = false, lineBreaks = false, specialCharacters = false;
            bool leadingSpace = false, leadingBreak = false, trailingSpace = false, trailingBreak = false;
            bool breakSpace = false, spaceBreak = false;

            if (scalar.StartsWith("---", StringComparison.Ordinal) || scalar.StartsWith("...", StringComparison.Ordinal))
            {
                blockIndicators = true;
                flowIndicators = true;
            }

            var precededByWhitespace = true;
            var followedByWhitespace = chars.Length == 1 || IsSpaceOrBreakOrNull(chars[1]);
            var previousSpace = false;
            var previousBreak = false;

            for (var index = 0; index < chars.Length; index++)
            {
                var ch = chars[index];
                if (index == 0)
                {
                    if (ch < 0x80 && "#,[]{}&*!|>'\"%@`".Contains((char)ch, StringComparison.Ordinal))
                    {
                        flowIndicators = true;
                        blockIndicators = true;
                    }

                    if (ch is '?' or ':')
                    {
                        flowIndicators = true;
                        if (followedByWhitespace)
                        {
                            blockIndicators = true;
                        }
                    }

                    if (ch == '-' && followedByWhitespace)
                    {
                        flowIndicators = true;
                        blockIndicators = true;
                    }
                }
                else
                {
                    if (ch is ',' or '?' or '[' or ']' or '{' or '}')
                    {
                        flowIndicators = true;
                    }

                    if (ch == ':')
                    {
                        flowIndicators = true;
                        if (followedByWhitespace)
                        {
                            blockIndicators = true;
                        }
                    }

                    if (ch == '#' && precededByWhitespace)
                    {
                        flowIndicators = true;
                        blockIndicators = true;
                    }
                }

                if (IsBreak(ch))
                {
                    lineBreaks = true;
                }

                if (!(ch == '\n' || (ch >= 0x20 && ch <= 0x7E)))
                {
                    var unicode = (ch == 0x85 || (ch >= 0xA0 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD)
                                   || (ch >= 0x10000 && ch < 0x10FFFF)) && ch != 0xFEFF;
                    if (!unicode)
                    {
                        specialCharacters = true;
                    }
                }

                if (ch == ' ')
                {
                    if (index == 0)
                    {
                        leadingSpace = true;
                    }

                    if (index == chars.Length - 1)
                    {
                        trailingSpace = true;
                    }

                    if (previousBreak)
                    {
                        breakSpace = true;
                    }

                    previousSpace = true;
                    previousBreak = false;
                }
                else if (IsBreak(ch))
                {
                    if (index == 0)
                    {
                        leadingBreak = true;
                    }

                    if (index == chars.Length - 1)
                    {
                        trailingBreak = true;
                    }

                    if (previousSpace)
                    {
                        spaceBreak = true;
                    }

                    previousSpace = false;
                    previousBreak = true;
                }
                else
                {
                    previousSpace = false;
                    previousBreak = false;
                }

                precededByWhitespace = IsSpaceOrBreakOrNull(ch);
                followedByWhitespace = index + 2 >= chars.Length || IsSpaceOrBreakOrNull(chars[index + 2]);
            }

            bool allowFlowPlain = true, allowBlockPlain = true, allowSingleQuoted = true;
            if (leadingSpace || leadingBreak || trailingSpace || trailingBreak)
            {
                allowFlowPlain = allowBlockPlain = false;
            }

            if (breakSpace)
            {
                allowFlowPlain = allowBlockPlain = allowSingleQuoted = false;
            }

            if (spaceBreak || specialCharacters)
            {
                allowFlowPlain = allowBlockPlain = allowSingleQuoted = false;
            }

            if (lineBreaks)
            {
                allowFlowPlain = allowBlockPlain = false;
            }

            if (flowIndicators)
            {
                allowFlowPlain = false;
            }

            if (blockIndicators)
            {
                allowBlockPlain = false;
            }

            return new Analysis
            {
                Scalar = scalar, Empty = false, Multiline = lineBreaks, AllowFlowPlain = allowFlowPlain,
                AllowBlockPlain = allowBlockPlain, AllowSingleQuoted = allowSingleQuoted,
            };
        }

        // ------------------------------------------------------------ writers

        private void Write(string data)
        {
            _column += Runes(data);
            _out.Append(data);
        }

        private void WriteIndicator(string indicator, bool needWhitespace, bool whitespace = false, bool indention = false)
        {
            var data = _whitespace || !needWhitespace ? indicator : " " + indicator;
            _whitespace = whitespace;
            _indention = _indention && indention;
            _openEnded = false;
            Write(data);
        }

        private void WriteIndent()
        {
            var indent = _indent ?? 0;
            if (!_indention || _column > indent || (_column == indent && !_whitespace))
            {
                WriteLineBreak();
            }

            if (_column < indent)
            {
                _whitespace = true;
                var data = new string(' ', indent - _column);
                _out.Append(data);
                _column = indent;
            }
        }

        private void WriteLineBreak(string data = "\n")
        {
            _whitespace = true;
            _indention = true;
            _column = 0;
            _out.Append(data);
        }

        private void WriteSingleQuoted(string scalar, bool split)
        {
            WriteIndicator("'", true);
            var text = scalar.EnumerateRunes().Select(r => r.ToString()).ToArray();
            bool spaces = false, breaks = false;
            int start = 0, end = 0;
            while (end <= text.Length)
            {
                string? ch = end < text.Length ? text[end] : null;
                if (spaces)
                {
                    if (ch is null || ch != " ")
                    {
                        if (start + 1 == end && _column > BestWidth && split && start != 0 && end != text.Length)
                        {
                            WriteIndent();
                        }
                        else
                        {
                            Write(string.Concat(text[start..end]));
                        }

                        start = end;
                    }
                }
                else if (breaks)
                {
                    if (ch is null || !IsBreak(ch[0]))
                    {
                        if (text[start] == "\n")
                        {
                            WriteLineBreak();
                        }

                        foreach (var br in text[start..end])
                        {
                            WriteLineBreak(br);
                        }

                        WriteIndent();
                        start = end;
                    }
                }
                else
                {
                    if (ch is null || ch == " " || IsBreak(ch[0]) || ch == "'")
                    {
                        if (start < end)
                        {
                            Write(string.Concat(text[start..end]));
                            start = end;
                        }
                    }
                }

                if (ch == "'")
                {
                    Write("''");
                    start = end + 1;
                }

                if (ch is not null)
                {
                    spaces = ch == " ";
                    breaks = IsBreak(ch[0]);
                }

                end++;
            }

            WriteIndicator("'", false);
        }

        private static readonly Dictionary<int, char> EscapeReplacements = new()
        {
            [0] = '0', [0x07] = 'a', [0x08] = 'b', [0x09] = 't', [0x0A] = 'n', [0x0B] = 'v', [0x0C] = 'f',
            [0x0D] = 'r', [0x1B] = 'e', ['"'] = '"', ['\\'] = '\\', [0x85] = 'N', [0xA0] = '_', [0x2028] = 'L',
            [0x2029] = 'P',
        };

        private void WriteDoubleQuoted(string scalar, bool split)
        {
            WriteIndicator("\"", true);
            var text = scalar.EnumerateRunes().Select(r => r.Value).ToArray();
            string Slice(int a, int b) => string.Concat(text[a..b].Select(c => char.ConvertFromUtf32(c)));
            int start = 0, end = 0;
            while (end <= text.Length)
            {
                int? ch = end < text.Length ? text[end] : null;
                if (ch is null || ch is '"' or '\\' or 0x85 or 0x2028 or 0x2029 or 0xFEFF
                    || !((ch >= 0x20 && ch <= 0x7E) || (ch >= 0xA0 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD)))
                {
                    if (start < end)
                    {
                        Write(Slice(start, end));
                        start = end;
                    }

                    if (ch is { } c)
                    {
                        var data = EscapeReplacements.TryGetValue(c, out var r) ? "\\" + r
                            : c <= 0xFF ? "\\x" + c.ToString("X2", CultureInfo.InvariantCulture)
                            : c <= 0xFFFF ? "\\u" + c.ToString("X4", CultureInfo.InvariantCulture)
                            : "\\U" + c.ToString("X8", CultureInfo.InvariantCulture);
                        Write(data);
                        start = end + 1;
                    }
                }

                if (end > 0 && end < text.Length - 1 && (ch == ' ' || start >= end)
                    && _column + (end - start) > BestWidth && split)
                {
                    var data = Slice(start, end) + "\\";
                    if (start < end)
                    {
                        start = end;
                    }

                    Write(data);
                    WriteIndent();
                    _whitespace = false;
                    _indention = false;
                    if (text[start] == ' ')
                    {
                        Write("\\");
                    }
                }

                end++;
            }

            WriteIndicator("\"", false);
        }

        private void WritePlain(string scalar, bool split)
        {
            if (_rootContext)
            {
                _openEnded = true;
            }

            if (scalar.Length == 0)
            {
                return;
            }

            if (!_whitespace)
            {
                Write(" ");
            }

            _whitespace = false;
            _indention = false;
            var text = scalar.EnumerateRunes().Select(r => r.ToString()).ToArray();
            bool spaces = false, breaks = false;
            int start = 0, end = 0;
            while (end <= text.Length)
            {
                string? ch = end < text.Length ? text[end] : null;
                if (spaces)
                {
                    if (ch != " ")
                    {
                        if (start + 1 == end && _column > BestWidth && split)
                        {
                            WriteIndent();
                            _whitespace = false;
                            _indention = false;
                        }
                        else
                        {
                            Write(string.Concat(text[start..end]));
                        }

                        start = end;
                    }
                }
                else if (breaks)
                {
                    if (ch is null || !IsBreak(ch[0]))
                    {
                        if (text[start] == "\n")
                        {
                            WriteLineBreak();
                        }

                        foreach (var br in text[start..end])
                        {
                            WriteLineBreak(br);
                        }

                        WriteIndent();
                        _whitespace = false;
                        _indention = false;
                        start = end;
                    }
                }
                else
                {
                    if (ch is null || ch == " " || IsBreak(ch[0]))
                    {
                        Write(string.Concat(text[start..end]));
                        start = end;
                    }
                }

                if (ch is not null)
                {
                    spaces = ch == " ";
                    breaks = IsBreak(ch[0]);
                }

                end++;
            }
        }
    }
}
