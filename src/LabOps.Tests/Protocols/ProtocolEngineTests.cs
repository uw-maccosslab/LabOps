using LabOps.Core.Processes;
using LabOps.Core.Protocols;

namespace LabOps.Tests.Protocols;

/// <summary>
/// The JSON contract with protocol.py. The fixtures were recorded from the real engine: the
/// listing of the lab protocols as they are, and a commit it refused because a published version
/// changed (see the engine's tests/test_contract.py for its side of the contract).
/// </summary>
public sealed class ProtocolEngineTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void The_list_reads_every_protocol_its_versions_and_the_categories()
    {
        using var doc = ProtocolEngine.Parse(new ProcessResult(0, Fixture("protocol-list.json"), ""));
        var list = ProtocolEngine.ReadList(doc.RootElement);

        list.Protocols.Count.ShouldBe(13);
        list.Categories.ShouldContain(new ProtocolCategory("sample-preparation", "Sample preparation"));
        list.People.ShouldContain(p => p.Login == "maccoss" && p.Name == "Michael MacCoss");
        list.Problems.ShouldBeEmpty();

        var kasil = list.Protocols.Single(p => p.Id == "kasil-capillary-frits");
        kasil.Folder.ShouldBe("protocols/kasil-capillary-frits");
        kasil.Status.ShouldBe("active");
        kasil.CategoryLabel.ShouldBe("LC-MS");
        kasil.LatestVersion.ShouldBe(2);
        kasil.DraftChanges.ShouldBeFalse();
        kasil.VersionText.ShouldBe("v2");
        kasil.Versions.Count.ShouldBe(2);
        kasil.Versions[0].ShouldBe(kasil.Versions[0] with { Version = 1, Date = null, By = "maccoss", Imported = true });
        kasil.Versions[1].Date.ShouldBe("2020-06");
        kasil.Figures.ShouldBe(["foil-1.jpg", "foil-2.jpg", "foil-3.jpg"]);
        kasil.Sources.ShouldBe(["Kasil Frit making.pdf"]);

        var strap = list.Protocols.Single(p => p.Id == "s-trap-micro-digestion");
        strap.IsDraft.ShouldBeTrue();
        strap.IsPublished.ShouldBeFalse();
        strap.DraftChanges.ShouldBeTrue();
        strap.VersionText.ShouldBe("draft");
        strap.Owner.ShouldBe("maccoss");
        strap.Tags.ShouldNotBeEmpty();

        var sax = list.Protocols.Single(p => p.Id == "sax-kfkf-ev-capture-digestion");
        sax.ShortTitle.ShouldBe("SAX KFKF");
        sax.AppliesTo.SampleTypes.ShouldContain("plasma");
        sax.AppliesTo.Instruments.ShouldContain("KingFisher Flex");
    }

    [Fact]
    public void A_refused_commit_names_the_published_version_it_would_change()
    {
        using var doc = ProtocolEngine.Parse(new ProcessResult(1, Fixture("protocol-check-staged.json"), ""), allowNotOk: true);
        var problems = doc.RootElement.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("message").GetString()).ToList();

        problems.ShouldContain(m => m!.Contains("versions/v1.md: version 1 of s-trap-micro-digestion was published and never changes",
            StringComparison.Ordinal));
    }

    [Fact]
    public void An_engine_error_is_reported_in_its_own_words()
    {
        var error = Should.Throw<LabOps.Core.Engines.EngineException>(() =>
            ProtocolEngine.Parse(new ProcessResult(1, """{"ok": false, "error": "protocol.md is the same as version 2; nothing to publish"}""", "")));
        error.Message.ShouldBe("protocol.md is the same as version 2; nothing to publish");
    }

    [Fact]
    public void A_list_from_another_engine_is_refused_plainly()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"ok": true, "labs": []}""");
        Should.Throw<LabOps.Core.Engines.EngineException>(() => ProtocolEngine.ReadList(doc.RootElement))
            .Message.ShouldContain("did not list any protocols");
    }

    /// <summary>
    /// Runs the real protocol.py through uv against a clone of LabOps-Protocols. Opt-in: set
    /// LAB_PROTOCOLS_REPO to the clone's path.
    /// </summary>
    [Fact]
    public async Task Real_engine_lists_and_checks_the_repository()
    {
        var repo = Environment.GetEnvironmentVariable("LAB_PROTOCOLS_REPO");
        if (string.IsNullOrWhiteSpace(repo))
        {
            Assert.Skip("Set LAB_PROTOCOLS_REPO to a clone of LabOps-Protocols to run this.");
        }

        var tools = new ToolLocator();
        if (tools.Find(Tool.Uv) is null)
        {
            Assert.Skip("uv is not installed.");
        }

        var engine = new ProtocolEngine(new ProcessRunner(tools), tools) { RepositoryPath = repo };
        var list = await engine.ListAsync();

        list.Protocols.ShouldNotBeEmpty();
        list.Protocols.ShouldAllBe(p => p.Folder == $"protocols/{p.Id}");
        list.Problems.ShouldNotContain(p => p.IsError);
        (await engine.CheckStagedAsync(CancellationToken.None)).ShouldNotContain(p => p.IsError);
    }
}
