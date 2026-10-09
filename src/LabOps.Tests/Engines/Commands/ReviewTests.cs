using LabOps.Engines.Projects;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// `labops projects review`: a person records that they read a free-text column for identifiers,
/// and check stops warning about it until its text changes.
/// </summary>
public sealed class ReviewTests
{
    private const string FreeText = "is free text; check it for names, contact details or other identifiers";

    private static string Project(TestRepo repo)
    {
        var project = repo.NewProject("Pilot-Project");
        TestRepo.Samples(project, [["Sample_ID", "QC", "Sample_Group", "Tube_Note", "Blood_Notes"], ["S1", "FALSE", "case", "bubble at bottom", "plasma clots"],
            ["S2", "FALSE", "control", "", "some hemolysis"], ["S3", "TRUE", "pool", "bubbly", ""]]);
        return project;
    }

    private static List<string> Warnings(TestRepo repo, params string[] args) =>
        [.. repo.Problems(args).Where(p => p.Level == "WARN").Select(p => p.Message)];

    [Fact]
    public void A_reviewed_column_is_no_longer_a_warning_until_its_text_changes()
    {
        using var repo = TestRepo.Create();
        var project = Project(repo);
        Warnings(repo).ShouldBe([$"projects/Test-Lab/Pilot-Project/metadata/samples.csv [Tube_Note]: {FreeText}",
                                 $"projects/Test-Lab/Pilot-Project/metadata/samples.csv [Blood_Notes]: {FreeText}"], ignoreOrder: true);

        repo.RunText("review", "Pilot-Project", "metadata/samples.csv", "Tube_Note", "--by", "maccoss").Stdout
            .ShouldStartWith("Pilot-Project: metadata/samples.csv reviewed by maccoss: Tube_Note", Case.Sensitive);

        Warnings(repo).ShouldBe([$"projects/Test-Lab/Pilot-Project/metadata/samples.csv [Blood_Notes]: {FreeText}"]);
        var entry = (PyDictEntry)TestRepo.Yaml(project);
        entry.File.ShouldBe("metadata/samples.csv");
        entry.Column.ShouldBe("Tube_Note");
        entry.By.ShouldBe("maccoss");
        entry.Date.ShouldBe(TestRepo.Today);
        TestRepo.Raw(project).ShouldContain(Reviews.Comment, Case.Sensitive);

        // More rows saying the same things, or in another order, need no new review.
        TestRepo.Samples(project, [["Sample_ID", "QC", "Sample_Group", "Tube_Note", "Blood_Notes"], ["S3", "TRUE", "pool", "bubbly", ""],
            ["S1", "FALSE", "case", "bubble at bottom", "plasma clots"], ["S4", "FALSE", "case", "bubbly", "some hemolysis"]]);
        Warnings(repo).ShouldBe([$"projects/Test-Lab/Pilot-Project/metadata/samples.csv [Blood_Notes]: {FreeText}"]);

        // New wording does.
        TestRepo.Samples(project, [["Sample_ID", "QC", "Sample_Group", "Tube_Note", "Blood_Notes"], ["S1", "FALSE", "case", "given by Dr. Lee", "plasma clots"]]);
        Warnings(repo).ShouldContain("projects/Test-Lab/Pilot-Project/metadata/samples.csv [Tube_Note]: is free text and has changed since "
                                     + $"maccoss read it for identifiers on {TestRepo.Iso(TestRepo.Today)}; check it again");
    }

    /// <summary>The first review entry of the record, typed.</summary>
    private sealed record PyDictEntry(string File, string Column, string Values, string By, DateOnly Date)
    {
        public static explicit operator PyDictEntry(LabOps.Engines.Python.PyDict record)
        {
            var e = (LabOps.Engines.Python.PyDict)((List<object?>)record[Reviews.Key]!)[0]!;
            return new PyDictEntry((string)e["file"]!, (string)e["column"]!, (string)e["values"]!, (string)e["by"]!, (DateOnly)e["date"]!);
        }
    }

    [Fact]
    public void Without_columns_every_free_text_column_of_the_file_is_reviewed()
    {
        using var repo = TestRepo.Create();
        var project = Project(repo);

        repo.Ok("review", "Pilot-Project", "projects/Test-Lab/Pilot-Project/metadata/samples.csv", "--by", "maccoss");

        Warnings(repo).ShouldBeEmpty();
        ((List<object?>)TestRepo.Yaml(project)[Reviews.Key]!).Count.ShouldBe(2);
        repo.Ok("review", "Pilot-Project", "metadata/samples.csv", "Tube_Note", "--by", "jdoe");
        ((List<object?>)TestRepo.Yaml(project)[Reviews.Key]!).Count.ShouldBe(2, "a second review replaces the first");
    }

    [Fact]
    public void A_review_needs_a_free_text_column_of_the_items_own_file_and_who_read_it()
    {
        using var repo = TestRepo.Create();
        Project(repo);
        repo.NewProject("Other-Project");

        repo.Fails("review", "Pilot-Project", "metadata/samples.csv", "QC", "--by", "maccoss")["error"]!.GetValue<string>()
            .ShouldBe("projects/Test-Lab/Pilot-Project/metadata/samples.csv has no free-text column 'QC'; the free-text columns are: "
                      + "Tube_Note, Blood_Notes");
        repo.Fails("review", "Other-Project", "../Pilot-Project/metadata/samples.csv", "--by", "maccoss")["error"]!.GetValue<string>()
            .ShouldStartWith("../Pilot-Project/metadata/samples.csv is not a file of Other-Project", Case.Sensitive);
        repo.Run("review", "Pilot-Project", "metadata/samples.csv").ExitCode.ShouldBe(2, "--by is required");
    }

    [Fact]
    public void The_pre_commit_check_honors_a_review_too()
    {
        using var repo = TestRepo.Create();
        Project(repo);
        repo.Ok("review", "Pilot-Project", "metadata/samples.csv", "--by", "maccoss");
        repo.Git("add", "-A");

        Warnings(repo, "--staged").ShouldBeEmpty();
    }

    [Fact]
    public void A_review_entry_written_by_hand_must_say_which_file_column_and_text()
    {
        using var repo = TestRepo.Create();
        var project = Project(repo);
        TestRepo.WriteRaw(project, TestRepo.Raw(project) + "reviewed:\n  - {file: metadata/samples.csv, column: Tube_Note}\n");

        repo.Problems().ShouldContain(p => p.Level == "ERROR" && p.Message.StartsWith("Pilot-Project: reviewed: each entry is written like", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fingerprint_is_of_the_distinct_wording_not_the_rows()
    {
        Reviews.Fingerprint(["b", "a", "", "b"]).ShouldBe(Reviews.Fingerprint(["a", "b"]));
        Reviews.Fingerprint(["a", "b"]).ShouldNotBe(Reviews.Fingerprint(["a", "b", "c"]));
        Reviews.Fingerprint(["a"]).Length.ShouldBe(12);
    }
}
