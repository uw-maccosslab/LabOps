namespace LabOps.Core.Projects;

/// <summary>A validation message from project.py (level ERROR or WARN).</summary>
public sealed record ProjectIssue(string Level, string Message)
{
    public bool IsError => string.Equals(Level, "ERROR", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Level}: {Message}";
}

/// <summary>The kinds of step project.py knows, and how they read.</summary>
public static class StageNames
{
    /// <summary>The kinds a person can add, in the order a timeline usually runs.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "samples_received", "metadata_organized", "plate_layout", "sample_prep", "assay_development",
        "data_acquisition", "data_deposited", "signal_processing", "data_analysis", "results_returned", "other",
    ];

    /// <summary>"plate_layout" as a person reads it: "Plate layout".</summary>
    public static string Label(string kind) => kind switch
    {
        "data_deposited" => "Data deposited to Panorama",
        "results_returned" => "Results returned",
        "" => "",
        _ => char.ToUpperInvariant(kind[0]) + kind[1..].Replace('_', ' '),
    };
}

/// <summary>One step of a project's or an experiment's timeline.</summary>
public sealed record StageEntry
{
    /// <summary>The step's id, which commands use.</summary>
    public string Stage { get; init; } = "";

    /// <summary>What the step is: one of <see cref="StageNames.Kinds"/>.</summary>
    public string? Kind { get; init; }

    /// <summary>How it reads (project.py fills it in from the kind when there is no label).</summary>
    public string? Label { get; init; }

    /// <summary>pending, in_progress, done or skipped.</summary>
    public string Status { get; init; } = "pending";

    /// <summary>GitHub login of who does it.</summary>
    public string? Assigned { get; init; }

    public string? Started { get; init; }

    public string? Finished { get; init; }

    /// <summary>GitHub login of who recorded it.</summary>
    public string? By { get; init; }

    public string? Note { get; init; }

    public string DisplayLabel => Label ?? StageNames.Label(Kind ?? Stage);

    public bool IsPending => Status == "pending";

    public bool IsInProgress => Status == "in_progress";

    public bool IsDone => Status == "done";

    public bool IsSkipped => Status == "skipped";
}

/// <summary>How work is paid for. Quote numbers only; prices stay in the quotes repository.</summary>
public sealed record Funding
{
    /// <summary>quote, grant or internal.</summary>
    public string? Type { get; init; }

    public IReadOnlyList<string> Quotes { get; init; } = [];

    public string? Grant { get; init; }

    /// <summary>"quote MacCoss-2026-X", "grant R01 ...", "internal".</summary>
    public string Text => Type switch
    {
        "quote" when Quotes.Count > 0 => "quote " + string.Join(", ", Quotes),
        "grant" when !string.IsNullOrWhiteSpace(Grant) => "grant " + Grant,
        null => "",
        var t => t,
    };
}

/// <summary>An ELN notebook (the LabKey ELN has no API, so it is an ID and a link).</summary>
public sealed record Notebook(string? Id, string? Url);

/// <summary>A Panorama folder holding raw data or shared results.</summary>
public sealed record PanoramaFolder(string? Folder, string? Kind);

/// <summary>Where signal processing and analysis live.</summary>
public sealed record AnalysisLocation(string? Repo, string? Folder);

/// <summary>A protocol from LabOps-Protocols that a step followed, at the version used.</summary>
/// <param name="Id">The protocol's ID.</param>
/// <param name="Version">The published version followed; null when it was not recorded.</param>
/// <param name="Title">Its title, as recorded when it was linked.</param>
/// <param name="Step">The step's id; null when it belongs to the item as a whole.</param>
public sealed record ProtocolLink(string? Id, int? Version, string? Title, string? Step)
{
    /// <summary>"S-Trap micro digestion, version 3".</summary>
    public string Text => $"{(string.IsNullOrWhiteSpace(Title) ? Id : Title)}{(Version is { } v ? $", version {v}" : "")}";
}

/// <summary>The plate layout imported from Octopus.</summary>
public sealed record LayoutInfo(int? Plates, int? Samples, string? Imported, string? OctopusVersion);

/// <summary>Which of a project's files exist.</summary>
/// <param name="Wiki">wiki.yaml: the written parts of its wiki page.</param>
public sealed record ProjectFiles(bool Samples, bool Layout, bool Wiki = false);

