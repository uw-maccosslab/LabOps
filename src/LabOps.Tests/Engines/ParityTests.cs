using System.Text;
using LabOps.Engines.Projects;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Engines;

/// <summary>
/// The C# engine against the Python one, command by command: on the real LabOps-Projects clone
/// (read-only commands) and on a repository built here with every rule's edge cases. Opt-in:
/// they run when LAB_PROJECTS_REPO names a clone and uv is installed, and skip otherwise.
/// </summary>
public sealed class ParityTests
{
    private static void ShouldMatch(List<string> differences) =>
        differences.ShouldBeEmpty(string.Join("\n\n", differences.Take(12)));

    [Fact]
    public void The_real_repository_reads_the_same()
    {
        var parity = Parity.TryCreate();
        Assert.SkipWhen(parity is null, "LAB_PROJECTS_REPO and uv are needed to compare with the Python engine");
        var root = parity!.Clone;
        var differences = new List<string>();
        parity.Compare(root, differences, ["list"], e => e.List(), "engine_version");
        parity.Compare(root, differences, ["list", "--active"], e => e.List(activeOnly: true), "engine_version");
        parity.Compare(root, differences, ["check"], e => e.Check());
        foreach (var project in new ProjectRepository(root).ProjectFiles().Select(f => Path.GetFileName(Path.GetDirectoryName(f))!))
        {
            parity.Compare(root, differences, ["wiki", project, "--date", "2026-10-08"], e => e.Wiki(project, date: new DateOnly(2026, 10, 8)));
        }

        // index writes the README, so on a copy.
        using var copy = new TempDirectory();
        parity.Seed(copy.Path);
        Parity.CopyFolder(Path.Combine(root, "projects"), Path.Combine(copy.Path, "projects"));
        // The C# index also says how many closed projects it left out (none here).
        parity.Compare(copy.Path, differences, ["index"], e => e.Index(), "closed_hidden");
        ShouldMatch(differences);
    }

    [Fact]
    public void A_repository_of_edge_cases_reads_the_same()
    {
        var parity = Parity.TryCreate();
        Assert.SkipWhen(parity is null, "LAB_PROJECTS_REPO and uv are needed to compare with the Python engine");
        using var dir = new TempDirectory();
        var root = dir.Path;
        parity!.Seed(root);
        EdgeCases.Write(root);

        var differences = new List<string>();
        parity.Compare(root, differences, ["list"], e => e.List(), "engine_version");
        parity.Compare(root, differences, ["list", "--active"], e => e.List(activeOnly: true), "engine_version");
        parity.Compare(root, differences, ["check"], e => e.Check());
        var documents = Path.Combine(root, "documents.json");
        File.WriteAllText(documents, EdgeCases.Documents);
        foreach (var project in new ProjectRepository(root).ProjectFiles().Select(f => Path.GetFileName(Path.GetDirectoryName(f))!).Distinct())
        {
            parity.Compare(root, differences, ["wiki", project, "--date", "2026-10-08"], e => e.Wiki(project, date: new DateOnly(2026, 10, 8)));
            parity.Compare(root, differences, ["wiki", project, "--date", "2026-10-08", "--documents", documents],
                e => e.Wiki(project, documents, date: new DateOnly(2026, 10, 8)));
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "inbox"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var shown = Path.GetRelativePath(root, file).Replace('\\', '/');
            parity.Compare(root, differences, ["scan", shown], e => e.Scan(file, shown));
            parity.Compare(root, differences, ["sheet", shown], e => e.Sheet(file));
            parity.Compare(root, differences, ["sheet", shown, "--rows", "2"], e => e.Sheet(file, rows: 2));
        }

        // No index here: it leaves out closed projects now, which project.py's listed (IndexTests).
        ShouldMatch(differences);
    }

