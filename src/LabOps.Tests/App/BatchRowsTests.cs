using CommunityToolkit.Mvvm.Input;
using LabOps.App.ViewModels;
using LabOps.Core.Projects;

namespace LabOps.Tests.App;

/// <summary>How the Projects list shows a step done in batches: one row with its batches indented under it.</summary>
public sealed class BatchRowsTests
{
    [Fact]
    public void A_step_in_batches_is_one_row_with_its_batches_under_it()
    {
        var section = Section(
            Step("plate_layout", "plate_layout", status: "done"),
            Step("sample_prep_batch1", "sample_prep", 1, "done", "2026-09-11", "2026-09-11", "maccoss"),
            Step("sample_prep_batch2", "sample_prep", 2, "done", "2026-09-12", "2026-09-12", "maccoss"),
            Step("sample_prep_batch3", "sample_prep", 3, "in_progress", "2026-09-13", assigned: "maccoss"),
            Step("data_acquisition", "data_acquisition"));
        var steps = section.Stages;

        var group = section.Rows.OfType<BatchGroupRowViewModel>().ShouldHaveSingleItem();
        section.Rows.ShouldBe([steps[0], group, steps[1], steps[2], steps[3], steps[4]]);
        (group.Label, group.CountText, group.Status, group.IsCurrent, group.Stage)
            .ShouldBe(("Sample prep", "3 batches", "in_progress", true, "sample_prep_batch1"));
        group.StatusText.ShouldBe("2 of 3 done, since 2026-09-11");
        group.AssignedText.ShouldBe("Assigned to maccoss");

        (steps[2].RowLabel, steps[2].Label, steps[2].IsBatch).ShouldBe(("Batch 2", "Sample prep, batch 2", true));
        (steps[2].CanSplit, steps[2].CanRemove).ShouldBe((false, false), "a batch goes by changing its step's batches");
        steps[4].CanSplit.ShouldBeTrue("acquisition can be done in batches");
        steps[0].CanSplit.ShouldBeFalse("only sample prep and acquisition are");
        (steps[4].RowLabel, steps[4].IsBatch).ShouldBe(("Data acquisition", false));
    }

    [Fact]
    public void A_step_whose_batches_are_all_done_says_from_when_to_when()
    {
        var group = Section(
                Step("prep_batch1", "sample_prep", 1, "done", "2026-09-11"),
                Step("prep_batch2", "sample_prep", 2, "done"),
                Step("prep_batch3", "sample_prep", 3, "done", finished: "2026-09-13"),
                Step("acq", "data_acquisition", 1),
                Step("acq2", "data_acquisition", 2))
            .Rows.OfType<BatchGroupRowViewModel>().ToList();

        group.Count.ShouldBe(2, "a run of another kind is a step of its own");
        (group[0].Status, group[0].StatusText, group[0].IsCurrent).ShouldBe(("done", "3 of 3 done, 2026-09-11 to 2026-09-13", false));
        (group[1].Status, group[1].StatusText, group[1].IsCurrent).ShouldBe(("pending", "0 of 2 done", true));
        group[1].AssignedText.ShouldBeNull();
    }

    [Fact]
    public void Two_steps_of_one_kind_in_batches_side_by_side_are_two_rows()
    {
        var groups = Section(
                Step("prep_batch1", "sample_prep", 1),
                Step("prep_batch2", "sample_prep", 2),
                Step("prep_2_batch1", "sample_prep", 1),
                Step("prep_2_batch2", "sample_prep", 2))
            .Rows.OfType<BatchGroupRowViewModel>().ToList();

        groups.Select(g => g.Batches.Select(b => b.Stage).ToList())
            .ShouldBe([["prep_batch1", "prep_batch2"], ["prep_2_batch1", "prep_2_batch2"]]);
    }

    /// <summary>
    /// The step's links and tools (its notebook, the protocol it followed, Add protocol) are the
    /// same for every batch, so they go on the row above the batches.
    /// </summary>
    [Fact]
    public void A_step_in_batches_has_its_links_and_tools_on_its_own_row()
    {
        var section = Section(
            Step("sample_prep_batch1", "sample_prep", 1),
            Step("sample_prep_batch2", "sample_prep", 2));
        var notebook = new LinkItem("ELN notebook ELN-1", "https://example.org/eln");
        var protocol = new LinkItem("Protocol: Spin trap digestion, version 2", "labops-protocol:spin-trap-digestion", Step: "sample_prep_batch1");
        var tool = new StepTool("Add protocol", "", new RelayCommand(() => { }));

        section.Place(notebook, StepHomes.Notebook);
        section.PlaceOnStep(protocol, "sample_prep_batch1");
        section.Place(tool, StepHomes.Protocol);

        var group = section.Rows.OfType<BatchGroupRowViewModel>().ShouldHaveSingleItem();
        group.Links.ShouldBe([notebook, protocol]);
        group.Tools.ShouldBe([tool]);
        section.Stages.ShouldAllBe(s => !s.HasLinks && !s.HasTools);
        section.Links.ShouldBeEmpty();
    }

    [Fact]
    public void A_batch_reads_with_its_number_wherever_steps_are_listed()
    {
        Step("sample_prep_batch2", "sample_prep", 2).DisplayLabel.ShouldBe("Sample prep, batch 2");
        Step("sample_prep_batch2", "sample_prep", 2).BaseLabel.ShouldBe("Sample prep");
        Step("sample_prep", "sample_prep").DisplayLabel.ShouldBe("Sample prep");
        (Step("x", "other", 1) with { Label = "Unblinding" }).DisplayLabel.ShouldBe("Unblinding, batch 1");
    }

    private static TimelineSection Section(params StageEntry[] steps) =>
        new(new ProjectSummary
            {
                Project = "Pilot-Project",
                Stages = steps,
                CurrentStage = steps.FirstOrDefault(s => s.Status is not ("done" or "skipped"))?.Stage,
            },
            "projects/Test-Lab/Pilot-Project", "Samples", "", [], []);

    private static StageEntry Step(string id, string kind, int? batch = null, string status = "pending", string? started = null,
        string? finished = null, string? assigned = null) =>
        new() { Stage = id, Kind = kind, Batch = batch, Status = status, Started = started, Finished = finished, Assigned = assigned };
}
