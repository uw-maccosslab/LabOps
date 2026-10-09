namespace LabOps.Tests.Engines.Commands;

/// <summary>The README index lists the open work, however many closed projects the repository holds.</summary>
public sealed class IndexTests
{
    [Fact]
    public void Closed_projects_are_left_out_of_the_index_and_counted()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Open-DIA", "Open-Project");
        foreach (var name in (string[])["Old-Project", "Older-Project"])
        {
            var closed = repo.NewProject(name);
            TestRepo.WriteRaw(closed, TestRepo.Raw(closed).Replace("status: active", "status: closed", StringComparison.Ordinal));
        }

        var json = repo.Ok("index");

        var readme = File.ReadAllText(Path.Combine(repo.Root, "README.md"));
        readme.ShouldContain("[Open-Project](projects/Test-Lab/Open-Project/)", Case.Sensitive);
        readme.ShouldContain("[2026-10-Open-DIA](projects/Test-Lab/Open-Project/2026-10-Open-DIA/)", Case.Sensitive);
        readme.ShouldNotContain("Old-Project", Case.Sensitive);
        readme.ShouldContain("\n\n2 closed projects are not listed; `labops projects list` lists every project.\n\n<!-- INDEX:END -->", Case.Sensitive);
        (json["projects"]!.GetValue<int>(), json["closed_hidden"]!.GetValue<int>()).ShouldBe((1, 2));
        repo.RunText("index").Stdout.ShouldBe("README index: 1 labs, 1 projects, 1 experiments (2 closed project(s) not listed)\n");
    }

    [Fact]
    public void With_nothing_closed_the_index_says_nothing_about_closed_projects()
    {
        using var repo = TestRepo.Create();
        repo.NewProject("Open-Project");

        repo.Ok("index")["closed_hidden"]!.GetValue<int>().ShouldBe(0);
        File.ReadAllText(Path.Combine(repo.Root, "README.md")).ShouldNotContain("closed project", Case.Sensitive);
    }
}
