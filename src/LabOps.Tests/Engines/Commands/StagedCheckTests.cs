using System.Text;
using System.Text.Json.Nodes;
using LabOps.Engines.CommandLine;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// check --staged: identifiers never reach git history. From LabOps-Projects' tests/test_precommit.py;
/// the tests of the pre-commit hook script itself stay there, with the hook.
/// </summary>
public sealed class StagedCheckTests
{
    private static List<string> Errors(List<(string Level, string Message)> problems) =>
        [.. problems.Where(p => p.Level == "ERROR").Select(p => p.Message)];

    /// <summary>Writes text into the repository's objects and returns its id.</summary>
    private static string Blob(TestRepo repo, string text)
    {
        var file = Path.Combine(Path.GetDirectoryName(repo.Root)!, "blob");
        File.WriteAllText(file, text, new UTF8Encoding(false));
        // --no-filters: the text as it is, as `git hash-object --stdin` takes it.
        return repo.Git("hash-object", "-w", "--no-filters", file).Stdout.Trim();
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    [Fact]
    public void Sample_data_is_checked_whatever_its_format()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var received = Path.Combine(folder, "metadata", "received");
        Write(Path.Combine(received, "manifest.json"), """[{"Sample_ID": "S1", "First Name": "Pat", "Remark": "206-555-0100"}]""");
        Write(Path.Combine(received, "manifest.yaml"), "- {Sample_ID: S1, Owner: Pat}\n");
        Write(Path.Combine(received, "notes.md"), "Shipped by pat@example.org.\n\n| Sample_ID | Last Name |\n| --- | --- |\n| S1 | Doe |\n");
        Write(Path.Combine(folder, "layout", "ids.json"), """["S1", "S2"]""");
        Write(Path.Combine(received, "broken.json"), "{not json");
        Write(Path.Combine(received, "plates.json"), """{"plates": [{"plate": 1, "wells": ["A01", "A02"]}]}""");
        repo.Git("add", "-A");
        var found = Errors(repo.Problems("--staged"));
        foreach (var expected in (string[])["manifest.json [First Name]: looks like a person's name", "manifest.json [Remark]: contains phone numbers",
                     "manifest.yaml [Owner]: looks like an animal owner's details", "notes.md: contains email addresses",
                     "notes.md [Last Name]: looks like a person's name", "ids.json: has values that are not under any key",
                     "broken.json: is not valid JSON"])
        {
            found.ShouldContain(m => m.Contains(expected, StringComparison.Ordinal), expected);
        }

        found.ShouldNotContain(m => m.Contains("plates.json", StringComparison.Ordinal));
        // The full check finds the same.
        found.ToHashSet().ShouldBeSubsetOf(Errors(repo.Problems()));
    }

