using System.Text.Json;
using ChargeState.Core.Engines;
using ChargeState.Core.Processes;
using ChargeState.Core.Projects;

namespace ChargeState.Tests.Projects;

/// <summary>
/// The JSON contract with project.py. The fixtures were recorded from the real engine (see
/// lab-projects); when project.py changes what it prints, re-record them and these tests show
/// what the app would misread.
/// </summary>
public sealed class ProjectEngineTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Real_list_output_is_read_into_projects_and_experiments()
    {
        using var doc = ProjectEngine.Parse(new ProcessResult(0, Fixture("project-list.json"), ""));
        var list = ProjectEngine.ReadList(doc.RootElement);

        list.Problems.ShouldBeEmpty();
        var zoo = list.Projects.Single(p => p.Group == "ClearwaterZoo-Cole");
        zoo.Pi.ShouldBe("Ella Cole, Ph.D.");
        zoo.Institution.ShouldBe("Clearwater Zoo and Botanical Garden");

        var marten = zoo.Experiments.Single();
        marten.Experiment.ShouldBe("2026-10-Marten-Plasma");
        marten.Folder.ShouldBe("projects/ClearwaterZoo-Cole/2026-10-Marten-Plasma");
        marten.Funding.Type.ShouldBe("quote");
        marten.Funding.Quotes.ShouldBe(["MacCoss-2026-CWZG-MARTEN"]);
        marten.FundingText.ShouldBe("quote MacCoss-2026-CWZG-MARTEN");
        marten.ExpectedSamples.ShouldBe(10);
        marten.Species.ShouldBe("American marten");
        marten.Notebooks.Single().Id.ShouldBe("ELN-4485-20230314-179");
        marten.Panorama.Single().ShouldBe(new PanoramaFolder("/MacCoss/maccoss/2026-Marten", "raw"));
        marten.Layout.ShouldNotBeNull().Plates.ShouldBe(1);
        marten.Files.ShouldBe(new ExperimentFiles(true, true));
        marten.CurrentStage.ShouldBe("sample_prep");
        marten.CurrentStageLabel.ShouldBe("Sample prep");
        marten.Progress.ShouldBe(3);
        marten.Stages.Select(s => s.Stage).ShouldBe(StageNames.All);
        marten.Stages[0].ShouldBe(new StageEntry
        {
            Stage = "samples_received", Status = "done", Started = "2026-10-01", Finished = "2026-10-01", By = "maccoss",
            Note = "10 tubes on dry ice",
        });
        marten.Stages[3].IsInProgress.ShouldBeTrue();

        var internalWork = list.Projects.Single(p => p.Group == "UW-Example").Experiments.Single();
        internalWork.Human.ShouldBeTrue();
        internalWork.Funding.Type.ShouldBe("internal");
        internalWork.FundingText.ShouldBe("internal");
        internalWork.CurrentStage.ShouldBe("samples_received");
        internalWork.Layout.ShouldBeNull();
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
        // Collection dates are kept (lab-projects engine 26.2.0); a name is still an error.
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

    /// <summary>
    /// Runs the real project.py through uv against a clone of lab-projects. Opt-in: set
    /// LAB_PROJECTS_REPO to the clone's path.
    /// </summary>
    [Fact]
    public async Task Real_engine_lists_and_checks_the_repository()
    {
        var repo = Environment.GetEnvironmentVariable("LAB_PROJECTS_REPO");
        if (string.IsNullOrWhiteSpace(repo))
        {
            Assert.Skip("Set LAB_PROJECTS_REPO to a clone of lab-projects to run this.");
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
