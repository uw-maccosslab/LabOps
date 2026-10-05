using System.Text.Json;
using LabOps.Core.Engines;
using LabOps.Core.Processes;
using LabOps.Core.Projects;

namespace LabOps.Tests.Projects;

/// <summary>
/// The JSON contract with project.py. The fixtures were recorded from the real engine (see
/// LabOps-Projects); when project.py changes what it prints, re-record them and these tests show
/// what the app would misread.
/// </summary>
public sealed class ProjectEngineTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Real_list_output_is_read_into_labs_projects_and_experiments()
    {
        using var doc = ProjectEngine.Parse(new ProcessResult(0, Fixture("project-list.json"), ""));
        var list = ProjectEngine.ReadList(doc.RootElement);

        list.Problems.ShouldBeEmpty();
        list.People.ShouldBe([new Person("maccoss", "Michael MacCoss", "PI")]);
        var zoo = list.Labs.Single(l => l.Lab == "ClearwaterZoo-Cole");
        zoo.Pi.ShouldBe("Ella Cole, Ph.D.");
        zoo.Institution.ShouldBe("Clearwater Zoo and Botanical Garden");

        // The project: funding, the samples and their steps.
        var marten = zoo.Projects.Single();
        marten.Project.ShouldBe("Marten-Plasma");
        marten.Lab.ShouldBe("ClearwaterZoo-Cole");
        marten.Folder.ShouldBe("projects/ClearwaterZoo-Cole/Marten-Plasma");
        marten.Funding.Type.ShouldBe("quote");
        marten.Funding.Quotes.ShouldBe(["MacCoss-2026-CWZG-MARTEN"]);
        marten.Funding.Text.ShouldBe("quote MacCoss-2026-CWZG-MARTEN");
        marten.ExpectedSamples.ShouldBe(10);
        marten.Species.ShouldBe("American marten");
        marten.Notebooks.Single().Id.ShouldBe("ELN-4485-20230314-179");
        marten.Layout.ShouldNotBeNull().Plates.ShouldBe(1);
        marten.Files.ShouldBe(new ProjectFiles(true, true));
        marten.CurrentStage.ShouldBe("sample_prep");
        marten.Stages.Select(s => s.Stage).ShouldBe(["samples_received", "metadata_organized", "plate_layout", "sample_prep"]);
        marten.Stages[0].ShouldBe(new StageEntry
        {
            Stage = "samples_received", Kind = "samples_received", Label = "Samples received", Status = "done",
            Started = "2026-10-01", Finished = "2026-10-01", By = "maccoss", Note = "10 tubes on dry ice",
        });
        // Starting a step nobody had assigns it to whoever started it.
        marten.Stages[3].IsInProgress.ShouldBeTrue();
        marten.Stages[3].Assigned.ShouldBe("maccoss");
        marten.Progress.ShouldBe("Samples: Sample prep");
        marten.Assigned.ShouldBe("maccoss");

        // Its experiment: instrument, Panorama, the measurement steps, the project's funding.
        var dia = marten.Experiments.Single();
        dia.Experiment.ShouldBe("2026-10-Marten-DIA");
        dia.Folder.ShouldBe("projects/ClearwaterZoo-Cole/Marten-Plasma/2026-10-Marten-DIA");
        dia.Project.ShouldBe("Marten-Plasma");
        dia.Instrument.ShouldBe("Orbitrap Astral");
        dia.Panorama.Single().ShouldBe(new PanoramaFolder("/MacCoss/maccoss/2026-Marten", "raw"));
        dia.FundingInherited.ShouldBeTrue();
        dia.Funding.Text.ShouldBe("quote MacCoss-2026-CWZG-MARTEN");
        dia.CurrentStage.ShouldBe("data_acquisition");
        dia.Current.ShouldNotBeNull().Assigned.ShouldBe("maccoss");
        dia.Stages.Select(s => s.DisplayLabel).ShouldBe(
            ["Data acquisition", "Data deposited to Panorama", "Signal processing", "Data analysis", "Results returned"]);

        // The lab's own project: samples not yet in, then DIA, then PRM on a grant after unblinding.
        var gradient = list.Labs.Single(l => l.Lab == "UW-MacCoss").Projects.Single();
        gradient.Human.ShouldBeTrue();
        gradient.Funding.Text.ShouldBe("internal");
        gradient.CurrentStage.ShouldBe("samples_received");
        gradient.Layout.ShouldBeNull();
        gradient.Assigned.ShouldBeNull();
        var prm = gradient.Experiments.Single(e => e.Experiment == "2026-12-Gradient-PRM");
        prm.FundingInherited.ShouldBeFalse();
        prm.Funding.Text.ShouldBe("grant R01 GM000000");
        prm.Stages.Take(2).Select(s => (s.Kind, s.DisplayLabel)).ShouldBe(
            [("metadata_organized", "Unblinded metadata"), ("assay_development", "Assay development")]);
    }

    [Fact]
    public void A_repository_from_before_labs_asks_for_a_sync_instead_of_showing_nothing()
    {
        using var doc = ProjectEngine.Parse(new ProcessResult(0, """{"ok": true, "projects": [], "problems": []}""", ""));

        Should.Throw<EngineException>(() => ProjectEngine.ReadList(doc.RootElement)).Message.ShouldContain("older than this app");
    }

    [Fact]
    public void A_refused_check_returns_its_problems_instead_of_throwing()
    {
        using var doc = ProjectEngine.Parse(new ProcessResult(1, Fixture("project-check-staged.json"), ""), allowNotOk: true);
        var problems = doc.RootElement.GetProperty("problems").Deserialize<List<ProjectIssue>>(EngineJson.Options)!;

        problems.Single().IsError.ShouldBeTrue();
        problems.Single().Message.ShouldContain("[Owner Name]");
    }

    [Fact]
    public void A_scan_lists_columns_and_findings_without_values()
    {
        var json = Fixture("project-scan.json");
        using var doc = ProjectEngine.Parse(new ProcessResult(0, json, ""));
        var scan = doc.RootElement.Deserialize<ScanResult>(EngineJson.Options)!;

        scan.Errors.ShouldBe(1);
        scan.Sheets.Single().Columns.ShouldBe(["Sample_ID", "Patient Name", "Collection date", "Notes"]);
        // Collection dates are kept (LabOps-Projects engine 26.2.0); a name is still an error.
        scan.Findings.Where(f => f.IsError).Select(f => f.Column).ShouldBe(["Patient Name"]);
        scan.Warnings.Single().Column.ShouldBe("Notes");
        json.ShouldNotContain("Pat Doe");
    }

    [Fact]
    public void An_engine_error_becomes_a_message_for_the_user()
    {
        var ex = Should.Throw<EngineException>(() => ProjectEngine.Parse(new ProcessResult(1,
            """{"ok": false, "error": "sample prep has started on the current layout"}""", "")));

        ex.Message.ShouldBe("sample prep has started on the current layout");
    }

    [Fact]
    public void Output_that_is_not_json_reports_what_went_wrong()
    {
        var ex = Should.Throw<EngineException>(() => ProjectEngine.Parse(new ProcessResult(2, "", "error: Failed to download Python")));

        ex.Message.ShouldBe("The project engine could not run: error: Failed to download Python");
    }

    private const string ActiveList = """{"ok": true, "labs": [], "people": [], "closed_hidden": 12, "problems": []}""";
    private const string FullList = """{"ok": true, "labs": [], "people": [], "closed_hidden": 0, "problems": []}""";

    [Fact]
    public async Task The_list_leaves_out_closed_projects_unless_asked_for_them()
    {
        using var engine = new FakeEngine(_ => new ProcessResult(0, ActiveList, ""));

        (await engine.Engine.ListAsync()).ClosedHidden.ShouldBe(12);
        engine.Calls.Single().ShouldEndWith("--json list --active");

        engine.Respond = _ => new ProcessResult(0, FullList, "");
        (await engine.Engine.ListAsync(includeClosed: true)).ClosedHidden.ShouldBe(0);
        engine.Calls[1].ShouldEndWith("--json list");
    }

    [Fact]
    public async Task An_engine_without_list_active_lists_everything_instead()
    {
        // What argparse prints for an option a 26.2.0 engine does not have.
        using var engine = new FakeEngine(args => args.EndsWith("--active", StringComparison.Ordinal)
            ? new ProcessResult(2, "", "usage: project.py [-h] [--json] [--version] ...\nproject.py: error: unrecognized arguments: --active")
            : new ProcessResult(0, """{"ok": true, "labs": [], "people": [], "problems": []}""", ""));

        var list = await engine.Engine.ListAsync();

        list.ClosedHidden.ShouldBe(0);
        engine.Calls.Select(c => c[c.IndexOf("list", StringComparison.Ordinal)..]).ShouldBe(["list --active", "list"]);
    }

    [Fact]
    public async Task Other_engine_errors_are_not_retried()
    {
        using var engine = new FakeEngine(_ => new ProcessResult(2, "", "error: Failed to download Python"));

        (await Should.ThrowAsync<EngineException>(() => engine.Engine.ListAsync())).Message.ShouldContain("Failed to download Python");
        engine.Calls.Count.ShouldBe(1);
    }

    /// <summary>A ProjectEngine whose process runner answers from a function and records each command.</summary>
    private sealed class FakeEngine : IProcessRunner, IDisposable
    {
        private readonly string _folder = Directory.CreateTempSubdirectory("labops-engine-").FullName;

        public FakeEngine(Func<string, ProcessResult> respond)
        {
            Respond = respond;
            Directory.CreateDirectory(Path.Combine(_folder, "tools"));
            File.WriteAllText(Path.Combine(_folder, "tools", "uv.exe"), "");
            Engine = new ProjectEngine(this, new ToolLocator(_folder)) { RepositoryPath = _folder };
        }

        public ProjectEngine Engine { get; }

        public Func<string, ProcessResult> Respond { get; set; }

        public List<string> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environment = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var args = string.Join(' ', arguments);
            Calls.Add(args);
            return Task.FromResult(Respond(args));
        }

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }

    /// <summary>
    /// Runs the real project.py through uv against a clone of LabOps-Projects. Opt-in: set
    /// LAB_PROJECTS_REPO to the clone's path.
    /// </summary>
    [Fact]
    public async Task Real_engine_lists_and_checks_the_repository()
    {
        var repo = Environment.GetEnvironmentVariable("LAB_PROJECTS_REPO");
        if (string.IsNullOrWhiteSpace(repo))
        {
            Assert.Skip("Set LAB_PROJECTS_REPO to a clone of LabOps-Projects to run this.");
        }

        var tools = new ToolLocator();
        if (tools.Find(Tool.Uv) is null)
        {
            Assert.Skip("uv is not installed.");
        }

        var engine = new ProjectEngine(new ProcessRunner(tools), tools) { RepositoryPath = repo };
        var list = await engine.ListAsync();

        list.Problems.ShouldNotContain(p => p.IsError);
        (await engine.CheckStagedAsync(CancellationToken.None)).ShouldNotContain(p => p.IsError);
    }
}
