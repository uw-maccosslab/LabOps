using System.Globalization;
using LabOps.Core.Projects;

namespace LabOps.Tests.Projects;

/// <summary>A made-up lab's open work for the overview's tests: four labs, plans, a late step, a clash.</summary>
internal static class DashboardSample
{
    public static readonly DateOnly Today = new(2026, 10, 9);

    private static string D(int days) => Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static StageEntry Step(string kind, string status = "pending", string? assigned = null, int? started = null, int? finished = null,
        int? plannedStart = null, int? plannedFinish = null, string? label = null, string? id = null) => new()
    {
        Stage = id ?? kind, Kind = kind, Label = label, Status = status, Assigned = assigned,
        Started = started is { } s ? D(s) : null, Finished = finished is { } f ? D(f) : null,
        PlannedStart = plannedStart is { } ps ? D(ps) : null, PlannedFinish = plannedFinish is { } pf ? D(pf) : null,
    };

    private static ProjectSummary Project(string lab, string name, string title, string? contact, StageEntry[] steps, params ExperimentSummary[] experiments) => new()
    {
        Project = name, Lab = lab, Folder = $"projects/{lab}/{name}", Title = title, Status = "active", LabContact = contact,
        Stages = steps, CurrentStage = steps.FirstOrDefault(s => !(s.IsDone || s.IsSkipped))?.Stage, Experiments = experiments,
        Wiki = name == "MNRF-BioTRACK" ? new WikiLocation("/MacCoss/Collaborations/MNRF/BioTRACK", null) : null,
    };

    private static ExperimentSummary Experiment(string lab, string project, string name, string instrument, StageEntry[] steps) => new()
    {
        Experiment = name, Project = project, Lab = lab, Folder = $"projects/{lab}/{project}/{name}", Title = $"{name} measurement",
        Status = "active", Instrument = instrument, Stages = steps, CurrentStage = steps.FirstOrDefault(s => !(s.IsDone || s.IsSkipped))?.Stage,
    };

    public static ProjectList List()
    {
        var otter = Project("ClearwaterZoo-Cole", "CWZ-Otter-EV", "Otter plasma extracellular vesicles", "jdoe",
        [
            Step("samples_received", "done", "jdoe", -40, -40), Step("metadata_organized", "done", "jdoe", -38, -30),
            Step("plate_layout", "done", "jdoe", -29, -28), Step("sample_prep", "done", "asmith", -27, -20, -28, -18),
        ], Experiment("ClearwaterZoo-Cole", "CWZ-Otter-EV", "2026-10-Otter-DIA", "Orbitrap Astral",
        [
            Step("data_acquisition", "in_progress", "kchen", -6, null, -7, 2), Step("data_deposited", "pending", "kchen", null, null, 3, 4),
            Step("signal_processing", "pending", "maccoss", null, null, 5, 12), Step("data_analysis", "pending", "maccoss", null, null, 13, 30),
            Step("results_returned", "pending", "maccoss", null, null, 31, 33),
        ]));
        var biotrack = Project("UW-MacCoss", "MNRF-BioTRACK", "Plasma proteomics of a <neurology> cohort & controls", "maccoss",
        [
            Step("samples_received", "done", "maccoss", -90, -90), Step("metadata_organized", "done", "jdoe", -85, -60),
            Step("other", "done", "jdoe", -55, -54, label: "Second shipment", id: "second_shipment"),
            Step("plate_layout", "done", "jdoe", -50, -49), Step("sample_prep", "done", "asmith", -45, -30),
        ], Experiment("UW-MacCoss", "MNRF-BioTRACK", "2026-09-BioTRACK-DIA", "Orbitrap Astral",
        [
            Step("data_acquisition", "done", "kchen", -28, -12), Step("data_deposited", "done", "kchen", -11, -11),
            Step("signal_processing", "in_progress", "maccoss", -10, null, -10, -2), Step("data_analysis", "pending", "maccoss", null, null, 0, 21),
            Step("results_returned", "pending", "maccoss"),
        ]), Experiment("UW-MacCoss", "MNRF-BioTRACK", "2026-12-BioTRACK-PRM", "Stellar",
        [
            Step("metadata_organized", "pending", "jdoe", null, null, 20, 25, label: "Unblinded metadata"),
            Step("assay_development", "pending", "kchen", null, null, 26, 40), Step("data_acquisition", "pending", "kchen", null, null, 41, 48),
            Step("data_deposited"), Step("signal_processing"), Step("data_analysis"), Step("results_returned"),
        ]));
        var heron = Project("Riverbend-Ortiz", "RVB-Heron-Liver", "Heron <liver> & kidney tissue, two batches", "asmith",
        [
            Step("samples_received", "done", "asmith", -12, -12), Step("metadata_organized", "in_progress", "asmith", -10, null, -11, -3),
            Step("plate_layout", "pending", "jdoe", null, null, 4, 5), Step("sample_prep", "pending", "asmith", null, null, 6, 14),
        ], Experiment("Riverbend-Ortiz", "RVB-Heron-Liver", "2026-10-Heron-DIA", "Orbitrap Astral",
        [
            Step("data_acquisition", "pending", "kchen", null, null, 1, 5), Step("data_deposited"), Step("signal_processing"),
            Step("data_analysis"), Step("results_returned"),
        ]));
        var lake = Project("Lakeside-Park", "LKS-Trout-CSF", "Trout CSF pilot", "kchen",
        [
            Step("samples_received", "pending", "kchen", null, null, 3, 3), Step("metadata_organized"), Step("plate_layout"), Step("sample_prep"),
        ]);
        var internalWork = Project("UW-MacCoss", "MAC-QC-Standards", "Instrument QC standards", "maccoss",
        [
            Step("samples_received", "done", "maccoss", -200, -200), Step("sample_prep", "done", "asmith", -190, -180),
        ], Experiment("UW-MacCoss", "MAC-QC-Standards", "2026-10-QC-Stellar", "Stellar",
        [
            Step("data_acquisition", "in_progress", "kchen", -3, null, -3, 10), Step("data_deposited"),
        ]), Experiment("UW-MacCoss", "MAC-QC-Standards", "2026-10-QC-Exploris", "Q-Orbitrap",
        [
            Step("data_acquisition", "pending", null, null, null, 12, 14),
        ]));

        return new ProjectList(
            [
                new LabSummary { Lab = "ClearwaterZoo-Cole", Projects = [otter] },
                new LabSummary { Lab = "Lakeside-Park", Projects = [lake] },
                new LabSummary { Lab = "Riverbend-Ortiz", Projects = [heron] },
                new LabSummary { Lab = "UW-MacCoss", Projects = [biotrack, internalWork] },
            ],
            [new Person("maccoss", "Michael MacCoss", "PI"), new Person("jdoe", "Jordan Doe", "graduate student"),
             new Person("asmith", "Alex Smith", "research scientist"), new Person("kchen", "Kai Chen", "staff scientist")],
            [new ProjectIssue("ERROR", "projects/Lakeside-Park/LKS-Trout-CSF/metadata/samples.csv [Owner]: looks like a name")],
            ClosedHidden: 0,
            Instruments: ["Orbitrap Astral", "Stellar", "Q-Orbitrap"]);
    }
}
