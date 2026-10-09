using System.Text.Json;
using LabOps.Engines;
using LabOps.Engines.CommandLine;
using LabOps.Engines.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LabOps.Core.Engines;
using LabOps.Core.Sync;

namespace LabOps.Core.Projects;

/// <summary>
/// Runs the lab projects engine's commands on the clone and reads their answers.
/// </summary>
/// <remarks>
/// The engine is LabOps.Engines' C# port of what was LabOps-Projects' scripts/project.py, run
/// in-process with the same command lines the labops tool takes (ProjectsCommandLine), so the app,
/// Claude's skills and the pre-commit hook always mean the same thing. It is also the repository's
/// pre-commit check.
/// </remarks>
public sealed class ProjectEngine : IPreCommitCheck
{
    private const string Name = "project engine";

    private readonly ILogger<ProjectEngine> _log;

    public ProjectEngine(ILogger<ProjectEngine>? log = null)
    {
        _log = log ?? NullLogger<ProjectEngine>.Instance;
    }

    /// <summary>The clone of the projects repository. Set once setup has found or made it.</summary>
    public string? RepositoryPath { get; set; }

    /// <summary>
    /// Runs one command line on the clone and returns the JSON it answers. The engine itself, but
    /// a test can answer instead, to see the command lines the app builds.
    /// </summary>
    internal Func<string, IReadOnlyList<string>, string> Commands { get; init; } = RunInProcess;

    /// <summary>Every lab, project and experiment.</summary>
    /// <param name="includeClosed">
    /// False leaves out closed projects (list --active), which the engine then neither checks nor
    /// summarizes, so the list costs what the current work costs; <see cref="ProjectList.ClosedHidden"/>
    /// says how many were left out.
    /// </param>
    public async Task<ProjectList> ListAsync(bool includeClosed = false, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(includeClosed ? ["list"] : ["list", "--active"], cancellationToken).ConfigureAwait(false);
        return ReadList(doc.RootElement);
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

    /// <summary>
    /// Plans steps: when they should start and finish. Both dates are written as given, so a date
    /// left out (null) is removed; with neither, the plan is cleared. It is one write either way.
    /// </summary>
    /// <param name="item">The project or experiment, by name.</param>
    /// <param name="stages">The steps' ids.</param>
    public async Task PlanAsync(string item, IReadOnlyList<string> stages, DateOnly? start, DateOnly? finish,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "plan", item };
        args.AddRange(stages);
        if (start is null && finish is null)
        {
            args.Add("--clear");
        }
        else
        {
            args.AddRange(start is { } s ? ["--start", s.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)] : ["--no-start"]);
            args.AddRange(finish is { } f ? ["--finish", f.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)] : ["--no-finish"]);
        }

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

    /// <summary>The files <see cref="ScanAsync"/> reads: spreadsheets and CSV.</summary>
    public static bool CanScan(string path) =>
        new[] { ".xlsx", ".xlsm", ".csv" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks for identifiers in a collaborator's file. Reports columns and problems, never values.</summary>
    public async Task<ScanResult> ScanAsync(string file, CancellationToken cancellationToken = default)
    {
        using var doc = await RunAsync(["scan", file], cancellationToken).ConfigureAwait(false);
        return EngineJson.Read(Name, m => new EngineException(m), () => doc.RootElement.Deserialize<ScanResult>(EngineJson.Options))
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

    internal static ProjectList ReadList(JsonElement root)
    {
        // Labs came with project engine 26.2.0; before that the top level was "projects". A
        // repository that old needs its own update, which config/app.yaml's version tells people.
        if (!root.TryGetProperty("labs", out var labs))
        {
            throw new EngineException("This copy of the lab projects is older than this app. Sync it, or ask Mike to update the repository.");
        }

        return EngineJson.Read(Name, m => new EngineException(m), () =>
        {
            var people = root.TryGetProperty("people", out var list) ? list.Deserialize<List<Person>>(EngineJson.Options) ?? [] : [];
            var hidden = root.TryGetProperty("closed_hidden", out var h) && h.TryGetInt32(out var n) ? n : 0;
            var instruments = root.TryGetProperty("instruments", out var i) ? i.Deserialize<List<string>>(EngineJson.Options) ?? [] : [];
            return new ProjectList(labs.Deserialize<List<LabSummary>>(EngineJson.Options) ?? [], people, ReadProblems(root), hidden, instruments);
        });
    }

    private static List<ProjectIssue> ReadProblems(JsonElement root) =>
        root.TryGetProperty("problems", out var p) ? p.Deserialize<List<ProjectIssue>>(EngineJson.Options) ?? [] : [];

    private async Task<JsonDocument> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowNotOk = false)
    {
        var root = RequireRepository();
        var begun = System.Diagnostics.Stopwatch.GetTimestamp();
        // The engine reads and writes files, so it runs off the window's thread.
        var json = await Task.Run(() => Commands(root, args), cancellationToken).ConfigureAwait(false);
        _log.LogDebug("projects {Arguments} ({Milliseconds} ms)", string.Join(' ', args),
            (int)System.Diagnostics.Stopwatch.GetElapsedTime(begun).TotalMilliseconds);
        return Answer(json, allowNotOk);
    }

    /// <summary>One command line run by the C# engine: the JSON `labops projects --json` prints.</summary>
    internal static string RunInProcess(string root, IReadOnlyList<string> args)
    {
        try
        {
            return ProjectsCommandLine.Execute(new ProjectRepository(root), args).Payload.ToJsonString();
        }
        catch (EngineError ex)
        {
            throw new EngineException(ex.Message);
        }
        catch (Exception ex) when (ex is UsageError or IOException or UnauthorizedAccessException or InvalidOperationException
                                       or FormatException or ArgumentException or LabOps.Engines.Yaml.YamlProblemException)
        {
            throw new EngineException($"The project engine could not run: {ex.Message}");
        }
    }

    /// <summary>
    /// A command's JSON, once it says ok: an answer that is not ok becomes an EngineException with
    /// the engine's own message, unless it carries the problems the caller asked for (a refused
    /// staged check answers ok: false with them).
    /// </summary>
    internal static JsonDocument Answer(string json, bool allowNotOk = false)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new EngineException($"The {Name} gave an answer LabOps could not read: {ex.Message}");
        }

        var answer = doc.RootElement;
        if ((answer.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            || (allowNotOk && answer.TryGetProperty("problems", out _)))
        {
            return doc;
        }

        var message = answer.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()!
            : $"The {Name} reported a problem.";
        doc.Dispose();
        throw new EngineException(message);
    }

    private string RequireRepository() =>
        RepositoryPath ?? throw new EngineException("No lab projects repository is set up yet. Open Setup.");

    private string ToLocalPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(RequireRepository(), path.Replace('/', Path.DirectorySeparatorChar));
}
