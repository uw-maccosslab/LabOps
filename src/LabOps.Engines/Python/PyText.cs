using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace LabOps.Engines.Python;

/// <summary>
/// Python's str(), repr(), truth and == for the values a record holds. Messages and JSON from
/// project.py were built with them ("project.yaml says project 'X' but the folder is 'Y'",
/// `if not d.get("title")`), so the C# engine answers the same only by asking the same questions.
/// </summary>
public static class PyText
{
    /// <summary>str(v).</summary>
    public static string Str(object? v) => v switch
    {
        null => "None",
        string s => s,
        bool b => b ? "True" : "False",
        double d => Py.FloatRepr(d),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        PyDateTime dt => dt.IsoFormat(' '),
        PyTime or PyTimeDelta => v.ToString()!,
        IFormattable f when v is int or long or BigInteger => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Repr(v),
    };

    /// <summary>repr(v).</summary>
    public static string Repr(object? v) => v switch
    {
        null => "None",
        string s => ReprString(s),
        bool or int or long or BigInteger or double => Str(v),
        DateOnly d => $"datetime.date({d.Year}, {d.Month}, {d.Day})",
        PyDateTime dt => ReprDateTime(dt),
        PyDict dict => "{" + string.Join(", ", dict.Select(p => Repr(p.Key) + ": " + Repr(p.Value))) + "}",
        IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Repr)) + "]",
        _ => v.ToString() ?? "",
    };

    private static string ReprDateTime(PyDateTime dt)
    {
        var v = dt.Value;
        var parts = new List<string> { $"{v.Year}", $"{v.Month}", $"{v.Day}", $"{v.Hour}", $"{v.Minute}" };
        var micro = (int)(v.Ticks % TimeSpan.TicksPerSecond / 10);
        if (v.Second != 0 || micro != 0)
        {
            parts.Add($"{v.Second}");
        }

        if (micro != 0)
        {
            parts.Add($"{micro}");
        }

        var text = "datetime.datetime(" + string.Join(", ", parts);
        if (dt.Offset is { } offset)
        {
            text += offset == TimeSpan.Zero
                ? ", tzinfo=datetime.timezone.utc"
                : $", tzinfo=datetime.timezone({(offset < TimeSpan.Zero ? "-" : "")}datetime.timedelta({(offset < TimeSpan.Zero ? "days=-1, seconds=" + (86400 + (int)offset.TotalSeconds) : "seconds=" + (int)offset.TotalSeconds)})))";
        }

        return text + ")";
    }

    /// <summary>repr of a str: single quotes unless it holds ' and not ", and Python's escapes.</summary>
    public static string ReprString(string s)
    {
        var quote = s.Contains('\'', StringComparison.Ordinal) && !s.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var text = new StringBuilder().Append(quote);
        foreach (var rune in s.EnumerateRunes())
        {
            var c = rune.Value;
            if (c == quote || c == '\\')
            {
                text.Append('\\').Append((char)c);
            }
            else if (c == '\t')
            {
                text.Append("\\t");
            }
            else if (c == '\n')
            {
                text.Append("\\n");
            }
            else if (c == '\r')
            {
                text.Append("\\r");
            }
            else if (c < ' ' || c == 0x7F)
            {
                text.Append("\\x").Append(c.ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (IsPrintable(c))
            {
                text.Append(rune.ToString());
            }
            else
            {
                text.Append(c < 0x100 ? "\\x" + c.ToString("x2", CultureInfo.InvariantCulture)
                    : c < 0x10000 ? "\\u" + c.ToString("x4", CultureInfo.InvariantCulture)
                    : "\\U" + c.ToString("x8", CultureInfo.InvariantCulture));
            }
        }

        return text.Append(quote).ToString();
    }

    /// <summary>str.isprintable for one character: not a control, format, surrogate, private, unassigned or separator (but a space is printable).</summary>
    public static bool IsPrintable(int c)
    {
        if (c == ' ')
        {
            return true;
        }

        return CharUnicodeInfo.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.SpaceSeparator => false,
            _ => true,
        };
    }

    /// <summary>Python truth: None, False, 0, 0.0, "", [] and {} are false.</summary>
    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        BigInteger big => !big.IsZero,
        double d => d != 0,
        string s => s.Length > 0,
        PyDict d => d.Count > 0,
        ICollection c => c.Count > 0,
        _ => true,
    };

    /// <summary>Python ==: numbers by value across bool, int and float; a date never equals a datetime.</summary>
    public static bool Eq(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (IsNumber(a) && IsNumber(b))
        {
            if (a is double || b is double)
            {
                return ToDouble(a) == ToDouble(b);
            }

            return ToBig(a) == ToBig(b);
        }

        return (a, b) switch
        {
            (string x, string y) => string.Equals(x, y, StringComparison.Ordinal),
            (DateOnly x, DateOnly y) => x == y,
            (PyDateTime x, PyDateTime y) => x == y,
            (PyDict x, PyDict y) => x.Count == y.Count && x.All(p => y.TryGetValue(p.Key, out var v) && Eq(p.Value, v)),
            (IList x, IList y) when a is not string && b is not string =>
                x.Count == y.Count && x.Cast<object?>().Zip(y.Cast<object?>()).All(p => Eq(p.First, p.Second)),
            _ => false,
        };
    }

    public static bool IsNumber(object? v) => v is bool or int or long or BigInteger or double;

    public static bool IsInt(object? v) => v is bool or int or long or BigInteger;

    public static BigInteger ToBig(object v) => v switch
    {
        bool b => b ? BigInteger.One : BigInteger.Zero,
        int i => i,
        long l => l,
        BigInteger big => big,
        _ => throw new InvalidCastException(),
    };

    public static double ToDouble(object v) => v is double d ? d : (double)ToBig(v);

    /// <summary>Whether a value is hashable, so it can be a dict key or be looked up in a set.</summary>
    public static bool Hashable(object? v) => v is not (PyDict or IList) || v is string;

    /// <summary>str.split() with no arguments: runs of whitespace separate, none at the ends.</summary>
    public static IReadOnlyList<string> Split(string s)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= s.Length; i++)
        {
            var space = i == s.Length || Py.IsSpace(s[i]);
            if (space && start >= 0)
            {
                words.Add(s[start..i]);
                start = -1;
            }
            else if (!space && start < 0)
            {
                start = i;
            }
        }

        return words;
    }

    /// <summary>str.casefold() for the Latin cases the engine compares (ß folds to ss).</summary>
    public static string CaseFold(string s) =>
        s.ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal).Replace("ſ", "s", StringComparison.Ordinal);

    /// <summary>int(v) as project.py's _int uses it: None when Python's int() would refuse.</summary>
    public static object? ToInt(object? v)
    {
        switch (v)
        {
            case bool b:
                return b ? 1L : 0L;
            case long or BigInteger:
                return v;
            case int i:
                return (long)i;
            case double d:
                return double.IsFinite(d) ? Py.Shrink(new BigInteger(Math.Truncate(d))) : null;
            case string s:
                var t = Py.Strip(s);
                var sign = 1;
                if (t.StartsWith('+') || t.StartsWith('-'))
                {
                    sign = t[0] == '-' ? -1 : 1;
                    t = t[1..];
                }

                // Underscores may separate digits, not lead, trail or double.
                if (t.Length == 0 || t[0] == '_' || t[^1] == '_' || t.Contains("__", StringComparison.Ordinal))
                {
                    return null;
                }

                BigInteger value = 0;
                foreach (var c in t)
                {
                    if (c == '_')
                    {
                        continue;
                    }

                    var digit = CharUnicodeInfo.GetDecimalDigitValue(c);
                    if (digit < 0)
                    {
                        return null;
                    }

                    value = value * 10 + digit;
                }

                return Py.Shrink(sign * value);
            default:
                return null;
        }
    }
}
