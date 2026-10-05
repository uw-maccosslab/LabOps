using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LabOps.Core.Engines;
using LabOps.Core.Processes;
using LabOps.Core.Sync;

namespace LabOps.Core.Projects;

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

    /// <summary>Every lab, project and experiment.</summary>
    /// <param name="includeClosed">
    /// False leaves out closed projects (list --active), which the engine then neither checks nor
    /// summarizes, so the list costs what the current work costs; <see cref="ProjectList.ClosedHidden"/>
    /// says how many were left out.
    /// </param>
    public async Task<ProjectList> ListAsync(bool includeClosed = false, CancellationToken cancellationToken = default)
    {
        JsonDocument doc;
        try
        {
            doc = await RunAsync(includeClosed ? ["list"] : ["list", "--active"], cancellationToken).ConfigureAwait(false);
        }
        catch (EngineException ex) when (!includeClosed && ex.Message.Contains("--active", StringComparison.Ordinal))
        {
            // A clone whose engine predates list --active refuses it (argparse); its list has
            // every project, so nothing is hidden.
            _log.LogInformation("This project engine has no list --active; listing every project.");
            doc = await RunAsync(["list"], cancellationToken).ConfigureAwait(false);
        }

        using (doc)
        {
            return ReadList(doc.RootElement);
        }
    }

    /// <summary>Starts, finishes or skips a step.</summary>
    /// <param name="item">The project (its sample steps) or experiment, by name.</param>
    /// <param name="stage">The step's id.</param>
    /// <param name="action">What to record.</param>
    /// <param name="date">When it happened; today when null.</param>
    /// <param name="by">GitHub login of who did it.</param>
    /// <param name="note">A short note; null leaves any existing note alone.</param>
    public async Task StageAsync(
        string item, string stage, StageAction action, DateOnly? date = null, string? by = null, string? note = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "stage", item, stage, action.ToString().ToLowerInvariant() };
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
    }

    /// <summary>Adds a step to a project's or an experiment's timeline.</summary>
    /// <param name="item">The project or experiment, by name.</param>
    /// <param name="kind">One of <see cref="StageNames.Kinds"/>.</param>
    /// <param name="label">How it reads; needed for kind other.</param>
    /// <param name="after">The step to put it after.</param>
    /// <param name="before">The step to put it before. With neither, it goes at the end.</param>
    public async Task AddStepAsync(
        string item, string kind, string? label, string? after, string? before = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "add-step", item, kind };
        if (!string.IsNullOrWhiteSpace(label))
        {
            args.AddRange(["--label", label.Trim()]);
        }

        if (!string.IsNullOrWhiteSpace(after))
        {
            args.AddRange(["--after", after]);
        }
        else if (!string.IsNullOrWhiteSpace(before))
        {
            args.AddRange(["--before", before]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Assigns steps to a person, or to nobody when <paramref name="login"/> is null.</summary>
    /// <param name="item">The project or experiment, by name.</param>
    /// <param name="stages">The steps' ids.</param>
    /// <param name="login">GitHub login of who does them.</param>
    public async Task AssignAsync(string item, IReadOnlyList<string> stages, string? login, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "assign", item };
        args.AddRange(stages);
        args.AddRange(login is null ? ["--nobody"] : ["--to", login]);
        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records an experiment's Panorama folder; any Panorama address is kept as the folder path.</summary>
    /// <param name="experiment">The experiment, by name.</param>
    /// <param name="folder">The folder path, or any address for it copied from the browser.</param>
    /// <param name="kind">raw (the raw data) or results (results shared there).</param>
    public async Task LinkPanoramaAsync(string experiment, string folder, string kind, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["link", experiment, "panorama", folder.Trim(), "--kind", kind], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Records a project's or an experiment's ELN notebook: its link, its ID, or both.</summary>
    public async Task LinkNotebookAsync(string item, string? url, string? id, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "link", item, "notebook" };
        if (!string.IsNullOrWhiteSpace(url))
        {
            args.Add(url.Trim());
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            args.AddRange(["--id", id.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records the protocol a step followed, at the published version used.</summary>
    /// <param name="item">The project (for its sample steps) or experiment, by name.</param>
    /// <param name="protocol">The protocol's ID in LabOps-Protocols.</param>
    /// <param name="version">The published version followed.</param>
    /// <param name="step">The step's id, or null for the item as a whole.</param>
    /// <param name="title">The protocol's title, shown on the wiki page.</param>
    public async Task LinkProtocolAsync(
        string item, string protocol, int version, string? step, string? title, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "link", item, "protocol", protocol, "--version", version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(step))
        {
            args.AddRange(["--step", step]);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            args.AddRange(["--title", title.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a Panorama folder, a notebook or a protocol.</summary>
    /// <param name="what">panorama, notebook or protocol.</param>
    /// <param name="value">The folder, the notebook's ID or link, or the protocol's ID.</param>
    /// <param name="step">For a protocol: only its link for this step.</param>
    public async Task UnlinkAsync(string item, string what, string value, string? step = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "unlink", item, what, value };
        if (!string.IsNullOrWhiteSpace(step))
        {
            args.AddRange(["--step", step]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records where the project's wiki page is: a Panorama folder (or its address) and the page's name.</summary>
    public async Task LinkWikiAsync(string project, string folder, string? page, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "link", project, "wiki", folder.Trim() };
        if (!string.IsNullOrWhiteSpace(page))
        {
            args.AddRange(["--page", page.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops recording the project's wiki page (the page stays on Panorama).</summary>
    public async Task UnlinkWikiAsync(string project, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["unlink", project, "wiki"], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the project's wiki page. <paramref name="documentsFile"/> is JSON mapping each results
    /// or qc folder to its Skyline documents, read from Panorama; without it they are not listed.
    /// </summary>
    public async Task<WikiPageContent> WikiAsync(string project, string? documentsFile = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "wiki", project };
        if (documentsFile is not null)
        {
            args.AddRange(["--documents", documentsFile]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        return ReadWiki(doc.RootElement);
    }

    internal static WikiPageContent ReadWiki(JsonElement root) => new(
        root.GetProperty("project").GetString() ?? "",
        root.TryGetProperty("folder", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null,
        root.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "default",
        root.GetProperty("title").GetString() ?? "",
        root.GetProperty("html").GetString() ?? "",
        root.TryGetProperty("written", out var w) && w.ValueKind == JsonValueKind.True,
        root.TryGetProperty("written_hash", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString()! : "none");

    /// <summary>Removes a step that has not started.</summary>
    public async Task RemoveStepAsync(string item, string stage, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["remove-step", item, stage], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Looks for identifiers in a collaborator's file. Reports columns and problems, never values.</summary>
    public async Task<ScanResult> ScanAsync(string file, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["scan", file], cancellationToken).ConfigureAwait(false);
        return doc.RootElement.Deserialize<ScanResult>(EngineJson.Options)
            ?? throw new EngineException("The project engine returned an empty scan.");
    }

    /// <summary>Writes the project's Octopus input to inbox/ and returns its full path.</summary>
    public async Task<OctopusInput> OctopusInputAsync(string project, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["octopus-input", project], cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        return new OctopusInput(
            ToLocalPath(root.GetProperty("file").GetString()!),
            root.GetProperty("samples").GetInt32(),
            root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString() ?? "").ToList());
    }

    /// <summary>Keeps an Octopus layout export with the input that produced it, and marks the layout done.</summary>
    public async Task ImportLayoutAsync(
        string project, string layoutFile, string? by, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "import-layout", project, layoutFile };
        if (!string.IsNullOrWhiteSpace(by))
        {
            args.AddRange(["--by", by.Trim()]);
        }

        using var doc = await RunAsync(args, cancellationToken).ConfigureAwait(false);
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

    internal static ProjectList ReadList(JsonElement root)
    {
        // Labs came with project engine 26.2.0; before that the top level was "projects". A
        // repository that old needs its own update, which config/app.yaml's version tells people.
        if (!root.TryGetProperty("labs", out var labs))
        {
            throw new EngineException("This copy of the lab projects is older than this app. Sync it, or ask Mike to update the repository.");
        }

        var people = root.TryGetProperty("people", out var list) ? list.Deserialize<List<Person>>(EngineJson.Options) ?? [] : [];
        var hidden = root.TryGetProperty("closed_hidden", out var h) && h.TryGetInt32(out var n) ? n : 0;
        return new(labs.Deserialize<List<LabSummary>>(EngineJson.Options) ?? [], people, ReadProblems(root), hidden);
    }

    private static List<ProjectIssue> ReadProblems(JsonElement root) =>
        root.TryGetProperty("problems", out var p) ? p.Deserialize<List<ProjectIssue>>(EngineJson.Options) ?? [] : [];

    private async Task<JsonDocument> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNotOk = false)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await EngineJson.RunAsync(_runner, _tools, RequireRepository(), Script, args, cancellationToken)
            .ConfigureAwait(false);
        _log.LogDebug("project.py {Arguments} ({Milliseconds} ms)", string.Join(' ', args),
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return Parse(result, allowNotOk);
    }

    internal static JsonDocument Parse(ProcessResult result, bool allowNotOk = false) =>
        EngineJson.Parse(result, Name, m => new EngineException(m), allowNotOk, partialKey: "problems");

    private string RequireRepository() =>
        RepositoryPath ?? throw new EngineException("No lab projects repository is set up yet. Open Setup.");

    private string ToLocalPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(RequireRepository(), path.Replace('/', Path.DirectorySeparatorChar));
}
