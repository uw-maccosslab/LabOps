using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ChargeState.Core.Engines;
using ChargeState.Core.Processes;

namespace ChargeState.Core.Quotes;

/// <summary>Status changes recorded by <c>quote.py status</c>.</summary>
public enum QuoteStatusChange
{
    Accepted,
    Invoiced,
    Declined,
}

/// <summary>The result of sending a quote.</summary>
public sealed record SendResult(QuoteSummary Quote, string PdfPath, string SpreadsheetPath, IReadOnlyList<string> Warnings);

/// <summary>quote.py reported a problem; <see cref="Exception.Message"/> is written for the user.</summary>
public sealed class QuoteEngineException(string message) : EngineException(message);

/// <summary>
/// Runs <c>scripts/quote.py</c> in the quotes repository through uv and reads its JSON output.
/// </summary>
/// <remarks>
/// All pricing, validation and file generation stays in quote.py, which lives in the quotes
/// repository and changes with it. This class only invokes it, so a fix to the engine reaches
/// every user through a normal sync rather than an app release.
/// </remarks>
public sealed class QuoteEngine
{
    internal static readonly JsonSerializerOptions JsonOptions = EngineJson.Options;

    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;
    private readonly ILogger<QuoteEngine> _log;

    public QuoteEngine(IProcessRunner runner, ToolLocator tools, ILogger<QuoteEngine>? log = null)
    {
        _runner = runner;
        _tools = tools;
        _log = log ?? NullLogger<QuoteEngine>.Instance;
    }

    /// <summary>The clone of the quotes repository. Set once setup has found or made it.</summary>
    public string? RepositoryPath { get; set; }

    public async Task<IReadOnlyList<QuoteSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["list"], cancellationToken).ConfigureAwait(false);
        return ReadQuotes(doc.RootElement);
    }

    public async Task<QuoteSummary> BuildAsync(string quote, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["build", quote], cancellationToken, allowNotOk: true).ConfigureAwait(false);
        return ReadQuotes(doc.RootElement).Single();
    }

    public async Task<SendResult> SendAsync(string quote, bool keepIssued, CancellationToken cancellationToken = default)
    {
        string[] args = keepIssued ? ["send", quote, "--keep-issued"] : ["send", quote];
        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        var warnings = root.TryGetProperty("warnings", out var w)
            ? w.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : [];
        return new SendResult(
            ReadQuote(root.GetProperty("quote")),
            ToLocalPath(root.GetProperty("pdf").GetString()!),
            ToLocalPath(root.GetProperty("xlsx").GetString()!),
            warnings);
    }

    public async Task<QuoteSummary> SetStatusAsync(
        string quote, QuoteStatusChange change, string? purchaseOrder = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "status", quote, change.ToString().ToLowerInvariant() };
        if (!string.IsNullOrWhiteSpace(purchaseOrder))
        {
            args.AddRange(["--po", purchaseOrder.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return ReadQuote(doc.RootElement.GetProperty("quote"));
    }

    public async Task<QuoteSummary> ReviseAsync(string quote, bool currentRates = false, CancellationToken cancellationToken = default)
    {
        string[] args = currentRates ? ["revise", quote, "--current-rates"] : ["revise", quote];
        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return ReadQuote(doc.RootElement.GetProperty("quote"));
    }

    /// <summary>Writes the untracked draft PDF and returns its full path.</summary>
    public async Task<string> DraftPdfAsync(string quote, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["pdf", quote], cancellationToken).ConfigureAwait(false);
        return ToLocalPath(doc.RootElement.GetProperty("files")[0].GetString()!);
    }

    /// <summary>
    /// Writes the statement of work (Exhibit A) priced at <paramref name="sampleCounts"/>, which
    /// quote.py also saves in quote.yaml, and returns the document's full path.
    /// </summary>
    public async Task<string> StatementOfWorkAsync(string quote, IEnumerable<int> sampleCounts, CancellationToken cancellationToken = default)
    {
        var counts = string.Join(',', sampleCounts.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var doc = await RunAsync(["sow", quote, "--samples", counts], cancellationToken).ConfigureAwait(false);
        return ToLocalPath(doc.RootElement.GetProperty("file").GetString()!);
    }

    /// <summary>Prepares the Python environment (first run downloads it). Safe to repeat.</summary>
    public Task EnsureEnvironmentAsync(CancellationToken cancellationToken = default) =>
        EngineJson.EnsureEnvironmentAsync(_runner, _tools, RequireRepository(), m => new QuoteEngineException(m), cancellationToken);

    internal static IReadOnlyList<QuoteSummary> ReadQuotes(JsonElement root) =>
        root.GetProperty("quotes").EnumerateArray().Select(ReadQuote).ToList();

    internal static QuoteSummary ReadQuote(JsonElement element) =>
        element.Deserialize<QuoteSummary>(JsonOptions)
        ?? throw new QuoteEngineException("quote.py returned an empty quote.");

    private async Task<JsonDocument> RunAsync(
        IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNotOk = false)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await EngineJson.RunAsync(_runner, _tools, RequireRepository(), "scripts/quote.py", args, cancellationToken)
            .ConfigureAwait(false);
        _log.LogDebug("quote.py {Arguments} ({Milliseconds} ms)", string.Join(' ', args),
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return Parse(result, allowNotOk);
    }

    /// <summary>quote.py's answer; a failed build still carries the quote with its errors.</summary>
    internal static JsonDocument Parse(ProcessResult result, bool allowNotOk = false) =>
        EngineJson.Parse(result, "quote engine", m => new QuoteEngineException(m), allowNotOk, partialKey: "quotes");

    private string RequireRepository() =>
        RepositoryPath ?? throw new QuoteEngineException("No quotes repository is set up yet. Open Setup.");

    private string ToLocalPath(string relative) =>
        Path.Combine(RequireRepository(), relative.Replace('/', Path.DirectorySeparatorChar));
}
