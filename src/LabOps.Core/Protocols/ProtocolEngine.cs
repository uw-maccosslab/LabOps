using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LabOps.Core.Engines;
using LabOps.Core.Processes;
using LabOps.Core.Projects;
using LabOps.Core.Sync;

namespace LabOps.Core.Protocols;

/// <summary>
/// Runs <c>scripts/protocol.py</c> in the lab protocols repository and reads its JSON output.
/// </summary>
/// <remarks>
/// Every rule (the format, publishing, the frozen versions) lives in protocol.py, which changes with
/// the repository; this class only invokes it. It is also the repository's pre-commit check, which
/// refuses any change to a published version.
/// </remarks>
public sealed class ProtocolEngine : IPreCommitCheck
{
    private const string Script = "scripts/protocol.py";
    private const string Name = "protocol engine";

    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;
    private readonly ILogger<ProtocolEngine> _log;

    public ProtocolEngine(IProcessRunner runner, ToolLocator tools, ILogger<ProtocolEngine>? log = null)
    {
        _runner = runner;
        _tools = tools;
        _log = log ?? NullLogger<ProtocolEngine>.Instance;
    }

    /// <summary>The clone of the protocols repository. Set once setup has found or made it.</summary>
    public string? RepositoryPath { get; set; }

    /// <summary>Every protocol, with its versions.</summary>
    public async Task<ProtocolList> ListAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["list"], cancellationToken).ConfigureAwait(false);
        return ReadList(doc.RootElement);
    }

    /// <summary>
    /// Writes the printable page of a version (or of the working draft, when <paramref name="version"/>
    /// is null) to <paramref name="outFile"/>, with its figures inside it, and returns how it stands:
    /// "Version 3, published 2026-10-05".
    /// </summary>
    public async Task<string> RenderAsync(string id, int? version, string outFile, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "render", id, "--out", outFile };
        if (version is { } v)
        {
            args.AddRange(["--version", v.ToString(CultureInfo.InvariantCulture)]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return doc.RootElement.GetProperty("state").GetString() ?? "";
    }

    /// <summary>What changed from version <paramref name="from"/> (default: the last published) to <paramref name="to"/> (default: the draft).</summary>
    public async Task<ProtocolDiff> DiffAsync(string id, int? from = null, int? to = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "diff", id };
        if (from is { } f)
        {
            args.AddRange(["--from", f.ToString(CultureInfo.InvariantCulture)]);
        }

        if (to is { } t)
        {
            args.AddRange(["--to", t.ToString(CultureInfo.InvariantCulture)]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        return new ProtocolDiff(root.GetProperty("from").GetString() ?? "", root.GetProperty("to").GetString() ?? "",
            root.GetProperty("same").GetBoolean(), root.GetProperty("diff").GetString() ?? "");
    }

    /// <summary>Extracts an uploaded file's text and figures into inbox/&lt;id&gt;/ for Claude to format.</summary>
    public async Task<ProtocolImport> ImportAsync(string file, string? id = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "import", file };
        if (!string.IsNullOrWhiteSpace(id))
        {
            args.AddRange(["--id", id.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return doc.RootElement.Deserialize<ProtocolImport>(EngineJson.Options)
            ?? throw new EngineException("The protocol engine returned an empty import.");
    }

    /// <summary>Freezes the working text as the next version and returns its number.</summary>
    /// <param name="summary">What this version changes, in a sentence.</param>
    /// <param name="by">GitHub login of who publishes it.</param>
    public async Task<int> PublishAsync(string id, string summary, string by, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["publish", id, "--summary", summary.Trim(), "--by", by.Trim()], cancellationToken)
            .ConfigureAwait(false);
        return doc.RootElement.GetProperty("published").GetInt32();
    }

    /// <summary>Retires a protocol (naming the one to use instead, if any), or makes it active again.</summary>
    public async Task SetStatusAsync(string id, bool retired, string? replacedBy = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "status", id, retired ? "retired" : "active" };
        if (retired && !string.IsNullOrWhiteSpace(replacedBy))
        {
            args.AddRange(["--replaced-by", replacedBy.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The repository's pre-commit check on what is staged: no published version may change.</summary>
    public async Task<IReadOnlyList<CommitProblem>> CheckStagedAsync(CancellationToken cancellationToken)
    {
        // A refused commit answers ok: false with the problems, so a not-ok answer with them is expected.
        using var doc = await RunAsync(["check", "--staged"], cancellationToken, allowNotOk: true).ConfigureAwait(false);
        return ReadProblems(doc.RootElement).Select(p => new CommitProblem(p.Level, p.Message)).ToList();
    }

    public Task EnsureEnvironmentAsync(CancellationToken cancellationToken = default) =>
        EngineJson.EnsureEnvironmentAsync(_runner, _tools, RequireRepository(), m => new EngineException(m), cancellationToken);

    internal static ProtocolList ReadList(JsonElement root)
    {
        if (!root.TryGetProperty("protocols", out var protocols))
        {
            throw new EngineException("The protocol engine did not list any protocols. Sync the lab protocols, or update the app.");
        }

        List<T> Read<T>(string key) =>
            root.TryGetProperty(key, out var list) ? list.Deserialize<List<T>>(EngineJson.Options) ?? [] : [];

        return new(protocols.Deserialize<List<ProtocolSummary>>(EngineJson.Options) ?? [], Read<ProtocolCategory>("categories"),
            Read<Person>("people"), ReadProblems(root));
    }

    private static List<ProjectIssue> ReadProblems(JsonElement root) =>
        root.TryGetProperty("problems", out var p) ? p.Deserialize<List<ProjectIssue>>(EngineJson.Options) ?? [] : [];

    private async Task<JsonDocument> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNotOk = false)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await EngineJson.RunAsync(_runner, _tools, RequireRepository(), Script, args, cancellationToken)
            .ConfigureAwait(false);
        _log.LogDebug("protocol.py {Command} ({Milliseconds} ms)", args.Count > 0 ? args[0] : "",
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return Parse(result, allowNotOk);
    }

    internal static JsonDocument Parse(ProcessResult result, bool allowNotOk = false) =>
        EngineJson.Parse(result, Name, m => new EngineException(m), allowNotOk, partialKey: "problems");

    private string RequireRepository() =>
        RepositoryPath ?? throw new EngineException("The lab protocols are not set up on this computer yet. Open Setup to download them.");
}
