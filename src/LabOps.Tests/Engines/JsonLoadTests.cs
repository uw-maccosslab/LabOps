using System.Text.Json;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines;

/// <summary>PyJsonDecoder against Python's json.loads: the same values, and the same messages for what it refuses.</summary>
public sealed class JsonLoadTests
{
    [Fact]
    public void Json_reads_as_Python_reads_it()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Golden.Table("json-load.json").GetProperty("cases").EnumerateArray())
        {
            var text = c.GetProperty("text").GetString()!;
            object? value = null;
            string? error = null;
            try
            {
                value = PyJsonDecoder.Loads(text);
            }
            catch (PyJsonException ex)
            {
                error = ex.Message;
            }

            var shown = JsonSerializer.Serialize(text);
            if (c.TryGetProperty("error", out var expected))
            {
                mismatches.Check(error == expected.GetString(), () => $"{shown}\n  python: {expected.GetString()}\n  c#:     {error ?? Golden.Show(value)}");
            }
            else
            {
                var want = Golden.Value(c.GetProperty("value"));
                mismatches.Check(error is null && Golden.Same(value, want), () => $"{shown}\n  python: {Golden.Show(want)}\n  c#:     {error ?? Golden.Show(value)}");
            }
        }

        mismatches.Checked.ShouldBeGreaterThan(50);
        mismatches.ShouldBeNone();
    }
}
