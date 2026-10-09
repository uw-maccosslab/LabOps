using System.Text;
using LabOps.Engines.Projects;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Engines;

/// <summary>
/// The commands that write, against the Python engine: a scenario of every command and its
/// refusals, run on three copies of one repository (Python with --json, Python as text, and C#),
/// comparing the answers and every file after each step. Opt-in, like ParityTests.
/// </summary>
public sealed class ParityWriteTests
{
    private sealed record Step(string[] Args, Func<ProjectsEngine, string, CommandResult> Run);

    private static Step S(string[] args, Func<ProjectsEngine, string, CommandResult> run) => new(args, run);

    private static Step S(string[] args, Func<ProjectsEngine, CommandResult> run) => new(args, (e, _) => run(e));

    private static readonly Step[] Scenario =
    [
        S(["new-lab", "NewLab-X", "--title", "New lab: X", "--pi", "Pat Example, Ph.D.", "--institution", "Example U", "--lab-contact", "maccoss"],
            e => e.NewLab("NewLab-X", "New lab: X", "Pat Example, Ph.D.", "Example U", "maccoss")),
        S(["new-lab", "Bad_Name", "--title", "T", "--pi", "P", "--institution", "I"], e => e.NewLab("Bad_Name", "T", "P", "I")),
        S(["new-lab", "TestLab-One", "--title", "T", "--pi", "P", "--institution", "I"], e => e.NewLab("TestLab-One", "T", "P", "I")),
        S(["new-project", "NewLab-X", "Proj-A", "--title", "A: project", "--funding", "quote", "--quote", "Q-1", "--quote", "Q-2", "--human",
                "--species", "Homo sapiens", "--sample-type", "plasma", "--expected-samples", "40", "--series", "s1", "--lab-contact", "maccoss"],
            e => e.NewProject("NewLab-X", "Proj-A", new NewProjectOptions("A: project", "quote", ["Q-1", "Q-2"], null, true, "maccoss", "s1", "Homo sapiens", "plasma", 40))),
        S(["new-project", "NewLab-X", "Proj-B", "--title", "yes"], e => e.NewProject("NewLab-X", "Proj-B", new NewProjectOptions("yes"))),
        S(["new-project", "NoSuchLab", "Proj-C", "--title", "C"], e => e.NewProject("NoSuchLab", "Proj-C", new NewProjectOptions("C"))),
        S(["new-project", "NewLab-X", "bad_name", "--title", "C"], e => e.NewProject("NewLab-X", "bad_name", new NewProjectOptions("C"))),
        S(["new-project", "NewLab-X", "Full-Project", "--title", "C"], e => e.NewProject("NewLab-X", "Full-Project", new NewProjectOptions("C"))),
        S(["new-experiment", "Proj-A", "2026-10-A-DIA", "--title", "DIA, extended gradient", "--instrument", "Orbitrap Astral",
                "--with", "assay_development", "--with", "other=Unblinded metadata", "--with", "other = Unblinded metadata ", "--lab-contact", "jdoe"],
            e => e.NewExperiment("Proj-A", "2026-10-A-DIA", new NewExperimentOptions("DIA, extended gradient", "Orbitrap Astral",
                WithSteps: ["assay_development", "other=Unblinded metadata", "other = Unblinded metadata "], LabContact: "jdoe"))),
        S(["new-experiment", "Proj-A", "2026-11-A-PRM", "--title", "PRM", "--funding", "grant", "--grant", "R01 X"],
            e => e.NewExperiment("Proj-A", "2026-11-A-PRM", new NewExperimentOptions("PRM", Funding: "grant", Grant: "R01 X"))),
        S(["new-experiment", "Proj-A", "bad-name", "--title", "X"], e => e.NewExperiment("Proj-A", "bad-name", new NewExperimentOptions("X"))),
        S(["new-experiment", "Proj-A", "2026-12-A-X", "--title", "X", "--with", "nonsense"],
            e => e.NewExperiment("Proj-A", "2026-12-A-X", new NewExperimentOptions("X", WithSteps: ["nonsense"]))),
        S(["new-experiment", "Proj-A", "2026-12-A-X", "--title", "X", "--with", "other"],
            e => e.NewExperiment("Proj-A", "2026-12-A-X", new NewExperimentOptions("X", WithSteps: ["other"]))),
        S(["new-experiment", "Proj-A", "2026-09-Full-DIA", "--title", "X"], e => e.NewExperiment("Proj-A", "2026-09-Full-DIA", new NewExperimentOptions("X"))),

        S(["stage", "Proj-A", "samples_received", "done", "--date", "2026-10-01", "--by", "maccoss", "--note", "Arrived: 40 tubes, 2 hemolyzed"],
            e => e.Stage("Proj-A", "samples_received", "done", "2026-10-01", "maccoss", "Arrived: 40 tubes, 2 hemolyzed")),
        S(["stage", "Proj-A", "metadata_organized", "start", "--by", "jdoe"], e => e.Stage("Proj-A", "metadata_organized", "start", by: "jdoe")),
        S(["stage", "Proj-A", "metadata_organized", "done", "--date", "2020-01-01"], e => e.Stage("Proj-A", "metadata_organized", "done", "2020-01-01")),
        S(["stage", "Proj-A", "plate_layout", "skip", "--note", ""], e => e.Stage("Proj-A", "plate_layout", "skip", note: "")),
        S(["stage", "Proj-A", "sample_prep", "done", "--date", "20261005"], e => e.Stage("Proj-A", "sample_prep", "done", "20261005")),
        S(["stage", "Proj-A", "sample_prep", "start", "--date", "2026-W41-2"], e => e.Stage("Proj-A", "sample_prep", "start", "2026-W41-2")),
        S(["stage", "Proj-A", "sample_prep", "start", "--date", "Oct 5"], e => e.Stage("Proj-A", "sample_prep", "start", "Oct 5")),
        S(["stage", "Proj-A", "nope", "start"], e => e.Stage("Proj-A", "nope", "start")),
        S(["stage", "Proj-A", "samples_received", "done", "--note", "mail x@y.org"], e => e.Stage("Proj-A", "samples_received", "done", note: "mail x@y.org")),
        S(["stage", "2026-10-A-DIA", "data_acquisition", "start", "--date", "2026-10-07"], e => e.Stage("2026-10-A-DIA", "data_acquisition", "start", "2026-10-07")),
        S(["stage", "Full-Project", "sample_prep", "done"], e => e.Stage("Full-Project", "sample_prep", "done")),
        S(["stage", "Merge-Project", "a", "done", "--date", "2026-10-02", "--by", "maccoss"], e => e.Stage("Merge-Project", "a", "done", "2026-10-02", "maccoss")),
        S(["stage", "Broken-Project", "a", "done"], e => e.Stage("Broken-Project", "a", "done")),
        S(["stage", "Old-Stages", "a", "done"], e => e.Stage("Old-Stages", "a", "done")),

        S(["assign", "Proj-A", "sample_prep", "plate_layout", "--to", "jdoe"], e => e.Assign("Proj-A", ["sample_prep", "plate_layout"], "jdoe", false)),
        S(["assign", "Proj-A", "plate_layout", "--nobody"], e => e.Assign("Proj-A", ["plate_layout"], null, true)),
        S(["assign", "Proj-A", "plate_layout", "--to", "jdoe", "--nobody"], e => e.Assign("Proj-A", ["plate_layout"], "jdoe", true)),
        S(["assign", "Proj-A", "plate_layout"], e => e.Assign("Proj-A", ["plate_layout"], null, false)),

        S(["add-step", "Proj-A", "other", "--label", "Second shipment", "--after", "samples_received", "--assigned", "maccoss"],
            e => e.AddStep("Proj-A", "other", "Second shipment", after: "samples_received", assigned: "maccoss")),
        S(["add-step", "Proj-A", "sample_prep", "--before", "plate_layout"], e => e.AddStep("Proj-A", "sample_prep", before: "plate_layout")),
        S(["add-step", "Proj-A", "other"], e => e.AddStep("Proj-A", "other")),
        S(["add-step", "Proj-A", "other", "--label", "x", "--id", "Bad Id"], e => e.AddStep("Proj-A", "other", "x", id: "Bad Id")),
        S(["add-step", "Proj-A", "data_analysis", "--after", "a", "--before", "b"], e => e.AddStep("Proj-A", "data_analysis", after: "a", before: "b")),
        S(["add-step", "Proj-A", "other", "--label", "Call 206-555-0100"], e => e.AddStep("Proj-A", "other", "Call 206-555-0100")),
        S(["remove-step", "Proj-A", "sample_prep_2"], e => e.RemoveStep("Proj-A", "sample_prep_2")),
        S(["remove-step", "Proj-A", "samples_received"], e => e.RemoveStep("Proj-A", "samples_received")),

        S(["link", "2026-10-A-DIA", "panorama", "https://panoramaweb.org/MacCoss/X/Raw/project-begin.view?pageId=a", "--kind", "raw"],
            e => e.Link("2026-10-A-DIA", "panorama", new LinkOptions("https://panoramaweb.org/MacCoss/X/Raw/project-begin.view?pageId=a", Kind: "raw"))),
        S(["link", "2026-10-A-DIA", "panorama", "/MacCoss/X/raw/", "--kind", "results"],
            e => e.Link("2026-10-A-DIA", "panorama", new LinkOptions("/MacCoss/X/raw/", Kind: "results"))),
        S(["link", "2026-10-A-DIA", "panorama", "https://panoramaweb.org/_webdav/MacCoss/X/%40files/Run%201/", "--kind", "raw"],
            e => e.Link("2026-10-A-DIA", "panorama", new LinkOptions("https://panoramaweb.org/_webdav/MacCoss/X/%40files/Run%201/", Kind: "raw"))),
        S(["link", "2026-10-A-DIA", "panorama", "https://panoramaweb.org/project/MacCoss/X/QC/begin.view", "--kind", "qc"],
            e => e.Link("2026-10-A-DIA", "panorama", new LinkOptions("https://panoramaweb.org/project/MacCoss/X/QC/begin.view", Kind: "qc"))),
        S(["link", "2026-10-A-DIA", "panorama", "https://example.org/MacCoss/X", "--kind", "qc"],
            e => e.Link("2026-10-A-DIA", "panorama", new LinkOptions("https://example.org/MacCoss/X", Kind: "qc"))),
        S(["link", "2026-10-A-DIA", "panorama", "C:/Program Files/Git/MacCoss/X", "--kind", "raw"],
            e => e.Link("2026-10-A-DIA", "panorama", new LinkOptions("C:/Program Files/Git/MacCoss/X", Kind: "raw"))),
        S(["link", "Proj-A", "panorama", "/MacCoss/X", "--kind", "raw"], e => e.Link("Proj-A", "panorama", new LinkOptions("/MacCoss/X", Kind: "raw"))),
        S(["link", "Proj-A", "notebook", "--id", "ELN-4485-20230314-179"], e => e.Link("Proj-A", "notebook", new LinkOptions(null, Id: "ELN-4485-20230314-179"))),
        S(["link", "Proj-A", "notebook", "https://example.org/nb/1"], e => e.Link("Proj-A", "notebook", new LinkOptions("https://example.org/nb/1"))),
        S(["link", "Proj-A", "notebook", "https://example.org/nb/1", "--id", "ELN-7"], e => e.Link("Proj-A", "notebook", new LinkOptions("https://example.org/nb/1", Id: "ELN-7"))),
        S(["link", "Proj-A", "notebook", "ftp://x"], e => e.Link("Proj-A", "notebook", new LinkOptions("ftp://x"))),
        S(["link", "Proj-A", "notebook"], e => e.Link("Proj-A", "notebook", new LinkOptions(null))),
        S(["link", "Proj-A", "wiki", "https://panoramaweb.org/MacCoss/Collaborations/X/wiki-page.view?name=Main%20Page"],
            e => e.Link("Proj-A", "wiki", new LinkOptions("https://panoramaweb.org/MacCoss/Collaborations/X/wiki-page.view?name=Main%20Page"))),
        S(["link", "Proj-A", "wiki", "/MacCoss/Collaborations/X", "--page", "default"],
            e => e.Link("Proj-A", "wiki", new LinkOptions("/MacCoss/Collaborations/X", Page: "default"))),
        S(["link", "Proj-A", "wiki", "/MacCoss/X/@files/y"], e => e.Link("Proj-A", "wiki", new LinkOptions("/MacCoss/X/@files/y"))),
        S(["link", "Proj-A", "wiki", "/MacCoss/X", "--page", "#bad"], e => e.Link("Proj-A", "wiki", new LinkOptions("/MacCoss/X", Page: "#bad"))),
        S(["link", "2026-10-A-DIA", "wiki", "/MacCoss/X"], e => e.Link("2026-10-A-DIA", "wiki", new LinkOptions("/MacCoss/X"))),
        S(["link", "Proj-A", "protocol", "s-trap-micro", "--version", "3", "--step", "sample_prep", "--title", "S-Trap micro"],
            e => e.Link("Proj-A", "protocol", new LinkOptions("s-trap-micro", Version: 3, Step: "sample_prep", Title: "S-Trap micro"))),
        S(["link", "Proj-A", "protocol", "s-trap-micro", "--version", "4", "--step", "sample_prep"],
            e => e.Link("Proj-A", "protocol", new LinkOptions("s-trap-micro", Version: 4, Step: "sample_prep"))),
        S(["link", "Proj-A", "protocol", "s-trap-micro", "--version", "1"], e => e.Link("Proj-A", "protocol", new LinkOptions("s-trap-micro", Version: 1))),
        S(["link", "Proj-A", "protocol", "Bad_ID", "--version", "1"], e => e.Link("Proj-A", "protocol", new LinkOptions("Bad_ID", Version: 1))),
        S(["link", "Proj-A", "protocol", "ok-id", "--version", "0"], e => e.Link("Proj-A", "protocol", new LinkOptions("ok-id", Version: 0))),
        S(["link", "Proj-A", "protocol", "ok-id", "--version", "1", "--step", "nowhere"], e => e.Link("Proj-A", "protocol", new LinkOptions("ok-id", Version: 1, Step: "nowhere"))),
        S(["link", "Proj-A", "protocol", "ok-id", "--version", "1", "--title", "Mail me at a@b.org"],
            e => e.Link("Proj-A", "protocol", new LinkOptions("ok-id", Version: 1, Title: "Mail me at a@b.org"))),
        S(["link", "2026-10-A-DIA", "protocol", "dia-method", "--version", "2"], e => e.Link("2026-10-A-DIA", "protocol", new LinkOptions("dia-method", Version: 2))),
        S(["link", "Merge-Project", "protocol", "x-y", "--version", "2"], e => e.Link("Merge-Project", "protocol", new LinkOptions("x-y", Version: 2))),
        S(["link", "Merge-Project", "notebook", "--id", "ELN-5"], e => e.Link("Merge-Project", "notebook", new LinkOptions(null, Id: "ELN-5"))),

        S(["unlink", "2026-10-A-DIA", "panorama", "/MacCoss/X/Raw"], e => e.Unlink("2026-10-A-DIA", "panorama", "/MacCoss/X/Raw")),
        S(["unlink", "2026-10-A-DIA", "panorama", "/MacCoss/X/Raw"], e => e.Unlink("2026-10-A-DIA", "panorama", "/MacCoss/X/Raw")),
        S(["unlink", "Proj-A", "notebook", "ELN-4485-20230314-179"], e => e.Unlink("Proj-A", "notebook", "ELN-4485-20230314-179")),
        S(["unlink", "Proj-A", "notebook"], e => e.Unlink("Proj-A", "notebook", null)),
        S(["unlink", "Proj-A", "wiki"], e => e.Unlink("Proj-A", "wiki", null)),
        S(["unlink", "Proj-A", "wiki"], e => e.Unlink("Proj-A", "wiki", null)),
        S(["unlink", "Proj-A", "protocol", "s-trap-micro", "--step", "sample_prep"], e => e.Unlink("Proj-A", "protocol", "s-trap-micro", "sample_prep")),
        S(["unlink", "Proj-A", "protocol", "s-trap-micro", "--step", "sample_prep"], e => e.Unlink("Proj-A", "protocol", "s-trap-micro", "sample_prep")),
        S(["unlink", "Proj-A", "protocol", "s-trap-micro", "--all", "--step", "x"], e => e.Unlink("Proj-A", "protocol", "s-trap-micro", "x", true)),
        S(["unlink", "Proj-A", "protocol", "s-trap-micro", "--all"], e => e.Unlink("Proj-A", "protocol", "s-trap-micro", all: true)),
        S(["unlink", "Proj-A", "panorama", "x", "--all"], e => e.Unlink("Proj-A", "panorama", "x", all: true)),

        S(["octopus-input", "Full-Project"], e => e.OctopusInput("Full-Project")),
        S(["octopus-input", "Proj-A"], e => e.OctopusInput("Proj-A")),
        S(["octopus-input", "Proj-B"], e => e.OctopusInput("Proj-B")),
        S(["import-layout", "Proj-A", "{root}/layout-bad.json"], (e, root) => e.ImportLayout("Proj-A", root + "/layout-bad.json")),
        S(["import-layout", "Proj-A", "{root}/missing.json"], (e, root) => e.ImportLayout("Proj-A", root + "/missing.json")),
        S(["import-layout", "Proj-A", "{root}/layout-v2.json"], (e, root) => e.ImportLayout("Proj-A", root + "/layout-v2.json")),
        S(["import-layout", "Proj-A", "{root}/layout.json", "--by", "maccoss"], (e, root) => e.ImportLayout("Proj-A", root + "/layout.json", by: "maccoss")),
        S(["import-layout", "Proj-A", "{root}/layout.json", "--step", "samples_received"],
            (e, root) => e.ImportLayout("Proj-A", root + "/layout.json", stepId: "samples_received")),
        S(["stage", "Proj-A", "sample_prep", "start"], e => e.Stage("Proj-A", "sample_prep", "start")),
        S(["import-layout", "Proj-A", "{root}/layout.json"], (e, root) => e.ImportLayout("Proj-A", root + "/layout.json")),
        S(["index"], e => e.Index()),
    ];

