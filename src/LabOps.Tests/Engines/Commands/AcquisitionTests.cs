using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabOps.Engines.Python;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// `labops projects acquisition`: what an experiment's raw files say, as LabOps read them on
/// Panorama, becomes its record of how the data were acquired, its data acquisition step's dates
/// (done), and its instrument.
/// </summary>
public sealed class AcquisitionTests
{
    private const string Raw = "/MacCoss/Lab/Pilot/@files/RawFiles";
    private const string Method = "Astral_DIA_24m.meth";

    private static void WriteInstruments(TestRepo repo, string text) =>
        File.WriteAllText(Path.Combine(repo.Root, "config", "instruments.yaml"), text, new UTF8Encoding(false));

    /// <summary>The app's input: one entry per raw file (folder, acquisition time, method, instrument).</summary>
    private static string Input(TestRepo repo, params (string Folder, string Acquired, string Method, string Serial)[] files)
    {
        var path = Path.Combine(repo.Root, "..", $"raw-{Guid.NewGuid():N}.json");
        var list = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject
        {
            ["folder"] = f.Folder, ["acquired"] = f.Acquired, ["method_file"] = Method, ["method"] = f.Method,
            ["model"] = "Orbitrap Astral Zoom", ["serial"] = f.Serial,
        })]);
        File.WriteAllText(path, new JsonObject { ["files"] = list }.ToJsonString());
        return path;
    }

    [Fact]
    public void The_raw_files_date_the_step_mark_it_done_and_name_the_instrument()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "instruments:\n  - name: Orbitrap Astral\n    serials: [OA10308]\n  - name: Stellar\n");
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("stage", "2026-10-Pilot-DIA", "data_acquisition", "start", "--date", "2026-10-05");
        var input = Input(repo,
            ($"{Raw}", "2026-10-03T21:10:00", "aaaaaaaaaaaa", "OA10308"),
            ($"{Raw}", "2026-10-04T08:00:00", "aaaaaaaaaaaa", "OA10308"),
            ($"{Raw}", "2026-10-06T23:59:00", "aaaaaaaaaaaa", "OA10308"));

        var answer = repo.RunText("acquisition", "2026-10-Pilot-DIA", "--from", input, "--by", "maccoss");

        answer.ExitCode.ShouldBe(0);
        answer.Stdout.ShouldStartWith("2026-10-Pilot-DIA: raw files read: 3 files, 1 method, Orbitrap Astral Zoom (OA10308), "
                                      + "2026-10-03 to 2026-10-06; instrument Orbitrap Astral; Data acquisition done", Case.Sensitive);
        // The raw files' dates replace the start someone recorded from memory.
        var step = TestRepo.Steps(experiment)["data_acquisition"];
        step["status"].ShouldBe("done");
        step["started"].ShouldBe(new DateOnly(2026, 10, 3));
        step["finished"].ShouldBe(new DateOnly(2026, 10, 6));
        step["by"].ShouldBe("maccoss");
        var record = TestRepo.Yaml(experiment);
        record["instrument"].ShouldBe("Orbitrap Astral");
        var line = ((List<object?>)record["acquisition"]!).ShouldHaveSingleItem().ShouldBeOfType<PyDict>();
        line["folder"].ShouldBe(Raw);
        line["method_file"].ShouldBe(Method);
        line["serial"].ShouldBe("OA10308");
        line["files"].ShouldBe(3L);
        line["first"].ShouldBe(new DateOnly(2026, 10, 3));
        line["last"].ShouldBe(new DateOnly(2026, 10, 6));
        line["method"].ShouldBe("aaaaaaaaaaaa");
        TestRepo.Raw(experiment).ShouldContain("\n# How the data were acquired, from the raw files on Panorama (Read raw files in LabOps).\nacquisition:\n  - {folder: ");
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Folders_numbered_like_the_batches_date_each_batch()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("split", "2026-10-Pilot-DIA", "data_acquisition", "--batches", "2");
        var input = Input(repo,
            ($"{Raw}/Plate_1", "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308"),
            ($"{Raw}/Plate_1", "2026-10-02T10:00:00", "aaaaaaaaaaaa", "OA10308"),
            ($"{Raw}/Plate_2", "2026-10-04T10:00:00", "aaaaaaaaaaaa", "OA10308"),
            ($"{Raw}/Plate_2", "2026-10-05T10:00:00", "bbbbbbbbbbbb", "OA10308"),
            // Process control runs: no number, so no batch of their own.
            ($"{Raw}/ProcessControl", "2026-09-30T10:00:00", "cccccccccccc", "OA10308"));

        repo.Ok("acquisition", "2026-10-Pilot-DIA", "--from", input);

        var steps = TestRepo.Steps(experiment);
        steps["data_acquisition_batch1"]["started"].ShouldBe(new DateOnly(2026, 10, 1));
        steps["data_acquisition_batch1"]["finished"].ShouldBe(new DateOnly(2026, 10, 2));
        steps["data_acquisition_batch2"]["started"].ShouldBe(new DateOnly(2026, 10, 4));
        steps["data_acquisition_batch2"]["finished"].ShouldBe(new DateOnly(2026, 10, 5));
        steps["data_acquisition_batch1"]["status"].ShouldBe("done");
        steps["data_acquisition_batch2"]["status"].ShouldBe("done");
        // One line per folder and method, in the order acquired.
        ((List<object?>)TestRepo.Yaml(experiment)["acquisition"]!).Cast<PyDict>()
            .Select(a => $"{a["folder"]} {a["method"]} {a["files"]}")
            .ShouldBe([$"{Raw}/ProcessControl cccccccccccc 1", $"{Raw}/Plate_1 aaaaaaaaaaaa 2", $"{Raw}/Plate_2 aaaaaaaaaaaa 1", $"{Raw}/Plate_2 bbbbbbbbbbbb 1"]);
    }

    [Fact]
    public void Folders_not_numbered_like_the_batches_date_the_first_start_and_the_last_finish()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("split", "2026-10-Pilot-DIA", "data_acquisition", "--batches", "3");
        var input = Input(repo,
            ($"{Raw}/Plate_1", "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308"),
            ($"{Raw}/Plate_2", "2026-10-05T10:00:00", "aaaaaaaaaaaa", "OA10308"));

        var answer = repo.RunText("acquisition", "2026-10-Pilot-DIA", "--from", input);

        answer.Stdout.ShouldContain("the folders are not numbered like its 3 batches");
        var steps = TestRepo.Steps(experiment);
        steps["data_acquisition_batch1"]["started"].ShouldBe(new DateOnly(2026, 10, 1));
        steps["data_acquisition_batch3"]["finished"].ShouldBe(new DateOnly(2026, 10, 5));
        steps.Where(s => s.Key.StartsWith("data_acquisition", StringComparison.Ordinal)).ShouldAllBe(s => (string)s.Value["status"]! == "done");
    }

    [Fact]
    public void Remembered_batch_dates_outside_the_raw_files_are_dropped_so_the_record_still_checks()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("split", "2026-10-Pilot-DIA", "data_acquisition", "--batches", "3");
        // Remembered wrong: the first batch "finished" before the first raw file was acquired.
        repo.Ok("stage", "2026-10-Pilot-DIA", "data_acquisition_batch1", "start", "--date", "2026-09-20");
        repo.Ok("stage", "2026-10-Pilot-DIA", "data_acquisition_batch1", "done", "--date", "2026-09-21");
        repo.Ok("stage", "2026-10-Pilot-DIA", "data_acquisition_batch2", "start", "--date", "2026-10-02");
        repo.Ok("stage", "2026-10-Pilot-DIA", "data_acquisition_batch2", "done", "--date", "2026-10-03");
        var input = Input(repo,
            (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308"),
            (Raw, "2026-10-05T10:00:00", "aaaaaaaaaaaa", "OA10308"));

        repo.Ok("acquisition", "2026-10-Pilot-DIA", "--from", input);

        var steps = TestRepo.Steps(experiment);
        steps["data_acquisition_batch1"]["started"].ShouldBe(new DateOnly(2026, 10, 1));
        steps["data_acquisition_batch1"]["finished"].ShouldBeNull();
        // Within the raw files' span, so kept.
        steps["data_acquisition_batch2"]["started"].ShouldBe(new DateOnly(2026, 10, 2));
        steps["data_acquisition_batch2"]["finished"].ShouldBe(new DateOnly(2026, 10, 3));
        steps["data_acquisition_batch3"]["finished"].ShouldBe(new DateOnly(2026, 10, 5));
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Serials_are_read_from_an_instruments_file_written_as_a_list()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "- name: Orbitrap Astral\n  serials: [OA10308]\n- name: Stellar\n");
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");

        repo.Ok("acquisition", "2026-10-Pilot-DIA", "--from", Input(repo, (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308")));

        TestRepo.Yaml(experiment)["instrument"].ShouldBe("Orbitrap Astral");
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Reading_again_replaces_what_the_files_said_before()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("acquisition", "2026-10-Pilot-DIA", "--from", Input(repo, (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308")));

        repo.Ok("acquisition", "2026-10-Pilot-DIA", "--from", Input(repo,
            (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308"), (Raw, "2026-10-09T10:00:00", "aaaaaaaaaaaa", "OA10308")));

        var line = ((List<object?>)TestRepo.Yaml(experiment)["acquisition"]!).ShouldHaveSingleItem().ShouldBeOfType<PyDict>();
        line["files"].ShouldBe(2L);
        TestRepo.Steps(experiment)["data_acquisition"]["finished"].ShouldBe(new DateOnly(2026, 10, 9));
        Regex.Count(TestRepo.Raw(experiment), "^acquisition:", RegexOptions.Multiline).ShouldBe(1);
    }

    [Fact]
    public void An_unlisted_serial_and_another_instrument_are_warnings_and_the_named_instrument_stays()
    {
        using var repo = TestRepo.Create();
        WriteInstruments(repo, "instruments:\n  - name: Orbitrap Astral\n    serials: [OA10308]\n  - name: Stellar\n    serials: [QLT-S-10013]\n");
        var experiment = repo.NewExperiment("2026-10-Pilot-PRM", "Pilot-Project", "--instrument", "Stellar");

        repo.Ok("acquisition", "2026-10-Pilot-PRM", "--from", Input(repo,
            (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "oa10308"), (Raw, "2026-10-02T10:00:00", "aaaaaaaaaaaa", "XYZ-1")));

        TestRepo.Yaml(experiment)["instrument"].ShouldBe("Stellar");
        repo.Problems().ShouldBe(
        [
            ("WARN", "2026-10-Pilot-PRM: instrument is 'Stellar', but the raw files were acquired on the Orbitrap Astral (serial oa10308)"),
            ("WARN", "2026-10-Pilot-PRM: the raw files were acquired on Orbitrap Astral Zoom serial XYZ-1, which config/instruments.yaml "
                     + "does not list; add it to that instrument's serials"),
        ]);
    }

    [Fact]
    public void An_experiment_without_raw_files_read_has_no_acquisition_in_its_summary()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.NewExperiment("2026-11-Pilot-PRM", "Pilot-Project");
        repo.Ok("acquisition", "2026-10-Pilot-DIA", "--from", Input(repo, (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308")));

        var experiments = repo.Ok("list")["labs"]!.AsArray().SelectMany(l => l!["projects"]!.AsArray())
            .SelectMany(p => p!["experiments"]!.AsArray()).ToDictionary(e => e!["experiment"]!.GetValue<string>(), e => e!.AsObject());

        experiments["2026-11-Pilot-PRM"].ContainsKey("acquisition").ShouldBeFalse();
        var read = experiments["2026-10-Pilot-DIA"]["acquisition"]!.AsArray().ShouldHaveSingleItem()!;
        read["files"]!.GetValue<long>().ShouldBe(1);
        read["first"]!.GetValue<string>().ShouldBe("2026-10-01");
        read["method_file"]!.GetValue<string>().ShouldBe(Method);
    }

    [Fact]
    public void It_is_refused_for_a_project_an_empty_folder_and_a_list_it_cannot_read()
    {
        using var repo = TestRepo.Create();
        repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        var one = Input(repo, (Raw, "2026-10-01T10:00:00", "aaaaaaaaaaaa", "OA10308"));
        var empty = Path.Combine(repo.Root, "..", "empty.json");
        File.WriteAllText(empty, "{\"files\": []}");
        var bad = Path.Combine(repo.Root, "..", "bad.json");
        File.WriteAllText(bad, "{\"files\": [{\"folder\": \"/x\"}]}");

        repo.Fails("acquisition", "Pilot-Project", "--from", one)["error"]!.GetValue<string>()
            .ShouldBe("raw files belong to an experiment (one measurement of the samples); give one of Pilot-Project's experiments");
        repo.Fails("acquisition", "2026-10-Pilot-DIA", "--from", empty)["error"]!.GetValue<string>()
            .ShouldBe("the raw data folder has no raw files yet; read it again once they are on Panorama");
        repo.Fails("acquisition", "2026-10-Pilot-DIA", "--from", bad)["error"]!.GetValue<string>()
            .ShouldEndWith("has a raw file without its folder, method or acquisition time");
    }
}
