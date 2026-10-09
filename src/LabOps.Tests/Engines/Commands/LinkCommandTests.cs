using System.Text.Json.Nodes;
using LabOps.Engines.Projects;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// `labops projects link` and `unlink`: Panorama folders, ELN notebooks and protocols. Ported from
/// LabOps-Projects' tests/test_links.py.
/// </summary>
public sealed class LinkCommandTests
{
    private const string NotebookBase = "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/";

    [Theory]
    [InlineData("https://panoramaweb.org/MacCoss/maccoss/2026-BioTRACK/project-begin.view?")]
    [InlineData("https://panoramaweb.org/project/MacCoss/maccoss/2026-BioTRACK/begin.view?pageId=Raw%20Data")]
    [InlineData("https://panoramaweb.org/MacCoss/maccoss/2026-BioTRACK/targetedms-showList.view")]
    [InlineData("/MacCoss/maccoss/2026-BioTRACK/")]
    [InlineData("MacCoss/maccoss/2026-BioTRACK")]
    public void Any_panorama_address_is_kept_as_its_folder(string given) =>
        Links.PanoramaFolder(given).ShouldBe("/MacCoss/maccoss/2026-BioTRACK");

    [Fact]
    public void A_file_area_keeps_its_place_in_the_folder()
    {
        // PanoramaBridge uploads raw files under the lab folder's @files, so that part is the location.
        Links.PanoramaFolder("https://panoramaweb.org/_webdav/MacCoss/maccoss/@files/2026-BioTRACK/")
            .ShouldBe("/MacCoss/maccoss/@files/2026-BioTRACK");
        Links.PanoramaFolder("https://panoramaweb.org/_webdav/MacCoss/maccoss/%40files/2026%20BioTRACK/")
            .ShouldBe("/MacCoss/maccoss/@files/2026 BioTRACK");
        Links.PanoramaFolder("/MacCoss/maccoss/@files/2026-BioTRACK/").ShouldBe("/MacCoss/maccoss/@files/2026-BioTRACK");
        Links.PanoramaFolder("https://panoramaweb.org/_webdav/MacCoss/maccoss/").ShouldBe("/MacCoss/maccoss");
    }

    [Fact]
    public void Spaces_in_a_folder_name_are_decoded() =>
        Links.PanoramaFolder("https://panoramaweb.org/MacCoss/maccoss/Heron%20Study/project-begin.view")
            .ShouldBe("/MacCoss/maccoss/Heron Study");

    [Fact]
    public void Panorama_folders_are_recorded_on_the_experiment_one_per_line()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var name = Path.GetFileName(experiment);
        var answer = repo.Ok("link", name, "panorama",
            "https://panoramaweb.org/MacCoss/maccoss/2026-Test/project-begin.view", "--kind", "raw");
        repo.Ok("link", name, "panorama", "/MacCoss/maccoss/2026-Test-Results", "--kind", "results");
        ShouldBePy(FromJson(answer["experiment"]!["panorama"]), PyList(Folder("/MacCoss/maccoss/2026-Test", "raw")));
        ShouldBePy(TestRepo.Yaml(experiment)["panorama"], PyList(
            Folder("/MacCoss/maccoss/2026-Test", "raw"),
            Folder("/MacCoss/maccoss/2026-Test-Results", "results")));
        var raw = TestRepo.Raw(experiment);
        raw.ShouldContain("panorama:\n  - {folder: /MacCoss/maccoss/2026-Test, kind: raw}\n", Case.Sensitive);
        raw.ShouldContain("#    kind: raw            # raw | results", Case.Sensitive); // the template's example stays