    [Fact]
    public void What_the_wiki_page_shows_from_the_records_has_no_contact_details()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var lab = Path.GetDirectoryName(project)!;
        const string Shown = "the project's wiki page shows it to the collaborators";
        var name = Path.GetFileName(project);
        repo.Fails("stage", name, "samples_received", "done", "--note", "Call Pat at 206-555-0100")["error"]!.GetValue<string>()
            .ShouldContain(Shown, Case.Sensitive);
        repo.Fails("add-step", name, "other", "--label", "Ask pat@example.org")["error"]!.GetValue<string>()
            .ShouldContain(Shown, Case.Sensitive);
        repo.Fails("link", name, "protocol", "s-trap-micro-digestion", "--version", "1", "--title", "From pat@example.org")["error"]!
            .GetValue<string>().ShouldContain(Shown, Case.Sensitive);
        // Written by hand, the check finds them.
        TestRepo.WriteRaw(project, TestRepo.Raw(project).Replace(
            "  - {id: samples_received, kind: samples_received, status: pending}",
            "  - {id: samples_received, kind: samples_received, status: done, finished: 2026-10-01, note: Call Pat at 206-555-0100}",
            StringComparison.Ordinal));
        TestRepo.WriteRaw(experiment, TestRepo.Raw(experiment).Replace("title: Test experiment", "title: DIA for pat@example.org", StringComparison.Ordinal));
        // A lab's contacts are not on the page, so they stay as they are.
        TestRepo.WriteRaw(lab, TestRepo.Raw(lab).Replace(
            "contacts: []", "contacts:\n  - {name: Pat Example, email: pat@example.org, phone: 206-555-0100}", StringComparison.Ordinal));
        repo.Git("add", "-A");
        var found = Errors(repo.Problems("--staged"));
        found.ShouldContain(m => m.Contains("project.yaml [steps[0].note]: has an email address or phone number", StringComparison.Ordinal));
        found.ShouldContain(m => m.Contains("experiment.yaml [title]: has an email address or phone number", StringComparison.Ordinal));
        found.ShouldNotContain(m => m.Contains("lab.yaml", StringComparison.Ordinal));
        var full = Errors(repo.Problems());
        full.ShouldContain(m => m.Contains("[steps[0].note]", StringComparison.Ordinal));
        full.ShouldNotContain(m => m.Contains("lab.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wiki_yaml_that_does_not_parse_is_refused()
    {
        // Its strings cannot be checked, so it cannot be let through: the phone number here would be.
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        Write(Path.Combine(project, "wiki.yaml"), "summary: Call Pat at 206-555-0100\nplan: [one, two\n");
        repo.Git("add", "-A");
        Errors(repo.Problems("--staged"))
            .ShouldContain(m => m.Contains("wiki.yaml: is not valid YAML, so it cannot be checked", StringComparison.Ordinal));
    }

    [Fact]
    public void Sheets_and_identifying_csvs_are_refused_anywhere_in_the_repository()
    {
        using var repo = TestRepo.Create();
        repo.NewProject();
        File.WriteAllBytes(Path.Combine(repo.Root, "BioTRACK manifest.xlsx"), "PK\u0003\u0004 not really a workbook"u8.ToArray());
        TestRepo.WriteCsv(Path.Combine(repo.Root, "docs", "patients.csv"), [["First Name", "Email"], ["Pat", "pat@example.org"]]);
        TestRepo.WriteCsv(Path.Combine(repo.Root, "docs", "plates.csv"), [["Plate", "Well"], ["1", "A01"]]);
        Write(Path.Combine(repo.Root, "docs", "notes.md"), "How the engine works.\n");
        repo.Git("add", "-A");
        var found = Errors(repo.Problems("--staged"));
        found.ShouldContain(m => m.StartsWith("BioTRACK manifest.xlsx: spreadsheets, PDFs and documents are never committed", StringComparison.Ordinal));
        found.ShouldContain(m => m.StartsWith("docs/patients.csv [First Name]", StringComparison.Ordinal));
        found.ShouldNotContain(m => m.Contains("plates.csv", StringComparison.Ordinal) || m.Contains("notes.md", StringComparison.Ordinal));
    }

    [Fact]
    public void A_link_and_a_link_replaced_by_a_file_are_checked()
    {
        using var repo = TestRepo.Create();
        repo.NewProject();
        repo.Commit("project");
        const string Link = "projects/Test-Lab/Test-Project/metadata/received/manifest.csv";
        repo.Git("update-index", "--add", "--cacheinfo", $"120000,{Blob(repo, "../../../../inbox/manifest.csv")},{Link}");
        Errors(repo.Problems("--staged")).ShouldContain(m => m.Contains("only files are committed under projects/", StringComparison.Ordinal));
        repo.Git("commit", "-q", "--no-verify", "-m", "a link");
        // What `git add` does on macOS or Linux when the link is replaced by the file it pointed to.
        repo.Git("update-index", "--cacheinfo", $"100644,{Blob(repo, "Sample_ID,First Name\nS1,Pat\n")},{Link}");
        repo.Git("diff", "--cached", "--name-status").Stdout.ShouldStartWith("T\t", caseSensitivity: Case.Sensitive);
        Errors(repo.Problems("--staged")).ShouldContain(m => m.StartsWith($"{Link} [First Name]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_renamed_file_is_checked_at_its_new_path()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        TestRepo.WriteCsv(Path.Combine(folder, "metadata", "received", "a.csv"), [["Sample_ID", "Email"], ["S1", "pat@example.org"]]);
        repo.Commit("committed without the hook");
        const string Old = "projects/Test-Lab/Test-Project/metadata/received/a.csv";
        const string New = "projects/Test-Lab/Test-Project/metadata/received/b.csv";
        repo.Git("mv", Old, New);
        repo.Git("diff", "--cached", "--name-status").Stdout.ShouldStartWith("R100\t", caseSensitivity: Case.Sensitive);
        Errors(repo.Problems("--staged")).ShouldContain(m => m.StartsWith($"{New} [Email]", StringComparison.Ordinal));
    }

    [Fact]
    public void The_check_runs_in_a_git_worktree()
    {
        using var repo = TestRepo.Create();
        repo.NewProject();
        repo.Commit("project");
        var worktree = Path.Combine(Path.GetDirectoryName(repo.Root)!, "worktree");
        repo.Git("worktree", "add", "-q", "--detach", worktree);
        TestRepo.WriteCsv(Path.Combine(worktree, "projects", "Test-Lab", "Test-Project", "metadata", "samples.csv"),
            [["Sample_ID", "QC", "Owner Name"], ["S1", "FALSE", "Pat"]]);
        TestRepo.RunGit(worktree, true, "add", "-A");
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = LabopsCommandLine.Run(["projects", "--root", worktree, "--json", "check", "--staged"], stdout, stderr, worktree);
        var output = stdout.ToString();
        var messages = JsonNode.Parse(output)!["problems"]!.AsArray().Select(p => p!["message"]!.GetValue<string>()).ToList();
        exitCode.ShouldBe(1, output);
        messages.ShouldContain(m => m.Contains("[Owner Name]", StringComparison.Ordinal), output);
    }

    [Fact]
    public void A_record_the_listing_could_not_use_is_refused()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        TestRepo.WriteRaw(project, TestRepo.Raw(project).Replace("title: Test project", "title: Test: project: {unclosed", StringComparison.Ordinal));
        TestRepo.WriteRaw(experiment, TestRepo.Raw(experiment).Replace(
            "  - {id: data_acquisition, kind: data_acquisition, status: pending}",
            "  - {id: data_acquisition, kind: data_acquisition, status: done, started: 2026-10-05, finished: 2026-09-01}",
            StringComparison.Ordinal));
        repo.Git("add", "-A");
        var found = Errors(repo.Problems("--staged"));
        found.ShouldContain(m => m.StartsWith("projects/Test-Lab/Test-Project/project.yaml: project.yaml is not valid YAML", StringComparison.Ordinal));
        found.ShouldContain(m => m.EndsWith("experiment.yaml: step data_acquisition: finished before it started", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spreadsheet_or_pdf_under_projects_is_refused()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        File.WriteAllBytes(Path.Combine(folder, "metadata", "received", "manifest.xlsx"), "PK\u0003\u0004 not really a workbook"u8.ToArray());
        File.WriteAllBytes(Path.Combine(folder, "metadata", "received", "manifest.pdf"), "%PDF-1.7"u8.ToArray());
        repo.Git("add", "-A");
        var found = Errors(repo.Problems("--staged"));
        found.Count.ShouldBe(2);
        found.ShouldAllBe(m => m.Contains("only CSV, YAML, JSON and Markdown", StringComparison.Ordinal));
    }

    [Fact]
    public void An_inbox_file_forced_into_the_index_is_refused()
    {
        using var repo = TestRepo.Create();
        var original = Path.Combine(repo.Root, "inbox", "2026-10-Test", "manifest.csv");
        TestRepo.WriteCsv(original, [["Sample_ID"], ["S1"]]);
        repo.Git("add", "-f", "inbox/2026-10-Test/manifest.csv");
        Errors(repo.Problems("--staged")).ShouldContain(m => m.Contains("inbox/ holds originals", StringComparison.Ordinal));
    }

    [Fact]
    public void The_staged_version_is_what_counts()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject("Cohort", "Test-Lab", "--human");
        var samples = Path.Combine(folder, "metadata", "samples.csv");
        TestRepo.WriteCsv(samples, [["Sample_ID", "QC", "Contact email"], ["S1", "FALSE", "pat@example.org"]]);
        repo.Git("add", "-A");
        // Fixing the working copy without staging the fix does not help: the commit would still
        // contain the addresses.
        TestRepo.WriteCsv(samples, [["Sample_ID", "QC"], ["S1", "FALSE"]]);
        Errors(repo.Problems("--staged")).ShouldContain(m => m.Contains("Contact email", StringComparison.Ordinal));
        repo.Git("add", "-A");
        Errors(repo.Problems("--staged")).ShouldBeEmpty();
    }

    [Fact]
    public void Dates_and_ages_can_be_committed_in_a_human_study()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject("BioTRACK", "MNRF-Test", "--human");
        TestRepo.WriteCsv(Path.Combine(folder, "metadata", "samples.csv"),
            [["Sample_ID", "QC", "Specimen Created On", "Age"], ["S1", "FALSE", "2014-03-14 09:30", "91"]]);
        repo.Git("add", "-A");
        Errors(repo.Problems("--staged")).ShouldBeEmpty();
    }

    [Fact]
    public void Full_check_reads_the_working_copy_too()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        TestRepo.WriteCsv(Path.Combine(folder, "metadata", "received", "manifest.csv"), [["Sample_ID", "Email"], ["S1", "a@b.org"]]);
        var found = Errors(repo.Problems());
        found.ShouldContain(m => m.Contains("[Email]", StringComparison.Ordinal) && m.Contains("email address", StringComparison.Ordinal));
    }
}
