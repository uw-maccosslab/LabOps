using LabOps.Engines.Python;

namespace LabOps.Tests.Engines.Commands;

/// <summary>
/// A step's work in batches (sample prep of three plates): `labops projects split`, the batches as
/// steps of their own, and what check says about them.
/// </summary>
public sealed class BatchTests
{
    [Fact]
    public void A_step_not_begun_becomes_batches_numbered_in_a_row_which_each_go_on_their_own()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("assign", "Pilot-Project", "sample_prep", "--to", "maccoss");
        repo.Ok("plan", "Pilot-Project", "sample_prep", "--start", "2026-11-02", "--finish", "2026-11-13");

        var answer = repo.RunText("split", "Pilot-Project", "sample_prep", "--batches", "3");

        answer.Stdout.ShouldStartWith(
            "Pilot-Project: Sample prep in 3 batches (sample_prep_batch1, sample_prep_batch2, sample_prep_batch3)", Case.Sensitive);
        TestRepo.Raw(project).ShouldContain(
            "  - {id: sample_prep_batch1, kind: sample_prep, batch: 1, status: pending, assigned: maccoss, planned_start: 2026-11-02}\n"
            + "  - {id: sample_prep_batch2, kind: sample_prep, batch: 2, status: pending, assigned: maccoss}\n"
            + "  - {id: sample_prep_batch3, kind: sample_prep, batch: 3, status: pending, assigned: maccoss, planned_finish: 2026-11-13}\n",
            Case.Sensitive);
        var json = repo.Ok("list");
        var stages = json["labs"]![0]!["projects"]![0]!["stages"]!.AsArray();
        stages.Where(s => s!["kind"]!.GetValue<string>() == "sample_prep")
            .Select(s => (s!["label"]!.GetValue<string>(), s["batch"]!.GetValue<long>()))
            .ShouldBe([("Sample prep", 1L), ("Sample prep", 2L), ("Sample prep", 3L)]);
        stages.Where(s => s!["kind"]!.GetValue<string>() != "sample_prep").ShouldAllBe(s => !s!.AsObject().ContainsKey("batch"));

        repo.RunText("stage", "Pilot-Project", "sample_prep_batch2", "start", "--by", "maccoss").Stdout
            .ShouldStartWith("Pilot-Project: Sample prep, batch 2 in progress", Case.Sensitive);
        TestRepo.Steps(project)["sample_prep_batch1"]["status"].ShouldBe("pending");
        repo.Problems().Where(p => p.Level == "ERROR" || p.Message.Contains("numbered", StringComparison.Ordinal)
            || p.Message.Contains("only batch", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    /// <summary>
    /// A step split after it was done keeps only what is known: when the first batch started and
    /// the last finished, every batch done. Check does not ask for the dates of the others.
    /// </summary>
    [Fact]
    public void A_done_step_keeps_its_start_on_the_first_batch_and_its_finish_on_the_last()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("stage", "Pilot-Project", "sample_prep", "start", "--date", "2026-09-11", "--by", "maccoss");
        repo.Ok("stage", "Pilot-Project", "sample_prep", "done", "--date", "2026-09-13", "--by", "maccoss");

        repo.Ok("split", "Pilot-Project", "sample_prep", "--batches", "3");

        TestRepo.Raw(project).ShouldContain(
            "  - {id: sample_prep_batch1, kind: sample_prep, batch: 1, status: done, assigned: maccoss, started: 2026-09-11, by: maccoss}\n"
            + "  - {id: sample_prep_batch2, kind: sample_prep, batch: 2, status: done, assigned: maccoss, by: maccoss}\n"
            + "  - {id: sample_prep_batch3, kind: sample_prep, batch: 3, status: done, assigned: maccoss, finished: 2026-09-13, by: maccoss}\n",
            Case.Sensitive);
        repo.Problems().ShouldNotContain(p => p.Message.Contains("finished date", StringComparison.Ordinal));
    }

    [Fact]
    public void Work_under_way_goes_on_the_first_batch_and_a_skipped_step_is_not_split()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("stage", "2026-10-Pilot-DIA", "data_acquisition", "start", "--date", "2026-10-05", "--by", "maccoss");

        repo.Ok("split", "2026-10-Pilot-DIA", "data_acquisition", "--batches", "2");

        var steps = TestRepo.Steps(experiment);
        (steps["data_acquisition_batch1"]["status"], steps["data_acquisition_batch1"]["started"]).ShouldBe(("in_progress", new DateOnly(2026, 10, 5)));
        (steps["data_acquisition_batch2"]["status"], steps["data_acquisition_batch2"]["started"]).ShouldBe(("pending", null));

        repo.Ok("stage", "2026-10-Pilot-DIA", "data_deposited", "skip");
        Error(repo.Fails("split", "2026-10-Pilot-DIA", "data_deposited", "--batches", "2"))
            .ShouldBe("step data_deposited was skipped, so there is no work to split into batches");
        Error(repo.Fails("split", "2026-10-Pilot-DIA", "signal_processing", "--batches", "1"))
            .ShouldBe("give --batches from 2 to 48; a step done all at once needs no batches");
    }