        // The same folder again changes its kind rather than adding it twice.
        repo.Ok("link", name, "panorama", "/MacCoss/maccoss/2026-Test/", "--kind", "results");
        Items(TestRepo.Yaml(experiment)["panorama"]).Select(f => ((PyDict)f!)["kind"]).ShouldBe(["results", "results"]);
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Panorama_needs_an_experiment_a_kind_and_a_panorama_address()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var name = Path.GetFileName(experiment);
        Error(repo.Fails("link", Path.GetFileName(project), "panorama", "/MacCoss/x", "--kind", "raw"))
            .ShouldContain("belong to an experiment", Case.Sensitive);
        Error(repo.Fails("link", name, "panorama", "/MacCoss/x")).ShouldContain("--kind raw", Case.Sensitive);
        Error(repo.Fails("link", name, "panorama", "https://example.org/MacCoss/x", "--kind", "raw"))
            .ShouldContain("not a Panorama address", Case.Sensitive);
    }

    [Fact]
    public void Notebooks_are_recorded_with_their_id_and_link()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var (experimentName, projectName) = (Path.GetFileName(experiment), Path.GetFileName(project));
        const string url = "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/4485";
        repo.Ok("link", projectName, "notebook", url, "--id", "ELN-4485-20230314-179");
        repo.Ok("link", experimentName, "notebook", "--id", "ELN-4490-20261001-12");
        ShouldBePy(TestRepo.Yaml(project)["notebooks"], PyList(Notebook("ELN-4485-20230314-179", url)));
        ShouldBePy(TestRepo.Yaml(experiment)["notebooks"], PyList(
            Notebook("ELN-4490-20261001-12", "https://panoramaweb.org/MacCoss/samplemanager-app.view#/notebooks/12")));

        // The link added later fills in the same notebook.
        var later = url.Replace("4485", "4490", StringComparison.Ordinal);
        var answer = repo.Ok("link", experimentName, "notebook", later, "--id", "ELN-4490-20261001-12");
        ShouldBePy(FromJson(answer["experiment"]!["notebooks"]), PyList(Notebook("ELN-4490-20261001-12", later)));
        repo.Problems().ShouldNotContain(p => p.Message.Contains("neither", StringComparison.Ordinal));

        Error(repo.Fails("link", projectName, "notebook")).ShouldContain("link, its ID", Case.Sensitive);
        Error(repo.Fails("link", projectName, "notebook", "ELN-1")).ShouldContain("not a link", Case.Sensitive);
    }

    [Theory]
    [InlineData("ELN-1567-20250730-131", "131")]
    [InlineData("ELN-20250730-131", "131")]
    [InlineData("ELN-131", "131")]
    [InlineData("eln-4485-20230314-179", "179")]
    public void A_notebook_id_gives_its_link(string notebookId, string row) =>
        Links.NotebookUrl(notebookId).ShouldBe(NotebookBase + row);

    [Fact]
    public void A_notebook_recorded_by_id_alone_gets_its_link()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var name = Path.GetFileName(experiment);
        repo.Ok("link", name, "notebook", "--id", "ELN-1567-20250730-131");
        repo.Ok("link", name, "notebook", "--id", "Lab book 7");
        ShouldBePy(TestRepo.Yaml(experiment)["notebooks"], PyList(
            Notebook("ELN-1567-20250730-131", NotebookBase + "131"),
            new PyDict { ["id"] = "Lab book 7" }));
        Links.NotebookUrl("not an id").ShouldBeNull();
    }

    [Fact]
    public void Links_can_be_removed()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var name = Path.GetFileName(experiment);
        repo.Ok("link", name, "panorama", "/MacCoss/maccoss/2026-Test", "--kind", "raw");
        repo.Ok("link", name, "notebook", "--id", "ELN-1");
        repo.Ok("unlink", name, "panorama", "https://panoramaweb.org/MacCoss/maccoss/2026-Test/project-begin.view");
        repo.Ok("unlink", name, "notebook", "ELN-1");
        ShouldBePy(TestRepo.Yaml(experiment)["panorama"], PyList());
        ShouldBePy(TestRepo.Yaml(experiment)["notebooks"], PyList());
        TestRepo.Raw(experiment).ShouldContain("panorama: []\n", Case.Sensitive);
        Error(repo.Fails("unlink", name, "notebook", "ELN-2")).ShouldContain("has no notebook ELN-2", Case.Sensitive);
    }

    [Fact]
    public void Check_warns_about_links_it_cannot_use()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var name = Path.GetFileName(experiment);
        var raw = ReplaceFirst(TestRepo.Raw(experiment), "panorama: []", "panorama:\n  - {folder: /MacCoss/x, kind: rawdata}");
        raw = ReplaceFirst(raw, "notebooks: []", "notebooks:\n  - {id: ELN-1, url: panoramaweb.org/x}");
        TestRepo.WriteRaw(experiment, raw);
        var problems = repo.Problems();
        problems.ShouldContain(("WARN", $"{name}: Panorama folder /MacCoss/x: kind should be raw, results or qc"));
        problems.ShouldContain(("WARN", $"{name}: notebook ELN-1: the url should start with https://"));
    }

    [Theory]
    [InlineData("C:/Program Files/Git/MacCoss/maccoss/2026-BioTRACK")]
    [InlineData("/C:/Program Files/Git/MacCoss/x")]
    [InlineData(@"D:\Data\RawFiles")]
    public void A_path_on_this_computer_is_refused_with_how_to_avoid_git_bashs_rewriting(string given)
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var error = Error(repo.Fails("link", Path.GetFileName(experiment), "panorama", given, "--kind", "raw"));
        error.ShouldContain("is a path on this computer", Case.Sensitive);
        error.ShouldContain("MSYS_NO_PATHCONV=1", Case.Sensitive);
    }

    [Fact]
    public void A_protocol_is_recorded_at_its_version_for_a_step()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var (experimentName, projectName) = (Path.GetFileName(experiment), Path.GetFileName(project));
        var answer = repo.Ok("link", projectName, "protocol", "s-trap-micro-digestion", "--version", "2", "--step", "sample_prep",
            "--title", "S-Trap micro digestion");
        ShouldBePy(FromJson(answer["project"]!["protocols"]), PyList(
            new PyDict { ["id"] = "s-trap-micro-digestion", ["version"] = 2, ["title"] = "S-Trap micro digestion", ["step"] = "sample_prep" }));
        var raw = TestRepo.Raw(project);
        raw.ShouldContain("protocols:\n  - {id: s-trap-micro-digestion, version: 2, title: S-Trap micro digestion, step: sample_prep}\n",
            Case.Sensitive);
        raw.ShouldContain("# Protocols from LabOps-Protocols", Case.Sensitive);
        // Linking it again for the same step records the newer version, keeping its title.
        answer = repo.Ok("link", projectName, "protocol", "s-trap-micro-digestion", "--version", "3", "--step", "sample_prep");
        ShouldBePy(FromJson(answer["project"]!["protocols"]), PyList(
            new PyDict { ["id"] = "s-trap-micro-digestion", ["version"] = 3, ["title"] = "S-Trap micro digestion", ["step"] = "sample_prep" }));
        // An experiment records its own; the same protocol can be used for two steps.
        repo.Ok("link", experimentName, "protocol", "kasil-capillary-frits", "--version", "2", "--step", "data_acquisition");
        repo.Ok("link", projectName, "protocol", "s-trap-micro-digestion", "--version", "3", "--step", "plate_layout");
        Items(TestRepo.Yaml(project)["protocols"]).Count.ShouldBe(2);
        ShouldBePy(TestRepo.Yaml(experiment)["protocols"], PyList(
            new PyDict { ["id"] = "kasil-capillary-frits", ["version"] = 2, ["step"] = "data_acquisition" }));
        repo.Problems().ShouldBeEmpty();

        repo.Ok("unlink", projectName, "protocol", "s-trap-micro-digestion", "--step", "plate_layout");
        Items(TestRepo.Yaml(project)["protocols"]).Select(p => ((PyDict)p!)["step"]).ShouldBe(["sample_prep"]);
        // Without --step, unlink means the link for the item as a whole, and there is none.
        var error = Error(repo.Fails("unlink", projectName, "protocol", "s-trap-micro-digestion"));
        error.ShouldContain("has no protocol s-trap-micro-digestion for the item as a whole", Case.Sensitive);
        error.ShouldContain("linked for sample_prep", Case.Sensitive);
        repo.Ok("unlink", projectName, "protocol", "s-trap-micro-digestion", "--step", "sample_prep");
        ShouldBePy(TestRepo.Yaml(project)["protocols"], PyList());
        Error(repo.Fails("unlink", projectName, "protocol", "s-trap-micro-digestion", "--all")).ShouldContain("has no protocol", Case.Sensitive);
    }

    [Fact]
    public void Removing_the_item_level_protocol_keeps_its_step_links()
    {
        // What LabOps's Remove sends for a protocol linked to the item as a whole: no --step.
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var name = Path.GetFileName(project);
        repo.Ok("link", name, "protocol", "s-trap-micro-digestion", "--version", "1");
        repo.Ok("link", name, "protocol", "s-trap-micro-digestion", "--version", "2", "--step", "sample_prep");
        repo.Ok("unlink", name, "protocol", "s-trap-micro-digestion");
        ShouldBePy(TestRepo.Yaml(project)["protocols"], PyList(
            new PyDict { ["id"] = "s-trap-micro-digestion", ["version"] = 2, ["step"] = "sample_prep" }));
    }

    [Fact]
    public void Unlink_all_removes_every_link_to_a_protocol()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var name = Path.GetFileName(project);
        string[][] steps = [[], ["--step", "sample_prep"], ["--step", "plate_layout"]];
        foreach (var step in steps)
        {
            repo.Ok(["link", name, "protocol", "s-trap-micro-digestion", "--version", "1", .. step]);
        }

        repo.Ok("link", name, "protocol", "kasil-capillary-frits", "--version", "1");
        Error(repo.Fails("unlink", name, "protocol", "s-trap-micro-digestion", "--all", "--step", "sample_prep"))
            .ShouldContain("not both", Case.Sensitive);
        Error(repo.Fails("unlink", name, "notebook", "ELN-1", "--all")).ShouldContain("--all is for protocols", Case.Sensitive);
        repo.Ok("unlink", name, "protocol", "s-trap-micro-digestion", "--all");
        Items(TestRepo.Yaml(project)["protocols"]).Select(p => ((PyDict)p!)["id"]).ShouldBe(["kasil-capillary-frits"]);
    }

    [Fact]
    public void A_protocol_link_needs_an_id_a_version_and_a_real_step()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var name = Path.GetFileName(project);
        Error(repo.Fails("link", name, "protocol", "S-Trap", "--version", "1")).ShouldContain("LabOps-Protocols", Case.Sensitive);
        Error(repo.Fails("link", name, "protocol", "s-trap-micro-digestion")).ShouldContain("give --version", Case.Sensitive);
        Error(repo.Fails("link", name, "protocol", "s-trap-micro-digestion", "--version", "1", "--step", "digest"))
            .ShouldContain("has no step 'digest'", Case.Sensitive);
        // Without a step it is the item's as a whole.
        var answer = repo.Ok("link", name, "protocol", "s-trap-micro-digestion", "--version", "1");
        var protocol = answer["project"]!["protocols"]![0]!.AsObject();
        protocol.ContainsKey("step").ShouldBeTrue();
        protocol["step"].ShouldBeNull();
    }

    [Fact]
    public void Check_warns_about_a_protocol_link_written_by_hand()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var raw = TestRepo.Raw(project).Replace("protocols: []", "protocols:\n  - {id: s-trap-micro-digestion, step: digest}", StringComparison.Ordinal);
        TestRepo.WriteRaw(project, raw);
        var warnings = repo.Problems().Where(p => p.Level == "WARN").Select(p => p.Message).ToList();
        warnings.ShouldContain(w => w.Contains("give the version that was used", StringComparison.Ordinal));
        warnings.ShouldContain(w => w.Contains("there is no step 'digest'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_protocol_goes_into_a_record_written_before_protocols_existed()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var raw = TestRepo.Raw(project);
        var start = Index(raw, "\n# Protocols from LabOps-Protocols");
        var end = Index(raw, "\n", Index(raw, "#  - {id: s-trap-micro-digestion")) + 1;
        TestRepo.WriteRaw(project, raw[..start] + "\n" + raw[end..]);
        TestRepo.Raw(project).ShouldNotContain("protocols:", Case.Sensitive);
        repo.Ok("link", Path.GetFileName(project), "protocol", "s-trap-micro-digestion", "--version", "1", "--step", "sample_prep");
        raw = TestRepo.Raw(project);
        Index(raw, "notebooks: []").ShouldBeLessThan(Index(raw, "protocols:"));
        Index(raw, "protocols:").ShouldBeLessThan(Index(raw, "analysis:"));
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void A_notebook_written_as_a_bare_link_keeps_its_link()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        TestRepo.WriteRaw(project, TestRepo.Raw(project).Replace("notebooks: []", "notebooks:\n  - https://example.org/eln/7", StringComparison.Ordinal));
        var before = repo.Ok("list")["labs"]![0]!["projects"]![0]!["notebooks"]!.AsArray();
        repo.Ok("link", Path.GetFileName(project), "notebook", "--id", "ELN-131");
        var after = repo.Ok("list")["labs"]![0]!["projects"]![0]!["notebooks"]!.AsArray();
        var bare = Notebook("https://example.org/eln/7", "https://example.org/eln/7");
        ShouldBePy(FromJson(after[0]), bare);
        ShouldBePy(FromJson(before[0]), bare);
    }

    // -- helpers --------------------------------------------------------------------------------

    private static string Error(JsonObject answer) => answer["error"]!.GetValue<string>();

    /// <summary>A command's JSON as Python's json.loads reads it, to compare with Python's ==.</summary>
    private static object? FromJson(JsonNode? node) => node is null ? null : PyJsonDecoder.Loads(node.ToJsonString());

    private static void ShouldBePy(object? actual, object? expected) =>
        PyText.Eq(actual, expected).ShouldBeTrue($"{PyText.Repr(actual)}\nshould be\n{PyText.Repr(expected)}");

    private static List<object?> PyList(params object?[] items) => [.. items];

    private static List<object?> Items(object? list) => (List<object?>)list!;

    private static PyDict Folder(string folder, string kind) => new() { ["folder"] = folder, ["kind"] = kind };

    private static PyDict Notebook(string id, string url) => new() { ["id"] = id, ["url"] = url };

    /// <summary>Python's str.replace(old, new, 1).</summary>
    private static string ReplaceFirst(string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + replacement + text[(at + old.Length)..];
    }

    /// <summary>Python's str.index: where the text is, failing the test when it is not there.</summary>
    private static int Index(string text, string value, int startAt = 0)
    {
        var at = text.IndexOf(value, startAt, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"{value} is not in the text");
        return at;
    }
}
