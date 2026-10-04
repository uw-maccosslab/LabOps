namespace ChargeState.Core.Projects;

/// <summary>A validation message from project.py (level ERROR or WARN).</summary>
public sealed record ProjectIssue(string Level, string Message)
{
    public bool IsError => string.Equals(Level, "ERROR", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Level}: {Message}";
}

/// <summary>The stages of an experiment, in order, as project.py names them.</summary>
public static class StageNames
{
    public static IReadOnlyList<string> All { get; } =
    [
        "samples_received", "metadata_organized", "plate_layout", "sample_prep", "data_acquisition",
        "data_deposited", "signal_processing", "data_analysis", "results_returned",
    ];

    /// <summary>"plate_layout" as a person reads it: "Plate layout".</summary>
    public static string Label(string stage) => stage switch
    {
        "data_deposited" => "Data deposited to Panorama",
        "results_returned" => "Results returned",
        _ => char.ToUpperInvariant(stage[0]) + stage[1..].Replace('_', ' '),
    };
}

/// <summary>One stage of an experiment's timeline.</summary>
public sealed record StageEntry
{
    public string Stage { get; init; } = "";

    /// <summary>pending, in_progress, done or skipped.</summary>
    public string Status { get; init; } = "pending";

    public string? Started { get; init; }

    public string? Finished { get; init; }

    /// <summary>GitHub login of who did it.</summary>
    public string? By { get; init; }

    public string? Note { get; init; }

    public bool IsPending => Status == "pending";

    public bool IsInProgress => Status == "in_progress";

    public bool IsDone => Status == "done";

    public bool IsSkipped => Status == "skipped";
}

/// <summary>How an experiment is paid for. Quote numbers only; prices stay in the quotes repository.</summary>
public sealed record Funding
{
    /// <summary>quote, grant or internal.</summary>
    public string? Type { get; init; }

    public IReadOnlyList<string> Quotes { get; init; } = [];

    public string? Grant { get; init; }
}

/// <summary>An ELN notebook (the LabKey ELN has no API, so it is an ID and a link).</summary>
public sealed record Notebook(string? Id, string? Url);

/// <summary>A Panorama folder holding raw data or shared results.</summary>
public sealed record PanoramaFolder(string? Folder, string? Kind);

/// <summary>Where signal processing and analysis live.</summary>
public sealed record AnalysisLocation(string? Repo, string? Folder);

/// <summary>The plate layout imported from Octopus.</summary>
public sealed record LayoutInfo(int? Plates, int? Samples, string? Imported, string? OctopusVersion);

/// <summary>Which of an experiment's working files exist.</summary>
public sealed record ExperimentFiles(bool Samples, bool Layout);

/// <summary>One experiment as <c>project.py list --json</c> reports it.</summary>
public sealed record ExperimentSummary
{
    /// <summary>The folder name, YYYY-MM-Topic, which is the experiment's ID.</summary>
    public string Experiment { get; init; } = "";

    /// <summary>Folder relative to the repository root, with forward slashes.</summary>
    public string Folder { get; init; } = "";

    public string Group { get; init; } = "";

    public string? Title { get; init; }

    /// <summary>active, on_hold or closed.</summary>
    public string? Status { get; init; }

    public string? Series { get; init; }

    public string? LabContact { get; init; }

    public Funding Funding { get; init; } = new();

    public bool Human { get; init; }

    public string? Species { get; init; }

    public string? SampleType { get; init; }

    public int? ExpectedSamples { get; init; }

    public string? Instrument { get; init; }

    public IReadOnlyList<Notebook> Notebooks { get; init; } = [];

    public IReadOnlyList<PanoramaFolder> Panorama { get; init; } = [];

    public AnalysisLocation Analysis { get; init; } = new(null, null);

    public LayoutInfo? Layout { get; init; }

    /// <summary>The first stage neither done nor skipped; null when every stage is.</summary>
    public string? CurrentStage { get; init; }

    public IReadOnlyList<StageEntry> Stages { get; init; } = [];

    public ExperimentFiles Files { get; init; } = new(false, false);

    public IReadOnlyList<ProjectIssue> Issues { get; init; } = [];

    public bool IsClosed => Status == "closed";

    public bool HasErrors => Issues.Any(i => i.IsError);

    /// <summary>"Plate layout", or "Complete".</summary>
    public string CurrentStageLabel => CurrentStage is null ? "Complete" : StageNames.Label(CurrentStage);

    /// <summary>Stages done or skipped, out of all of them.</summary>
    public int Progress => Stages.Count(s => s.IsDone || s.IsSkipped);

    /// <summary>"quote MacCoss-2026-X", "grant R01 ...", "internal".</summary>
    public string FundingText => Funding.Type switch
    {
        "quote" when Funding.Quotes.Count > 0 => "quote " + string.Join(", ", Funding.Quotes),
        "grant" when !string.IsNullOrWhiteSpace(Funding.Grant) => "grant " + Funding.Grant,
        null => "",
        var t => t,
    };

    /// <summary>The folder on disk.</summary>
    public string FolderPath(string repositoryPath) =>
        Path.Combine(repositoryPath, Folder.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>One collaboration with its experiments.</summary>
public sealed record ProjectSummary
{
    public string Group { get; init; } = "";

    public string Folder { get; init; } = "";

    public string? Title { get; init; }

    public string? Pi { get; init; }

    public string? Institution { get; init; }

    public string? Status { get; init; }

    public string? LabContact { get; init; }

    public string? AnalysisRepo { get; init; }

    public IReadOnlyList<Notebook> Notebooks { get; init; } = [];

    public IReadOnlyList<ProjectIssue> Issues { get; init; } = [];

    public IReadOnlyList<ExperimentSummary> Experiments { get; init; } = [];
}

/// <summary>Everything <c>project.py list</c> returns.</summary>
public sealed record ProjectList(IReadOnlyList<ProjectSummary> Projects, IReadOnlyList<ProjectIssue> Problems);

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

/// <summary>The Octopus input file written for an experiment.</summary>
public sealed record OctopusInput(string File, int Samples, IReadOnlyList<string> Warnings);

/// <summary>What a stage button does.</summary>
public enum StageAction
{
    Start,
    Done,
    Skip,
}
