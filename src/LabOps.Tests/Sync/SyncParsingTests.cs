using LabOps.Core.Repositories;
using LabOps.Core.Sync;

namespace LabOps.Tests.Sync;

public sealed class SyncParsingTests
{
    [Theory]
    [InlineData("## main...origin/main\n", SyncState.UpToDate, 0, 0)]
    [InlineData("## main...origin/main [ahead 2]\n", SyncState.Ahead, 2, 0)]
    [InlineData("## main...origin/main [behind 3]\n", SyncState.Behind, 0, 3)]
    [InlineData("## main...origin/main [ahead 1, behind 4]\n", SyncState.Diverged, 1, 4)]
    public void Status_header_gives_ahead_and_behind(string porcelain, SyncState state, int ahead, int behind)
    {
        var status = SyncService.ParseStatus(porcelain);

        status.State.ShouldBe(state);
        status.Ahead.ShouldBe(ahead);
        status.Behind.ShouldBe(behind);
    }

    [Fact]
    public void Local_changes_are_listed_by_path()
    {
        var status = SyncService.ParseStatus("## main...origin/main\n M quotes/G/2026/A/quote.yaml\n?? quotes/G/2026/C/\n");

        status.Changes.ShouldBe(["quotes/G/2026/A/quote.yaml", "quotes/G/2026/C/"]);
        status.Message.ShouldBe("2 unsaved change(s)");
    }

    [Theory]
    [InlineData("quotes/G/2026/A/calculation.md", true)]
    [InlineData("quotes/G/2026/A/quote.md", true)]
    [InlineData("quotes/G/2026/A/A-SOW.md", true)]
    [InlineData("quotes/G/2026/A/A-SOW.docx", false)]
    [InlineData("templates/A-SOW.md", false)]
    [InlineData("README.md", true)]
    [InlineData("quotes/G/2026/A/quote.yaml", false)]
    [InlineData("quotes/G/2026/A/rates.lock.yaml", false)]
    [InlineData("rates/rates.yaml", false)]
    [InlineData("templates/quote.md", false)]
    public void Only_regenerable_files_count_as_generated(string path, bool generated) =>
        RepositoryProfile.Quotes.IsGenerated(path).ShouldBe(generated);

    [Theory]
    [InlineData("README.md", true)]
    [InlineData("projects/Lab/Pilot/project.yaml", false)]
    [InlineData("projects/Lab/Pilot/2026-10-Pilot-DIA/experiment.yaml", false)]
    [InlineData("projects/Lab/Pilot/metadata/samples.csv", false)]
    [InlineData("projects/Lab/Pilot/calculation.md", false)]
    public void Projects_have_no_generated_files_but_the_index(string path, bool generated) =>
        RepositoryProfile.Projects.IsGenerated(path).ShouldBe(generated);

    [Fact]
    public void Quote_folder_is_the_first_four_path_segments() =>
        RepositoryProfile.Quotes.ItemFolder("quotes/UW-Alder/2026/MacCoss-2026-UW-ALDER-GCF15/calculation.md")
            .ShouldBe("quotes/UW-Alder/2026/MacCoss-2026-UW-ALDER-GCF15");

    [Fact]
    public void Project_folder_is_the_first_three_path_segments_and_holds_its_experiments()
    {
        RepositoryProfile.Projects.ItemFolder("projects/Lab/Pilot/metadata/received/manifest.csv")
            .ShouldBe("projects/Lab/Pilot");
        RepositoryProfile.Projects.ItemFolder("projects/Lab/Pilot/2026-10-Pilot-DIA/experiment.yaml")
            .ShouldBe("projects/Lab/Pilot");
        RepositoryProfile.Projects.IsItemPath("projects/Lab/Pilot/project.yaml").ShouldBeTrue();
        RepositoryProfile.Projects.IsItemPath("projects/Lab/lab.yaml").ShouldBeFalse();
        RepositoryProfile.Projects.IsItemPath("config/people.yaml").ShouldBeFalse();
    }

    [Fact]
    public void A_rejected_push_because_github_moved_is_recognized()
    {
        const string error = " ! [rejected]        HEAD -> main (fetch first)\nerror: failed to push some refs";
        SyncService.IsRejectedBecauseBehind(error).ShouldBeTrue();
        SyncService.IsRejectedBecauseBehind("fatal: Authentication failed").ShouldBeFalse();
    }

    [Fact]
    public void Git_errors_are_reworded_for_a_non_expert()
    {
        SyncService.Friendly("fatal: unable to access 'https://github.com/x/': Could not resolve host: github.com")
            .ShouldStartWith("GitHub cannot be reached.");
        SyncService.Friendly("fatal: Authentication failed for 'https://github.com/x'")
            .ShouldContain("sign in to GitHub again");
    }
}
