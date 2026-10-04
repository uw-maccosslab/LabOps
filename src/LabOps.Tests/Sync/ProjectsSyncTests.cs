using LabOps.Core.Repositories;
using LabOps.Core.Sync;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Sync;

/// <summary>
/// Syncing the projects repository, against a real git remote: every commit passes the
/// pre-commit check first, and conflicts name experiments rather than quotes.
/// </summary>
public sealed class ProjectsSyncTests : IDisposable
{
    private readonly GitFixture _git = new();

    public void Dispose() => _git.Dispose();

    [Fact]
    public async Task A_refused_commit_commits_nothing_and_leaves_the_files_for_fixing()
    {
        var alice = _git.Clone("Alice");
        GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/metadata/samples.csv", "Sample_ID,Owner\nS1,SECRET\n");
        var head = GitFixture.Git(alice, "rev-parse", "HEAD");

        var check = new MarkerCheck(alice);
        var result = await _git.ProjectsSyncFor(alice, check).SaveAsync(["projects/Lab/2026-10-Pilot"], "2026-10-Pilot: samples");

        result.Succeeded.ShouldBeFalse();
        result.Committed.ShouldBeFalse();
        result.Pushed.ShouldBeFalse();
        result.Refused.ShouldNotBeNull().ShouldBe(["projects/Lab/2026-10-Pilot/metadata/samples.csv: contains SECRET"]);
        check.Calls.ShouldBe(1);
        GitFixture.Git(alice, "rev-parse", "HEAD").ShouldBe(head);
        GitFixture.Git(alice, "diff", "--cached", "--name-only").ShouldBeEmpty();
        GitFixture.Read(alice, "projects/Lab/2026-10-Pilot/metadata/samples.csv").ShouldContain("SECRET");
        GitFixture.Git(_git.Clone("Checker"), "log", "--format=%s").ShouldBe("seed\n");
    }

    [Fact]
    public async Task Warnings_alone_do_not_stop_a_commit()
    {
        var alice = _git.Clone("Alice");
        GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/metadata/samples.csv", "Sample_ID,QC\nS1,FALSE\n");

        var result = await _git.ProjectsSyncFor(alice, new MarkerCheck(alice)).SaveAsync(["projects/Lab/2026-10-Pilot"], "2026-10-Pilot: samples");

        result.Succeeded.ShouldBeTrue($"{result.Error}");
        result.Pushed.ShouldBeTrue();
        GitFixture.Read(_git.Clone("Checker"), "projects/Lab/2026-10-Pilot/metadata/samples.csv").ShouldContain("S1,FALSE");
    }

