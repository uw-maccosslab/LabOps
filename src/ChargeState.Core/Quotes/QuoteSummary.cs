namespace ChargeState.Core.Quotes;

/// <summary>A validation message from quote.py (level ERROR or WARN).</summary>
public sealed record QuoteIssue(string Level, string Message)
{
    public bool IsError => string.Equals(Level, "ERROR", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Which output files exist in the quote's folder.</summary>
public sealed record QuoteFiles(bool Pdf, bool Xlsx, bool DraftPdf, bool DraftXlsx);

/// <summary>
/// One quote as <c>quote.py list --json</c> reports it. Totals come from the engine; the app
/// never computes a price itself.
/// </summary>
public sealed record QuoteSummary
{
    public string QuoteNumber { get; init; } = "";

    /// <summary>Folder relative to the repository root, with forward slashes.</summary>
    public string Folder { get; init; } = "";

    public string? Status { get; init; }

    public string? Year { get; init; }

    public string? Group { get; init; }

    public string? Institution { get; init; }

    public string? Pi { get; init; }

    public string? PreparedFor { get; init; }

    public string? Title { get; init; }

    public string? Short { get; init; }

    public string? Service { get; init; }

    public string? Issued { get; init; }

    public string? ValidUntil { get; init; }

    public string? Sent { get; init; }

    public string? Accepted { get; init; }

    public string? Invoiced { get; init; }

    public string? Declined { get; init; }

    public string? Po { get; init; }

    public decimal StudySamples { get; init; }

    public decimal Total { get; init; }

    public decimal PerSample { get; init; }

    public bool? MatchesTab { get; init; }

    public bool NeedsReview { get; init; }

    public IReadOnlyList<QuoteIssue> Issues { get; init; } = [];

    public QuoteFiles? Files { get; init; }

    /// <summary>Set instead of the fields above when quote.yaml could not be read.</summary>
    public string? Error { get; init; }

    public bool IsHistorical => Status == "historical";

    public bool IsDraft => Status == "draft";

    /// <summary>Sent or later: the text and spreadsheet are final; changes need a revision.</summary>
    public bool IsFrozen => Status is "sent" or "accepted" or "invoiced" or "declined";

    public bool HasErrors => Error is not null || Issues.Any(i => i.IsError);

    /// <summary>Who the quote is for, for the list: the PI when known, else the group.</summary>
    public string Client => !string.IsNullOrWhiteSpace(Pi) ? Pi! : Group ?? "";

    /// <summary>The project, for the list.</summary>
    public string Project => !string.IsNullOrWhiteSpace(Short) ? Short! : Title ?? "";

    /// <summary>The most recent lifecycle date, for sorting and display.</summary>
    public string? LatestDate => Invoiced ?? Accepted ?? Declined ?? Sent ?? Issued;

    public string FolderPath(string repositoryPath) =>
        Path.Combine(repositoryPath, Folder.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The PDF to show: the sent one if the quote was sent, otherwise the latest draft PDF, or
    /// null when neither exists. Checked on disk, because a draft PDF is usually made after the
    /// list was loaded.
    /// </summary>
    public string? ExistingPdf(string repositoryPath) => FirstExisting(repositoryPath, ".pdf");

    /// <summary>The sent spreadsheet if there is one, otherwise the draft one, or null.</summary>
    public string? ExistingSpreadsheet(string repositoryPath) => FirstExisting(repositoryPath, ".xlsx");

    private string? FirstExisting(string repositoryPath, string extension)
    {
        var folder = FolderPath(repositoryPath);
        return new[] { $"{QuoteNumber}{extension}", $"{QuoteNumber}-draft{extension}" }
            .Select(name => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
    }
}
