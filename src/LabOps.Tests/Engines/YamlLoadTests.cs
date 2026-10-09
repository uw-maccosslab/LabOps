using System.Text.Json;
using LabOps.Engines.Projects;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines;

/// <summary>
/// Records.Parse against project.py's parse_record, which reads YAML as PyYAML 1.1 does: `yes` is
/// true, `012` is 10, `2026-10-01` a date and `'2026-10-01'` text, the last duplicate key wins,
/// and merge keys work. YamlDotNet's own messages differ from libyaml's, so a problem is compared
/// by its presence, not its wording.
/// </summary>
public sealed class YamlLoadTests
{
    [Fact]
    public void Records_read_as_the_Python_engine_read_them()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Golden.Table("yaml-load.json").GetProperty("cases").EnumerateArray())
        {
            var text = c.GetProperty("text").GetString()!;
            if (c.TryGetProperty("error", out var error) && error.GetString() == "TypeError")
            {
                // !!binary, !!set and !!omap, which the table could not record; the C# engine
                // refuses them, and no record uses them.
                continue;
            }

            var (record, problem) = Records.Parse(text, "x.yaml");
            var shown = JsonSerializer.Serialize(text);
            if (c.TryGetProperty("value", out var expected))
            {
                var want = Golden.Value(expected);
                mismatches.Check(problem is null && Golden.Same(record, want),
                    () => $"{shown}\n  python: {Golden.Show(want)}\n  c#:     {problem ?? Golden.Show(record)}");
            }
            else if (c.TryGetProperty("problem", out var pythonProblem))
            {
                var mapping = pythonProblem.GetString()!.Contains("should be a mapping", StringComparison.Ordinal);
                mismatches.Check(problem is not null && problem.Contains("should be a mapping", StringComparison.Ordinal) == mapping,
                    () => $"{shown}\n  python: {pythonProblem.GetString()}\n  c#:     {problem ?? Golden.Show(record)}");
            }
            else
            {
                // Python crashed (ValueError: an impossible date or time). The C# engine reports
                // the record as one to fix instead: a deliberate difference.
                mismatches.Check(problem is not null && problem.Contains("not valid YAML", StringComparison.Ordinal),
                    () => $"{shown}\n  python: crashed with {error.GetString()}\n  c#:     {problem ?? Golden.Show(record)}");
            }
        }

        mismatches.ShouldBeNone();
    }

    [Fact]
    public void Problems_are_worded_as_libyaml_words_them()
    {
        var mismatches = new Golden.Mismatches();
        foreach (var c in Golden.Table("yaml-load.json").GetProperty("cases").EnumerateArray())
        {
            if (!c.TryGetProperty("problem", out var expected))
            {
                continue;
            }

            var text = c.GetProperty("text").GetString()!;
            var (_, problem) = Records.Parse(text, "x.yaml");
            mismatches.Check(problem == expected.GetString(),
                () => $"{JsonSerializer.Serialize(text)}\n  python: {expected.GetString()}\n  c#:     {problem}");
        }

        mismatches.ShouldBeNone();
    }

    [Fact]
    public void An_impossible_date_is_a_problem_with_the_record_not_a_crash()
    {
        var (_, problem) = Records.Parse("steps:\n  - {id: a, started: 2026-02-30}\n", "experiment.yaml");
        problem.ShouldBe("experiment.yaml is not valid YAML: 2026-02-30 is not a real date (line 2)");
    }

    [Fact]
    public void Keys_compare_as_Python_compares_them()
    {
        var (record, _) = Records.Parse("1: a\n1.0: b\ntrue: c\n", "x.yaml");
        record.Count.ShouldBe(1);
        record[1L].ShouldBe("c");
        record.Keys.Single().ShouldBe(1L);
        record[true].ShouldBe("c");
    }

    [Fact]
    public void Dates_quoted_dates_and_yes_are_what_PyYAML_makes_them()
    {
        var (record, _) = Records.Parse("a: 2026-10-01\nb: '2026-10-01'\nc: yes\nd: y\ne: 012\nf: 2026-1-5\n", "x.yaml");
        record["a"].ShouldBe(new DateOnly(2026, 10, 1));
        record["b"].ShouldBe("2026-10-01");
        record["c"].ShouldBe(true);
        record["d"].ShouldBe("y");
        record["e"].ShouldBe(10L);
        record["f"].ShouldBe("2026-1-5");
        record["missing"].ShouldBeNull();
        record.ShouldBeOfType<PyDict>();
    }
}