/// <summary>Where a project's wiki page is on Panorama.</summary>
/// <param name="Folder">The Panorama folder, for example /MacCoss/Collaborations/MNRF/BioTRACK.</param>
/// <param name="Page">The page's name; default is the folder's main page.</param>
public sealed record WikiLocation(string Folder, string? Page)
{
    public string PageName => string.IsNullOrWhiteSpace(Page) ? "default" : Page!;
}

/// <summary>A project's wiki page as <c>project.py wiki</c> builds it.</summary>
/// <param name="Folder">Where it goes, or null when the project has no wiki page recorded.</param>
/// <param name="Written">Whether wiki.yaml (the summary, plan and samples, by Claude) exists.</param>
/// <param name="WrittenHash">A fingerprint of the written parts, also in the page's footer.</param>
public sealed record WikiPageContent(string Project, string? Folder, string Page, string Title, string Html, bool Written, string WrittenHash);

/// <summary>What a project and an experiment share: a timeline of steps.</summary>
public interface ITimeline
{
    /// <summary>The folder name, which commands use.</summary>
    string Name { get; }

    /// <summary>The first step neither done nor skipped; null when every step is.</summary>
    string? CurrentStage { get; }

    IReadOnlyList<StageEntry> Stages { get; }
}

/// <summary>One experiment, a measurement and analysis of its project's samples, as project.py reports it.</summary>
public sealed record ExperimentSummary : ITimeline
{
    /// <summary>The folder name, YYYY-MM-Topic, which is the experiment's ID.</summary>
    public string Experiment { get; init; } = "";

    /// <summary>Folder relative to the repository root, with forward slashes.</summary>
    public string Folder { get; init; } = "";

    public string Project { get; init; } = "";

    public string Lab { get; init; } = "";

    public string? Title { get; init; }

    /// <summary>active, on_hold or closed.</summary>
    public string? Status { get; init; }

    public string? LabContact { get; init; }

    public string? Instrument { get; init; }

    /// <summary>The experiment's own funding, or its project's when <see cref="FundingInherited"/>.</summary>
    public Funding Funding { get; init; } = new();

    public bool FundingInherited { get; init; }

    public IReadOnlyList<Notebook> Notebooks { get; init; } = [];

    public IReadOnlyList<PanoramaFolder> Panorama { get; init; } = [];

    /// <summary>The protocols its steps followed (none from an engine older than 26.3.0).</summary>
    public IReadOnlyList<ProtocolLink> Protocols { get; init; } = [];

    public AnalysisLocation Analysis { get; init; } = new(null, null);

    public string? CurrentStage { get; init; }

    public IReadOnlyList<StageEntry> Stages { get; init; } = [];

    public IReadOnlyList<ProjectIssue> Issues { get; init; } = [];

    public string Name => Experiment;

    public bool IsClosed => Status == "closed";

    public StageEntry? Current => Stages.FirstOrDefault(s => s.Stage == CurrentStage);
}

/// <summary>One project, a body of work with one set of samples, with its experiments.</summary>
public sealed record ProjectSummary : ITimeline
{
    public string Project { get; init; } = "";

    public string Folder { get; init; } = "";

    public string Lab { get; init; } = "";

    public string? Title { get; init; }

    public string? Status { get; init; }

    public string? Series { get; init; }

    public string? LabContact { get; init; }

    public Funding Funding { get; init; } = new();

    public bool Human { get; init; }

    public string? Species { get; init; }

    public string? SampleType { get; init; }

    public int? ExpectedSamples { get; init; }

    public IReadOnlyList<Notebook> Notebooks { get; init; } = [];

    /// <summary>The protocols its sample steps followed, such as the sample prep's.</summary>
    public IReadOnlyList<ProtocolLink> Protocols { get; init; } = [];

    public AnalysisLocation Analysis { get; init; } = new(null, null);

    public LayoutInfo? Layout { get; init; }

    /// <summary>Where the project's wiki page is on Panorama, when one is recorded.</summary>
    public WikiLocation? Wiki { get; init; }

    /// <summary>The samples' current step.</summary>
    public string? CurrentStage { get; init; }

    /// <summary>The samples' steps.</summary>
    public IReadOnlyList<StageEntry> Stages { get; init; } = [];

    public ProjectFiles Files { get; init; } = new(false, false);

    public IReadOnlyList<ProjectIssue> Issues { get; init; } = [];

    public IReadOnlyList<ExperimentSummary> Experiments { get; init; } = [];

    public string Name => Project;

    public bool IsClosed => Status == "closed";

