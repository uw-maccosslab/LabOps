using System.Text;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;

namespace LabOps.Engines.Yaml;

/// <summary>
/// Edits to a record's text, line by line, that keep its comments. Records are written by people
/// and by Claude, and their comments explain choices, so commands never re-dump a whole file.
/// Ported from project.py (_block, set_fields, set_child, write_text), whose output this matches.
/// </summary>
public static class YamlText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// A file's text as Python's Path.read_text(encoding="utf-8") gives it: a byte-order mark kept
    /// as U+FEFF, and CRLF and lone CR read as LF, so edits always work on LF lines.
    /// </summary>
    public static string ReadText(string path, string shownAs)
    {
        try
        {
            return NormalizeNewlines(StrictUtf8.GetString(File.ReadAllBytes(path)));
        }
        catch (DecoderFallbackException)
        {
            throw new EngineError($"{shownAs} is not UTF-8 text; save it as UTF-8 and try again");
        }
    }

    public static string NormalizeNewlines(string text) =>
        text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : text;

    /// <summary>UTF-8 without a byte-order mark and LF line ends on every OS, as git stores it.</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, StrictUtf8.GetBytes(text));
    }

    /// <summary>
    /// _block: (start, end) of a top-level `key:` and the lines that belong to it (indented lines,
    /// list items at the margin, and blank lines or margin comments that more of it follows).
    /// </summary>
    public static (int Start, int End)? Block(IReadOnlyList<string> lines, string key)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith(key + ":", StringComparison.Ordinal)
                && (line.Length == key.Length + 1 || Py.IsSpace(line[key.Length + 1])))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        static bool Continues(string line) => line.StartsWith(' ') || line.StartsWith('\t') || line.StartsWith('-');

        var end = start + 1;
        while (end < lines.Count)
        {
            if (Continues(lines[end]))
            {
                end++;
                continue;
            }

            var following = end;
            while (following < lines.Count && (Py.Strip(lines[following]).Length == 0 || lines[following].StartsWith('#')))
            {
                following++;
            }

            if (following < lines.Count && Continues(lines[following]))
            {
                end = following;
                continue;
            }

            break;
        }

        return (start, end);
    }

    /// <summary>
    /// set_fields: sets top-level keys, keeping the comment on the key's line. The whole value is
    /// replaced, so a key written as a block over several lines becomes one line. A missing key goes
    /// after `after` (and the next missing one after it), or at the end when there is no `after`.
    /// </summary>
    public static string SetFields(string raw, IEnumerable<KeyValuePair<string, object?>> fields, string after = "status")
    {
        foreach (var (key, value) in fields)
        {
            var line = $"{key}: {YamlEmitter.Scalar(value)}";
            var lines = raw.Split('\n').ToList();
            if (Block(lines, key) is { } where)
            {
                var comment = FieldComment(lines[where.Start], key);
                Replace(lines, where.Start, where.End, [line + comment]);
            }
            else
            {
                if (Block(lines, after) is { } anchor)
                {
                    lines.Insert(anchor.End, line);
                }
                else
                {
                    while (lines.Count > 0 && Py.Strip(lines[^1]).Length == 0)
                    {
                        lines.RemoveAt(lines.Count - 1);
                    }

                    lines.Add(line);
                    lines.Add("");
                }

                after = key;
            }

            raw = string.Join('\n', lines);
        }

        return raw;
    }

    /// <summary>
    /// set_child: sets `key` inside the top-level mapping `parent:` (two-space indent), keeping its
    /// comment. A key that is not there goes first in the mapping.
    /// </summary>
    public static string SetChild(string raw, string parent, string key, object? value)
    {
        var block = Regex.Match(raw, $@"^{Regex.Escape(parent)}:[^\n]*\n((?:[ \t]+[^\n]*\n|[ \t]*#[^\n]*\n)*)", RegexOptions.Multiline);
        if (!block.Success)
        {
            throw new EngineError($"no top-level `{parent}:` mapping");
        }

        var body = block.Groups[1].Value;
        var line = $"  {key}: {YamlEmitter.Scalar(value)}";
        var pattern = new Regex($@"^  {Regex.Escape(key)}:[ \t]*(?<val>[^#\n]*?)(?<comment>[ \t]+#[^\n]*)?$", RegexOptions.Multiline);
        var m = pattern.Match(body);
        var prefix = $"  {key}:";
        if (m.Success && (!m.Groups["comment"].Success || !EndsInsideQuotes(body[(m.Index + prefix.Length)..m.Groups["comment"].Index])))
        {
            body = body[..m.Index] + line + m.Groups["comment"].Value + body[(m.Index + m.Length)..];
        }
        else if (FirstLineStarting(body, prefix) is { } at)
        {
            // project.py's pattern misses a value with a # in it ("a#b") and then added the key a
            // second time; and a quoted value with " #" in it lost part of itself to the comment.
            var lineEnd = body.IndexOf('\n', at);
            var old = body[at..(lineEnd < 0 ? body.Length : lineEnd)];
            body = body[..at] + line + Comment(old, prefix.Length, emptyValueKeepsOneSpace: true) + body[(at + old.Length)..];
        }
        else
        {
            body = line + "\n" + body;
        }

        return raw[..block.Groups[1].Index] + body + raw[(block.Groups[1].Index + block.Groups[1].Length)..];
    }

    private static int? FirstLineStarting(string text, string prefix)
    {
        var at = 0;
        while (at <= text.Length)
        {
            if (string.CompareOrdinal(text, at, prefix, 0, prefix.Length) == 0)
            {
                return at;
            }

            var next = text.IndexOf('\n', at);
            if (next < 0)
            {
                return null;
            }

            at = next + 1;
        }

        return null;
    }

    /// <summary>The comment on a `key: value  # comment` line, as set_fields keeps it.</summary>
    private static string FieldComment(string line, string key)
    {
        // project.py's pattern, which is right unless the # it found is inside a quoted value
        // ('a #b'), where it took part of the value for the comment.
        var m = Regex.Match(line, $@"^{Regex.Escape(key)}:[ \t]*?[^#]*?(?<comment>[ \t]+#.*)?$");
        var start = key.Length + 1;
        if (m.Success && (!m.Groups["comment"].Success || !EndsInsideQuotes(line[start..m.Groups["comment"].Index])))
        {
            return m.Groups["comment"].Value;
        }

        return Comment(line, start, emptyValueKeepsOneSpace: false);
    }

    /// <summary>
    /// The comment after the value that starts at `start`: the first run of spaces and tabs before
    /// a # that is not inside a quoted value. set_child's pattern leaves only one
    /// space before the # when the value is empty; set_fields keeps them all.
    /// </summary>
    private static string Comment(string line, int start, bool emptyValueKeepsOneSpace)
    {
        for (var i = start; i < line.Length; i++)
        {
            if (line[i] is not (' ' or '\t') || (i > start && line[i - 1] is ' ' or '\t'))
            {
                continue;
            }

            var j = i;
            while (j < line.Length && line[j] is ' ' or '\t')
            {
                j++;
            }

            if (j < line.Length && line[j] == '#' && !EndsInsideQuotes(line[start..i]))
            {
                return emptyValueKeepsOneSpace && i == start ? line[(j - 1)..] : line[i..];
            }
        }

        return "";
    }

    /// <summary>
    /// Whether the end of `value` is inside a quoted scalar ('it''s', "a \" b"), where a # is text
    /// and not a comment. A quote opens a scalar only where one can start: first in the value, or
    /// after [ { , or :. An apostrophe inside plain text (it's) opens nothing.
    /// </summary>
    private static bool EndsInsideQuotes(string value)
    {
        char? quote = null;
        char? previous = null;  // the last character outside quotes that is not a space or tab
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote == '\'')
            {
                if (c == '\'')
                {
                    if (i + 1 < value.Length && value[i + 1] == '\'')
                    {
                        i++;
                    }
                    else
                    {
                        quote = null;
                        previous = c;
                    }
                }
            }
            else if (quote == '"')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    quote = null;
                    previous = c;
                }
            }
            else if (c is '\'' or '"' && previous is null or '[' or '{' or ',' or ':')
            {
                quote = c;
            }
            else if (c is not (' ' or '\t'))
            {
                previous = c;
            }
        }

        return quote is not null;
    }

    /// <summary>lines[start:end] = replacement.</summary>
    public static void Replace(List<string> lines, int start, int end, IEnumerable<string> replacement)
    {
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacement);
    }
}
