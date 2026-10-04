using System.Globalization;
using ChargeState.Core.Projects;

namespace ChargeState.App.ViewModels;

/// <summary>One row of the experiment list: the experiment, its collaboration, and when it last changed.</summary>
public sealed record ExperimentRow(ProjectSummary Project, ExperimentSummary Experiment, DateTimeOffset? Modified)
{
    public string Name => Experiment.Experiment;

    public string Title => Experiment.Title ?? "";

    /// <summary>The PI when known, else the group.</summary>
    public string Collaborator => !string.IsNullOrWhiteSpace(Project.Pi) ? Project.Pi! : Project.Group;

    public string Stage => Experiment.CurrentStageLabel;

    /// <summary>For sorting by stage: the stage's position in the timeline, complete last.</summary>
    public int StageIndex => Experiment.CurrentStage is { } s ? StageNames.All.ToList().IndexOf(s) : StageNames.All.Count;

    public string ModifiedText => Modified?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public bool IsClosed => Experiment.IsClosed;

    /// <summary>Every word of the query appears somewhere in the row's text.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var haystack = string.Join(' ', Name, Title, Project.Group, Project.Pi, Project.Institution, Project.Title,
            Experiment.Species, Experiment.SampleType, Experiment.Series, Experiment.FundingText, Experiment.LabContact, Stage);
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One stage of the selected experiment's timeline, with the buttons that apply to it.</summary>
public sealed class StageRowViewModel(StageEntry entry, bool isCurrent)
{
    public StageEntry Entry { get; } = entry;

    public string Stage => Entry.Stage;

    public string Label => StageNames.Label(Entry.Stage);

    public string Status => Entry.Status;

    /// <summary>The first stage not yet done or skipped.</summary>
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
}

/// <summary>A link shown for an experiment; with no URL it is shown as text only.</summary>
public sealed record LinkItem(string Label, string? Url)
{
    public bool HasUrl => Url is not null;

    public bool IsText => Url is null;
}