    /// <summary>Files the scenario reads: a sample table for Proj-A, its Octopus layouts.</summary>
    private static void Inputs(string root)
    {
        void W(string rel, string text)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        W("projects/NewLab-X-samples.csv", "Sample_ID,QC,Sample_Group,Search Name,,Notes\r\nS1,FALSE,Case,x1,,\"a, b\"\r\nS2,FALSE,Control,x2,,\r\nS3,TRUE,,x3,,\r\n");
        W("layout.json", "{\"format\": \"octopus-layout\", \"schemaVersion\": 1, \"appVersion\": \"1.4.0\", \"plateCount\": 1,\n"
                         + " \"settings\": {\"idColumn\": \"Sample_ID\", \"covariates\": [\"Sample_Group\"], \"qcColumn\": \"QC\", \"subjectColumn\": \"\"},\n"
                         + " \"samples\": [{\"id\": \"S1\", \"plate\": 1, \"well\": \"A1\", \"ratio\": 0.5}, {\"id\": \"S2\", \"plate\": 1, \"well\": \"A2\", \"x\": 1e20},"
                         + " {\"id\": \"S3\", \"plate\": 1, \"well\": \"A3\", \"note\": \"caf\\u00e9\"}]}");
        W("layout-bad.json", "{\"format\": \"something else\"}");
        W("layout-v2.json", "{\"format\": \"octopus-layout\", \"schemaVersion\": 2.0}");
    }

