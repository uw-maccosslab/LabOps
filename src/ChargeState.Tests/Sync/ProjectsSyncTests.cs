using ChargeState.Core.Repositories;
using ChargeState.Core.Sync;
using ChargeState.Tests.TestSupport;

namespace ChargeState.Tests.Sync;

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
        var tools = new ChargeState.Core.Processes.ToolLocator();
        var git = new GitClient(new ChargeState.Core.Processes.ProcessRunner(tools), tools) { RepositoryPath = alice };

        Should.Throw<ArgumentException>(() => new SyncService(git, RepositoryProfile.Projects, new NoGeneratedFiles()));
    }
}