    [Fact]
    public async Task Nothing_to_commit_does_not_run_the_check()
    {
        var alice = _git.Clone("Alice");
        var check = new MarkerCheck(alice);

        var result = await _git.ProjectsSyncFor(alice, check).SaveAsync(["projects/Lab/2026-10-Pilot"], "nothing");

        result.Succeeded.ShouldBeTrue();
        result.Committed.ShouldBeFalse();
        check.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Two_people_changing_one_experiment_name_it_in_the_conflict()
    {
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");
        GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nstatus: active\nsamples: 12\n");
        GitFixture.Write(bob, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nstatus: active\nsamples: 14\n");
        (await _git.ProjectsSyncFor(alice, new MarkerCheck(alice)).SaveAsync(["projects"], "Alice")).Succeeded.ShouldBeTrue();

        var result = await _git.ProjectsSyncFor(bob, new MarkerCheck(bob)).SaveAsync(["projects"], "Bob");

        result.Conflict.ShouldNotBeNull().Items.ShouldBe(["2026-10-Pilot"]);
        result.Conflict.OtherAuthor.ShouldBe("Alice");
        result.Conflict.Message.ShouldContain("Alice changed 2026-10-Pilot");
    }

    [Fact]
    public async Task Setting_aside_keeps_work_the_check_would_refuse_on_this_computer_only()
    {
        // Set-aside must never lose work: its commit is local and skips the check, and main is
        // then reset to GitHub's version. Nothing on the set-aside branch is pushed.
        var alice = _git.Clone("Alice");
        var bob = _git.Clone("Bob");
        GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nsamples: 12\n");
        (await _git.ProjectsSyncFor(alice, new MarkerCheck(alice)).SaveAsync(["projects"], "Alice")).Succeeded.ShouldBeTrue();
        GitFixture.Write(bob, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nsamples: 14\n");
        var sync = _git.ProjectsSyncFor(bob, new MarkerCheck(bob));
        (await sync.SaveAsync(["projects"], "Bob")).Conflict.ShouldNotBeNull();
        GitFixture.Write(bob, "projects/Lab/2026-10-Pilot/notes.md", "SECRET draft\n");

        var branch = await sync.SetAsideAsync();

        GitFixture.Git(bob, "show", $"{branch}:projects/Lab/2026-10-Pilot/notes.md").ShouldContain("SECRET");
        File.Exists(Path.Combine(bob, "projects", "Lab", "2026-10-Pilot", "notes.md")).ShouldBeFalse();
        GitFixture.Git(GitFixture.Git(bob, "rev-parse", "--show-toplevel").Trim(), "ls-remote", "origin").ShouldNotContain("set-aside");
    }

    [Fact]
    public void The_projects_repository_cannot_sync_without_its_check()
    {
        var alice = _git.Clone("Alice");
        var tools = new LabOps.Core.Processes.ToolLocator();
        var git = new GitClient(new LabOps.Core.Processes.ProcessRunner(tools), tools) { RepositoryPath = alice };

        Should.Throw<ArgumentException>(() => new SyncService(git, RepositoryProfile.Projects, new NoGeneratedFiles()));
    }

    [Fact]
    public async Task Saving_locally_commits_without_GitHub_and_sharing_pushes_it_after()
    {
        var alice = _git.Clone("Alice");
        var sync = _git.ProjectsSyncFor(alice, new MarkerCheck(alice));
        GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nstatus: on_hold\nsamples: 10\n");

        var saved = await sync.SaveLocallyAsync(["projects/Lab/2026-10-Pilot"], "2026-10-Pilot: on hold");

        saved.Committed.ShouldBeTrue();
        saved.Pushed.ShouldBeFalse();
        GitFixture.Git(alice, "log", "-1", "--format=%s").ShouldBe("2026-10-Pilot: on hold\n");
        GitFixture.Git(_git.Clone("Before"), "log", "--format=%s").ShouldBe("seed\n");
        sync.SyncedWithin(TimeSpan.FromMinutes(1)).ShouldBeFalse();

        var shared = sync.ShareAsync();
        await sync.WhenIdleAsync();

        shared.IsCompleted.ShouldBeTrue();
        (await shared).Pushed.ShouldBeTrue();
        GitFixture.Git(_git.Clone("After"), "log", "-1", "--format=%s").ShouldBe("2026-10-Pilot: on hold\n");
        sync.SyncedWithin(TimeSpan.FromMinutes(1)).ShouldBeTrue();
        sync.SyncedWithin(TimeSpan.Zero).ShouldBeFalse();
    }

    [Fact]
    public async Task Shares_run_one_after_another_and_idle_waits_for_all_of_them()
    {
        var alice = _git.Clone("Alice");
        var sync = _git.ProjectsSyncFor(alice, new MarkerCheck(alice));
        foreach (var status in new[] { "on_hold", "closed" })
        {
            GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/experiment.yaml", $"experiment: 2026-10-Pilot\nstatus: {status}\nsamples: 10\n");
            (await sync.SaveLocallyAsync(["projects/Lab/2026-10-Pilot"], $"2026-10-Pilot: {status}")).Committed.ShouldBeTrue();
            _ = sync.ShareAsync();
        }

        await sync.WhenIdleAsync();

        GitFixture.Git(_git.Clone("After"), "log", "--format=%s").ShouldBe("2026-10-Pilot: closed\n2026-10-Pilot: on_hold\nseed\n");
    }

    [Fact]
    public async Task The_commit_tells_the_hook_which_tree_the_check_passed()
    {
        var alice = _git.Clone("Alice");
        // A hook that records what it was told and what is being committed.
        var hooks = Path.Combine(alice, ".test-hooks");
        Directory.CreateDirectory(hooks);
        File.WriteAllText(Path.Combine(hooks, "pre-commit"),
            "#!/bin/sh\necho \"$LABOPS_CHECKED_TREE $(git write-tree) $CHARGESTATE_CHECKED_TREE\" > .git/hook-saw\n");
        GitFixture.Git(alice, "config", "core.hooksPath", ".test-hooks");
        GitFixture.Write(alice, "projects/Lab/2026-10-Pilot/experiment.yaml", "experiment: 2026-10-Pilot\nstatus: closed\nsamples: 10\n");

        (await _git.ProjectsSyncFor(alice, new MarkerCheck(alice)).SaveLocallyAsync(["projects/Lab/2026-10-Pilot"], "closed"))
            .Committed.ShouldBeTrue();

        var saw = File.ReadAllText(Path.Combine(alice, ".git", "hook-saw")).Trim().Split(' ');
        // A clone whose hook predates the rename reads the variable by the app's earlier name.
        saw.Length.ShouldBe(3);
        saw[0].ShouldBe(saw[1]);
        saw[2].ShouldBe(saw[1]);
        saw[0].ShouldBe(GitFixture.Git(alice, "rev-parse", "HEAD^{tree}").Trim());
        SyncService.CheckedTreeVariable.ShouldBe("LABOPS_CHECKED_TREE");
        SyncService.LegacyCheckedTreeVariable.ShouldBe("CHARGESTATE_CHECKED_TREE");
    }
}
