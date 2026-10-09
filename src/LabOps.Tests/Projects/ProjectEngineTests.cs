using System.Text.Json;
using LabOps.Core.Engines;
using LabOps.Core.Projects;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Projects;

/// <summary>
/// The JSON contract with the project engine. The fixtures were recorded from project.py, which
/// the C# engine answers the same way; when the engine changes what it prints, re-record them and
/// these tests show what the app would misread.
/// </summary>
public sealed class ProjectEngineTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Real_list_output_is_read_into_labs_projects_and_experiments()
    {
        using var doc = ProjectEngine.Answer(Fixture("project-list.json"));
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
        using var doc = ProjectEngine.Answer("""{"ok": true, "projects": [], "problems": []}""");

        Should.Throw<EngineException>(() => ProjectEngine.ReadList(doc.RootElement)).Message.ShouldContain("older than this app");
    }

    [Fact]
    public void A_refused_check_returns_its_problems_instead_of_throwing()
    {
        using var doc = ProjectEngine.Answer(Fixture("project-check-staged.json"), allowNotOk: true);
        var problems = doc.RootElement.GetProperty("problems").Deserialize<List<ProjectIssue>>(EngineJson.Options)!;

        problems.Single().IsError.ShouldBeTrue();
        problems.Single().Message.ShouldContain("[Owner Name]");
    }

    [Fact]
    public void A_scan_lists_columns_and_findings_without_values()
    {
        var json = Fixture("project-scan.json");
        using var doc = ProjectEngine.Answer(json);
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
        var ex = Should.Throw<EngineException>(() => ProjectEngine.Answer(
            """{"ok": false, "error": "sample prep has started on the current layout"}"""));

        ex.Message.ShouldBe("sample prep has started on the current layout");
    }

    [Fact]
    public void An_answer_that_is_not_json_reports_what_went_wrong()
    {
        var ex = Should.Throw<EngineException>(() => ProjectEngine.Answer("not json"));

        ex.Message.ShouldStartWith("The project engine gave an answer LabOps could not read");
    }

    private const string ActiveList = """{"ok": true, "labs": [], "people": [], "closed_hidden": 12, "problems": []}""";
    private const string FullList = """{"ok": true, "labs": [], "people": [], "closed_hidden": 0, "problems": []}""";

    [Fact]
    public async Task The_list_leaves_out_closed_projects_unless_asked_for_them()
    {
        using var engine = new FakeEngine(_ => ActiveList);

        (await engine.Engine.ListAsync()).ClosedHidden.ShouldBe(12);
        engine.Calls.Single().ShouldBe("list --active");

        engine.Respond = _ => FullList;
        (await engine.Engine.ListAsync(includeClosed: true)).ClosedHidden.ShouldBe(0);
        engine.Calls[1].ShouldBe("list");
    }

    /// <summary>The Plan dialog's answer is the whole plan, written with one command so it is never half changed.</summary>
    [Fact]
    public async Task A_plan_is_one_command_whatever_it_changes()
    {
        using var engine = new FakeEngine(_ => """{"ok": true}""");

        await engine.Engine.PlanAsync("Proj", ["sample_prep"], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 13));
        await engine.Engine.PlanAsync("Proj", ["sample_prep"], null, new DateOnly(2026, 11, 13));
        await engine.Engine.PlanAsync("Proj", ["sample_prep"], new DateOnly(2026, 11, 2), null);
        await engine.Engine.PlanAsync("Proj", ["sample_prep"], null, null);

        engine.Calls.ShouldBe(
        [
            "plan Proj sample_prep --start 2026-11-02 --finish 2026-11-13",
            "plan Proj sample_prep --no-start --finish 2026-11-13",
            "plan Proj sample_prep --start 2026-11-02 --no-finish",
            "plan Proj sample_prep --clear",
        ]);
    }

    /// <summary>
    /// The app's calls run the C# engine in-process: each builds project.py's command line, and the
    /// answer comes back as the same JSON. No Python and no process.
    /// </summary>
    [Fact]
    public async Task The_app_changes_records_with_the_built_in_engine()
    {
        using var dir = new TempDirectory();
        void Write(string rel, string text)
        {
            var path = dir.Combine(rel.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Write("config/people.yaml", "people:\n  - {login: maccoss, name: Michael MacCoss, role: PI}\n");
        Write("projects/Lab-A/lab.yaml", "lab: Lab-A\ntitle: A lab\nstatus: active\n");
        Write("projects/Lab-A/Proj/project.yaml",
            "project: Proj\ntitle: A project\nstatus: active\nfunding: {type: internal}\nnotebooks: []\n"
            + "steps:\n  - {id: samples_received, kind: samples_received, status: pending}\n  - {id: sample_prep, kind: sample_prep, status: pending}\n");
        Write("projects/Lab-A/Proj/2026-10-Proj-DIA/experiment.yaml",
            "experiment: 2026-10-Proj-DIA\ntitle: DIA\nstatus: active\npanorama: []\nsteps:\n  - {id: data_acquisition, kind: data_acquisition, status: pending}\n");
        var engine = new ProjectEngine { RepositoryPath = dir.Path };

        await engine.StageAsync("Proj", "samples_received", StageAction.Done, new DateOnly(2026, 10, 1), "maccoss", "92 tubes: on dry ice");
        await engine.AssignAsync("Proj", ["sample_prep"], "maccoss");
        await engine.AddStepAsync("Proj", "other", "Second shipment", "samples_received");
        await engine.LinkPanoramaAsync("2026-10-Proj-DIA", "https://panoramaweb.org/MacCoss/X/Raw/project-begin.view", "raw");
        await engine.LinkNotebookAsync("Proj", null, "ELN-4485-20230314-179");
        await engine.RemoveStepAsync("Proj", "second_shipment");

        File.ReadAllText(dir.Combine("projects", "Lab-A", "Proj", "project.yaml")).ShouldBe(
            "project: Proj\ntitle: A project\nstatus: active\nfunding: {type: internal}\n"
            + "notebooks:\n  - {id: ELN-4485-20230314-179, url: 'https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/179'}\n"
            + "steps:\n  - {id: samples_received, kind: samples_received, status: done, started: 2026-10-01, finished: 2026-10-01, by: maccoss, note: '92 tubes: on dry ice'}\n"
            + "  - {id: sample_prep, kind: sample_prep, status: pending, assigned: maccoss}\n");
        var project = (await engine.ListAsync()).Labs.Single().Projects.Single();
        project.CurrentStage.ShouldBe("sample_prep");
        project.Experiments.Single().Panorama.Single().ShouldBe(new PanoramaFolder("/MacCoss/X/Raw", "raw"));

        // A refusal comes back as the engine's message, as project.py's {"ok": false, "error"} did.
        (await Should.ThrowAsync<EngineException>(() => engine.RemoveStepAsync("Proj", "samples_received")))
            .Message.ShouldBe("step samples_received has been started or has a record; skip it instead of removing it");
    }

    /// <summary>A ProjectEngine whose commands are answered by a function, recording each command line.</summary>
    private sealed class FakeEngine : IDisposable
    {
        private readonly string _folder = Directory.CreateTempSubdirectory("labops-engine-").FullName;

        public FakeEngine(Func<string, string> respond)
        {
            Respond = respond;
            Engine = new ProjectEngine
            {
                RepositoryPath = _folder,
                Commands = (_, args) =>
                {
                    var line = string.Join(' ', args);
                    Calls.Add(line);
                    return Respond(line);
                },
            };
        }

        public ProjectEngine Engine { get; }

        public Func<string, string> Respond { get; set; }

        public List<string> Calls { get; } = [];

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }

    /// <summary>
    /// Runs the engine (in-process, the C# one) against a clone of LabOps-Projects. Opt-in: set
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

        var engine = new ProjectEngine { RepositoryPath = repo };
        var list = await engine.ListAsync();

        list.Labs.ShouldNotBeEmpty();
        list.Problems.ShouldNotContain(p => p.IsError);
        (await engine.CheckStagedAsync(CancellationToken.None)).ShouldNotContain(p => p.IsError);
        (await engine.WikiAsync(list.Labs.SelectMany(l => l.Projects).First().Project)).Html.ShouldContain("labops-wiki");
    }
}
