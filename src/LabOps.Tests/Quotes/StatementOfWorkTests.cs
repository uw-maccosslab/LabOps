using LabOps.App.ViewModels;
using LabOps.Core.Processes;
using LabOps.Core.Quotes;

namespace LabOps.Tests.Quotes;

/// <summary>The Statement of work button: which sample counts it offers and accepts.</summary>
public sealed class StatementOfWorkTests
{
    [Fact]
    public void It_offers_the_quotes_own_count_with_the_usual_columns()
    {
        new QuoteSummary { StudySamples = 84m }.DefaultSowCounts().ShouldBe([20, 40, 60, 80, 84]);
        new QuoteSummary { StudySamples = 40m }.DefaultSowCounts().ShouldBe([20, 40, 60, 80]);
        new QuoteSummary { StudySamples = 30m, SowSampleCounts = [24, 48] }.DefaultSowCounts().ShouldBe([24, 48]);
    }

    [Theory]
    [InlineData("20, 40, 60, 80", new[] { 20, 40, 60, 80 })]
    [InlineData("80 40;20,20", new[] { 20, 40, 80 })]
    public void Counts_can_be_typed_loosely(string text, int[] expected) =>
        MainViewModel.ParseCounts(text).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("twenty")]
    [InlineData("0, 20")]
    [InlineData("20.5")]
    public void Anything_but_whole_numbers_is_refused(string text) => MainViewModel.ParseCounts(text).ShouldBeNull();

    [Fact]
    public void The_engine_reports_a_saved_statement_of_work()
    {
        const string json = """
            {"ok": true, "quotes": [{"quote_number": "Q", "folder": "quotes/G/2026/Q", "status": "sent",
             "study_samples": 84, "total": 26000.00, "per_sample": 335.83, "issues": [],
             "files": {"pdf": true, "xlsx": true, "draft_pdf": false, "draft_xlsx": false, "sow": true},
             "sow_sample_counts": [20, 40, 60, 80, 84]}]}
            """;

        using var doc = QuoteEngine.Parse(new ProcessResult(0, json, ""));
        var quote = QuoteEngine.ReadQuotes(doc.RootElement).Single();

        quote.Files.ShouldNotBeNull().Sow.ShouldBeTrue();
        quote.SowSampleCounts.ShouldBe([20, 40, 60, 80, 84]);
        quote.DefaultSowCounts().ShouldBe([20, 40, 60, 80, 84]);
    }
}
