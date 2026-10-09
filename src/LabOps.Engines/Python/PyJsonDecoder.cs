using System.Globalization;
using System.Numerics;
using System.Text;

namespace LabOps.Engines.Python;

/// <summary>What json.loads refuses, with Python's message: "Expecting ',' delimiter: line 1 column 12 (char 11)".</summary>
public sealed class PyJsonException(string message) : Exception(message);

/// <summary>
/// Python's json.loads (the C scanner in Modules/_json.c, which the json module uses): the same
/// values (NaN and Infinity included, whole numbers as ints, the last duplicate key winning) and the
/// same error messages, which the identifier check shows for a JSON file it cannot read.
/// </summary>
public static class PyJsonDecoder
{
    public static object? Loads(string text)
    {
        // Positions in Python's messages count characters, not UTF-16 units.
        var s = text.EnumerateRunes().Select(r => r.Value).ToArray();
        if (s.Length > 0 && s[0] == 0xFEFF)
        {
            throw Error("Unexpected UTF-8 BOM (decode using utf-8-sig)", s, 0);
        }

        var idx = SkipSpace(s, 0);
        object? value;
        try
        {
            value = Scan(s, idx, out idx);
        }
        catch (StopScan stop)
        {
            throw Error("Expecting value", s, stop.Index);
        }

        idx = SkipSpace(s, idx);
        if (idx != s.Length)
        {
            throw Error("Extra data", s, idx);
        }

        return value;
    }

    private sealed class StopScan(int index) : Exception
    {
        public int Index { get; } = index;
    }

    private static PyJsonException Error(string message, int[] s, int pos)
    {
        var line = 1;
        var lastNewline = -1;
        for (var i = 0; i < pos && i < s.Length; i++)
        {
            if (s[i] == '\n')
            {
                line++;
                lastNewline = i;
            }
        }

        return new PyJsonException($"{message}: line {line} column {pos - lastNewline} (char {pos})");
    }

    private static bool IsSpace(int c) => c is ' ' or '\t' or '\n' or '\r';

    private static int SkipSpace(int[] s, int idx)
    {
        while (idx < s.Length && IsSpace(s[idx]))
        {
            idx++;
        }

        return idx;
    }