    [Fact]
    public void Batches_can_be_added_and_taken_away_while_nothing_is_recorded_for_them()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("assign", "Pilot-Project", "sample_prep", "--to", "maccoss");
        repo.Ok("split", "Pilot-Project", "sample_prep", "--batches", "2");

        repo.RunText("split", "Pilot-Project", "sample_prep_batch1", "--batches", "4").Stdout
            .ShouldStartWith("Pilot-Project: Sample prep in 4 batches, added sample_prep_batch3, sample_prep_batch4", Case.Sensitive);
        TestRepo.Raw(project).ShouldContain(
            "  - {id: sample_prep_batch4, kind: sample_prep, batch: 4, status: pending, assigned: maccoss}\n", Case.Sensitive);

        repo.Ok("stage", "Pilot-Project", "sample_prep_batch3", "start");
        Error(repo.Fails("split", "Pilot-Project", "sample_prep_batch2", "--batches", "2"))
            .ShouldBe("batch 3 of Sample prep (sample_prep_batch3) has a record or a plan, so Sample prep cannot go down to 2 batches");
        repo.RunText("split", "Pilot-Project", "sample_prep_batch2", "--batches", "3").Stdout
            .ShouldStartWith("Pilot-Project: Sample prep in 3 batches, removed sample_prep_batch4", Case.Sensitive);
        repo.RunText("split", "Pilot-Project", "sample_prep_batch2", "--batches", "3").Stdout
            .ShouldStartWith("Pilot-Project: Sample prep is in 3 batches already", Case.Sensitive);

        Error(repo.Fails("remove-step", "Pilot-Project", "sample_prep_batch2"))
            .ShouldBe("step sample_prep_batch2 is batch 2 of Sample prep; change the number of batches with "
                      + "split Pilot-Project sample_prep_batch2 --batches N");
    }

    [Fact]
    public void A_protocol_recorded_for_the_step_is_recorded_for_its_first_batch()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("link", "Pilot-Project", "protocol", "spin-trap-digestion", "--version", "2", "--step", "sample_prep");

        repo.Ok("split", "Pilot-Project", "sample_prep", "--batches", "3");

        var protocol = (PyDict)((List<object?>)TestRepo.Yaml(project)["protocols"]!)[0]!;
        protocol["step"].ShouldBe("sample_prep_batch1");
        repo.Problems().Where(p => p.Level == "ERROR").ShouldBeEmpty();
    }

    [Fact]
    public void Check_says_when_batches_written_by_hand_are_not_numbered_in_a_row()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        var raw = TestRepo.Raw(project);
        TestRepo.WriteRaw(project, raw.Replace(
            "  - {id: sample_prep, kind: sample_prep, status: pending}\n",
            "  - {id: prep_a, kind: sample_prep, batch: 1, status: done}\n"
            + "  - {id: prep_b, kind: sample_prep, batch: 3, status: done}\n"
            + "  - {id: lone, kind: data_acquisition, batch: 1, status: pending}\n"
            + "  - {id: odd, kind: other, label: Odd, batch: zero, status: pending}\n",
            StringComparison.Ordinal));

        var problems = repo.Problems();

        problems.ShouldContain(("WARN", "Pilot-Project: steps prep_a to prep_b: the batches of Sample prep are numbered 1 to 2 in order"));
        problems.ShouldContain(("WARN", "Pilot-Project: step lone is the only batch of Data acquisition; a step done all at once needs no batch number"));
        problems.ShouldContain(("ERROR", "Pilot-Project: step odd: batch is the batch's number, 1 or more"));
        // Done batches with no finished date at all: said once, on the last.
        problems.Where(p => p.Message.EndsWith("is done but has no finished date", StringComparison.Ordinal)).Select(p => p.Message)
            .ShouldBe(["Pilot-Project: step prep_b is done but has no finished date"]);
    }

    [Fact]
    public void The_wiki_page_names_each_batch()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("split", "Pilot-Project", "sample_prep", "--batches", "2");
        var page = Path.Combine(repo.Root, "page.html");

        repo.Ok("wiki", "Pilot-Project", "--out", page);

        var html = File.ReadAllText(page);
        html.ShouldContain("Sample prep, batch 1</td>", Case.Sensitive);
        html.ShouldContain("Sample prep, batch 2</td>", Case.Sensitive);
    }

    private static string Error(System.Text.Json.Nodes.JsonObject answer) => answer["error"]!.GetValue<string>();
}
