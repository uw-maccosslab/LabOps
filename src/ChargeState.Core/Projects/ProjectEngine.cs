using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ChargeState.Core.Engines;
using ChargeState.Core.Processes;
using ChargeState.Core.Sync;

namespace ChargeState.Core.Projects;

/// <summary>
/// Runs <c>scripts/project.py</c> in the lab projects repository and reads its JSON output.
/// </summary>
/// <remarks>
/// Every rule (stages, identifier checks, the Octopus formats) lives in project.py, which changes
/// with the repository. This class only invokes it, so a fix to a rule reaches everyone through a
/// normal sync rather than an app release. It is also the repository's pre-commit check.
/// </remarks>
public sealed class ProjectEngine : IPreCommitCheck
{
    private const string Script = "scripts/project.py";
    private const string Name = "project engine";

    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;
    private readonly ILogger<ProjectEngine> _log;

    public ProjectEngine(IProcessRunner runner, ToolLocator tools, ILogger<ProjectEngine>? log = null)
    {
        _runner = runner;
        _tools = tools;
        _log = log ?? NullLogger<ProjectEngine>.Instance;
    }

    /// <summary>The clone of the projects repository. Set once setup has found or made it.</summary>
    public string? RepositoryPath { get; set; }

    public async Task<ProjectList> ListAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["list"], cancellationToken).ConfigureAwait(false);
        return ReadList(doc.RootElement);
    }

    /// <summary>Starts, finishes or skips a stage.</summary>
    /// <param name="experiment">The experiment's name.</param>
    /// <param name="stage">One of <see cref="StageNames.All"/>.</param>
    /// <param name="action">What to record.</param>
    /// <param name="date">When it happened; today when null.</param>
    /// <param name="by">GitHub login of who did it.</param>
    /// <param name="note">A short note; null leaves any existing note alone.</param>
    public async Task<ExperimentSummary> StageAsync(
        string experiment, string stage, StageAction action, DateOnly? date = null, string? by = null, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "stage", experiment, stage, action.ToString().ToLowerInvariant() };
        if (date is { } d)
        {
            args.AddRange(["--date", d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)]);
        }

        if (!string.IsNullOrWhiteSpace(by))
        {
            args.AddRange(["--by", by.Trim()]);
        }

        if (note is not null)
        {
            args.AddRange(["--note", note.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return ReadExperiment(doc.RootElement.GetProperty("experiment"));
    }

    /// <summary>Looks for identifiers in a collaborator's file. Reports columns and problems, never values.</summary>
    public async Task<ScanResult> ScanAsync(string file, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["scan", file], cancellationToken).ConfigureAwait(false);
        return doc.RootElement.Deserialize<ScanResult>(EngineJson.Options)
            ?? throw new EngineException("The project engine returned an empty scan.");
    }

    /// <summary>Writes the experiment's Octopus input to inbox/ and returns its full path.</summary>
    public async Task<OctopusInput> OctopusInputAsync(string experiment, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["octopus-input", experiment], cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        return new OctopusInput(
            ToLocalPath(root.GetProperty("file").GetString()!),
            root.GetProperty("samples").GetInt32(),
            root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString() ?? "").ToList());
    }

    /// <summary>Keeps an Octopus layout export with the input that produced it, and marks the layout done.</summary>
    public async Task<ExperimentSummary> ImportLayoutAsync(
        string experiment, string layoutFile, string? by, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "import-layout", experiment, layoutFile };
        if (!string.IsNullOrWhiteSpace(by))
        {
            args.AddRange(["--by", by.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return ReadExperiment(doc.RootElement.GetProperty("experiment"));
    }

    /// <summary>The identifier check on what is staged: the repository's pre-commit check.</summary>
    public async Task<IReadOnlyList<CommitProblem>> CheckStagedAsync(CancellationToken cancellationToken)
    {
        // A refused commit answers ok: false with the problems, so a not-ok answer with them is expected.
        using var doc = await RunAsync(["check", "--staged"], cancellationToken, allowNotOk: true).ConfigureAwait(false);
        return ReadProblems(doc.RootElement).Select(p => new CommitProblem(p.Level, p.Message)).ToList();
    }

    public Task EnsureEnvironmentAsync(CancellationToken cancellationToken = default) =>
        EngineJson.EnsureEnvironmentAsync(_runner, _tools, RequireRepository(), m => new EngineException(m), cancellationToken);

    internal static ProjectList ReadList(JsonElement root) =>
        new(root.GetProperty("projects").Deserialize<List<ProjectSummary>>(EngineJson.Options) ?? [], ReadProblems(root));

    internal static ExperimentSummary ReadExperiment(JsonElement element) =>
        element.Deserialize<ExperimentSummary>(EngineJson.Options)
        ?? throw new EngineException("The project engine returned an empty experiment.");

    private static List<ProjectIssue> ReadProblems(JsonElement root) =>
        root.TryGetProperty("problems", out var p) ? p.Deserialize<List<ProjectIssue>>(EngineJson.Options) ?? [] : [];

    private async Task<JsonDocument> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNotOk = false)
    {
        _log.LogDebug("project.py {Arguments}", string.Join(' ', args));
        var result = await EngineJson.RunAsync(_runner, _tools, RequireRepository(), Script, args, cancellationToken)
            .ConfigureAwait(false);
        return Parse(result, allowNotOk);
    }

    internal static JsonDocument Parse(ProcessResult result, bool allowNotOk = false) =>
        EngineJson.Parse(result, Name, m => new EngineException(m), allowNotOk, partialKey: "problems");

    private string RequireRepository() =>
        RepositoryPath ?? throw new EngineException("No lab projects repository is set up yet. Open Setup.");

    private string ToLocalPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(RequireRepository(), path.Replace('/', Path.DirectorySeparatorChar));
}