    [Fact]
    public void Workbooks_read_the_same()
    {
        var parity = Parity.TryCreate();
        Assert.SkipWhen(parity is null, "LAB_PROJECTS_REPO and uv are needed to compare with the Python engine");
        using var dir = new TempDirectory();
        parity!.Seed(dir.Path);
        var made = parity.Tool("make_workbooks.py", Path.Combine(dir.Path, "inbox"));
        made.ExitCode.ShouldBe(0, made.Stderr);
        var differences = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir.Path, "inbox")).Order(StringComparer.Ordinal))
        {
            var shown = "inbox/" + Path.GetFileName(file);
            parity.Compare(dir.Path, differences, ["scan", shown], e => e.Scan(file, shown));
            parity.Compare(dir.Path, differences, ["sheet", shown], e => e.Sheet(file));
            parity.Compare(dir.Path, differences, ["sheet", shown, "--sheet", "Hidden"], e => e.Sheet(file, "Hidden"));
        }

        ShouldMatch(differences);
    }

    [Fact]
    public void The_pre_commit_check_judges_staged_files_the_same()
    {
        var parity = Parity.TryCreate();
        Assert.SkipWhen(parity is null, "LAB_PROJECTS_REPO and uv are needed to compare with the Python engine");
        using var dir = new TempDirectory();
        var root = dir.Path;
        parity!.Seed(root);
        EdgeCases.Write(root);
        File.WriteAllText(Path.Combine(root, "notes.pdf"), "not really a PDF");
        File.WriteAllText(Path.Combine(root, "elsewhere.csv"), "Email,Value\nx@y.org,1\n");
        File.WriteAllText(Path.Combine(root, "elsewhere.txt"), "ignored");
        Parity.Git(root, "init", "-q", "-b", "main");
        Parity.Git(root, "config", "core.autocrlf", "false");
        Parity.Git(root, "add", "-A");
        var differences = new List<string>();
        parity.Compare(root, differences, ["check", "--staged"], e => e.Check(staged: true));

        // A rename and a change on top of a commit.
        Parity.Git(root, "-c", "user.name=Test", "-c", "user.email=test@example.org", "commit", "-q", "-m", "seed", "--no-verify");
        Parity.Git(root, "mv", "projects/TestLab-One/Full-Project/metadata/received/notes.md", "projects/TestLab-One/Full-Project/metadata/received/renamed.md");
        File.AppendAllText(Path.Combine(root, "projects", "TestLab-One", "Merge-Project", "project.yaml"), "pi_email: someone@example.org\n");
        Parity.Git(root, "add", "-A");
        parity.Compare(root, differences, ["check", "--staged"], e => e.Check(staged: true));
        ShouldMatch(differences);
    }
}

