using System.Collections.ObjectModel;
using System.Globalization;
using LabOps.Core.Projects;
using CommunityToolkit.Mvvm.Input;

namespace LabOps.App.ViewModels;

/// <summary>One row of the project list: the project, its lab, and when anything in it last changed.</summary>
/// <param name="Assigned">Who has the step <see cref="Progress"/> names, as a name; empty when nobody.</param>
public sealed record ProjectRow(LabSummary Lab, ProjectSummary Project, DateTimeOffset? Modified, string Assigned = "")
{
    public string Name => Project.Project;

    public string Title => Project.Title ?? "";

    /// <summary>The PI when known, else the lab's folder name.</summary>
    public string Collaborator => !string.IsNullOrWhiteSpace(Lab.Pi) ? Lab.Pi! : Lab.Lab;

    /// <summary>The lab as institution and PI: "UW - MacCoss" for the folder UW-MacCoss.</summary>
    public string LabName => LabDisplayName(Lab.Lab, Lab.Pi);

    /// <summary>
    /// "Institution - LastName" from a lab folder named Institution-LastName. The PI's last name
    /// (from lab.yaml) says where the institution ends, so "UCSF-Garcia-Lopez" with PI "Ana
    /// Garcia-Lopez, Ph.D." reads "UCSF - Garcia-Lopez"; without it, the last hyphen does.
    /// </summary>
    public static string LabDisplayName(string folder, string? pi)
    {
        var lastName = pi?.Split(',')[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (lastName is not null && folder.Length > lastName.Length + 1
            && folder.EndsWith("-" + lastName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{folder[..^(lastName.Length + 1)]} - {folder[^lastName.Length..]}";
        }

        var hyphen = folder.LastIndexOf('-');
        return hyphen > 0 && hyphen < folder.Length - 1 ? $"{folder[..hyphen]} - {folder[(hyphen + 1)..]}" : folder;
    }

    /// <summary>"Samples: Metadata organized", "DIA: Data acquisition", or "Complete".</summary>
    public string Progress => Project.Progress;

    public int ProgressRank => Project.ProgressRank;

    /// <summary>The experiments by short name: "DIA, PRM".</summary>
    public string ExperimentNames => string.Join(", ", Project.Experiments.Select(ProjectSummary.ShortName));

    public string ModifiedText => Modified?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public bool IsClosed => Project.IsClosed;

    /// <summary>Every word of the query appears somewhere in the row's text.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var p = Project;
        var haystack = string.Join(' ', new[]
        {
            Name, Title, Lab.Lab, LabName, Lab.Pi, Lab.Institution, Lab.Title, p.Species, p.SampleType, p.Series,
            p.Funding.Text, p.LabContact, Progress, Assigned,
        }.Concat(p.Experiments.SelectMany(e => new[] { e.Experiment, e.Title, e.Instrument, e.Funding.Text })));
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// One timeline on the project page: the samples' steps (on the project) or one experiment's
/// measurement and analysis steps, with what is linked to it. Tools and links go on the step they
/// belong to (see <see cref="StepHomes"/>); the section keeps those with no such step.
/// </summary>
public sealed class TimelineSection
{
    private readonly List<LinkItem> _links;
    private readonly List<StepTool> _tools = [];

    public TimelineSection(
        ITimeline timeline, string folder, string heading, string detail, IEnumerable<LinkItem> links, IEnumerable<string> issues,
        Func<string, string>? nameOf = null)
    {
        NameOf = nameOf ?? (login => login);
        Item = timeline.Name;
        Folder = folder;
        Heading = heading;
        Detail = detail;
        _links = [.. links];
        Issues = [.. issues];
        Stages = [.. timeline.Stages.Select(s => new StageRowViewModel(this, s, s.Stage == timeline.CurrentStage))];
    }

    /// <summary>The project or experiment, by name, which the engine's commands take.</summary>
    public string Item { get; }

    /// <summary>An experiment's timeline (rather than the samples'), which can have Panorama folders.</summary>
    public bool IsExperiment { get; init; }

    /// <summary>A person's name from their GitHub login.</summary>
    public Func<string, string> NameOf { get; }

    /// <summary>The folder to save after a change, relative to the repository root.</summary>
    public string Folder { get; }

    /// <summary>"Samples", or "DIA: 2026-09-BioTRACK-DIA".</summary>
    public string Heading { get; }

    /// <summary>The experiment's title, instrument and funding; empty for the samples.</summary>
    public string Detail { get; }

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public ObservableCollection<StageRowViewModel> Stages { get; }

    /// <summary>Links with no step to go on.</summary>
    public IReadOnlyList<LinkItem> Links => _links;

    public bool HasLinks => Links.Count > 0;

    /// <summary>Tools with no step to go on, shown beside the section's heading.</summary>
    public IReadOnlyList<StepTool> Tools => _tools;

    public IReadOnlyList<string> Issues { get; }

    /// <summary>The first step of the first kind in <paramref name="kinds"/> this timeline has.</summary>
    public StageRowViewModel? Home(IReadOnlyList<string> kinds) =>
        kinds.Select(k => Stages.FirstOrDefault(s => (s.Entry.Kind ?? s.Stage) == k)).FirstOrDefault(s => s is not null);

    /// <summary>Shows a link on its step, or on the section when the timeline has no such step.</summary>
    public void Place(LinkItem link, IReadOnlyList<string> kinds)
    {
        if (Home(kinds) is { } step)
        {
            step.AddLink(link);
        }
        else
        {
            _links.Add(link);
        }
    }

    /// <summary>Shows a link on the step with this id, or on the section when there is none (or no step is named).</summary>
    public void PlaceOnStep(LinkItem link, string? stepId)
    {
        if (stepId is not null && Stages.FirstOrDefault(s => s.Stage == stepId) is { } step)
        {
            step.AddLink(link);
        }
        else
        {
            _links.Add(link);
        }
    }

    /// <summary>Offers a tool on its step, or on the section when the timeline has no such step.</summary>
    public void Place(StepTool tool, IReadOnlyList<string> kinds)
    {
        if (Home(kinds) is { } step)
        {
            step.AddTool(tool);
        }
        else
        {
            _tools.Add(tool);
        }
    }
}

/// <summary>
/// Which step each tool and link belongs on: the first of these kinds a timeline has. They work
/// whatever the step's status, since a Panorama folder or a notebook is often set up before the
/// work starts.
/// </summary>
public static class StepHomes
{
    /// <summary>Organize with Claude, View samples.</summary>
    public static IReadOnlyList<string> Metadata { get; } = ["metadata_organized"];

    /// <summary>Open in Octopus, Import layout.</summary>
    public static IReadOnlyList<string> Layout { get; } = ["plate_layout"];

    /// <summary>The ELN notebook: where the bench work is written up.</summary>
    public static IReadOnlyList<string> Notebook { get; } = ["sample_prep", "assay_development", "data_acquisition"];

    /// <summary>The Panorama folder the raw files go to.</summary>
    public static IReadOnlyList<string> RawData { get; } = ["data_deposited", "data_acquisition"];

    /// <summary>The Panorama folder with the process control (system suitability) runs, watched during acquisition.</summary>
    public static IReadOnlyList<string> ProcessControl { get; } = ["data_acquisition"];

    /// <summary>The Panorama folder with the Skyline documents.</summary>
    public static IReadOnlyList<string> Results { get; } = ["signal_processing", "data_analysis", "results_returned"];

    /// <summary>The analysis repository folder.</summary>
    public static IReadOnlyList<string> Analysis { get; } = ["data_analysis"];

    /// <summary>The protocol the bench work followed, at the version used: sample prep first.</summary>
    public static IReadOnlyList<string> Protocol { get; } = ["sample_prep", "assay_development", "data_acquisition"];
}

/// <summary>A button that does the work of a step, such as Open in Octopus on Plate layout.</summary>
/// <param name="Parameter">What the command acts on, when it needs more than the selected project.</param>
public sealed record StepTool(string Label, string ToolTip, IRelayCommand Command, object? Parameter = null);

/// <summary>What Add raw data folder, Add results folder or Add notebook records, and on what.</summary>
/// <param name="Kind">raw, results, qc or notebook.</param>
public sealed record LinkRequest(TimelineSection Section, string Kind);

/// <summary>What Add protocol records a protocol for: a timeline, and the step that followed it.</summary>
/// <param name="Step">The step's id; null for the timeline as a whole.</param>
public sealed record ProtocolRequest(TimelineSection Section, string? Step);

/// <summary>One step of a timeline, with the buttons that apply to it.</summary>
public sealed class StageRowViewModel(TimelineSection section, StageEntry entry, bool isCurrent)
{
    private readonly List<LinkItem> _links = [];
    private readonly List<StepTool> _tools = [];

    /// <summary>The timeline it belongs to, which says what to update.</summary>
    public TimelineSection Section { get; } = section;

    public StageEntry Entry { get; } = entry;

    /// <summary>The step's id.</summary>
    public string Stage => Entry.Stage;

    public string Label => Entry.DisplayLabel;

    public string Status => Entry.Status;

    /// <summary>The first step not yet done or skipped.</summary>
    public bool IsCurrent { get; } = isCurrent;

    public string StatusText
    {
        get
        {
            var text = Entry.Status switch
            {
                "in_progress" => Entry.Started is null ? "In progress" : $"In progress since {Entry.Started}",
                "done" => Entry.Finished is null ? "Done" : $"Done {Entry.Finished}",
                "skipped" => "Skipped",
                _ => "Not started",
            };
            return Entry.By is null ? text : $"{text} ({Entry.By})";
        }
    }

    public string? Note => Entry.Note;

    public bool CanStart => Entry.IsPending;

    public bool CanFinish => Entry.IsPending || Entry.IsInProgress;

    public bool CanSkip => Entry.IsPending;

    public bool CanReopen => Entry.IsDone || Entry.IsSkipped;

    /// <summary>"Assigned to Michael MacCoss"; for a finished step, only when someone else recorded it.</summary>
    public string? AssignedText => Entry.Assigned is not { } who || ((Entry.IsDone || Entry.IsSkipped) && who == Entry.By)
        ? null
        : $"Assigned to {Section.NameOf(who)}";

    /// <summary>Work still to do can be given to someone.</summary>
    public bool CanAssign => Entry.IsPending || Entry.IsInProgress;

    /// <summary>Only a step with nothing recorded can go; anything else is skipped instead.</summary>
    public bool CanRemove => Entry.IsPending && Entry.Started is null && Entry.Finished is null && Entry.By is null
        && string.IsNullOrWhiteSpace(Entry.Note) && Section.Stages.Count > 1;

    /// <summary>What was recorded for this step: its notebook, its Panorama folder.</summary>
    public IReadOnlyList<LinkItem> Links => _links;

    public bool HasLinks => _links.Count > 0;

    /// <summary>The buttons that do this step's work.</summary>
    public IReadOnlyList<StepTool> Tools => _tools;

    public bool HasTools => _tools.Count > 0;

    internal void AddLink(LinkItem link) => _links.Add(link);

    internal void AddTool(StepTool tool) => _tools.Add(tool);
}

/// <summary>A link shown for a project or experiment; with no URL it is shown as text only.</summary>
/// <param name="Item">The project or experiment it is recorded on, when it can be removed there.</param>
/// <param name="What">panorama, notebook or protocol, for project.py unlink; null for links kept elsewhere.</param>
/// <param name="Value">What unlink takes: the folder, the notebook's ID or link, or the protocol's ID.</param>
/// <param name="Folder">The folder to save after removing it.</param>
/// <param name="Step">For a protocol: the step it is recorded for.</param>
public sealed record LinkItem(
    string Label, string? Url, string? Item = null, string? What = null, string? Value = null, string? Folder = null, string? Step = null)
{
    /// <summary>The address of a protocol version in the app's own Protocols area.</summary>
    public const string ProtocolScheme = "labops-protocol:";

    public bool HasUrl => Url is not null;

    public bool IsText => Url is null;

    /// <summary>Panorama folders, notebooks and protocols recorded on this project or experiment.</summary>
    public bool CanRemove => Item is not null && What is not null && Value is not null;

    /// <summary>A protocol at a version, opened in the Protocols area: labops-protocol:id/3.</summary>
    public static string ProtocolUrl(string id, int? version) => $"{ProtocolScheme}{id}/{version}";

    /// <summary>The protocol and version a <see cref="ProtocolUrl"/> names, or null for any other link.</summary>
    public static (string Id, int? Version)? ParseProtocolUrl(string? url)
    {
        if (url is null || !url.StartsWith(ProtocolScheme, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = url[ProtocolScheme.Length..].Split('/', 2);
        return (parts[0], parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : null);
    }
}