    public bool HasErrors => Issues.Any(i => i.IsError) || Experiments.Any(e => e.Issues.Any(i => i.IsError));

    /// <summary>
    /// Where the project stands, in a few words: the samples' current step while the samples are
    /// still in hand, then the first open experiment's ("DIA: Data acquisition"), else Complete.
    /// </summary>
    public string Progress => ProgressStep is var (where, step) && step is not null ? $"{where}: {step.DisplayLabel}" : "Complete";

    /// <summary>GitHub login of who has the step <see cref="Progress"/> names.</summary>
    public string? Assigned => ProgressStep.Step?.Assigned;

    /// <summary>The step <see cref="Progress"/> names, and whose it is ("Samples" or the experiment's short name).</summary>
    private (string Where, StageEntry? Step) ProgressStep
    {
        get
        {
            if (Stages.FirstOrDefault(s => s.Stage == CurrentStage) is { } sample)
            {
                return ("Samples", sample);
            }

            var open = Experiments.FirstOrDefault(e => !e.IsClosed && e.Current is not null);
            return open is null ? ("", null) : (ShortName(open), open.Current);
        }
    }

    /// <summary>For sorting by progress: samples first, then experiments in order, complete last.</summary>
    public int ProgressRank
    {
        get
        {
            var sample = Stages.ToList().FindIndex(s => s.Stage == CurrentStage);
            if (sample >= 0)
            {
                return sample;
            }

            var open = Experiments.Select((e, i) => (e, i)).FirstOrDefault(x => !x.e.IsClosed && x.e.Current is not null);
            return open.e is null ? 1000 : 100 + (open.i * 100) + open.e.Stages.ToList().IndexOf(open.e.Current!);
        }
    }

    /// <summary>"2026-09-BioTRACK-DIA" read as "DIA": the part after the project's own words.</summary>
    public static string ShortName(ExperimentSummary e)
    {
        var parts = e.Experiment.Split('-');
        return parts.Length > 2 ? parts[^1] : e.Experiment;
    }

    /// <summary>The folder on disk.</summary>
    public string FolderPath(string repositoryPath) =>
        Path.Combine(repositoryPath, Folder.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>A lab we work with, with its projects.</summary>
public sealed record LabSummary
{
    public string Lab { get; init; } = "";

    public string Folder { get; init; } = "";

    public string? Title { get; init; }

    public string? Pi { get; init; }

    public string? Institution { get; init; }

    public string? Status { get; init; }

    public string? LabContact { get; init; }

    public string? AnalysisRepo { get; init; }

    public IReadOnlyList<Notebook> Notebooks { get; init; } = [];

    public IReadOnlyList<ProjectIssue> Issues { get; init; } = [];

    public IReadOnlyList<ProjectSummary> Projects { get; init; } = [];
}

/// <summary>Someone in config/people.yaml, who can be assigned steps.</summary>
public sealed record Person(string Login, string? Name, string? Role)
{
    /// <summary>The name, or the login when there is none.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name!;
}

/// <summary>Everything <c>project.py list</c> returns.</summary>
/// <param name="ClosedHidden">How many closed projects <c>list --active</c> left out; 0 when it listed them all.</param>
public sealed record ProjectList(
    IReadOnlyList<LabSummary> Labs, IReadOnlyList<Person> People, IReadOnlyList<ProjectIssue> Problems, int ClosedHidden = 0);

/// <summary>One sheet the scan read: its name, row count and column headers (never its values).</summary>
public sealed record ScanSheet(string Sheet, int Rows, IReadOnlyList<string> Columns);

/// <summary>Something the scan found that may identify a person.</summary>
public sealed record ScanFinding(string Level, string File, string? Column, string Message, int? Count)
{
    public bool IsError => string.Equals(Level, "ERROR", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{Level}: {(Column is null ? "" : Column + ": ")}{Message}{(Count is { } n ? $" ({n} values)" : "")}";
}

/// <summary>What <c>project.py scan</c> reports about a collaborator's file.</summary>
public sealed record ScanResult(IReadOnlyList<ScanSheet> Sheets, IReadOnlyList<ScanFinding> Findings, int Errors)
{
    public IEnumerable<ScanFinding> Warnings => Findings.Where(f => !f.IsError);
}

/// <summary>The Octopus input file written for a project.</summary>
public sealed record OctopusInput(string File, int Samples, IReadOnlyList<string> Warnings);

/// <summary>What a step button does.</summary>
public enum StageAction
{
    Start,
    Done,
    Skip,
}
