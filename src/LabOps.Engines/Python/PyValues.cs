using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace LabOps.Engines.Python;

// The records are YAML read the way PyYAML 1.1 reads it, so their values are Python's:
//   None -> null, bool -> bool, int -> long (BigInteger past long), float -> double, str -> string,
//   date -> DateOnly, datetime -> PyDateTime, list -> List<object?>, dict -> PyDict.
// Keeping Python's types (rather than mapping to strings) is what lets the C# engine give the same
// answers as project.py: `started: 2026-10-01` is a date, `started: '2026-10-01'` is text and an
// error, and `human: yes` is true.

/// <summary>A Python datetime as PyYAML builds it: to the microsecond, with an offset only when the text gave one.</summary>
public readonly record struct PyDateTime(DateTime Value, TimeSpan? Offset)
{
    /// <summary>datetime.isoformat(sep): 2026-10-01T09:30:00, .ffffff when there are microseconds, +HH:MM when aware.</summary>
    public string IsoFormat(char separator = 'T')
    {
        var text = new StringBuilder(Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(separator).Append(Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        var micro = (int)(Value.Ticks % TimeSpan.TicksPerSecond / 10);
        if (micro != 0)
        {
            text.Append('.').Append(micro.ToString("D6", CultureInfo.InvariantCulture));
        }

        if (Offset is { } offset)
        {
            text.Append(offset < TimeSpan.Zero ? '-' : '+');
            var abs = offset.Duration();
            text.Append(abs.Hours.ToString("D2", CultureInfo.InvariantCulture)).Append(':')
                .Append(abs.Minutes.ToString("D2", CultureInfo.InvariantCulture));
            if (abs.Seconds != 0)
            {
                text.Append(':').Append(abs.Seconds.ToString("D2", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    public override string ToString() => IsoFormat(' ');
}

/// <summary>A Python datetime.time (what openpyxl gives for a time-only cell), to the microsecond.</summary>
public readonly record struct PyTime(TimeSpan TimeOfDay)
{
    /// <summary>str(time): HH:MM:SS, with .ffffff when there are microseconds.</summary>
    public override string ToString()
    {
        var micro = (int)(TimeOfDay.Ticks % TimeSpan.TicksPerSecond / 10);
        var text = $"{TimeOfDay.Hours:D2}:{TimeOfDay.Minutes:D2}:{TimeOfDay.Seconds:D2}";
        return micro == 0 ? text : text + "." + micro.ToString("D6", CultureInfo.InvariantCulture);
    }
}

/// <summary>A Python datetime.timedelta (what openpyxl gives for a duration cell such as [h]:mm:ss).</summary>
public readonly record struct PyTimeDelta(long Microseconds)
{
    /// <summary>str(timedelta): "2:03:04", "1 day, 2:03:04", "-1 day, 23:59:59", with .ffffff when there are microseconds.</summary>
    public override string ToString()
    {
        const long PerDay = 86_400_000_000L;
        var days = (long)Math.Floor(Microseconds / (double)PerDay);
        var rest = Microseconds - days * PerDay;
        var seconds = rest / 1_000_000;
        var micro = rest % 1_000_000;
        var text = $"{seconds / 3600}:{seconds / 60 % 60:D2}:{seconds % 60:D2}";
        if (days != 0)
        {
            text = $"{days} day{(Math.Abs(days) != 1 ? "s" : "")}, " + text;
        }

        return micro == 0 ? text : text + "." + micro.ToString("D6", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// A Python dict: insertion order, and keys compared the way Python compares them (1, 1.0 and
/// True are one key). Setting a key that is there keeps its place and its original key object.
/// </summary>
public sealed class PyDict : IEnumerable<KeyValuePair<object?, object?>>
{
    private readonly List<KeyValuePair<object?, object?>> _items = [];
    private readonly Dictionary<PyKey, int> _index = [];

    public PyDict()
    {
    }

    public PyDict(IEnumerable<KeyValuePair<object?, object?>> items)
    {
        foreach (var (key, value) in items)
        {
            this[key] = value;
        }
    }

    public int Count => _items.Count;

    /// <summary>The value, or null when the key is missing (Python's d.get(key)).</summary>
    public object? this[object? key]
    {
        get => TryGetValue(key, out var value) ? value : null;
        set
        {
            var k = new PyKey(key);
            if (_index.TryGetValue(k, out var i))
            {
                _items[i] = new(_items[i].Key, value);
            }
            else
            {
                _index[k] = _items.Count;
                _items.Add(new(key, value));
            }
        }
    }

    public IEnumerable<object?> Keys => _items.Select(i => i.Key);

    public IEnumerable<object?> Values => _items.Select(i => i.Value);

    public bool ContainsKey(object? key) => _index.ContainsKey(new PyKey(key));

    public bool TryGetValue(object? key, out object? value)
    {
        if (_index.TryGetValue(new PyKey(key), out var i))
        {
            value = _items[i].Value;
            return true;
        }

        value = null;
        return false;
    }

    public bool Remove(object? key)
    {
        if (!_index.Remove(new PyKey(key), out var i))
        {
            return false;
        }

        _items.RemoveAt(i);
        for (var j = i; j < _items.Count; j++)
        {
            _index[new PyKey(_items[j].Key)] = j;
        }

        return true;
    }

    /// <summary>A shallow copy, as dict(d) makes.</summary>
    public PyDict Copy() => new(_items);

    public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A dict key with Python's equality: numbers by value across bool, int and float.</summary>
internal readonly struct PyKey(object? value) : IEquatable<PyKey>
{
    private readonly object? _normal = Normalize(value);

    public bool Equals(PyKey other) => Equals(_normal, other._normal);

    public override bool Equals(object? obj) => obj is PyKey other && Equals(other);

    public override int GetHashCode() => _normal?.GetHashCode() ?? 0;

    private static object? Normalize(object? v) => v switch
    {
        bool b => b ? 1L : 0L,
        int i => (long)i,
        double d when double.IsFinite(d) && d == Math.Floor(d) && Math.Abs(d) < 9.2e18 => (long)d,
        double d when double.IsFinite(d) && d == Math.Floor(d) => new BigInteger(d),
        BigInteger b when b >= long.MinValue && b <= long.MaxValue => (long)b,
        _ => v,
    };
}

/// <summary>Python's text rules that matter to the engine.</summary>
public static class Py
{
    /// <summary>str.isspace for one character (Python's whitespace, which is not .NET's: it adds \x1c-\x1f).</summary>
    public static bool IsSpace(int c) => c switch
    {
        >= 0x09 and <= 0x0D => true,
        >= 0x1C and <= 0x20 => true,
        0x85 or 0xA0 or 0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        >= 0x2000 and <= 0x200A => true,
        _ => false,
    };

    /// <summary>str.strip() with no arguments.</summary>
    public static string Strip(string s)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && IsSpace(s[start]))
        {
            start++;
        }

        while (end > start && IsSpace(s[end - 1]))
        {
            end--;
        }

        return s[start..end];
    }

    /// <summary>str.rstrip(chars).</summary>
    public static string RStrip(string s, string chars)
    {
        var end = s.Length;
        while (end > 0 && chars.Contains(s[end - 1], StringComparison.Ordinal))
        {
            end--;
        }

        return s[..end];
    }

    /// <summary>A Python int from text in a base (int(text, base)); BigInteger only when it does not fit a long.</summary>
    public static object ParseInt(string digits, int radix)
    {
        BigInteger value = BigInteger.Zero;
        foreach (var c in digits)
        {
            var d = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'z' => c - 'a' + 10,
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => throw new FormatException($"invalid digit {c}"),
            };
            if (d >= radix)
            {
                throw new FormatException($"invalid digit {c} for base {radix}");
            }

            value = value * radix + d;
        }

        return Shrink(value);
    }

    /// <summary>A long when it fits, else the BigInteger.</summary>
    public static object Shrink(BigInteger value) =>
        value >= long.MinValue && value <= long.MaxValue ? (object)(long)value : value;

    /// <summary>repr(float): the shortest text that reads back, in Python's exponent style (1e+20, 1e-05, 0.1, 1.0).</summary>
    public static string FloatRepr(double d)
    {
        if (double.IsNaN(d))
        {
            return "nan";
        }

        if (double.IsInfinity(d))
        {
            return d > 0 ? "inf" : "-inf";
        }

        if (d == 0)
        {
            return double.IsNegative(d) ? "-0.0" : "0.0";
        }

        // "R" gives the shortest round-trip digits; take them apart and lay them out as Python's
        // float_repr_style 'short' does: scientific when the exponent is below -4 or at least 16.
        var shortest = d.ToString("R", CultureInfo.InvariantCulture);
        var negative = shortest.StartsWith('-');
        if (negative)
        {
            shortest = shortest[1..];
        }

        string mantissa;
        int exponent;
        var e = shortest.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            mantissa = shortest[..e];
            exponent = int.Parse(shortest[(e + 1)..], CultureInfo.InvariantCulture);
        }
        else
        {
            mantissa = shortest;
            exponent = 0;
        }

        // Digits without the point, and the decimal exponent of the first digit.
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa[..point] + mantissa[(point + 1)..];
        var intLength = point < 0 ? mantissa.Length : point;
        var lead = digits.Length - digits.TrimStart('0').Length;
        digits = digits.TrimStart('0');
        var decimalExponent = exponent + intLength - lead - 1;  // position of the first significant digit
        digits = digits.TrimEnd('0');
        if (digits.Length == 0)
        {
            digits = "0";
        }

        string body;
        if (decimalExponent < -4 || decimalExponent >= 16)
        {
            body = digits[..1] + (digits.Length > 1 ? "." + digits[1..] : "") + "e"
                + (decimalExponent < 0 ? "-" : "+") + Math.Abs(decimalExponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        else if (decimalExponent < 0)
        {
            body = "0." + new string('0', -decimalExponent - 1) + digits;
        }
        else if (digits.Length <= decimalExponent + 1)
        {
            body = digits + new string('0', decimalExponent + 1 - digits.Length) + ".0";
        }
        else
        {
            body = digits[..(decimalExponent + 1)] + "." + digits[(decimalExponent + 1)..];
        }

        return negative ? "-" + body : body;
    }
}
