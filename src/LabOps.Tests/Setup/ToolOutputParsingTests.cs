using LabOps.Core.GitHub;
using LabOps.Core.Repositories;
using LabOps.Core.Setup;

namespace LabOps.Tests.Setup;

public sealed class ToolOutputParsingTests
{
    [Fact]
    public void Claude_auth_status_reports_the_signed_in_account()
    {
        var auth = SetupService.ParseClaudeAuth("""
            {"loggedIn": true, "authMethod": "claude.ai", "email": "someone@uw.edu", "orgName": "MacCoss Lab", "subscriptionType": "team"}
            """);

        auth.ShouldNotBeNull();
        auth.LoggedIn.ShouldBeTrue();
        auth.Email.ShouldBe("someone@uw.edu");
        auth.OrgName.ShouldBe("MacCoss Lab");
        auth.Subscription.ShouldBe("team");
    }

    [Fact]
    public void Unreadable_claude_status_means_not_signed_in() =>
        SetupService.ParseClaudeAuth("Not logged in").ShouldBeNull();

    [Fact]
    public void Latest_check_run_is_read_from_gh()
    {
        var run = GitHubCli.ParseCheckRun("""
            [{"conclusion":"failure","createdAt":"2026-10-03T21:00:00Z","headSha":"abc","status":"completed","url":"https://github.com/x/runs/1"}]
            """);

        run.ShouldNotBeNull();
        run.Failed.ShouldBeTrue();
        run.Passed.ShouldBeFalse();
        run.Url.ShouldBe("https://github.com/x/runs/1");
    }

    [Fact]
    public void A_running_check_has_no_conclusion_yet()
    {
        var run = GitHubCli.ParseCheckRun("""
            [{"conclusion":"","createdAt":"2026-10-03T21:00:00Z","headSha":"abc","status":"in_progress","url":"u"}]
            """);

        run.ShouldNotBeNull().InProgress.ShouldBeTrue();
        run.Conclusion.ShouldBeNull();
    }

    [Fact]
    public void No_runs_yet_is_not_an_error() => GitHubCli.ParseCheckRun("[]").ShouldBeNull();

    [Theory]
    [InlineData("https://github.com/uw-maccosslab/LabOps-Quotes.git", true)]
    [InlineData("https://github.com/uw-maccosslab/LabOps-Quotes", true)]
    [InlineData("git@github.com:uw-maccosslab/LabOps-Quotes.git", true)]
    [InlineData("https://github.com/uw-maccosslab/LabOps.git", false)]
    [InlineData("https://github.com/someone/LabOps-Quotes.git", false)]
    [InlineData("https://github.com/uw-maccosslab/services-quotes.git", true)]  // its name until October 2026
    [InlineData("git@github.com:uw-maccosslab/services-quotes.git", true)]
    public void Only_the_quotes_repository_is_accepted_as_an_existing_copy(string url, bool accepted) =>
        RepositoryProfile.Quotes.IsRemote(url).ShouldBe(accepted);

    [Theory]
    [InlineData("https://github.com/uw-maccosslab/LabOps-Projects.git", true)]
    [InlineData("git@github.com:uw-maccosslab/LabOps-Projects", true)]
    [InlineData("https://github.com/uw-maccosslab/LabOps-Quotes.git", false)]
    [InlineData("https://github.com/uw-maccosslab/lab-projects.git", true)]  // its name until October 2026
    [InlineData("https://github.com/uw-maccosslab/services-quotes.git", false)]
    public void Only_the_projects_repository_is_accepted_as_a_projects_copy(string url, bool accepted) =>
        RepositoryProfile.Projects.IsRemote(url).ShouldBe(accepted);

    [Theory]
    [InlineData("https://github.com/uw-maccosslab/lab-projects.git", "https://github.com/uw-maccosslab/LabOps-Projects.git")]
    [InlineData("https://github.com/uw-maccosslab/lab-projects", "https://github.com/uw-maccosslab/LabOps-Projects")]
    [InlineData("git@github.com:uw-maccosslab/lab-projects.git\n", "git@github.com:uw-maccosslab/LabOps-Projects.git")]  // as git prints it
    [InlineData("https://github.com/uw-maccosslab/LabOps-Projects.git", null)]
    [InlineData("https://github.com/someone/lab-projects.git", null)]
    public void A_clone_made_under_the_former_name_gets_its_remote_renamed(string url, string? renamed) =>
        RepositoryProfile.Projects.RenamedRemote(url).ShouldBe(renamed);

    [Fact]
    public void The_quotes_remote_is_renamed_too_and_new_clones_get_the_new_folder_names()
    {
        RepositoryProfile.Quotes.RenamedRemote("https://github.com/uw-maccosslab/services-quotes.git")
            .ShouldBe("https://github.com/uw-maccosslab/LabOps-Quotes.git");
        (RepositoryProfile.Projects.GitHubName, RepositoryProfile.Quotes.GitHubName)
            .ShouldBe(("uw-maccosslab/LabOps-Projects", "uw-maccosslab/LabOps-Quotes"));
        (RepositoryProfile.Projects.DefaultFolderName, RepositoryProfile.Quotes.DefaultFolderName)
            .ShouldBe(("LabOps-Projects", "LabOps-Quotes"));
    }

    [Fact]
    public void The_quotes_are_optional_but_the_projects_are_not()
    {
        SetupItem Item(SetupStep step, bool done, bool optional = false) => new(step, step.ToString(), done, "", null, Optional: optional);

        SetupService.AllDone([Item(SetupStep.Git, true), Item(SetupStep.ProjectsRepository, true),
            Item(SetupStep.QuotesRepository, false, optional: true)]).ShouldBeTrue();
        SetupService.AllDone([Item(SetupStep.Git, true), Item(SetupStep.ProjectsRepository, false),
            Item(SetupStep.QuotesRepository, true, optional: true)]).ShouldBeFalse();
    }

    [Fact]
    public void An_existing_clone_is_found_by_its_engine_not_its_name()
    {
        using var temp = new LabOps.Tests.TestSupport.TempDirectory();
        var projects = temp.Combine("anything");
        Directory.CreateDirectory(Path.Combine(projects, ".git"));
        Directory.CreateDirectory(Path.Combine(projects, "scripts"));
        File.WriteAllText(Path.Combine(projects, "scripts", "project.py"), "");

        SetupService.FindExistingClone(RepositoryProfile.Projects, projects).ShouldBe(projects);
        RepositoryProfile.Quotes.LooksLikeClone(projects).ShouldBeFalse();
    }

    [Fact]
    public void Commit_email_is_the_github_noreply_address() =>
        new GitHubUser("maccoss", "Mike MacCoss", 40654111).NoReplyEmail.ShouldBe("40654111+maccoss@users.noreply.github.com");
}