    private static bool At(int[] s, int idx, string word)
    {
        if (idx + word.Length > s.Length)
        {
            return false;
        }

        for (var i = 0; i < word.Length; i++)
        {
            if (s[idx + i] != word[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>scan_once_unicode: one value at idx; StopScan when none starts there.</summary>
    private static object? Scan(int[] s, int idx, out int next)
    {
        if (idx >= s.Length)
        {
            throw new StopScan(idx);
        }

        switch (s[idx])
        {
            case '"':
                return ScanString(s, idx + 1, out next);
            case '{':
                return ParseObject(s, idx + 1, out next);
            case '[':
                return ParseArray(s, idx + 1, out next);
            case 'n' when At(s, idx, "null"):
                next = idx + 4;
                return null;
            case 't' when At(s, idx, "true"):
                next = idx + 4;
                return true;
            case 'f' when At(s, idx, "false"):
                next = idx + 5;
                return false;
            case 'N' when At(s, idx, "NaN"):
                next = idx + 3;
                return double.NaN;
            case 'I' when At(s, idx, "Infinity"):
                next = idx + 8;
                return double.PositiveInfinity;
            case '-' when At(s, idx, "-Infinity"):
                next = idx + 9;
                return double.NegativeInfinity;
        }

        return MatchNumber(s, idx, out next);
    }

    private static bool Digit(int[] s, int i) => i < s.Length && s[i] is >= '0' and <= '9';

    private static object MatchNumber(int[] s, int start, out int next)
    {
        var idx = start;
        if (s[idx] == '-')
        {
            idx++;
            if (idx >= s.Length)
            {
                throw new StopScan(start);
            }
        }

        if (s[idx] is >= '1' and <= '9')
        {
            idx++;
            while (Digit(s, idx))
            {
                idx++;
            }
        }
        else if (s[idx] == '0')
        {
            idx++;
        }
        else
        {
            throw new StopScan(start);
        }

        var isFloat = false;
        if (idx < s.Length - 1 && s[idx] == '.' && Digit(s, idx + 1))
        {
            isFloat = true;
            idx += 2;
            while (Digit(s, idx))
            {
                idx++;
            }
        }

        if (idx < s.Length - 1 && s[idx] is 'e' or 'E')
        {
            var eStart = idx;
            idx++;
            if (idx < s.Length - 1 && s[idx] is '-' or '+')
            {
                idx++;
            }

            while (Digit(s, idx))
            {
                idx++;
            }

            if (Digit(s, idx - 1))
            {
                isFloat = true;
            }
            else
            {
                idx = eStart;
            }
        }

        next = idx;
        var text = string.Concat(s[start..idx].Select(c => (char)c));
        return isFloat
            ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
            : Py.Shrink(BigInteger.Parse(text, CultureInfo.InvariantCulture));
    }

    /// <summary>scanstring_unicode: the string whose opening quote is just before `end`.</summary>
    private static string ScanString(int[] s, int end, out int next)
    {
        var begin = end - 1;
        var text = new StringBuilder();
        while (true)
        {
            var c = 0;
            int at;
            for (at = end; at < s.Length; at++)
            {
                c = s[at];
                if (c is '"' or '\\')
                {
                    break;
                }

                if (c <= 0x1F)
                {
                    throw Error("Invalid control character at", s, at);
                }
            }

            if (c is not ('"' or '\\') || at >= s.Length)
            {
                throw Error("Unterminated string starting at", s, begin);
            }

            for (var i = end; i < at; i++)
            {
                text.Append(char.ConvertFromUtf32(s[i]));
            }

            at++;
            if (c == '"')
            {
                next = at;
                return text.ToString();
            }

            if (at == s.Length)
            {
                throw Error("Unterminated string starting at", s, begin);
            }

            c = s[at];
            if (c != 'u')
            {
                end = at + 1;
                c = c switch
                {
                    '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => 0,
                };
                if (c == 0)
                {
                    throw Error("Invalid \\escape", s, end - 2);
                }

                text.Append((char)c);
                continue;
            }

            at++;
            end = at + 4;
            if (end >= s.Length)
            {
                throw Error("Invalid \\uXXXX escape", s, at - 1);
            }

            var code = Hex(s, at, end) ?? throw Error("Invalid \\uXXXX escape", s, end - 5);
            at = end;
            if (code is >= 0xD800 and <= 0xDBFF && end + 6 < s.Length && s[at] == '\\' && s[at + 1] == 'u')
            {
                var low = Hex(s, at + 2, at + 6) ?? throw Error("Invalid \\uXXXX escape", s, end + 1);
                if (low is >= 0xDC00 and <= 0xDFFF)
                {
                    code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00);
                    end += 6;
                }
            }

            // A lone surrogate stays one, as Python keeps it.
            text.Append(code is >= 0xD800 and <= 0xDFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code));
        }
    }

    private static int? Hex(int[] s, int from, int to)
    {
        var value = 0;
        for (var i = from; i < to; i++)
        {
            var d = s[i] switch
            {
                >= '0' and <= '9' => s[i] - '0',
                >= 'a' and <= 'f' => s[i] - 'a' + 10,
                >= 'A' and <= 'F' => s[i] - 'A' + 10,
                _ => -1,
            };
            if (d < 0)
            {
                return null;
            }

            value = value * 16 + d;
        }

        return value;
    }

    private static PyDict ParseObject(int[] s, int idx, out int next)
    {
        var dict = new PyDict();
        idx = SkipSpace(s, idx);
        if (idx >= s.Length || s[idx] != '}')
        {
            while (true)
            {
                if (idx >= s.Length || s[idx] != '"')
                {
                    throw Error("Expecting property name enclosed in double quotes", s, idx);
                }

                var key = ScanString(s, idx + 1, out idx);
                idx = SkipSpace(s, idx);
                if (idx >= s.Length || s[idx] != ':')
                {
                    throw Error("Expecting ':' delimiter", s, idx);
                }

                idx = SkipSpace(s, idx + 1);
                dict[key] = Scan(s, idx, out idx);
                idx = SkipSpace(s, idx);
                if (idx < s.Length && s[idx] == '}')
                {
                    break;
                }

                if (idx >= s.Length || s[idx] != ',')
                {
                    throw Error("Expecting ',' delimiter", s, idx);
                }

                var comma = idx;
                idx = SkipSpace(s, idx + 1);
                if (idx < s.Length && s[idx] == '}')
                {
                    throw Error("Illegal trailing comma before end of object", s, comma);
                }
            }
        }

        next = idx + 1;
        return dict;
    }

    private static List<object?> ParseArray(int[] s, int idx, out int next)
    {
        var list = new List<object?>();
        idx = SkipSpace(s, idx);
        if (idx >= s.Length || s[idx] != ']')
        {
            while (true)
            {
                list.Add(Scan(s, idx, out idx));
                idx = SkipSpace(s, idx);
                if (idx < s.Length && s[idx] == ']')
                {
                    break;
                }

                if (idx >= s.Length || s[idx] != ',')
                {
                    throw Error("Expecting ',' delimiter", s, idx);
                }

                var comma = idx;
                idx = SkipSpace(s, idx + 1);
                if (idx < s.Length && s[idx] == ']')
                {
                    throw Error("Illegal trailing comma before end of array", s, comma);
                }
            }
        }

        next = idx + 1;
        return list;
    }
}
