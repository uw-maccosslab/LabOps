using System.Text.Json;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;

namespace LabOps.Tests.Engines;

/// <summary>
/// YamlEmitter.Scalar against project.py's yaml_scalar: every value a command writes into a
/// record goes through it, so a difference would rewrite lines nobody changed. The table has
/// curated edge cases (every YAML 1.1 boolean and null spelling, numbers, indicators, quotes,
/// control and Unicode characters), 3,000 random strings, typed values, and long values that wrap.
/// </summary>
public sealed class YamlScalarTests
{
    [Fact]
    public void Every_value_is_written_as_the_Python_engine_wrote_it()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Golden.Table("yaml-scalar.json").GetProperty("cases").EnumerateArray())
        {
            var value = Golden.Value(c.GetProperty("value"));
            foreach (var context in (string[])["top", "map", "seq", "key"])
            {
                if (!c.TryGetProperty(context, out var expected))
                {
                    continue;
                }

                object? wrapped = context switch
                {
                    "map" => new PyDict { ["k"] = value },
                    "seq" => new List<object?> { value },
                    "key" => new PyDict { [value] = 1L },
                    _ => value,
                };
                string actual;
                try
                {
                    actual = YamlEmitter.Scalar(wrapped);
                }
                catch (Exception ex)
                {
                    actual = $"<{ex.GetType().Name}: {ex.Message}>";
                }

                mismatches.Check(actual == expected.GetString(),
                    () => $"{context} {Golden.Show(value)}\n  python: {JsonSerializer.Serialize(expected.GetString())}\n  c#:     {JsonSerializer.Serialize(actual)}");
            }
        }

        mismatches.Checked.ShouldBeGreaterThan(10_000);
        mismatches.ShouldBeNone();
    }

    [Theory]
    [InlineData(1e20, "1e+20")]
    [InlineData(1e-5, "1e-05")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.0, "1.0")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(123456789.123, "123456789.123")]
    [InlineData(-2.5e-10, "-2.5e-10")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    public void Floats_are_written_as_Python_repr_writes_them(double value, string repr) => Py.FloatRepr(value).ShouldBe(repr);
}
