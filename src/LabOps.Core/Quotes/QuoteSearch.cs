namespace LabOps.Core.Quotes;

/// <summary>Which quotes the list shows.</summary>
public enum QuoteFilter
{
    /// <summary>Everything except the historical estimates.</summary>
    Current,
    All,
    Draft,
    Sent,
    Accepted,
    Invoiced,
    Declined,
    Historical,
}

/// <summary>
/// Search over the quote list: every word typed must appear somewhere in the quote's details or
/// in the text of its quote.yaml and quote.md.
/// </summary>
/// <remarks>
/// A plain substring match over a few hundred small files is instant, and it finds things a
/// structured search would miss (a species, a cc'd collaborator, a phrase in a text override),
/// which is what someone hunting for "that marten quote" actually needs.
/// </remarks>
public sealed class QuoteSearch
{
    private Dictionary<string, string> _text = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads quote.yaml and quote.md for every quote, for full-text matching.</summary>
    public void Index(string repositoryPath, IEnumerable<QuoteSummary> quotes)
    {
        var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var quote in quotes)
        {
            var folder = quote.FolderPath(repositoryPath);
            text[quote.QuoteNumber] = string.Concat(ReadOrEmpty(Path.Combine(folder, "quote.yaml")), "\n",
                ReadOrEmpty(Path.Combine(folder, "quote.md")));
        }

        _text = text;
    }

    public IReadOnlyList<QuoteSummary> Filter(IEnumerable<QuoteSummary> quotes, string? query, QuoteFilter filter)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return quotes
            .Where(q => Matches(q, filter))
            .Where(q => words.All(w => Haystack(q).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(q => q.IsHistorical)
            .ThenByDescending(q => q.Year == "undated" ? "" : q.Year)
            .ThenByDescending(q => q.LatestDate)
            .ThenBy(q => q.QuoteNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Matches(QuoteSummary quote, QuoteFilter filter) => filter switch
    {
        QuoteFilter.All => true,
        QuoteFilter.Current => !quote.IsHistorical,
        QuoteFilter.Historical => quote.IsHistorical,
        _ => string.Equals(quote.Status, filter.ToString(), StringComparison.OrdinalIgnoreCase),
    };

    private string Haystack(QuoteSummary q) => string.Join('\n',
        q.QuoteNumber, q.Group, q.Institution, q.Pi, q.PreparedFor, q.Title, q.Short, q.Status, q.Year, q.Po,
        _text.GetValueOrDefault(q.QuoteNumber, ""));

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (IOException)
        {
            return "";
        }
    }
}
