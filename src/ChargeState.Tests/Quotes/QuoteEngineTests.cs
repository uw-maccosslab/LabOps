using ChargeState.Core.Processes;
using ChargeState.Core.Quotes;

namespace ChargeState.Tests.Quotes;

public sealed class QuoteEngineTests
{
    [Fact]
    public void Real_list_output_is_read_into_summaries()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "quote-list.json"));

        using var doc = QuoteEngine.Parse(new ProcessResult(0, json, ""));
        var quotes = QuoteEngine.ReadQuotes(doc.RootElement);

        var draft = quotes.Single(q => q.QuoteNumber == "MacCoss-2026-NWU-SC");
        draft.IsDraft.ShouldBeTrue();
        draft.Folder.ShouldBe("quotes/Northwind-Park/2026/MacCoss-2026-NWU-SC");
        draft.Total.ShouldBe(12345.67m);
        draft.PerSample.ShouldBe(411.52m);
        draft.StudySamples.ShouldBe(30m);
        draft.Client.ShouldBe("Jordan Park, Ph.D.");
        draft.Files.ShouldNotBeNull().Pdf.ShouldBeFalse();
        draft.HasErrors.ShouldBeFalse();

        var historical = quotes.Single(q => q.QuoteNumber == "MacCoss-2025-UW-FENWICK-ABC");
        historical.IsHistorical.ShouldBeTrue();
        historical.MatchesTab.ShouldBe(true);
        historical.Group.ShouldBe("UW-Fenwick");
    }

    [Fact]
    public void An_engine_error_becomes_a_message_for_the_user()
    {
        var ex = Should.Throw<QuoteEngineException>(() => QuoteEngine.Parse(new ProcessResult(1,
            """{"ok": false, "error": "MacCoss-2026-X is already sent; make a revision instead"}""", "")));

        ex.Message.ShouldBe("MacCoss-2026-X is already sent; make a revision instead");
    }

    [Fact]
    public void Output_that_is_not_json_reports_what_went_wrong()
    {
        var ex = Should.Throw<QuoteEngineException>(() => QuoteEngine.Parse(new ProcessResult(2, "",
            "error: Failed to download Python")));

        ex.Message.ShouldBe("The quote engine could not run: error: Failed to download Python");
    }

    [Fact]
    public void A_build_with_errors_still_returns_the_quote_and_its_issues()
    {
        const string json = """
            {"ok": false, "quotes": [{"quote_number": "Q", "folder": "quotes/G/2026/Q", "status": "draft",
             "study_samples": 10, "total": 100.0, "per_sample": 10.0,
             "issues": [{"level": "ERROR", "message": "quote text contains an em dash"}]}]}
            """;

        using var doc = QuoteEngine.Parse(new ProcessResult(1, json, ""), allowNotOk: true);
        var quote = QuoteEngine.ReadQuotes(doc.RootElement).Single();

        quote.HasErrors.ShouldBeTrue();
        quote.Issues.Single().IsError.ShouldBeTrue();
    }

    /// <summary>
    /// Runs the real quote.py through uv against a clone of the quotes repository. Opt-in:
    /// set SERVICES_QUOTES_REPO to the clone's path.
    /// </summary>
    [Fact]
    public async Task Real_engine_lists_every_quote_without_errors()
    {
        var repo = Environment.GetEnvironmentVariable("SERVICES_QUOTES_REPO");
        if (string.IsNullOrWhiteSpace(repo))
        {
            Assert.Skip("Set SERVICES_QUOTES_REPO to a clone of services-quotes to run this.");
        }

        var tools = new ToolLocator();
        if (tools.Find(Tool.Uv) is null)
        {
            Assert.Skip("uv is not installed.");
        }

        var engine = new QuoteEngine(new ProcessRunner(tools), tools) { RepositoryPath = repo };
        var quotes = await engine.ListAsync();

        quotes.ShouldNotBeEmpty();
        quotes.ShouldAllBe(q => q.Error == null);
        quotes.Where(q => q.IsHistorical).ShouldAllBe(q => q.MatchesTab == true);
    }
}
