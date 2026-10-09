namespace LabOps.Tests.Engines.Commands;

/// <summary>The README index lists the open work, however many closed projects the repository holds.</summary>
public sealed class IndexTests
{
    private static void Close(string folder) =>
        TestRepo.WriteRaw(folder, TestRepo.Raw(folder).Replace("status: active", "status: closed", StringComparison.Ordinal));

    [Fact]
    public void Closed_projects_and_closed_experiments_are_left_out_of_the_index_and_counted()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Open-DIA", "Open-Project");
        Close(repo.NewExperiment("2026-08-Done-DIA", "Open-Project"));
        foreach (var name in (string[])["Old-Project", "Older-Project"])
        {
            Close(repo.NewProject(name));
        }

        // A lab whose projects are all closed has no rows, so it is not counted either.
        Close(repo.NewProject("Gone-Project", "Gone-Lab"));

        var json = repo.Ok("index");

        var readme = File.ReadAllText(Path.Combine(repo.Root, "README.md"));
        readme.ShouldContain("[Open-Project](projects/Test-Lab/Open-Project/)", Case.Sensitive);
        readme.ShouldContain("[2026-10-Open-DIA](projects/Test-Lab/Open-Project/2026-10-Open-DIA/)", Case.Sensitive);
        readme.ShouldNotContain("Old-Project", Case.Sensitive);
        readme.ShouldNotContain("2026-08-Done-DIA", Case.Sensitive);
        readme.ShouldNotContain("Gone-Lab", Case.Sensitive);
        readme.ShouldContain("\n\nNot listed: 3 closed projects and 1 closed experiment of open projects; "
                             + "`labops projects list` lists everything.\n\n<!-- INDEX:END -->", Case.Sensitive);
        (json["labs"]!.GetValue<int>(), json["projects"]!.GetValue<int>(), json["experiments"]!.GetValue<int>()).ShouldBe((1, 1, 1));
        (json["closed_hidden"]!.GetValue<int>(), json["closed_experiments_hidden"]!.GetValue<int>()).ShouldBe((3, 1));
        repo.RunText("index").Stdout.ShouldBe(
            "README index: 1 labs, 1 projects, 1 experiments (not listed: 3 closed projects and 1 closed experiment of open projects)\n");
    }

    [Fact]
    public void With_nothing_closed_the_index_says_nothing_about_closed_work()
    {
        using var repo = TestRepo.Create();
        repo.NewProject("Open-Project");

        repo.Ok("index")["closed_hidden"]!.GetValue<int>().ShouldBe(0);
        File.ReadAllText(Path.Combine(repo.Root, "README.md")).ShouldNotContain("Not listed", Case.Sensitive);
    }

    [Fact]
    public void An_experiment_still_open_in_a_closed_project_is_a_warning()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Late-DIA", "Shut-Project");
        Close(Path.GetDirectoryName(experiment)!);

        repo.Problems().ShouldContain(("WARN",
            "2026-10-Late-DIA: its project is closed but this experiment is not; close it too, or reopen the project"));
    }
}