    [Fact]
    public void Every_command_that_writes_does_what_the_Python_engine_does()
    {
        var parity = Parity.TryCreate();
        Assert.SkipWhen(parity is null, "LAB_PROJECTS_REPO and uv are needed to compare with the Python engine");
        using var python = new TempDirectory();
        using var text = new TempDirectory();
        using var csharp = new TempDirectory();
        foreach (var root in (string[])[python.Path, text.Path, csharp.Path])
        {
            parity!.Seed(root);
            EdgeCases.Write(root);
            Inputs(root);
        }

        var differences = new List<string>();
        foreach (var step in Scenario)
        {
            // Proj-A's sample table arrives once the project exists.
            foreach (var root in (string[])[python.Path, text.Path, csharp.Path])
            {
                var table = Path.Combine(root, "projects", "NewLab-X-samples.csv");
                var target = Path.Combine(root, "projects", "NewLab-X", "Proj-A", "metadata", "samples.csv");
                if (File.Exists(table) && Directory.Exists(Path.GetDirectoryName(target)))
                {
                    File.Move(table, target);
                }
            }

            parity!.CompareWriteIn(python.Path, text.Path, csharp.Path, differences, step.Args, step.Run);
        }

        differences.ShouldBeEmpty(string.Join("\n\n", differences.Take(10)));
    }
}
