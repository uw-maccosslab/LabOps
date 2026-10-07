using LabOps.Core.Projects;

namespace LabOps.Core.Protocols;

/// <summary>One published version of a protocol: frozen, with when, who and what changed.</summary>
/// <param name="Date">YYYY-MM-DD; an imported version may have only a month or year, or none.</param>
/// <param name="Imported">History brought in from an older record rather than published in LabOps.</param>
public sealed record ProtocolVersion(int Version, string? Date, string? By, string? Summary, bool Imported);

/// <summary>What a protocol is for.</summary>
public sealed record ProtocolAppliesTo
{
    public IReadOnlyList<string> SampleTypes { get; init; } = [];

    public IReadOnlyList<string> Instruments { get; init; } = [];
}

/// <summary>A category protocols are sorted into (config/categories.yaml).</summary>
public sealed record ProtocolCategory(string Id, string Label)
{
    // What screen readers and UI Automation read for it in a list.
    public override string ToString() => Label;
}

/// <summary>One protocol, as <c>protocol.py list</c> reports it.</summary>
public sealed record ProtocolSummary
{
    /// <summary>The folder name, which is the protocol's ID and never changes.</summary>
    public string Id { get; init; } = "";

    /// <summary>Folder relative to the repository root, with forward slashes.</summary>
    public string Folder { get; init; } = "";

    public string? Title { get; init; }

    /// <summary>What people call it at the bench, for example SAX KFKF.</summary>
    public string? ShortTitle { get; init; }

    public string? Category { get; init; }

    public string? CategoryLabel { get; init; }

    /// <summary>draft (never published), active or retired.</summary>
    public string? Status { get; init; }

    /// <summary>GitHub login of who keeps it current.</summary>
    public string? Owner { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public ProtocolAppliesTo AppliesTo { get; init; } = new();

    /// <summary>The protocol to use instead, when retired.</summary>
    public string? ReplacedBy { get; init; }

    /// <summary>Every published version, oldest first.</summary>
    public IReadOnlyList<ProtocolVersion> Versions { get; init; } = [];

    public int? LatestVersion { get; init; }

    public string? LatestDate { get; init; }

    /// <summary>The working text differs from the last published version (or nothing is published yet).</summary>
    public bool DraftChanges { get; init; }

    public IReadOnlyList<string> Figures { get; init; } = [];

    /// <summary>The originals it was written from, in sources/.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    public IReadOnlyList<ProjectIssue> Issues { get; init; } = [];

    public bool IsDraft => Status == "draft";

    public bool IsRetired => Status == "retired";

    public bool IsPublished => LatestVersion is not null;

    public bool HasErrors => Issues.Any(i => i.IsError);

    /// <summary>"v3", "v3 + draft" (unpublished changes since v3) or "draft".</summary>
    public string VersionText => LatestVersion is { } v ? (DraftChanges ? $"v{v} + draft" : $"v{v}") : "draft";

    /// <summary>The title, or the ID when there is none.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Id : Title!;

    /// <summary>The folder on disk.</summary>
    public string FolderPath(string repositoryPath) =>
        Path.Combine(repositoryPath, Folder.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Everything <c>protocol.py list</c> returns.</summary>
public sealed record ProtocolList(
    IReadOnlyList<ProtocolSummary> Protocols, IReadOnlyList<ProtocolCategory> Categories, IReadOnlyList<Person> People,
    IReadOnlyList<ProjectIssue> Problems);

/// <summary>What <c>protocol.py import</c> extracted from an uploaded file, for Claude to format.</summary>
/// <param name="Folder">inbox/&lt;id&gt;, relative to the repository root.</param>
/// <param name="Text">The extracted text, relative to the repository root.</param>
/// <param name="Scanned">A PDF whose pages are pictures: Claude reads the PDF itself.</param>
/// <param name="Exists">A protocol with this ID exists already, so this is a new version of it.</param>
public sealed record ProtocolImport(
    string Id, string Folder, string Original, string Text, int Characters, IReadOnlyList<string> Figures, int? Pages,
    bool Scanned, bool Exists)
{
    /// <summary>Each file uploaded, in order (protocol engine 26.4.0 and later; empty before).</summary>
    public IReadOnlyList<ImportedFile> Files { get; init; } = [];
}

/// <summary>One uploaded file: a document whose text was read, or another file kept as it is (a method file, say).</summary>
/// <param name="Original">Its copy in inbox/&lt;id&gt;, relative to the repository root.</param>
/// <param name="Kind">"document" or "other".</param>
/// <param name="Scanned">A PDF whose pages are pictures: Claude reads it itself.</param>
public sealed record ImportedFile(
    string Original, string Kind, int? Characters, IReadOnlyList<string>? Figures, int? Pages, bool Scanned)
{
    public bool IsDocument => Kind == "document";
}

/// <summary>What changed between two versions, or between a version and the draft.</summary>
public sealed record ProtocolDiff(string From, string To, bool Same, string Diff);
