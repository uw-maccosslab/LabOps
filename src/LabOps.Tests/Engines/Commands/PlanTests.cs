using LabOps.Core.Projects;

namespace LabOps.Tests.Engines.Commands;

/// <summary>Planned dates on steps: `labops projects plan`, what check says about them, and lateness.</summary>
public sealed class PlanTests
{
    [Fact]
    public void A_plan_is_written_on_the_step_line_after_who_does_it()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("assign", "Pilot-Project", "sample_prep", "--to", "maccoss");

        var json = repo.Ok("plan", "Pilot-Project", "sample_prep", "--start", "2026-11-02", "--finish", "2026-11-13");

        TestRepo.Raw(project).ShouldContain(
            "  - {id: sample_prep, kind: sample_prep, status: pending, assigned: maccoss, planned_start: 2026-11-02, planned_finish: 2026-11-13}\n",
            Case.Sensitive);
        var step = json["project"]!["stages"]!.AsArray().Single(s => s!["stage"]!.GetValue<string>() == "sample_prep")!;
        step["planned_start"]!.GetValue<string>().ShouldBe("2026-11-02");
        step["planned_finish"]!.GetValue<string>().ShouldBe("2026-11-13");
        repo.RunText("plan", "Pilot-Project", "plate_layout", "--finish", "2026-10-30").Stdout
            .ShouldStartWith("Pilot-Project: plate_layout planned to finish 2026-10-30", Case.Sensitive);
        repo.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void A_date_not_given_keeps_the_one_recorded_and_clear_removes_both()
    {
        using var repo = TestRepo.Create();
        var experiment = repo.NewExperiment("2026-10-Pilot-DIA", "Pilot-Project");
        repo.Ok("plan", "2026-10-Pilot-DIA", "data_acquisition", "data_deposited", "--start", "2026-11-02", "--finish", "2026-11-06");

        repo.Ok("plan", "2026-10-Pilot-DIA", "data_deposited", "--finish", "2026-11-09");

        var steps = TestRepo.Steps(experiment);
        steps["data_acquisition"]["planned_finish"].ShouldBe(new DateOnly(2026, 11, 6));
        steps["data_deposited"]["planned_start"].ShouldBe(new DateOnly(2026, 11, 2));
        steps["data_deposited"]["planned_finish"].ShouldBe(new DateOnly(2026, 11, 9));

        repo.Ok("plan", "2026-10-Pilot-DIA", "data_acquisition", "--clear");
        TestRepo.Steps(experiment)["data_acquisition"].ContainsKey("planned_start").ShouldBeFalse();
        TestRepo.Steps(experiment)["data_acquisition"].ContainsKey("planned_finish").ShouldBeFalse();
    }

    [Fact]
    public void One_date_can_be_removed_and_the_other_set_in_one_write()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("plan", "Pilot-Project", "sample_prep", "--start", "2026-11-02", "--finish", "2026-11-13");

        repo.RunText("plan", "Pilot-Project", "sample_prep", "--no-start", "--finish", "2026-11-20").Stdout
            .ShouldStartWith("Pilot-Project: sample_prep planned start removed, planned to finish 2026-11-20", Case.Sensitive);

        var step = TestRepo.Steps(project)["sample_prep"];
        step.ContainsKey("planned_start").ShouldBeFalse();
        step["planned_finish"].ShouldBe(new DateOnly(2026, 11, 20));
        repo.RunText("plan", "Pilot-Project", "sample_prep", "--no-finish").Stdout
            .ShouldStartWith("Pilot-Project: sample_prep planned finish removed", Case.Sensitive);
        TestRepo.Steps(project)["sample_prep"].ContainsKey("planned_finish").ShouldBeFalse();
        repo.Fails("plan", "Pilot-Project", "sample_prep", "--start", "2026-11-02", "--no-start")["error"]!.GetValue<string>()
            .ShouldBe("give a date or remove it, not both: --start or --no-start, --finish or --no-finish");
    }

    [Theory]
    [InlineData("--finish", "2026-11-01", "would be planned to finish (2026-11-01) before it starts (2026-11-02)")]
    [InlineData("--start", "2026-11-31", "--start 2026-11-31 is not a date written YYYY-MM-DD")]
    [InlineData("--clear", null, "give --start DATE and/or --finish DATE, or --clear to remove the plan")]
    public void A_plan_that_cannot_be_is_refused_and_nothing_is_written(string option, string? value, string message)
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        repo.Ok("plan", "Pilot-Project", "sample_prep", "--start", "2026-11-02");
        var before = TestRepo.Raw(project);

        // --clear with a date is refused too: it is one or the other.
        string[] args = value is null ? ["plan", "Pilot-Project", "sample_prep", option, "--start", "2026-11-03"]
            : ["plan", "Pilot-Project", "sample_prep", option, value];
        repo.Fails(args)["error"]!.GetValue<string>().ShouldContain(message, Case.Sensitive);
        TestRepo.Raw(project).ShouldBe(before);
        repo.Fails("plan", "Pilot-Project", "sample_prep")["error"]!.GetValue<string>()
            .ShouldBe("give --start DATE and/or --finish DATE, or --clear to remove the plan");
    }

    [Fact]
    public void Check_refuses_a_plan_written_by_hand_that_is_not_dates_or_ends_first()
    {
        using var repo = TestRepo.Create();
        var project = repo.NewProject("Pilot-Project");
        TestRepo.WriteRaw(project, TestRepo.Raw(project)
            .Replace("{id: plate_layout, kind: plate_layout, status: pending}",
                "{id: plate_layout, kind: plate_layout, status: pending, planned_start: next week}", StringComparison.Ordinal)
            .Replace("{id: sample_prep, kind: sample_prep, status: pending}",
                "{id: sample_prep, kind: sample_prep, status: pending, planned_start: 2026-11-10, planned_finish: 2026-11-02}", StringComparison.Ordinal));

        var errors = repo.Problems().Where(p => p.Level == "ERROR").Select(p => p.Message).ToList();

        errors.ShouldContain(m => m.EndsWith("step plate_layout: planned_start must be a date (YYYY-MM-DD)", StringComparison.Ordinal));
        errors.ShouldContain(m => m.EndsWith("step sample_prep: planned to finish before it starts", StringComparison.Ordinal));
    }

    [Theory]
    // status, planned start, planned finish, late on 2026-11-10?
    [InlineData("pending", null, null, false)]
    [InlineData("pending", "2026-11-09", null, true)]
    [InlineData("pending", "2026-11-10", null, false)]
    [InlineData("in_progress", "2026-11-01", null, false)]
    [InlineData("in_progress", "2026-11-01", "2026-11-09", true)]
    [InlineData("in_progress", "2026-11-01", "2026-11-10", false)]
    [InlineData("done", "2026-11-01", "2026-11-02", false)]
    [InlineData("skipped", "2026-11-01", "2026-11-02", false)]
    [InlineData("pending", "not a date", null, false)]
    public void A_step_is_late_when_its_plan_has_passed_and_it_is_not_done(string status, string? start, string? finish, bool late) =>
        new StageEntry { Stage = "x", Status = status, PlannedStart = start, PlannedFinish = finish }
            .IsLate(new DateOnly(2026, 11, 10)).ShouldBe(late);
}
