using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Projects;
using LabOps.Engines.Python;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// labops projects wiki: the project's page on Panorama, and where it is recorded
/// (link &lt;project&gt; wiki). From LabOps-Projects' tests/test_wiki.py.
/// </summary>
public sealed partial class WikiCommandTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly string Wiki = """
        summary: Proteomics of 10 otter plasma samples with the [Mag-Net](/Services/wiki-page.view?name=Mag-Net) protocol. **Groups blinded.**
        samples_card: otter EDTA plasma
        qc_card: one reference pool
        plan:
          - DIA discovery on the Orbitrap Astral
          - A PRM assay <for the hits>
        samples:
          intro: 12 tubes arrived in one box.
          sections:
            - heading: Study samples (10)
              bullets:
                - Plasma from 5 farms.
        folders:
          /MacCoss/maccoss/@files/2026-Otter: The raw files, by plate.

        """.ReplaceLineEndings("\n");

    /// <summary>A project with a sample table, a written wiki.yaml and one experiment part way through.</summary>
    private static string Otter(TestRepo repo)
    {
        var project = repo.NewProject("Otter-Plasma", "Zoo-Cole", "--funding", "grant", "--grant", "Riverbend Animal Foundation");
        TestRepo.WriteCsv(Path.Combine(project, "metadata", "samples.csv"),
            [["Sample_ID", "QC", "Sample_Group"],
             .. Enumerable.Range(0, 10).Select(i => new object?[] { $"S{i}", "FALSE", i % 2 == 1 ? "Case" : "Control" }),
             ["Pool-1", "TRUE", "Pool"], ["Pool-2", "TRUE", "Pool"]]);
        File.WriteAllText(Path.Combine(project, "wiki.yaml"), Wiki, Utf8);
        repo.Ok("stage", "Otter-Plasma", "samples_received", "done", "--date", "2026-09-09", "--by", "maccoss",
            "--note", "UPS <dry ice> & cold");
        repo.Ok("stage", "Otter-Plasma", "metadata_organized", "start", "--date", "2026-09-10", "--by", "maccoss");
        repo.Ok("stage", "Otter-Plasma", "metadata_organized", "done", "--date", "2026-10-03", "--by", "maccoss");
        var dia = Path.GetFileName(repo.NewExperiment("2026-09-Otter-DIA", "Otter-Plasma", "--instrument", "Orbitrap Astral"));
        repo.Ok("stage", dia, "data_acquisition", "start", "--date", "2026-09-23", "--by", "maccoss");
        repo.Ok("stage", dia, "data_acquisition", "done", "--date", "2026-09-29", "--by", "maccoss");
        repo.Ok("assign", dia, "signal_processing", "--to", "maccoss");
        repo.Ok("link", dia, "panorama", "/MacCoss/maccoss/@files/2026-Otter", "--kind", "raw");
        repo.Ok("link", dia, "panorama", "/MacCoss/maccoss/2026-Otter", "--kind", "results");
        repo.Ok("link", dia, "panorama", "/MacCoss/maccoss/2026-Otter-QC", "--kind", "qc");
        return project;
    }

    private static JsonObject Page(TestRepo repo, params string[] args) =>
        repo.Ok(["wiki", "Otter-Plasma", "--date", "2026-10-04", .. args]);

    private static string Str(JsonNode? node) => node!.GetValue<string>();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    /// <summary>The page's words, without its tags, one space between each.</summary>
    private static string Text(string html) => Space().Replace(Tag().Replace(html, " "), " ").Trim();

    private static void ShouldBeJson(JsonNode? actual, JsonNode expected) =>
        JsonNode.DeepEquals(actual, expected).ShouldBeTrue($"{actual?.ToJsonString()} should be {expected.ToJsonString()}");

    private static void ShouldBePy(object? actual, object? expected) =>
        PyText.Eq(actual, expected).ShouldBeTrue($"{PyText.Repr(actual)} should be {PyText.Repr(expected)}");

    [Theory]
    [InlineData("2026-09-09", "2026-09-09", "Sep 9, 2026")]
    [InlineData("2026-09-23", "2026-09-29", "Sep 23–29, 2026")]
    [InlineData("2026-09-10", "2026-10-03", "Sep 10 – Oct 3, 2026")]
    [InlineData("2026-12-30", "2027-01-02", "Dec 30, 2026 – Jan 2, 2027")]
    [InlineData(null, "2026-10-03", "Oct 3, 2026")]
    [InlineData(null, null, "")]
    public void Dates_read_as_a_person_writes_them(string? start, string? end, string shown) =>
        WikiPage.Range(start, end).ShouldBe(shown);

    [Fact]
    public void The_page_has_the_written_parts_and_every_step_from_the_records()
    {
        using var repo = TestRepo.Create();
        var otter = Otter(repo);
        var output = Page(repo);
        var html = Str(output["html"]);
        var words = Text(html);

        Str(output["title"]).ShouldBe("Test project");
        output["written"]!.GetValue<bool>().ShouldBeTrue();
        output.ContainsKey("folder").ShouldBeTrue();
        output["folder"].ShouldBeNull();
        Str(output["page"]).ShouldBe("default");
        html.ShouldContain("MacCoss Lab project &middot; with Pat Example, Ph.D., Example University &middot; Funded by Riverbend Animal Foundation", Case.Sensitive);
        html.ShouldContain("<a href=\"/Services/wiki-page.view?name=Mag-Net\">Mag-Net</a>", Case.Sensitive);
        html.ShouldContain("<strong>Groups blinded.</strong>", Case.Sensitive);
        words.ShouldContain("10 samples otter EDTA plasma", Case.Sensitive);
        words.ShouldContain("2 QC samples one reference pool", Case.Sensitive);
        words.ShouldContain("Orbitrap Astral Test experiment; acquired Sep 23–29, 2026", Case.Sensitive);
        words.ShouldContain("Plate layout current step", Case.Sensitive);                       // the samples come first
        html.ShouldContain("A PRM assay &lt;for the hits&gt;", Case.Sensitive);                   // written text is escaped
        // Every step with its status, dates, who and note; notes escaped.
        words.ShouldContain("Samples received Done Sep 9, 2026 Michael MacCoss UPS &lt;dry ice&gt; &amp; cold", Case.Sensitive);
        words.ShouldContain("Metadata organized Done Sep 10 – Oct 3, 2026 Michael MacCoss", Case.Sensitive);
        words.ShouldContain("Plate layout Not started", Case.Sensitive);
        words.ShouldContain("Test experiment (2026-09-Otter-DIA)", Case.Sensitive);
        words.ShouldContain("Signal processing Not started Michael MacCoss", Case.Sensitive);    // assigned, not yet started
        words.ShouldContain("12 tubes arrived in one box.", Case.Sensitive);
        words.ShouldContain("Plasma from 5 farms.", Case.Sensitive);
        // The footer marks the page as LabOps's.
        var writtenHash = Str(output["written_hash"]);
        html.ShouldContain($"<div id=\"labops-wiki\" data-written=\"{writtenHash}\" data-body=\"", Case.Sensitive);
        // data-body is the fingerprint of everything above the footer, as written.
        var above = html[..html.IndexOf("<div id=\"labops-wiki\"", StringComparison.Ordinal)];
        html.ShouldContain($"data-body=\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(above)))[..12]}\"", Case.Sensitive);
        html.ShouldContain("Generated by LabOps from the LabOps-Projects repository on Oct 4, 2026.", Case.Sensitive);
        // The fingerprint follows the written parts, and only them.
        Str(Page(repo)["written_hash"]).ShouldBe(writtenHash);
        writtenHash.ShouldNotBe("none");
        repo.Ok("stage", "Otter-Plasma", "plate_layout", "start");
        Str(Page(repo)["written_hash"]).ShouldBe(writtenHash);
        File.WriteAllText(Path.Combine(otter, "wiki.yaml"), Wiki + "qc_card: two pools\n", Utf8);
        Str(Page(repo)["written_hash"]).ShouldNotBe(writtenHash);
    }

    [Fact]
    public void Data_folders_link_to_panorama_with_their_skyline_documents()
    {
        using var repo = TestRepo.Create();
        Otter(repo);
        using var temp = new TempDirectory();
        var documents = temp.Combine("documents.json");
        // Written with a byte order mark, which the engine reads past.
        File.WriteAllText(documents, "﻿" + """
            {"/MacCoss/maccoss/2026-Otter/": [{"name": "Otter-plate1", "replicates": 89, "peptides": 84806, "proteins": 6746, "uploaded": "2026-10-01"}], "/MacCoss/maccoss/2026-Otter-QC": []}
            """, Utf8);
        var html = Str(Page(repo, "--documents", documents)["html"]);

        html.ShouldContain("<a href=\"/_webdav/MacCoss/maccoss/%40files/2026-Otter/\">2026-Otter folder</a>", Case.Sensitive);
        html.ShouldContain("The raw files, by plate.", Case.Sensitive);
        html.ShouldContain("<a href=\"/MacCoss/maccoss/2026-Otter/project-begin.view\">2026-Otter</a>", Case.Sensitive);
        html.ShouldContain("Otter-plate1: 89 replicates, 84,806 peptides from 6,746 proteins (uploaded Oct 1, 2026)", Case.Sensitive);
        html.ShouldContain("Process control", Case.Sensitive);
        html.ShouldContain("No Skyline documents yet.", Case.Sensitive);
        // Without the documents (the engine alone), the folders are listed without them.
        Str(Page(repo)["html"]).ShouldNotContain("No Skyline documents yet.", Case.Sensitive);
    }

    [Fact]
    public void Without_written_parts_the_page_is_made_from_the_records()
    {
        using var repo = TestRepo.Create();
        var otter = Otter(repo);
        File.Delete(Path.Combine(otter, "wiki.yaml"));
        var output = Page(repo);
        var words = Text(Str(output["html"]));

        output["written"]!.GetValue<bool>().ShouldBeFalse();
        words.ShouldContain("Plan 1 Test experiment", Case.Sensitive);                          // each experiment in turn
        words.ShouldContain("10 study samples and 2 QC samples in the deidentified sample table.", Case.Sensitive);
        words.ShouldContain("Case: 5", Case.Sensitive);
        words.ShouldContain("Control: 5", Case.Sensitive);
    }

    [Fact]
    public void The_text_output_says_where_the_page_goes_and_can_write_a_file()
    {
        using var repo = TestRepo.Create();
        Otter(repo);
        repo.Ok("link", "Otter-Plasma", "wiki", "/MacCoss/Collaborations/Zoo/Otter");
        var shown = repo.RunText("wiki", "Otter-Plasma", "--out", "inbox/Otter-Plasma/wiki.html").Stdout;

        shown.ShouldContain("Otter-Plasma: page default in /MacCoss/Collaborations/Zoo/Otter", Case.Sensitive);
        shown.ShouldContain("wrote inbox/Otter-Plasma/wiki.html", Case.Sensitive);
        File.ReadAllText(Path.Combine(repo.Root, "inbox", "Otter-Plasma", "wiki.html"), Encoding.UTF8)
            .ShouldContain("labops-wiki", Case.Sensitive);
    }

    [Theory]
    [InlineData("https://panoramaweb.org/MacCoss/Collaborations/MNRF/BioTRACK/project-begin.view", "/MacCoss/Collaborations/MNRF/BioTRACK", "default")]
    [InlineData("https://panoramaweb.org/MacCoss/Collaborations/MNRF/BioTRACK/wiki-page.view?name=status", "/MacCoss/Collaborations/MNRF/BioTRACK", "status")]
    [InlineData("/MacCoss/Collaborations/MNRF/BioTRACK/", "/MacCoss/Collaborations/MNRF/BioTRACK", "default")]
    public void The_wiki_page_is_recorded_on_the_project_from_any_address(string given, string folder, string name)
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var output = repo.Ok("link", "Test-Project", "wiki", given);

        ShouldBeJson(output["project"]!["wiki"], new JsonObject { ["folder"] = folder, ["page"] = name });
        ShouldBePy(TestRepo.Yaml(project)["wiki"], new PyDict { ["folder"] = folder, ["page"] = name });
        TestRepo.Raw(project).ShouldEndWith($"wiki: {{folder: {folder}, page: {name}}}\n", Case.Sensitive);
        // Linking again replaces it in place, and unlink keeps the line, empty.
        repo.Ok("link", "Test-Project", "wiki", "/MacCoss/Other", "--page", "Home");
        (TestRepo.Raw(project).Split("wiki:").Length - 1).ShouldBe(1);
        ShouldBePy(TestRepo.Yaml(project)["wiki"], new PyDict { ["folder"] = "/MacCoss/Other", ["page"] = "Home" });
        repo.Ok("unlink", "Test-Project", "wiki");
        TestRepo.Yaml(project).ContainsKey("wiki").ShouldBeTrue();
        TestRepo.Yaml(project)["wiki"].ShouldBeNull();
        var listed = repo.Ok("list")["labs"]![0]!["projects"]![0]!.AsObject();
        listed.ContainsKey("wiki").ShouldBeTrue();
        listed["wiki"].ShouldBeNull();
    }

    [Fact]
    public void The_wiki_page_belongs_to_the_project_folder()
    {
        using var repo = TestRepo.Create();
        var experiment = Path.GetFileName(repo.NewExperiment());
        Str(repo.Fails("link", experiment, "wiki", "/MacCoss/x")["error"]).ShouldContain("belongs to the project", Case.Sensitive);
        Str(repo.Fails("link", "Test-Project", "wiki", "/MacCoss/maccoss/@files/x")["error"]).ShouldContain("file area", Case.Sensitive);
        Str(repo.Fails("link", "Test-Project", "wiki", "/MacCoss/x", "--page", "a/b")["error"]).ShouldContain("not a wiki page name", Case.Sensitive);
        Str(repo.Fails("unlink", "Test-Project", "wiki")["error"]).ShouldContain("no wiki page recorded", Case.Sensitive);
    }

    [Fact]
    public void Process_control_folders_are_a_kind_of_panorama_folder()
    {
        using var repo = TestRepo.Create();
        var experiment = Path.GetFileName(repo.NewExperiment());
        var output = repo.Ok("link", experiment, "panorama", "/MacCoss/maccoss/2026-QC", "--kind", "qc");
        ShouldBeJson(output["experiment"]!["panorama"], new JsonArray(new JsonObject { ["folder"] = "/MacCoss/maccoss/2026-QC", ["kind"] = "qc" }));
        repo.Problems().Where(p => p.Message.Contains("kind should be", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void Check_finds_a_wiki_yaml_that_will_not_show_as_meant()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        File.WriteAllText(Path.Combine(project, "wiki.yaml"), "summary: [not, text]\nplan: one line\nsidebar: x\n"
            + "samples: {sections: [{heading: Only a heading}]}\n", Utf8);
        var problems = repo.Problems().Where(p => p.Message.Contains("wiki.yaml", StringComparison.Ordinal)).ToList();

        problems.Select(p => (p.Level, p.Message.Split(": ", 2)[1]))
            .ShouldContain(("ERROR", "projects/Test-Lab/Test-Project/wiki.yaml: `summary` should be text"));
        problems.ShouldContain(p => p.Message.Contains("`plan` should be a list", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Level == "WARN" && p.Message.Contains("`sidebar` is not shown", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Message.Contains("needs a heading and text or bullets", StringComparison.Ordinal));
        var projectYaml = TestRepo.Raw(project) + "\nwiki: {page: default}\n";
        TestRepo.WriteRaw(project, projectYaml);
        repo.Problems().ShouldContain(p => p.Message.Contains("wiki: needs the project's Panorama folder", StringComparison.Ordinal));
    }

    [Fact]
    public void Contact_details_never_go_onto_the_shared_page()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        File.WriteAllText(Path.Combine(project, "wiki.yaml"), "summary: Ask pat@example.org or call 206-555-0100.\n", Utf8);
        repo.Git("add", "-A");
        var found = repo.Problems("--staged").Where(p => p.Level == "ERROR").Select(p => p.Message).ToList();

        found.ShouldContain(m => m.Contains("wiki.yaml", StringComparison.Ordinal) && m.Contains("summary", StringComparison.Ordinal)
            && m.Contains("email address or phone number", StringComparison.Ordinal));
    }

    [Fact]
    public void The_page_lists_the_protocols_used_at_their_versions()
    {
        using var repo = TestRepo.Create();
        Otter(repo);
        Text(Str(Page(repo)["html"])).ShouldNotContain("Protocols", Case.Sensitive);              // none yet: no section
        repo.Ok("link", "Otter-Plasma", "protocol", "sax-kfkf-ev-capture-digestion", "--version", "3", "--step", "sample_prep",
            "--title", "KingFisher EV capture and digestion");
        repo.Ok("link", "2026-09-Otter-DIA", "protocol", "kasil-capillary-frits", "--version", "2", "--step", "data_acquisition");
        var words = Text(Str(Page(repo)["html"]));
        words.ShouldContain("Protocols Protocol Version Used for KingFisher EV capture and digestion version 3 Sample prep", Case.Sensitive);
        words.ShouldContain("kasil-capillary-frits version 2 Data acquisition, Test experiment", Case.Sensitive);
        words.ShouldContain("Data on Panorama", Case.Sensitive);
        words.IndexOf("Protocols Protocol", StringComparison.Ordinal).ShouldBeLessThan(words.IndexOf("Data on Panorama", StringComparison.Ordinal));
    }

    [Fact]
    public void Links_on_the_page_go_to_panorama_or_a_web_address_only()
    {
        WikiPage.Inline("[a](/MacCoss/x)").ShouldBe("<a href=\"/MacCoss/x\">a</a>");
        WikiPage.Inline("[a](https://example.org)").ShouldBe("<a href=\"https://example.org\">a</a>");
        WikiPage.Inline("[a](//example.org/x)").ShouldBe("[a](//example.org/x)");                 // another site, by //
        WikiPage.Inline("[a](javascript:alert(1))").ShouldBe("[a](javascript:alert(1))");
    }
}