/// <summary>A repository with an example of every rule, every odd way of writing a record, and every kind of data file.</summary>
internal static class EdgeCases
{
    public static void Write(string root)
    {
        void W(string rel, string text) => Bytes(rel, Encoding.UTF8.GetBytes(text));
        void Bytes(string rel, byte[] data)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        W("config/people.yaml", "people:\n  - login: maccoss\n    name: Michael MacCoss\n    role: PI\n  - login: jdoe\n    name: J. Doe\n  - name: No Login\n  - plain string\n");

        // A lab with everything right, and its projects.
        W("projects/TestLab-One/lab.yaml", "lab: TestLab-One\ntitle: Test lab one  # a comment\npi: Pat Example, Ph.D.\ninstitution: Example University\n"
                                           + "status: active\nlab_contact: maccoss\ncontacts:\n  - {name: Pat, email: pat@example.org}\nnotebooks:\n  - ELN-123-20260101-45\n  - https://panoramaweb.org/x\n  - {id: '', url: ''}\n  - {url: ftp://x}\n");
        W("projects/TestLab-One/Full-Project/project.yaml",
            "project: Full-Project\ntitle: 'Full: a project with every field'\nstatus: active\nlab_contact: maccoss\nseries: dog-aging\n"
            + "funding:\n  type: quote\n  quotes: [MacCoss-2026-X, 7]\n  grant: null\nhuman: yes\nspecies: Homo sapiens\nsample_type: plasma\nexpected_samples: '12'\n"
            + "notebooks: [ELN-99, {id: ELN-1-2, url: 'http://x'}]\n"
            + "protocols:\n  - {id: s-trap-micro, version: 3, title: S-Trap micro, step: sample_prep}\n  - {id: Bad_ID, version: 0}\n  - {id: ok-id, step: nowhere}\n  - plain-protocol\n"
            + "analysis: {repo: uw-maccosslab/collab-x, folder: 2026-09-X}\n"
            + "layout: {plates: 2, samples: '160', imported: 2026-10-03, octopus_version: 1.4.0}\n"
            + "wiki: {folder: /MacCoss/Collaborations/X/Full, page: Main Page}\n"
            + "steps:\n"
            + "  - {id: samples_received, kind: samples_received, status: done, started: 2026-09-09, finished: 2026-09-09, by: maccoss, note: 'FedEx, 92 tubes: see manifest'}\n"
            + "  - {id: metadata_organized, kind: metadata_organized, status: done, assigned: jdoe, started: 2026-09-10, finished: 2026-10-03}\n"
            + "  - {id: plate_layout, kind: plate_layout, status: skipped, by: stranger}\n"
            + "  - {id: sample_prep, kind: sample_prep, status: in_progress, started: 2026-09-11, assigned: maccoss}\n"
            + "  - {id: second_shipment, kind: other, label: Second shipment, status: pending}\n"
            + "  - {id: Bad Id, kind: nonsense, status: done}\n"
            + "  - {id: second_shipment, kind: other, status: waiting}\n"
            + "  - {id: dates, kind: data_analysis, status: done, started: '2026-10-01', finished: 2026-09-01 09:30:00}\n"
            + "  - {id: backwards, kind: data_analysis, status: done, started: 2026-10-05, finished: 2026-10-01}\n"
            + "  - just a string\n"
            + "  - {kind: other}\n"
            + "notes: []\n");
        W("projects/TestLab-One/Full-Project/metadata/samples.csv",
            "﻿Sample_ID,QC,Sample_Group,Age\r\nS1,FALSE,Case,61\r\ns1,FALSE,Control,55\r\nS2,TRUE,,40\r\nS2,maybe,Case,\r\n,FALSE,Control,33\r\nS3,FALSE,Case,70\r\n".Replace("﻿", ((char)0xFEFF).ToString(), StringComparison.Ordinal));
        W("projects/TestLab-One/Full-Project/metadata/received/manifest.csv",
            "Patient Name,DOB,Email,Phone,Sample Name,Notes,,Box,Box\nA,2001-01-01,a@b.org,206-555-0100,S1,this is a long free text note here,x,1,2\n"
            + "B,2001-01-02,c@d.com,(206) 555-0101,S2,another long text value goes right here,,1,2\n\"quoted, with comma\",,,+44 20 7946 0958,S3,\"multi\nline\",,1,2\n");
        W("projects/TestLab-One/Full-Project/metadata/received/notes.md",
            "# Notes\n\n| Subject | Initials | City |\n| --- | :---: | --- |\n| 1 | AB | Seattle |\n\nCall 206-555-0199 or mail x@y.org\n");
        W("projects/TestLab-One/Full-Project/metadata/received/records.json",
            "[{\"Sample_ID\": \"S1\", \"Patient_Email\": \"p@q.org\", \"age\": 61.0}, {\"Sample_ID\": \"S2\", \"Address1\": \"1 Main St\"}, 5]");
        W("projects/TestLab-One/Full-Project/metadata/received/records.yaml", "- {Sample_ID: S1, Surname: X, collected: 2026-01-02}\n- {Sample_ID: S2, Surname: Y}\n");
        W("projects/TestLab-One/Full-Project/metadata/received/broken.json", "{\"a\": [1, 2}");
        W("projects/TestLab-One/Full-Project/metadata/received/broken.yaml", "a: [1, 2\n");
        W("projects/TestLab-One/Full-Project/metadata/received/latin1.csv", "Name,Ok\nJosé,1\n".Replace("é", "é", StringComparison.Ordinal));
        Bytes("projects/TestLab-One/Full-Project/metadata/received/cp1252.csv", [.. Encoding.ASCII.GetBytes("Header,Value\nCaf"), 0xE9, 0x81, (byte)',', (byte)'1', (byte)'\n']);
        Bytes("projects/TestLab-One/Full-Project/metadata/received/utf16.csv", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Sample_ID,Owner\nS1,x\n")]);
        Bytes("projects/TestLab-One/Full-Project/metadata/received/has-nul.csv", [.. Encoding.ASCII.GetBytes("a,b\n1,"), 0, (byte)'\n']);
        W("projects/TestLab-One/Full-Project/metadata/received/cr-only.csv", "a,b\r1,2\r");
        W("projects/TestLab-One/Full-Project/metadata/received/original.xlsx", "not really a workbook");
        W("projects/TestLab-One/Full-Project/metadata/received/.gitkeep", "");
        W("projects/TestLab-One/Full-Project/metadata/received/README", "no suffix");
        W("projects/TestLab-One/Full-Project/layout/octopus-layout.json", "{\"format\": \"octopus-layout\", \"schemaVersion\": 1, \"samples\": [{\"id\": \"S1\", \"plate\": 1}]}");
        W("projects/TestLab-One/Full-Project/wiki.yaml",
            "summary: A **bold** study of [plasma](https://example.org/a) and [a page](/MacCoss/x) but not [this](//evil.org) <script>\n"
            + "samples_card: 6 samples, coded\nqc_card: 3\nplan:\n  - Prep the samples\n  - ''\n  - Acquire on the Astral\n"
            + "samples:\n  intro: Coded plasma; contact me at someone@example.org\n  sections:\n    - {heading: Groups, text: Cases and controls, bullets: [Case, Control]}\n    - {heading: Empty}\n    - just text\n"
            + "folders:\n  /MacCoss/X/Raw/: The raw files.\n  /macCoss/x/results: Results here.\nextra_key: ignored\n");
        W("projects/TestLab-One/Full-Project/2026-09-Full-DIA/experiment.yaml",
            "experiment: 2026-09-Full-DIA\ntitle: DIA on the Astral\nstatus: active\nlab_contact: jdoe\ninstrument: Orbitrap Astral\n"
            + "funding: {type: null}\nnotebooks: []\n"
            + "panorama:\n  - {folder: /MacCoss/X/Raw, kind: raw}\n  - {folder: /MacCoss/X/@files/More, kind: raw}\n  - {folder: /MacCoss/X/Results, kind: results}\n  - {folder: /MacCoss/X/QC, kind: qc}\n  - {folder: '', kind: raw}\n  - {folder: /MacCoss/X/Odd, kind: weird}\n  - /MacCoss/X/Bare\n"
            + "protocols: []\nanalysis: {repo: null, folder: null}\n"
            + "steps:\n  - {id: data_acquisition, kind: data_acquisition, status: done, assigned: maccoss, started: 2026-09-23, finished: 2026-09-29}\n"
            + "  - {id: data_deposited, kind: data_deposited, status: done, started: 2026-09-29, finished: 2026-09-29}\n"
            + "  - {id: signal_processing, kind: signal_processing, status: in_progress, started: 2026-10-03, assigned: jdoe}\n"
            + "  - {id: data_analysis, kind: data_analysis, status: pending}\nnotes: []\n");
        W("projects/TestLab-One/Full-Project/2026-10-Full-PRM/experiment.yaml",
            "experiment: 2026-10-Full-PRM\ntitle: PRM on the Stellar\nstatus: active\ninstrument: Stellar\nfunding:\n  type: grant\n  grant: R01 example\n"
            + "wiki: {folder: /x}\nsteps:\n  - {id: data_acquisition, kind: data_acquisition, status: in_progress}\n"
            + "  - {id: assay_development, kind: assay_development, status: done}\n");
        W("projects/TestLab-One/Full-Project/2026-10-Full-PRM/metadata/samples.csv", "Sample_ID,QC\nS1,FALSE\n");
        W("projects/TestLab-One/Full-Project/2026-11-Closed-Exp/experiment.yaml",
            "experiment: 2026-11-Closed-Exp\ntitle: Closed experiment\nstatus: closed\nsteps:\n  - {id: a, kind: other, label: A, status: done, finished: 2026-11-01}\n");
        W("projects/TestLab-One/Full-Project/Bad-Experiment-Name/experiment.yaml",
            "experiment: Something-Else\ntitle: ''\nstatus: paused\nfunding: {type: barter}\nsteps: []\n");

        W("projects/TestLab-One/Closed-Project/project.yaml",
            "project: Closed-Project\ntitle: Done long ago\nstatus: closed\nfunding: {type: internal}\nsteps:\n  - {id: a, kind: other, label: A, status: done, finished: 2025-01-01}\n");
        W("projects/TestLab-One/Merge-Project/project.yaml",
            "base: &base {type: grant, grant: Foundation &amp; Co}\nproject: Merge-Project\ntitle: Anchors and merges\nstatus: on_hold\n"
            + "funding:\n  <<: *base\n  quotes: MacCoss-2026-STR\nhuman: no\nexpected_samples: 0\nwiki: {folder: /MacCoss/X/@files/Bad}\n"
            + "steps:\n- id: a\n  kind: other\n  label: Block style step\n  status: pending\n# a margin comment\n- {id: b, kind: other, label: B, status: done, finished: 2026-10-01, by: maccoss}\n");
        W("projects/TestLab-One/Empty-Project/project.yaml", "");
        W("projects/TestLab-One/List-Project/project.yaml", "- a\n- b\n");
        W("projects/TestLab-One/Broken-Project/project.yaml", "project: Broken-Project\ntitle: [unclosed\n");
        W("projects/TestLab-One/Old-Stages/project.yaml", "project: Old-Stages\ntitle: Old form\nstatus: active\nfunding: {type: grant}\nstages: {received: done}\n");
        W("projects/TestLab-One/bad_name/project.yaml", "project: bad_name\ntitle: Bad folder name\nstatus: active\nfunding: {type: internal}\nsteps:\n  - {id: a, kind: other, label: A}\nwiki: {folder: /MacCoss/X, page: '#bad'}\n");

        // A lab whose name breaks the rule, a lab without lab.yaml, a lab whose lab.yaml is broken,
        // names used twice, and files in the older layout.
        W("projects/Bad_Lab/lab.yaml", "lab: Wrong\ntitle: Bad lab\nstatus: active\n");
        W("projects/Bad_Lab/Full-Project/project.yaml", "project: Full-Project\ntitle: Same name\nstatus: active\nfunding: {type: internal}\nsteps:\n  - {id: a, kind: other, label: A}\n");
        W("projects/Bad_Lab/Full-Project/2026-09-Full-DIA/experiment.yaml", "experiment: 2026-09-Full-DIA\ntitle: Same name\nstatus: active\nsteps:\n  - {id: a, kind: other, label: A}\n");
        W("projects/No-Lab-File/Orphan/project.yaml", "project: Orphan\ntitle: No lab\nstatus: active\nfunding: {type: internal}\nsteps:\n  - {id: a, kind: other, label: A}\n");
        W("projects/Broken-Lab/lab.yaml", "lab: Broken-Lab\ntitle: 'unterminated\n");
        W("projects/Broken-Lab/Inside/project.yaml", "project: Inside\ntitle: Inside a broken lab\nstatus: active\nfunding: {type: internal}\nsteps:\n  - {id: a, kind: other, label: A}\n");
        W("projects/Old-Layout/project.yaml", "project: Old-Layout\n");
        W("projects/Old-Layout/Exp/experiment.yaml", "experiment: Exp\n");
        W("projects/TestLab-One/notes.txt", "a stray text file");

        // Originals for scan and sheet.
        W("inbox/Full-Project/clinical.csv",
            "Title row,,,\nSubject,First Name,Patient,Zip\n1,Ann,P1,98105\n2,Bob,P2,98195\n3,,P3,\n");
        W("inbox/Full-Project/quoted.csv", "\"a\"\"b\",c\n\"x\"y,\"multi\r\nline\"\n,\n\n\"last\"");
        W("inbox/Full-Project/wide.csv", "h1\n1,2,3\n");
    }

    public const string Documents =
        "{\"/MacCoss/X/Results/\": [{\"name\": \"Study.sky.zip\", \"replicates\": 1234, \"peptides\": 56789, \"proteins\": 4321, \"uploaded\": \"2026-09-30\"},"
        + " {\"replicates\": \"12\"}], \"/MacCoss/X/QC\": [], \"/MacCoss/X/Raw\": null}";
}
