using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LabOps.Engines.Python;
using LabOps.Engines.Yaml;
using LabOps.Tests.TestSupport;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// Labs, projects, experiments and their timelines: the everyday `labops projects` commands
/// (LabOps-Projects' tests/test_commands.py).
/// </summary>
public sealed class CommandTests
{
    [Fact]
    public void A_project_and_its_experiments_come_from_the_templates()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("BioTRACK", "MNRF-Lab", "--funding", "grant", "--grant", "Midwest Neuro Research Foundation",
            "--human", "--species", "human", "--sample-type", "plasma", "--expected-samples", "150");
        var p = TestRepo.Yaml(project);
        p["project"].ShouldBe("BioTRACK");
        p["title"].ShouldBe("Test project");
        p["human"].ShouldBe(true);
        ShouldEqual(p["funding"], Dict(("type", "grant"), ("quotes", new List<object?>()), ("grant", "Midwest Neuro Research Foundation")));
        p["expected_samples"].ShouldBe(150L);
        p["sample_type"].ShouldBe("plasma");
        StepIds(project).ShouldBe(["samples_received", "metadata_organized", "plate_layout", "sample_prep"]);
        TestRepo.Raw(project).ShouldContain("  type: grant          # quote | grant | internal", Case.Sensitive);
        Directory.Exists(Path.Combine(repo.Root, "inbox", "BioTRACK")).ShouldBeTrue();

