using System.Globalization;
using System.Numerics;
using System.Text;

namespace LabOps.Engines.Python;

/// <summary>
/// Python's json.dumps(value, sort_keys=True, default=str) with its default separators and
/// ensure_ascii. The wiki page's footer carries a fingerprint of wiki.yaml made from this text, and
/// LabOps republishes a page on its own only while that fingerprint matches, so it has to be
/// Python's text exactly.
/// </summary>
public static class PyJson
{
    public static string Dumps(object? value)
    {
        var text = new StringBuilder();
        Write(text, value);
        return text.ToString();
    }

    /// <summary>json.dumps(value, indent=2): keys in their own order, one item per line.</summary>
    public static string DumpsIndented(object? value, int indent = 2)
    {
        var text = new StringBuilder();
        WriteIndented(text, value, indent, 0);
        return text.ToString();
    }

    private static void WriteIndented(StringBuilder text, object? value, int indent, int level)
    {
        switch (value)
        {
            case PyDict dict when dict.Count > 0:
                text.Append('{');
                var first = true;
                foreach (var (key, item) in dict)
                {
                    text.Append(first ? "" : ",").Append('\n').Append(' ', indent * (level + 1));
                    first = false;
                    String(text, Key(key));
                    text.Append(": ");
                    WriteIndented(text, item, indent, level + 1);
                }

                text.Append('\n').Append(' ', indent * level).Append('}');
                break;
            case List<object?> list when list.Count > 0:
                text.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    text.Append(i == 0 ? "" : ",").Append('\n').Append(' ', indent * (level + 1));
                    WriteIndented(text, list[i], indent, level + 1);
                }

                text.Append('\n').Append(' ', indent * level).Append(']');
                break;
            default:
                Write(text, value);
                break;
        }
    }

    private static void Write(StringBuilder text, object? value)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                break;
            case bool b:
                text.Append(b ? "true" : "false");
                break;
            case int or long or BigInteger:
                text.Append(PyText.Str(value));
                break;
            case double d:
                text.Append(Float(d));
                break;
            case string s:
                String(text, s);
                break;
            case PyDict dict:
                text.Append('{');
                var first = true;
                foreach (var (key, item) in dict.Select(p => (Key: Key(p.Key), p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    text.Append(first ? "" : ", ");
                    first = false;
                    String(text, key);
                    text.Append(": ");
                    Write(text, item);
                }

                text.Append('}');
                break;
            case List<object?> list:
                text.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    text.Append(i == 0 ? "" : ", ");
                    Write(text, list[i]);
                }

                text.Append(']');
                break;
            default:
                // default=str: a date as 2026-10-01, a datetime as 2026-10-01 09:30:00
                String(text, PyText.Str(value));
                break;
        }
    }

    // json turns a key that is not text into text; Python would refuse a date key or keys that
    // cannot be sorted together, which wiki.yaml never has.
    private static string Key(object? key) => key switch
    {
        string s => s,
        null => "null",
        bool b => b ? "true" : "false",
        double d => Float(d),
        _ => PyText.Str(key),
    };

    private static string Float(double d) =>
        double.IsNaN(d) ? "NaN" : double.IsPositiveInfinity(d) ? "Infinity" : double.IsNegativeInfinity(d) ? "-Infinity" : Py.FloatRepr(d);

    /// <summary>A JSON string as Python writes it with ensure_ascii: everything outside space..~ escaped.</summary>
    private static void String(StringBuilder text, string s)
    {
        text.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                default:
                    if (c is >= ' ' and <= '~')
                    {
                        text.Append(c);
                    }
                    else
                    {
                        // UTF-16 units, so a character beyond the first plane is its surrogate pair, as in Python.
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        text.Append('"');
    }
}
