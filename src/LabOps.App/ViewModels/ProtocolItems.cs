using LabOps.Core.Protocols;

namespace LabOps.App.ViewModels;

/// <summary>One row of the protocol list.</summary>
/// <param name="OwnerName">Who keeps it current, as a name.</param>
/// <param name="CategoryOrder">Where its category comes in config/categories.yaml, for the list's order.</param>
public sealed record ProtocolRow(ProtocolSummary Protocol, string OwnerName, int CategoryOrder = 0)
{
    public string Id => Protocol.Id;

    // What screen readers and UI Automation read for the row.
    public override string ToString() => $"{Title}, {Version}";

    public string Title => Protocol.DisplayTitle;

    public string ShortTitle => Protocol.ShortTitle ?? "";

    public string Category => Protocol.CategoryLabel ?? Protocol.Category ?? "";

    /// <summary>"v3", "v3 + draft" or "draft".</summary>
    public string Version => Protocol.VersionText;

    /// <summary>For sorting by version: the number, drafts first.</summary>
    public int VersionRank => Protocol.LatestVersion ?? 0;

    /// <summary>When the current version was published.</summary>
    public string Published => Protocol.LatestDate ?? "";

    public string Status => Protocol.Status ?? "";

    public bool IsRetired => Protocol.IsRetired;

    /// <summary>Every word of the query appears somewhere in the row's text.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var p = Protocol;
        var haystack = string.Join(' ', new[] { p.Id, p.Title, p.ShortTitle, Category, p.Owner, OwnerName, Status }
            .Concat(p.Tags).Concat(p.AppliesTo.SampleTypes).Concat(p.AppliesTo.Instruments));
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A category to show, or every category when <see cref="Id"/> is null.</summary>
public sealed record CategoryChoice(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A version to show: a published one, or the working draft when <see cref="Version"/> is null.</summary>
public sealed record VersionChoice(int? Version, string Label)
{
    public override string ToString() => Label;

    /// <summary>The draft, then every published version, newest first.</summary>
    public static IReadOnlyList<VersionChoice> For(ProtocolSummary p)
    {
        var choices = new List<VersionChoice>();
        if (p.DraftChanges)
        {
            choices.Add(new VersionChoice(null, p.IsPublished
                ? $"Draft: changes since version {p.LatestVersion}, not yet published"
                : "Draft, not yet published"));
        }

        foreach (var v in p.Versions.Reverse())
        {
            var when = v.Date ?? "date not recorded";
            var summary = v.Summary is { Length: > 70 } s ? s[..67] + "..." : v.Summary;
            choices.Add(new VersionChoice(v.Version,
                $"Version {v.Version}{(v.Version == p.LatestVersion ? " (current)" : "")}, {when}{(summary is null ? "" : $": {summary}")}"));
        }

        return choices;
    }

    /// <summary>What to show first: the current version, for the bench; the draft when nothing is published.</summary>
    public static VersionChoice? Default(IReadOnlyList<VersionChoice> choices, ProtocolSummary p) =>
        choices.FirstOrDefault(c => c.Version == p.LatestVersion && c.Version is not null) ?? choices.FirstOrDefault();
}
