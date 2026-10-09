using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines;

/// <summary>
/// The golden tables: what LabOps-Projects' Python engine did, recorded by
/// tools/engine-golden/make_golden.py. Values carry their Python type ({"t": "date", "v": ...}),
/// since JSON alone cannot tell a date from text.
/// </summary>
internal static class Golden
{
    public static JsonElement Table(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "engine-golden", name);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    /// <summary>A tagged value as the C# engine holds it.</summary>
    public static object? Value(JsonElement tagged)
    {
        var v = tagged.TryGetProperty("v", out var x) ? x : default;
        return tagged.GetProperty("t").GetString() switch
        {
            "null" => null,
            "bool" => v.GetBoolean(),
            "int" => Py.Shrink(BigInteger.Parse(v.GetString()!, CultureInfo.InvariantCulture)),
            "float" => v.GetString() switch
            {
                "inf" => double.PositiveInfinity,
                "-inf" => double.NegativeInfinity,
                "nan" => double.NaN,
                var text => double.Parse(text!, CultureInfo.InvariantCulture),
            },
            "str" => Text(v),
            "date" => DateOnly.ParseExact(v.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            "datetime" => new PyDateTime(
                DateTime.ParseExact(v.GetString()!, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.ffffff"], CultureInfo.InvariantCulture, DateTimeStyles.None),
                tagged.GetProperty("offset").ValueKind == JsonValueKind.Null ? null : TimeSpan.FromSeconds(tagged.GetProperty("offset").GetInt32())),
            "list" => v.EnumerateArray().Select(Value).ToList(),
            "dict" => new PyDict(v.EnumerateArray().Select(p => new KeyValuePair<object?, object?>(Value(p[0]), Value(p[1])))),
            var t => throw new InvalidOperationException($"unknown tag {t}"),
        };
    }

    /// <summary>A JSON string's text, keeping a lone surrogate (which Python strings can hold and GetString refuses).</summary>
    private static string Text(JsonElement v)
    {
        try
        {
            return v.GetString()!;
        }
        catch (InvalidOperationException)
        {
            var raw = v.GetRawText();
            var text = new StringBuilder();
            for (var i = 1; i < raw.Length - 1; i++)
            {
                if (raw[i] != '\\')
                {
                    text.Append(raw[i]);
                    continue;
                }

                var e = raw[++i];
                switch (e)
                {
                    case 'u':
                        text.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    case 'n':
                        text.Append('\n');
                        break;
                    case 't':
                        text.Append('\t');
                        break;
                    case 'r':
                        text.Append('\r');
                        break;
                    case 'b':
                        text.Append('\b');
                        break;
                    case 'f':
                        text.Append('\f');
                        break;
                    default:
                        text.Append(e);
                        break;
                }
            }

            return text.ToString();
        }
    }

    /// <summary>The same type and value, with dict keys in the same order (NaN equals NaN here).</summary>
    public static bool Same(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (double x, double y) => x.Equals(y),
        (PyDict x, PyDict y) => x.Count == y.Count && x.Zip(y).All(p => Same(p.First.Key, p.Second.Key) && Same(p.First.Value, p.Second.Value)),
        (List<object?> x, List<object?> y) => x.Count == y.Count && x.Zip(y).All(p => Same(p.First, p.Second)),
        _ => a is not null && b is not null && a.GetType() == b.GetType() && a.Equals(b),
    };

    /// <summary>A value as Python would write it, for failure messages.</summary>
    public static string Show(object? v) => v switch
    {
        null => "None",
        string s => JsonSerializer.Serialize(s),
        PyDict d => "{" + string.Join(", ", d.Select(p => Show(p.Key) + ": " + Show(p.Value))) + "}",
        List<object?> l => "[" + string.Join(", ", l.Select(Show)) + "]",
        double d => Py.FloatRepr(d),
        _ => $"{v} ({v.GetType().Name})",
    };

    /// <summary>Collects mismatches and fails once, listing the first few, so one bad rule shows as one failure.</summary>
    public sealed class Mismatches
    {
        private readonly List<string> _found = [];

        public int Checked { get; private set; }

        public void Check(bool ok, Func<string> describe)
        {
            Checked++;
            if (!ok)
            {
                _found.Add(describe());
            }
        }

        public void ShouldBeNone()
        {
            if (_found.Count == 0)
            {
                return;
            }

            var text = new StringBuilder($"{_found.Count} of {Checked} cases differ from the Python engine; the first ones:\n");
            foreach (var f in _found.Take(15))
            {
                text.AppendLine(f).AppendLine();
            }

            throw new Xunit.Sdk.XunitException(text.ToString());
        }
    }
}
