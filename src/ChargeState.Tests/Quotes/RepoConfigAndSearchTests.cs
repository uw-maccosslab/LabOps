using ChargeState.Core.Infrastructure;
using ChargeState.Core.Quotes;
using ChargeState.Tests.TestSupport;

namespace ChargeState.Tests.Quotes;

public sealed class RepoConfigTests
{
    [Fact]
    public void Approvers_and_minimum_version_are_read()
    {
        var config = RepoConfig.Parse("""
            # comment
            approvers:
              - maccoss
              - Someone-Else
            min_app_version: "26.10.0"
            """);

        config.IsApprover("MacCoss").ShouldBeTrue();
        config.IsApprover("someone-else").ShouldBeTrue();
        config.IsApprover("stranger").ShouldBeFalse();
        config.IsApprover(null).ShouldBeFalse();
        config.RequiresNewerThan(new Version(26, 9, 4)).ShouldBeTrue();
        config.RequiresNewerThan(new Version(26, 10, 0)).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_file_means_nobody_can_send_and_any_version_works()
    {
        using var temp = new TempDirectory();

        var config = RepoConfig.Load(temp.Path);

        config.Approvers.ShouldBeEmpty();
        config.RequiresNewerThan(new Version(0, 0, 1)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("26.10.0", 26, 10, 0)]
    [InlineData("26.10.2-beta.1", 26, 10, 2)]
    [InlineData("26.10", 26, 10, 0)]
    [InlineData("nonsense", 0, 0, 0)]
    public void Versions_parse_without_prerelease_suffixes(string text, int major, int minor, int build) =>
        AppInfo.ParseVersion(text).ShouldBe(new Version(major, minor, build));
}

public sealed class QuoteSearchTests
{
    private static readonly QuoteSummary[] Quotes =
    [
        new() { QuoteNumber = "MacCoss-2026-CWZG-MARTEN", Folder = "quotes/ClearwaterZoo-Cole/2026/MacCoss-2026-CWZG-MARTEN",
                Status = "draft", Year = "2026", Group = "ClearwaterZoo-Cole", Pi = "Ella Cole, Ph.D.", Short = "Mag-Net plasma EV" },
        new() { QuoteNumber = "MacCoss-2026-NWU-SC", Folder = "quotes/Northwind-Park/2026/MacCoss-2026-NWU-SC",
                Status = "sent", Year = "2026", Group = "Northwind-Park", Pi = "Jordan Park, Ph.D.", Short = "Spinal cord" },
        new() { QuoteNumber = "MacCoss-2024-NWU-SPATIAL-NERVE", Folder = "quotes/Northwind-Park/2024/MacCoss-2024-NWU-SPATIAL-NERVE",
                Status = "historical", Year = "2024", Group = "Northwind-Park", Short = "Spatial proteomics of nerves" },
    ];

    [Fact]
    public void Current_hides_historical_estimates()
    {
        var found = new QuoteSearch().Filter(Quotes, "", QuoteFilter.Current);

        found.Select(q => q.QuoteNumber).ShouldBe(["MacCoss-2026-CWZG-MARTEN", "MacCoss-2026-NWU-SC"], ignoreOrder: true);
    }

    [Fact]
    public void Every_word_must_match_somewhere()
    {
        var search = new QuoteSearch();

        search.Filter(Quotes, "park nerve", QuoteFilter.All).ShouldHaveSingleItem().QuoteNumber.ShouldBe("MacCoss-2024-NWU-SPATIAL-NERVE");
        search.Filter(Quotes, "PARK", QuoteFilter.Sent).ShouldHaveSingleItem().QuoteNumber.ShouldBe("MacCoss-2026-NWU-SC");
        search.Filter(Quotes, "park marten", QuoteFilter.All).ShouldBeEmpty();
    }

    [Fact]
    public void Full_text_finds_words_inside_quote_files()
    {
        using var temp = new TempDirectory();
        var folder = Quotes[0].FolderPath(temp.Path);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "quote.yaml"), "project:\n  title: American marten (Martes americana)\n");

        var search = new QuoteSearch();
        search.Index(temp.Path, Quotes);

        search.Filter(Quotes, "martes", QuoteFilter.All).ShouldHaveSingleItem().QuoteNumber.ShouldBe("MacCoss-2026-CWZG-MARTEN");
    }

    [Fact]
    public void Current_quotes_sort_before_historical_and_newest_first()
    {
        var found = new QuoteSearch().Filter(Quotes, "", QuoteFilter.All);

        found.Last().IsHistorical.ShouldBeTrue();
    }
}

public sealed class QuoteFileTests
{
    [Fact]
    public void Open_pdf_prefers_the_sent_pdf_then_the_draft()
    {
        using var temp = new TempDirectory();
        var quote = new QuoteSummary { QuoteNumber = "MacCoss-2026-X", Folder = "quotes/G/2026/MacCoss-2026-X" };
        var folder = quote.FolderPath(temp.Path);
        Directory.CreateDirectory(folder);

        quote.ExistingPdf(temp.Path).ShouldBeNull();

        File.WriteAllText(Path.Combine(folder, "MacCoss-2026-X-draft.pdf"), "draft");
        quote.ExistingPdf(temp.Path).ShouldBe(Path.Combine(folder, "MacCoss-2026-X-draft.pdf"));

        File.WriteAllText(Path.Combine(folder, "MacCoss-2026-X.pdf"), "sent");
        quote.ExistingPdf(temp.Path).ShouldBe(Path.Combine(folder, "MacCoss-2026-X.pdf"));

        File.WriteAllText(Path.Combine(folder, "MacCoss-2026-X-draft.xlsx"), "draft");
        quote.ExistingSpreadsheet(temp.Path).ShouldBe(Path.Combine(folder, "MacCoss-2026-X-draft.xlsx"));
    }
}
