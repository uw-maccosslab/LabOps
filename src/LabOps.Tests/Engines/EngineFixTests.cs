using System.Text;
using LabOps.Engines.Projects;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Engines;

/// <summary>
/// Where project.py stopped with a Python error on a loosely written record, the C# engine reports
/// a problem to fix and carries on. These are the deliberate differences from the Python engine.
/// </summary>
public sealed class EngineFixTests
{
    private static ProjectsEngine Repository(TempDirectory dir, params (string Path, string Text)[] files)
    {
        void Write(string rel, byte[] data)
        {
            var path = dir.Combine(rel.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        Write("config/people.yaml", Encoding.UTF8.GetBytes("people:\n  - {login: maccoss, name: Michael MacCoss}\n"));
        Write("projects/Lab-A/lab.yaml", Encoding.UTF8.GetBytes("lab: Lab-A\ntitle: A lab\nstatus: active\n"));
        foreach (var (rel, text) in files)
        {
            Write(rel, Encoding.UTF8.GetBytes(text));
        }

        return new ProjectsEngine(new ProjectRepository(dir.Path) { Today = () => new DateOnly(2026, 10, 8) });
    }

    private const string Project = "project: P\ntitle: P\nstatus: active\nfunding: {type: internal}\n";

    [Fact]
    public void A_number_as_a_key_in_wiki_yaml_is_a_warning()
    {
        using var dir = new TempDirectory();
        var engine = Repository(dir, ("projects/Lab-A/P/project.yaml", Project + "steps:\n  - {id: a, kind: other, label: A}\n"),
            ("projects/Lab-A/P/wiki.yaml", "summary: S\n7: a number\nzeta: text\n"));
        var problems = engine.Check().Payload["problems"]!.AsArray().Select(p => p!["message"]!.GetValue<string>()).ToList();
        problems.ShouldContain(m => m.Contains("`7` is not shown on the page", StringComparison.Ordinal));
        problems.ShouldContain(m => m.Contains("`zeta` is not shown on the page", StringComparison.Ordinal));
    }

    [Fact]
    public void A_step_finished_as_a_datetime_and_started_as_a_date_is_compared()
    {
        using var dir = new TempDirectory();
        var engine = Repository(dir, ("projects/Lab-A/P/project.yaml",
            Project + "steps:\n  - {id: a, kind: other, label: A, status: done, started: 2026-10-05, finished: 2026-10-01 09:30:00}\n"));
        engine.Check().Lines.ShouldContain("ERROR: P: step a: finished before it started");
    }

    [Fact]
    public void A_status_nobody_knows_shows_on_the_wiki_page_as_written()
    {
        using var dir = new TempDirectory();
        var engine = Repository(dir, ("projects/Lab-A/P/project.yaml", Project + "steps:\n  - {id: a, kind: other, label: A, status: waiting}\n"));
        engine.Wiki("P").Payload["html"]!.GetValue<string>().ShouldContain(">waiting</span>");
    }

    [Fact]
    public void A_record_that_is_not_UTF8_is_a_problem_not_a_crash()
    {
        using var dir = new TempDirectory();
        var engine = Repository(dir);
        var path = dir.Combine("projects", "Lab-A", "P", "project.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes("project: P\ntitle: Caf"), 0xE9, (byte)'\n']);
        var project = engine.List().Payload["labs"]![0]!["projects"]![0]!;
        project["issues"]![0]!["message"]!.GetValue<string>().ShouldBe("project.yaml is not UTF-8 text; save it as UTF-8 and try again");
    }

    [Fact]
    public void A_kind_written_as_a_list_is_an_unknown_kind()
    {
        using var dir = new TempDirectory();
        var engine = Repository(dir, ("projects/Lab-A/P/project.yaml", Project + "steps:\n  - {id: a, kind: [other], label: A}\n"));
        engine.Check().Lines.ShouldContain(l => l.StartsWith("ERROR: P: step a: kind must be one of", StringComparison.Ordinal));
    }
}