        var dia = repo.NewExperiment("2026-09-BioTRACK-DIA", "BioTRACK", "--instrument", "Orbitrap Astral");
        var prm = repo.NewExperiment("2026-12-BioTRACK-PRM", "BioTRACK", "--instrument", "Stellar",
            "--with", "metadata_organized=Unblinded metadata from MNRF", "--with", "assay_development");
        Path.GetDirectoryName(dia).ShouldBe(project);
        TestRepo.Yaml(dia)["instrument"].ShouldBe("Orbitrap Astral");
        StepIds(dia).ShouldBe(["data_acquisition", "data_deposited", "signal_processing", "data_analysis", "results_returned"]);
        ShouldEqual(((List<object?>)TestRepo.Yaml(prm)["steps"]!).Take(3).ToList(), new List<object?>
        {
            Dict(("id", "metadata_organized"), ("kind", "metadata_organized"), ("label", "Unblinded metadata from MNRF"), ("status", "pending")),
            Dict(("id", "assay_development"), ("kind", "assay_development"), ("status", "pending")),
            Dict(("id", "data_acquisition"), ("kind", "data_acquisition"), ("status", "pending")),
        });
        TestRepo.Raw(prm).ShouldContain("# Only when this experiment is paid for differently", Case.Sensitive);
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Names_are_checked_and_unique_across_the_repository()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Pilot");
        Error(repo.Fails("new-experiment", "Test-Project", "Pilot", "--title", "x")).ShouldContain("YYYY-MM-Topic", Case.Sensitive);
        Error(repo.Fails("new-experiment", "Test-Project", "2026-13-Pilot", "--title", "x")).ShouldContain("YYYY-MM-Topic", Case.Sensitive);
        Error(repo.Fails("new-project", "Test-Lab", "2026-Study", "--title", "x")).ShouldContain("starting with a letter", Case.Sensitive);
        repo.NewProject("Other-Project", "Other-Lab");
        // Names are what commands go by, so they are unique across labs and projects.
        Error(repo.Fails("new-project", "Other-Lab", "Test-Project", "--title", "x")).ShouldContain("already exists", Case.Sensitive);
        Error(repo.Fails("new-experiment", "Other-Project", "2026-10-Pilot", "--title", "x")).ShouldContain("already exists", Case.Sensitive);
        Error(repo.Fails("new-project", "Nobody-Lab", "Some-Project", "--title", "x")).ShouldContain("no lab", Case.Sensitive);
        Error(repo.Fails("new-experiment", "Nobody-Project", "2026-10-Other", "--title", "x")).ShouldContain("no project", Case.Sensitive);
        Error(repo.Fails("new-lab", "Other-Lab", "--title", "t", "--pi", "p", "--institution", "i")).ShouldContain("already exists", Case.Sensitive);
    }

    [Fact]
    public void Stage_updates_a_projects_samples_and_an_experiment()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var projectName = Path.GetFileName(project);
        repo.Ok("stage", projectName, "samples_received", "start", "--date", "2026-10-01", "--by", "maccoss");
        repo.Ok("stage", projectName, "samples_received", "done", "--note", "92 tubes on dry ice");
        var json = repo.Ok("stage", project, "metadata_organized", "skip");
        // Starting a step nobody was assigned assigns it to whoever started it.
        ShouldEqual(TestRepo.Steps(project)["samples_received"], Dict(("status", "done"), ("assigned", "maccoss"),
            ("started", new DateOnly(2026, 10, 1)), ("finished", TestRepo.Today), ("by", "maccoss"), ("note", "92 tubes on dry ice")));
        ShouldEqual(TestRepo.Steps(project)["metadata_organized"], Dict(("status", "skipped")));
        json["project"]!["current_stage"]!.GetValue<string>().ShouldBe("plate_layout");
        TestRepo.Raw(project).ShouldContain("# The samples' timeline", Case.Sensitive);

        json = repo.Ok("stage", Path.GetFileName(experiment), "data_acquisition", "start", "--date", "2026-10-05");
        ShouldEqual(TestRepo.Steps(experiment)["data_acquisition"], Dict(("status", "in_progress"), ("started", new DateOnly(2026, 10, 5))));
        json["experiment"]!["current_stage"]!.GetValue<string>().ShouldBe("data_acquisition");
        Error(repo.Fails("stage", Path.GetFileName(experiment), "sample_prep", "done"))
            .ShouldContain("there is no step 'sample_prep'", Case.Sensitive);
    }

    [Fact]
    public void Steps_marked_done_in_one_step_keep_the_file_readable()
    {
        // Done without a start uses one date for started and finished. Written as a YAML anchor and
        // alias, a second such step repeated the anchor and the file no longer loaded.
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var projectName = Path.GetFileName(project);
        repo.Ok("stage", projectName, "samples_received", "done", "--date", "2026-10-01");
        repo.Ok("stage", projectName, "metadata_organized", "done", "--date", "2026-10-02");
        TestRepo.Raw(project).ShouldNotContain("&", Case.Sensitive);
        TestRepo.Raw(project).ShouldNotContain("*id", Case.Insensitive);   // stricter than Python's test: no alias in any case
        ShouldEqual(TestRepo.Steps(project)["metadata_organized"], Dict(("status", "done"),
            ("started", new DateOnly(2026, 10, 2)), ("finished", new DateOnly(2026, 10, 2))));
    }

    [Fact]
    public void Steps_can_be_added_anywhere_and_removed_until_started()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var projectName = Path.GetFileName(project);
        repo.Ok("add-step", projectName, "samples_received", "--label", "Second shipment", "--after", "samples_received");
        repo.Ok("add-step", projectName, "other", "--label", "Aliquots returned to the client");
        repo.Ok("add-step", projectName, "metadata_organized", "--before", "plate_layout", "--id", "late-metadata");
        StepIds(project).ShouldBe([
            "samples_received", "samples_received_2", "metadata_organized", "late-metadata", "plate_layout",
            "sample_prep", "aliquots_returned_to_the_client"]);
        ShouldEqual(TestRepo.Steps(project)["samples_received_2"], Dict(("label", "Second shipment"), ("status", "pending")));

        Error(repo.Fails("add-step", projectName, "other")).ShouldContain("needs a label", Case.Sensitive);
        Error(repo.Fails("add-step", projectName, "unblinding")).ShouldContain("not a kind of step", Case.Sensitive);
        Error(repo.Fails("add-step", projectName, "plate_layout", "--id", "plate_layout")).ShouldContain("cannot be a step id", Case.Sensitive);

        repo.Ok("remove-step", projectName, "late-metadata");
        repo.Ok("stage", projectName, "samples_received_2", "start");
        Error(repo.Fails("remove-step", projectName, "samples_received_2")).ShouldContain("skip it instead", Case.Sensitive);
        TestRepo.Steps(project).ShouldNotContainKey("late-metadata");
        // The second shipment started before the first is recorded: a warning, as it should be.
        repo.Problems().ShouldBe([
            ("WARN", "Test-Project: step samples_received_2 is in progress while samples_received is still pending")]);
    }

    [Fact]
    public void Steps_are_assigned_to_people()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var experimentName = Path.GetFileName(experiment);
        var project = Path.GetDirectoryName(experiment)!;
        var projectName = Path.GetFileName(project);
        var json = repo.Ok("assign", experimentName, "data_acquisition", "data_deposited", "--to", "maccoss");
        ShouldEqual(TestRepo.Steps(experiment)["data_acquisition"], Dict(("status", "pending"), ("assigned", "maccoss")));
        ShouldEqual(TestRepo.Steps(experiment)["data_deposited"], Dict(("status", "pending"), ("assigned", "maccoss")));
        json["experiment"]!["stages"]![0]!["assigned"]!.GetValue<string>().ShouldBe("maccoss");
        TestRepo.Raw(experiment).ShouldContain("{id: data_acquisition, kind: data_acquisition, status: pending, assigned: maccoss}", Case.Sensitive);

        repo.Ok("assign", experimentName, "data_deposited", "--nobody");
        ShouldEqual(TestRepo.Steps(experiment)["data_deposited"], Dict(("status", "pending")));
        Error(repo.Fails("assign", experimentName, "data_deposited")).ShouldContain("--nobody", Case.Sensitive);
        Error(repo.Fails("assign", experimentName, "data_deposited", "--to", "maccoss", "--nobody")).ShouldContain("--nobody", Case.Sensitive);
        Error(repo.Fails("assign", projectName, "data_analysis", "--to", "maccoss")).ShouldContain("there is no step", Case.Sensitive);

        // Someone already assigned keeps the step when another person starts it.
        repo.Ok("stage", experimentName, "data_acquisition", "start", "--by", "someone-else");
        TestRepo.Steps(experiment)["data_acquisition"]["assigned"].ShouldBe("maccoss");

        // An assignment is a plan, not a record: the step can still be removed.
        repo.Ok("add-step", projectName, "other", "--label", "Aliquots returned", "--assigned", "new-student");
        ShouldEqual(TestRepo.Steps(project)["aliquots_returned"],
            Dict(("label", "Aliquots returned"), ("status", "pending"), ("assigned", "new-student")));
        repo.Problems().ShouldContain(("WARN", $"{projectName}: step aliquots_returned: new-student is not in config/people.yaml"));
        repo.Ok("remove-step", projectName, "aliquots_returned");
        repo.RunText("list").Stdout.ShouldContain("samples: samples_received\n", Case.Sensitive);
        repo.Ok("assign", projectName, "samples_received", "--to", "maccoss");
        repo.RunText("list").Stdout.ShouldContain("samples: samples_received (maccoss)", Case.Sensitive);
    }

    [Fact]
    public void Experiment_funding_is_the_projects_unless_it_says_otherwise()
    {
        using var repo = TestRepo.Create();
        repo.NewProject("Grant-Work", "Test-Lab", "--funding", "grant", "--grant", "R01 AG012345");
        repo.NewExperiment("2026-10-Inherits", "Grant-Work");
        repo.NewExperiment("2026-11-Quoted", "Grant-Work", "--funding", "quote", "--quote", "MacCoss-2026-TEST-A");
        var p = repo.Ok("list")["labs"]![0]!["projects"]!.AsArray().ShouldHaveSingleItem()!;
        var experiments = p["experiments"]!.AsArray();
        experiments.Count.ShouldBe(2);
        var (inherits, quoted) = (experiments[0]!, experiments[1]!);
        ShouldBeJson(inherits["funding"], """{"type": "grant", "quotes": [], "grant": "R01 AG012345"}""");
        inherits["funding_inherited"]!.GetValue<bool>().ShouldBeTrue();
        ShouldBeJson(quoted["funding"], """{"type": "quote", "quotes": ["MacCoss-2026-TEST-A"], "grant": null}""");
        quoted["funding_inherited"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Check_warns_about_out_of_order_steps_and_unknown_people()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var projectName = Path.GetFileName(project);
        repo.Ok("stage", projectName, "sample_prep", "start", "--by", "someone-new");
        var problems = repo.Problems();
        problems.ShouldContain(("WARN", $"{projectName}: step sample_prep is in progress while samples_received is still pending"));
        // Assigned (on start) and recorded by the same unknown person: one warning, not two.
        problems.Count(p => p == ("WARN", $"{projectName}: step sample_prep: someone-new is not in config/people.yaml")).ShouldBe(1);
        problems.ShouldNotContain(p => p.Level == "ERROR");
    }

    [Fact]
    public void Check_finds_broken_records()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var raw = TestRepo.Raw(experiment).Replace("experiment: 2026-10-Test-DIA", "experiment: 2026-10-Typo", StringComparison.Ordinal);
        raw = raw.Replace("  type: null ", "  type: gift ", StringComparison.Ordinal);
        raw = raw.Replace("  - {id: data_deposited, kind: data_deposited, status: pending}",
            "  - {id: data_acquisition, kind: data_deposited, status: pending}\n  - {id: misc, kind: other}", StringComparison.Ordinal);
        TestRepo.WriteRaw(experiment, raw);
        raw = TestRepo.Raw(project).Replace("  - {id: plate_layout, kind: plate_layout, status: pending}",
            "  - {id: plate_layout, kind: plating, status: done, started: 2026-10-05, finished: 2026-10-01}", StringComparison.Ordinal);
        TestRepo.WriteRaw(project, raw);
        var errors = repo.Problems().Where(p => p.Level == "ERROR").Select(p => p.Message).ToList();
        errors.ShouldContain(m => m.Contains("says experiment '2026-10-Typo' but the folder is '2026-10-Test-DIA'", StringComparison.Ordinal));
        errors.ShouldContain(m => m.Contains("or null to use the project's", StringComparison.Ordinal));
        errors.ShouldContain(m => m.Contains("step data_acquisition: the id is used more than once", StringComparison.Ordinal));
        errors.ShouldContain(m => m.Contains("step misc: a step of kind other needs a label", StringComparison.Ordinal));
        errors.ShouldContain(m => m.Contains("step plate_layout: kind must be one of", StringComparison.Ordinal));
        errors.ShouldContain(m => m.Contains("step plate_layout: finished before it started", StringComparison.Ordinal));
        repo.Run("check").ExitCode.ShouldBe(1);
    }

    [Fact]
    public void The_older_layout_is_reported_not_ignored()
    {
        using var repo = TestRepo.Create();
        var old = Path.Combine(repo.Root, "projects", "Old-Group");
        Directory.CreateDirectory(Path.Combine(old, "2026-01-Old"));
        Write(Path.Combine(old, "project.yaml"), "group: Old-Group\ntitle: t\nstatus: active\n");
        Write(Path.Combine(old, "2026-01-Old", "experiment.yaml"),
            "experiment: 2026-01-Old\ntitle: t\nstatus: active\nstages:\n  samples_received: {status: done}\n");
        var errors = repo.Problems().Where(p => p.Level == "ERROR").Select(p => p.Message).ToList();
        errors.ShouldContain(m => m.Contains("projects/Old-Group/project.yaml is in the older layout", StringComparison.Ordinal));
        errors.ShouldContain(m => m.Contains("projects/Old-Group/2026-01-Old is in the older layout", StringComparison.Ordinal));
    }

    [Fact]
    public void List_active_leaves_out_closed_projects_and_counts_them()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Open-DIA", "Open-Project");
        repo.NewExperiment("2026-10-Done-DIA", "Done-Project");
        repo.NewExperiment("2026-11-Open-PRM", "Open-Project");
        var done = Path.Combine(repo.Root, "projects", "Test-Lab", "Done-Project");
        TestRepo.WriteRaw(done, ReplaceFirst(TestRepo.Raw(done), "status: active", "status: closed"));
        // A closed project is not even validated: a broken sample table there does not slow or fail the list.
        Directory.CreateDirectory(Path.Combine(done, "metadata"));
        Write(Path.Combine(done, "metadata", "samples.csv"), "not,a,sample,table\n");

        var everything = repo.Ok("list");
        var active = repo.Ok("list", "--active");

        static List<string> Names(JsonObject json) =>
            [.. json["labs"]!.AsArray().SelectMany(lab => lab!["projects"]!.AsArray()).Select(p => p!["project"]!.GetValue<string>())];
        Names(everything).ShouldBe(["Done-Project", "Open-Project"]);
        everything["closed_hidden"]!.GetValue<int>().ShouldBe(0);
        Names(active).ShouldBe(["Open-Project"]);
        active["closed_hidden"]!.GetValue<int>().ShouldBe(1);
        active["labs"]![0]!["projects"]![0]!["experiments"]!.AsArray().Select(e => e!["experiment"]!.GetValue<string>())
            .ShouldBe(["2026-10-Open-DIA", "2026-11-Open-PRM"]);
        repo.RunText("list", "--active").Stdout.ShouldContain("(1 closed project(s) not shown)", Case.Sensitive);
        repo.Problems().ShouldContain(p => p.Message.Contains("Done-Project", StringComparison.Ordinal));  // check still covers closed projects
    }

    [Fact]
    public void Records_are_read_with_the_c_yaml_loader_when_there_is_one()
    {
        // project.py read records with PyYAML's C loader (CSafeLoader) when PyYAML had one, and
        // checked that it did. The C# engine has one loader, YamlLoader, which reads as CSafeLoader
        // does; what is left to check is what project.py's parse_yaml returned.
        var value = YamlLoader.Load("a: 2026-10-01\nb: [1, two]\n");
        ShouldEqual(value, Dict(("a", new DateOnly(2026, 10, 1)), ("b", new List<object?> { 1L, "two" })));
    }

    [Fact]
    public void Duplicate_sample_ids_are_found_in_a_large_table()
    {
        using var repo = TestRepo.Create();
        var folder = repo.NewProject();
        var rows = new List<object?[]> { new object?[] { "Sample_ID", "QC", "Sample_Group" } };
        rows.AddRange(Enumerable.Range(0, 5000).Select(i => new object?[] { $"S{i}", "FALSE", "Case" }));
        rows.Add(["S42", "FALSE", "Case"]);
        TestRepo.Samples(folder, rows);
        var problems = repo.Problems().Where(p => p.Level == "ERROR").Select(p => p.Message).ToList();
        problems.ShouldContain(m => m.Contains("repeats Sample_ID S42", StringComparison.Ordinal));
    }

    [Fact]
    public void List_returns_what_the_app_reads()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot", "Pilot-Project");
        repo.Ok("stage", "Pilot-Project", "samples_received", "done", "--date", "2026-10-02");
        // The lab's own list grows as people join, so the test gives its own.
        Write(Path.Combine(repo.Root, "config", "people.yaml"),
            "people:\n" +
            "  - login: maccoss\n    name: Michael MacCoss\n    role: PI\n" +
            "  - login: jdoe\n    name: Jordan Doe\n    role: graduate student\n");
        var json = repo.Ok("list");
        json.Select(kv => kv.Key).ShouldBe(["ok", "engine_version", "labs", "projects", "people", "instruments", "closed_hidden", "problems"], ignoreOrder: true);
        json["closed_hidden"]!.GetValue<int>().ShouldBe(0);
        ShouldBeJson(json["people"], """
            [{"login": "maccoss", "name": "Michael MacCoss", "role": "PI"},
             {"login": "jdoe", "name": "Jordan Doe", "role": "graduate student"}]
            """);
        json["projects"]!.AsArray().ShouldBeEmpty();  // the top level before labs; ChargeState 26.3.0 finds nothing there
        var lab = json["labs"]!.AsArray().ShouldHaveSingleItem()!.AsObject();
        lab.Select(kv => kv.Key).ShouldBe(["lab", "folder", "title", "pi", "institution", "status", "lab_contact", "analysis_repo",
            "notebooks", "issues", "projects"], ignoreOrder: true);
        var p = lab["projects"]!.AsArray().ShouldHaveSingleItem()!.AsObject();
        p.Select(kv => kv.Key).ShouldBe(["project", "folder", "lab", "title", "status", "series", "lab_contact", "funding", "human",
            "species", "sample_type", "expected_samples", "notebooks", "protocols", "analysis", "layout", "wiki",
            "current_stage",
            "stages", "files", "issues", "experiments"], ignoreOrder: true);
        var e = p["experiments"]!.AsArray().ShouldHaveSingleItem()!.AsObject();
        e.Select(kv => kv.Key).ShouldBe(["experiment", "folder", "project", "lab", "title", "status", "lab_contact", "instrument",
            "funding", "funding_inherited", "notebooks", "panorama", "protocols", "analysis", "current_stage", "stages",
            "issues"], ignoreOrder: true);
        p["folder"]!.GetValue<string>().ShouldBe("projects/Test-Lab/Pilot-Project");
        e["folder"]!.GetValue<string>().ShouldBe($"projects/Test-Lab/Pilot-Project/{Path.GetFileName(experiment)}");
        p["current_stage"]!.GetValue<string>().ShouldBe("metadata_organized");
        ShouldBeJson(p["stages"]![0], """
            {"stage": "samples_received", "kind": "samples_received", "label": "Samples received",
             "status": "done", "assigned": null, "planned_start": null, "planned_finish": null, "started": "2026-10-02", "finished": "2026-10-02",
             "by": null, "note": null}
            """);
        ShouldBeJson(e["stages"]![1], """
            {"stage": "data_deposited", "kind": "data_deposited", "label": "Data deposited to Panorama",
             "status": "pending", "assigned": null, "planned_start": null, "planned_finish": null, "started": null, "finished": null, "by": null,
             "note": null}
            """);
        ShouldBeJson(p["files"], """{"samples": false, "layout": false, "wiki": false}""");
        p["layout"].ShouldBeNull();
        p["wiki"].ShouldBeNull();
    }

    [Fact]
    public void Index_writes_the_readme_table()
    {
        using var repo = TestRepo.Create();
        repo.NewProject("Pilot-Project", "Test-Lab", "--funding", "quote", "--quote", "MacCoss-2026-TEST-A");
        repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("index");
        var readme = ReadText(Path.Combine(repo.Root, "README.md"));
        readme.ShouldContain("| [Test-Lab](projects/Test-Lab/) | [Pilot-Project](projects/Test-Lab/Pilot-Project/) | samples | active | " +
            "Samples received |  | quote: MacCoss-2026-TEST-A |", Case.Sensitive);
        readme.ShouldContain("| | | [2026-10-Pilot-DIA](projects/Test-Lab/Pilot-Project/2026-10-Pilot-DIA/) | active | Data acquisition |  |  |",
            Case.Sensitive);
        repo.Ok("assign", "2026-10-Pilot-DIA", "data_acquisition", "--to", "maccoss");
        repo.Ok("index");
        readme = ReadText(Path.Combine(repo.Root, "README.md"));
        readme.ShouldContain("| active | Data acquisition | maccoss |  |", Case.Sensitive);
        repo.Ok("index");
        ReadText(Path.Combine(repo.Root, "README.md")).ShouldBe(readme);
    }

    [Fact]
    public void Errors_come_back_as_json()
    {
        using var repo = TestRepo.Create();
        var json = repo.Fails("stage", "2099-01-Nothing", "sample_prep", "start");
        Error(json).ShouldContain("no project or experiment", Case.Sensitive);
    }

    [Fact]
    public void Loosely_written_yaml_still_gives_the_app_the_same_shape()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        var raw = TestRepo.Raw(experiment);
        raw = raw.Replace("notebooks: []", "notebooks:\n  - ELN-4485-20230314-179\n  - {id: ELN-1, url: https://panoramaweb.org/x}", StringComparison.Ordinal);
        raw = raw.Replace("panorama: []", "panorama:\n  - /MacCoss/maccoss/2026-Test", StringComparison.Ordinal);
        raw = raw.Replace("title: Test experiment", "title: 2026", StringComparison.Ordinal);
        raw = raw.Replace("  - {id: data_analysis, kind: data_analysis, status: pending}",
            "  - {id: data_analysis, kind: data_analysis, status: in_progress, note: 42}", StringComparison.Ordinal);
        TestRepo.WriteRaw(experiment, raw);
        TestRepo.WriteRaw(project, TestRepo.Raw(project).Replace("expected_samples: null", "expected_samples: about 90", StringComparison.Ordinal));
        var p = repo.Ok("list")["labs"]![0]!["projects"]!.AsArray().ShouldHaveSingleItem()!;
        var e = p["experiments"]!.AsArray().ShouldHaveSingleItem()!;
        ShouldBeJson(e["notebooks"], """
            [{"id": "ELN-4485-20230314-179", "url": null},
             {"id": "ELN-1", "url": "https://panoramaweb.org/x"}]
            """);
        ShouldBeJson(e["panorama"], """[{"folder": "/MacCoss/maccoss/2026-Test", "kind": null}]""");
        e["title"]!.GetValue<string>().ShouldBe("2026");
        p["expected_samples"].ShouldBeNull();
        e["stages"]![3]!["note"]!.GetValue<string>().ShouldBe("42");

        TestRepo.WriteRaw(experiment, TestRepo.Raw(experiment).Replace(
            "  - {id: signal_processing, kind: signal_processing, status: pending}", "  - signal_processing", StringComparison.Ordinal));
        repo.Problems().ShouldContain(problem => problem.Level == "ERROR"
            && problem.Message.Contains("each step is written like", StringComparison.Ordinal));
        repo.Ok("list")["labs"]![0]!["projects"]![0]!["experiments"]![0]!["stages"]!.AsArray()
            .Select(s => s!["stage"]!.GetValue<string>())
            .ShouldBe(["data_acquisition", "data_deposited", "data_analysis", "results_returned"]);
    }

    [Fact]
    public void One_unreadable_record_does_not_stop_the_listing()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment();
        var project = Path.GetDirectoryName(experiment)!;
        repo.NewProject("Other-Project");
        TestRepo.WriteRaw(project, TestRepo.Raw(project).Replace("title: Test project", "title: Test: project: {unclosed", StringComparison.Ordinal));
        var projects = ProjectsByName(repo.Ok("list"));
        projects["Other-Project"]["issues"]!.AsArray().Select(i => i!["level"]!.GetValue<string>()).ShouldBeEmpty();
        var issue = projects["Test-Project"]["issues"]!.AsArray().ShouldHaveSingleItem()!;
        issue["level"]!.GetValue<string>().ShouldBe("ERROR");
        issue["message"]!.GetValue<string>().ShouldStartWith("project.yaml is not valid YAML", Case.Sensitive);
        projects["Test-Project"]["experiments"]!.AsArray().Select(x => x!["experiment"]!.GetValue<string>())
            .ShouldBe([Path.GetFileName(experiment)]);
        repo.Problems().ShouldContain(p => p.Level == "ERROR" && p.Message.Contains("project.yaml is not valid YAML", StringComparison.Ordinal));
        string[][] commands =
        [
            ["stage", "Test-Project", "sample_prep", "start"],
            ["wiki", "Test-Project"],
            ["link", "Test-Project", "notebook", "--id", "ELN-1"],
        ];
        foreach (var command in commands)
        {
            Error(repo.Fails(command)).ShouldContain("fix it first", Case.Sensitive);
        }

        TestRepo.WriteRaw(experiment, "- a list\n- not a record\n");
        var e = ProjectsByName(repo.Ok("list"))["Test-Project"]["experiments"]!.AsArray().ShouldHaveSingleItem()!;
        ShouldBeJson(e["issues"], """[{"level": "ERROR", "message": "experiment.yaml should be a mapping of fields (key: value lines)"}]""");

        static Dictionary<string, JsonNode> ProjectsByName(JsonObject json) =>
            json["labs"]![0]!["projects"]!.AsArray().ToDictionary(p => p!["project"]!.GetValue<string>(), p => p!);
    }

    [Fact]
    public void Stage_refuses_a_date_it_cannot_read_or_a_finish_before_the_start()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject();
        var projectName = Path.GetFileName(project);
        var error = Error(repo.Fails("stage", projectName, "samples_received", "done", "--date", "10/05/2026"));
        error.ShouldBe("--date 10/05/2026 is not a date written YYYY-MM-DD");
        repo.Ok("stage", projectName, "samples_received", "start", "--date", "2026-10-05");
        error = Error(repo.Fails("stage", projectName, "samples_received", "done", "--date", "2026-09-01"));
        error.ShouldContain("started on 2026-10-05, so it cannot be done on 2026-09-01", Case.Sensitive);
        TestRepo.Steps(project)["samples_received"]["status"].ShouldBe("in_progress");
    }

    [Fact]
    public void A_folder_outside_the_repository_is_refused()
    {
        using var repo = TestRepo.Create();
        using var temp = new TempDirectory();
        var outside = temp.Combine("elsewhere", "Test-Project");
        Directory.CreateDirectory(outside);
        var template = ReadText(Path.Combine(repo.Root, "templates", "project.example.yaml"));
        Write(Path.Combine(outside, "project.yaml"), template);
        var error = Error(repo.Fails("stage", outside, "samples_received", "done"));
        error.ShouldContain("is not in this repository's projects/ folder", Case.Sensitive);
        ReadText(Path.Combine(outside, "project.yaml")).ShouldBe(template);
    }

    [Fact]
    public void Index_cells_keep_to_their_column()
    {
        using var repo = TestRepo.Create();
        repo.NewProject("Grant-Project", "Test-Lab", "--funding", "grant", "--grant", "NIH R01 | supplement");
        repo.Ok("index");
        var row = File.ReadAllLines(Path.Combine(repo.Root, "README.md"), Encoding.UTF8)
            .First(l => l.Contains("Grant-Project", StringComparison.Ordinal));
        row.ShouldEndWith(@"| grant: NIH R01 \| supplement |", Case.Sensitive);
        row.Replace(@"\|", "", StringComparison.Ordinal).Count(c => c == '|').ShouldBe(8);
    }

    // -- helpers ----------------------------------------------------------------------------------

    private static string Error(JsonObject json) => json["error"]!.GetValue<string>();

    private static string ReadText(string path) => File.ReadAllText(path, Encoding.UTF8);

    private static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    /// <summary>Python's str.replace(old, new, 1).</summary>
    private static string ReplaceFirst(string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + replacement + text[(at + old.Length)..];
    }

    /// <summary>The ids of the record's steps, in order.</summary>
    private static List<string> StepIds(string folder) =>
        [.. ((List<object?>)TestRepo.Yaml(folder)["steps"]!).Cast<PyDict>().Select(s => (string)s["id"]!)];

    /// <summary>A mapping as a YAML record holds one, for comparing with <see cref="ShouldEqual"/>.</summary>
    private static PyDict Dict(params (string Key, object? Value)[] items)
    {
        var dict = new PyDict();
        foreach (var (key, value) in items)
        {
            dict[key] = value;
        }

        return dict;
    }

    /// <summary>YAML values compared as Python's == compares them: a dict's keys in any order.</summary>
    private static void ShouldEqual(object? actual, object? expected) => Shown(actual).ShouldBe(Shown(expected));

    private static string Shown(object? value) => value switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        string s => JsonSerializer.Serialize(s),
        int or long => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        DateOnly d => $"date({TestRepo.Iso(d)})",
        PyDict d => "{" + string.Join(", ", d.Select(p => Shown(p.Key) + ": " + Shown(p.Value)).Order(StringComparer.Ordinal)) + "}",
        List<object?> l => "[" + string.Join(", ", l.Select(Shown)) + "]",
        _ => $"{value} ({value.GetType().Name})",
    };

    /// <summary>JSON compared as Python compares the parsed values: an object's keys in any order.</summary>
    private static void ShouldBeJson(JsonNode? actual, string expected) =>
        Canonical(actual).ShouldBe(Canonical(JsonNode.Parse(expected)));

    private static string Canonical(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        _ => node.ToJsonString(),
    };
}
