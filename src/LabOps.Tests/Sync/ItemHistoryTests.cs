using LabOps.Core.Processes;
using LabOps.Core.Repositories;
using LabOps.Core.Sync;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Sync;

/// <summary>The Modified column: when each quote or experiment folder last changed.</summary>
public sealed class ItemHistoryTests
{
    [Fact]
    public void The_newest_commit_touching_a_folder_wins()
    {
        const string log = """
            @@2026-10-03T15:00:00-07:00

            quotes/G/2026/A/quote.yaml
            quotes/G/2026/A/calculation.md

            @@2026-09-01T09:00:00-07:00

            quotes/G/2026/A/quote.yaml
            quotes/G/2026/B/quote.yaml
            quotes/README-not-a-quote.md
            """;

        var times = ItemHistory.Parse(log, RepositoryProfile.Quotes);

        times["quotes/G/2026/A"].ShouldBe(DateTimeOffset.Parse("2026-10-03T15:00:00-07:00"));
        times["quotes/G/2026/B"].ShouldBe(DateTimeOffset.Parse("2026-09-01T09:00:00-07:00"));
        times.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Git_history_and_unsaved_changes_give_each_folder_its_time()
    {
        using var git = new GitFixture();
        var clone = git.Clone("Alice");
        GitFixture.Write(clone, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 12\n");
        GitFixture.Git(clone, "commit", "-q", "-a", "--date=2020-01-02T03:04:05Z", "-m", "A: changed long ago");
        GitFixture.Write(clone, "quotes/G/2026/B/quote.yaml", "quote_number: B\nstatus: draft\nsamples: 99\n");
        var before = DateTimeOffset.Now;

        var tools = new ToolLocator();
        var repository = new RepositoryFactory(new ProcessRunner(tools), tools)
            .Open(RepositoryProfile.Quotes, clone, new RecordingRebuilder(clone), check: null);
        var times = await ItemHistory.LastModifiedAsync(repository);

        times.Keys.Order().ShouldBe(["quotes/G/2026/A", "quotes/G/2026/B"]);
        times["quotes/G/2026/A"].ShouldBe(DateTimeOffset.Parse("2020-01-02T03:04:05Z"));
        times["quotes/G/2026/B"].ShouldBeGreaterThanOrEqualTo(before);
    }
}
