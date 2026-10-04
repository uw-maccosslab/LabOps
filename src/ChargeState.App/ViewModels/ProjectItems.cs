using System.Collections.ObjectModel;
using System.Globalization;
using ChargeState.Core.Projects;

namespace ChargeState.App.ViewModels;

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
/// measurement and analysis steps, with what is linked to it.
/// </summary>
public sealed class TimelineSection
{
    public TimelineSection(
        ITimeline timeline, string folder, string heading, string detail, IEnumerable<LinkItem> links, IEnumerable<string> issues,
        Func<string, string>? nameOf = null)
    {
        NameOf = nameOf ?? (login => login);
        Item = timeline.Name;
        Folder = folder;
        Heading = heading;
        Detail = detail;
        Links = [.. links];
        Issues = [.. issues];
        Stages = [.. timeline.Stages.Select(s => new StageRowViewModel(this, s, s.Stage == timeline.CurrentStage))];
    }

    /// <summary>The project or experiment, by name, which the engine's commands take.</summary>
    public string Item { get; }

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

    public IReadOnlyList<LinkItem> Links { get; }

    public bool HasLinks => Links.Count > 0;

    public IReadOnlyList<string> Issues { get; }
}

/// <summary>One step of a timeline, with the buttons that apply to it.</summary>
public sealed class StageRowViewModel(TimelineSection section, StageEntry entry, bool isCurrent)
{
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
}

/// <summary>A link shown for a project or experiment; with no URL it is shown as text only.</summary>
public sealed record LinkItem(string Label, string? Url)
{
    public bool HasUrl => Url is not null;

    public bool IsText => Url is null;
}
