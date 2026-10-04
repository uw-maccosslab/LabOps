using LabOps.Core.Sync;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Sync;

/// <summary>
/// Two people saving at once, against a real git remote. These are the cases the rebase-only
/// workflow exists for, so they run git for real rather than a fake.
/// </summary>
public sealed class SyncServiceTests : IDisposable
{
    private readonly GitFixture _git = new();

    public void Dispose() => _git.Dispose();

    /// <summary>Asserts success, showing what went wrong when it did not.</summary>
    private static SaveResult Ok(SaveResult result)
    {
        result.Succeeded.ShouldBeTrue($"error: {result.Error}; conflict: {result.Conflict?.Message} files: {string.Join(", ", result.Conflict?.Files ?? [])}");
        return result;
    }

    [Fact]
    public async Task Different_quotes_both_sync_with_linear_history()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");

        GitFixture.Write(alice, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 20\n");
        GitFixture.Write(bob, "quotes/G/2026/B/quote.yaml", "quote_number: B\nstatus: draft\nsamples: 30\n");

        var first = await _git.SyncFor(alice).SaveAsync(["quotes/G/2026/A"], "A: draft");
        var second = await _git.SyncFor(bob).SaveAsync(["quotes/G/2026/B"], "B: draft");

        Ok(first).ShouldSatisfyAllConditions( r => r.Committed.ShouldBeTrue(), r => r.Pushed.ShouldBeTrue());
        Ok(second).ShouldSatisfyAllConditions( r => r.Committed.ShouldBeTrue(), r => r.Pushed.ShouldBeTrue());

        var check = _git.Clone("Checker");
        GitFixture.Read(check, "quotes/G/2026/A/quote.yaml").ShouldContain("samples: 20");
        GitFixture.Read(check, "quotes/G/2026/B/quote.yaml").ShouldContain("samples: 30");
        GitFixture.Git(check, "rev-list", "--merges", "--count", "HEAD").Trim().ShouldBe("0");
        GitFixture.Git(check, "log", "--format=%s").ShouldBe("B: draft\nA: draft\nseed\n");
    }

    [Fact]
    public async Task Conflict_only_in_generated_files_is_resolved_by_rebuilding()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");

        // Both change the same quote's generated text, in ways git cannot merge, but Bob's input
        // change touches a different line of quote.yaml than anything Alice changed.
        GitFixture.Write(alice, "quotes/G/2026/A/calculation.md", "# A\ntotal 111\n");
        GitFixture.Write(bob, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 12\n");
        GitFixture.Write(bob, "quotes/G/2026/A/calculation.md", "# A\ntotal 222\n");

        Ok(await _git.SyncFor(alice).SaveAsync(["quotes/G/2026/A"], "A: Alice"));

        var rebuilder = new RecordingRebuilder(bob);
        var result = await _git.SyncFor(bob, rebuilder).SaveAsync(["quotes/G/2026/A"], "A: Bob");

        Ok(result);
        result.Pushed.ShouldBeTrue();
        rebuilder.Folders.ShouldBe(["quotes/G/2026/A"]);
        GitFixture.Read(bob, "quotes/G/2026/A/calculation.md").ShouldContain("samples: 12");
        GitFixture.Git(bob, "rev-list", "--merges", "--count", "HEAD").Trim().ShouldBe("0");
    }

    [Fact]
    public async Task Same_quote_edited_by_two_people_stops_and_keeps_local_work()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");

        GitFixture.Write(alice, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 40\n");
        GitFixture.Write(bob, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 50\n");

        Ok(await _git.SyncFor(alice).SaveAsync(["quotes/G/2026/A"], "A: Alice"));

        var sync = _git.SyncFor(bob);
        var result = await sync.SaveAsync(["quotes/G/2026/A"], "A: Bob");

        result.Conflict.ShouldNotBeNull();
        result.Conflict.Items.ShouldBe(["A"]);
        result.Conflict.OtherAuthor.ShouldBe("Alice");
        result.Pushed.ShouldBeFalse();
        sync.Status.State.ShouldBe(SyncState.Conflict);

        // Nothing lost and nothing half-done: Bob's commit is still there, no rebase is open.
        Directory.Exists(Path.Combine(bob, ".git", "rebase-merge")).ShouldBeFalse();
        GitFixture.Git(bob, "log", "-1", "--format=%s").Trim().ShouldBe("A: Bob");
        GitFixture.Read(bob, "quotes/G/2026/A/quote.yaml").ShouldContain("samples: 50");
    }

    [Fact]
    public async Task Setting_aside_after_a_conflict_keeps_both_versions_and_unblocks_sync()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");
        GitFixture.Write(alice, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 40\n");
        GitFixture.Write(bob, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 50\n");
        Ok(await _git.SyncFor(alice).SaveAsync(["quotes/G/2026/A"], "A: Alice"));
        var sync = _git.SyncFor(bob);
        (await sync.SaveAsync(["quotes/G/2026/A"], "A: Bob")).Conflict.ShouldNotBeNull();

        var branch = await sync.SetAsideAsync();

        branch.ShouldStartWith("set-aside/");
        GitFixture.Read(bob, "quotes/G/2026/A/quote.yaml").ShouldContain("samples: 40");
        GitFixture.Git(bob, "show", $"{branch}:quotes/G/2026/A/quote.yaml").ShouldContain("samples: 50");
        sync.Status.State.ShouldBe(SyncState.UpToDate);
        Ok(await sync.SyncAsync());
    }

    [Fact]
    public async Task Saving_with_no_changes_commits_nothing_and_brings_in_others_work()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");
        GitFixture.Write(alice, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 99\n");
        Ok(await _git.SyncFor(alice).SaveAsync(["quotes/G/2026/A"], "A: Alice"));

        var sync = _git.SyncFor(bob);
        var updated = false;
        sync.RepositoryUpdated += () => updated = true;
        var result = await sync.SaveAsync(["quotes/G/2026/B"], "B: nothing");

        result.Committed.ShouldBeFalse();
        Ok(result);
        updated.ShouldBeTrue();
        GitFixture.Read(bob, "quotes/G/2026/A/quote.yaml").ShouldContain("samples: 99");
        sync.Status.State.ShouldBe(SyncState.UpToDate);
    }

    [Fact]
    public async Task Readme_conflict_takes_the_github_version()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");
        GitFixture.Write(alice, "README.md", "index from the workflow\n");
        GitFixture.Write(bob, "README.md", "index Bob regenerated by hand\n");

        Ok(await _git.SyncFor(alice).SaveAsync(["README.md"], "README: index"));
        var result = await _git.SyncFor(bob).SaveAsync(["README.md"], "README: by hand");

        Ok(result);
        GitFixture.Read(bob, "README.md").ShouldBe("index from the workflow\n");
    }

    [Fact]
    public async Task Refresh_reports_new_work_on_github()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");
        GitFixture.Write(alice, "quotes/G/2026/A/quote.yaml", "quote_number: A\nstatus: draft\nsamples: 7\n");
        Ok(await _git.SyncFor(alice).SaveAsync(["quotes/G/2026/A"], "A: Alice"));

        var status = await _git.SyncFor(bob).RefreshAsync(fetch: true);

        status.State.ShouldBe(SyncState.Behind);
        status.Behind.ShouldBe(1);
    }
}
