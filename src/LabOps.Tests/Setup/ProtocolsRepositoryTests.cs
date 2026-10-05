using System.Text.Json;
using LabOps.Core.Claude;
using LabOps.Core.Projects;
using LabOps.Core.Repositories;
using LabOps.Core.Setup;

namespace LabOps.Tests.Setup;

/// <summary>The lab protocols as the app's third repository: where it is, how it is laid out, and what Claude may do there.</summary>
public sealed class ProtocolsRepositoryTests
{
    [Theory]
    [InlineData("https://github.com/uw-maccosslab/LabOps-Protocols.git", true)]
    [InlineData("git@github.com:uw-maccosslab/LabOps-Protocols", true)]
    [InlineData("https://github.com/uw-maccosslab/LabOps-Projects.git", false)]
    [InlineData("https://github.com/someone/LabOps-Protocols.git", false)]
    public void Only_the_protocols_repository_is_accepted_as_a_protocols_copy(string url, bool accepted) =>
        RepositoryProfile.Protocols.IsRemote(url).ShouldBe(accepted);

    [Fact]
    public void A_protocol_folder_is_the_first_two_path_segments_and_commits_are_checked()
    {
        var p = RepositoryProfile.Protocols;
        (p.GitHubName, p.DefaultFolderName, p.EngineScript).ShouldBe(("uw-maccosslab/LabOps-Protocols", "LabOps-Protocols", "scripts/protocol.py"));
        p.ItemFolder("protocols/kasil-capillary-frits/versions/v2.md").ShouldBe("protocols/kasil-capillary-frits");
        p.IsItemPath("protocols/kasil-capillary-frits/protocol.md").ShouldBeTrue();
        p.IsItemPath("config/categories.yaml").ShouldBeFalse();
        p.IsGenerated("README.md").ShouldBeTrue();
        p.IsGenerated("protocols/kasil-capillary-frits/protocol.md").ShouldBeFalse();
        p.ChecksCommits.ShouldBeTrue();
        p.RenamedRemote("https://github.com/uw-maccosslab/LabOps-Protocols.git").ShouldBeNull();
        RepositoryProfile.All.ShouldContain(p);
    }

    [Fact]
    public void An_existing_protocols_clone_is_found_by_its_engine()
    {
        using var temp = new TestSupport.TempDirectory();
        var clone = temp.Combine("my-protocols");
        Directory.CreateDirectory(Path.Combine(clone, ".git"));
        Directory.CreateDirectory(Path.Combine(clone, "scripts"));
        File.WriteAllText(Path.Combine(clone, "scripts", "protocol.py"), "");

        SetupService.FindExistingClone(RepositoryProfile.Protocols, clone).ShouldBe(clone);
        RepositoryProfile.Projects.LooksLikeClone(clone).ShouldBeFalse();
    }

    [Fact]
    public void The_protocols_are_optional_so_an_updated_app_still_opens()
    {
        SetupItem Item(SetupStep step, bool done, bool optional = false) => new(step, step.ToString(), done, "", null, Optional: optional);

        SetupService.AllDone([Item(SetupStep.ProjectsRepository, true), Item(SetupStep.ProtocolsRepository, false, optional: true)])
            .ShouldBeTrue();
        new SetupItem(SetupStep.ProtocolsEngine, "", false, "", null).Profile.ShouldBe(RepositoryProfile.Protocols);
    }

    [Fact]
    public void Claude_runs_only_the_protocol_engine_and_knows_published_versions_never_change()
    {
        var tools = ClaudeLauncher.AllowedTools(RepositoryProfile.Protocols);
        tools.ShouldContain("Bash(uv run python scripts/protocol.py:*)");
        tools.ShouldNotContain("Bash(uv run python scripts/project.py:*)");
        tools.ShouldNotContain("Bash(uv run python scripts/quote.py:*)");

        var prompt = ClaudeLauncher.SystemPrompt(RepositoryProfile.Protocols, "Mike");
        prompt.ShouldContain("format-protocol");
        prompt.ShouldContain("revise-protocol");
        prompt.ShouldContain("A published version never changes");
        prompt.ShouldContain("lab protocols repository folder");
        prompt.ShouldNotContain("report_quote_summary");
        prompt.ShouldNotContain("—");
    }

    [Theory]
    [InlineData("""{"command":"uv run python scripts/protocol.py list --json"}""", "Looking through the protocols")]
    [InlineData("""{"command":"uv run python scripts/protocol.py import inbox/x.docx --id x"}""", "Reading the uploaded file")]
    [InlineData("""{"command":"uv run python scripts/protocol.py check"}""", "Checking the protocol")]
    [InlineData("""{"command":"uv run python scripts/protocol.py publish x --summary y --by z"}""", "Publishing a version")]
    public void Claude_s_protocol_commands_read_plainly_in_the_chat(string input, string shown)
    {
        using var doc = JsonDocument.Parse(input);
        ToolDescriptions.Describe("Bash", doc.RootElement).ShouldBe(shown);
    }

    [Fact]
    public void A_project_s_protocols_are_read_with_their_versions_and_steps()
    {
        const string json = """
            {"ok": true, "engine_version": "26.3.0", "people": [], "problems": [], "closed_hidden": 0, "projects": [],
             "labs": [{"lab": "Zoo", "folder": "projects/Zoo", "notebooks": [], "issues": [], "projects": [
               {"project": "Marten", "folder": "projects/Zoo/Marten", "lab": "Zoo", "notebooks": [], "stages": [], "issues": [],
                "protocols": [{"id": "sax-kfkf-ev-capture-digestion", "version": 3, "title": "KingFisher EV capture", "step": "sample_prep"}],
                "experiments": [{"experiment": "2026-10-Marten-DIA", "folder": "projects/Zoo/Marten/2026-10-Marten-DIA", "notebooks": [],
                  "panorama": [], "stages": [], "issues": [],
                  "protocols": [{"id": "kasil-capillary-frits", "version": null, "title": null, "step": null}]}]}]}]}
            """;
        using var doc = JsonDocument.Parse(json);
        var project = ProjectEngine.ReadList(doc.RootElement).Labs.Single().Projects.Single();

        project.Protocols.ShouldBe([new ProtocolLink("sax-kfkf-ev-capture-digestion", 3, "KingFisher EV capture", "sample_prep")]);
        project.Experiments.Single().Protocols.ShouldBe([new ProtocolLink("kasil-capillary-frits", null, null, null)]);
    }
}
