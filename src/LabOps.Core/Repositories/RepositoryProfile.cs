namespace LabOps.Core.Repositories;

/// <summary>The repositories the app works on.</summary>
public enum RepositoryKind
{
    Projects,
    Quotes,
    Protocols,
}

/// <summary>
/// Everything that differs between the repositories the app works on: where each lives on
/// GitHub, its engine, how its folders are laid out, which files are generated, and whether
/// commits must pass the engine's identifier check first.
/// </summary>
/// <remarks>
/// Lab members work in the projects and protocols repositories; only people who prepare quotes
/// have the quotes repository, so the app must run with any of them alone. Each open repository
/// gets its own git client and sync service built from its profile (see <see cref="Repository"/>).
/// </remarks>
public sealed class RepositoryProfile
{
    private RepositoryProfile(
        RepositoryKind kind, string id, string gitHubName, string displayName, string defaultFolderName,
        string engineScript, string rootFolder, int itemDepth, IReadOnlyList<string> generatedFileNames, bool checksCommits,
        IReadOnlyList<string> formerGitHubNames, string? cloneMarker = null)
    {
        CloneMarker = cloneMarker ?? engineScript;
        Kind = kind;
        Id = id;
        GitHubName = gitHubName;
        FormerGitHubNames = formerGitHubNames;
        DisplayName = displayName;
        DefaultFolderName = defaultFolderName;
        EngineScript = engineScript;
        RootFolder = rootFolder;
        ItemDepth = itemDepth;
        GeneratedFileNames = generatedFileNames;
        ChecksCommits = checksCommits;
    }

    /// <summary>uw-maccosslab/LabOps-Projects: labs, their projects and experiments, open to the whole lab.</summary>
    public static RepositoryProfile Projects { get; } = new(
        RepositoryKind.Projects, "projects", "uw-maccosslab/LabOps-Projects", "lab projects", "LabOps-Projects",
        "scripts/project.py", "projects", itemDepth: 3, generatedFileNames: [], checksCommits: true,
        formerGitHubNames: ["uw-maccosslab/lab-projects"], cloneMarker: "templates/project.example.yaml");

    /// <summary>uw-maccosslab/LabOps-Quotes: Proteomics Services quotes, for the people who prepare them.</summary>
    public static RepositoryProfile Quotes { get; } = new(
        RepositoryKind.Quotes, "quotes", "uw-maccosslab/LabOps-Quotes", "quotes", "LabOps-Quotes",
        "scripts/quote.py", "quotes", itemDepth: 4, generatedFileNames: ["calculation.md", "quote.md", "*-SOW.md"], checksCommits: false,
        formerGitHubNames: ["uw-maccosslab/services-quotes"]);

    /// <summary>
    /// uw-maccosslab/LabOps-Protocols: the lab's protocols and every version ever published, open to
    /// the whole lab. Its pre-commit check refuses any change to a published version.
    /// </summary>
    public static RepositoryProfile Protocols { get; } = new(
        RepositoryKind.Protocols, "protocols", "uw-maccosslab/LabOps-Protocols", "lab protocols", "LabOps-Protocols",
        "scripts/protocol.py", "protocols", itemDepth: 2, generatedFileNames: [], checksCommits: true,
        formerGitHubNames: []);

    public static IReadOnlyList<RepositoryProfile> All { get; } = [Projects, Quotes, Protocols];

    public RepositoryKind Kind { get; }

    /// <summary>Short stable key, used in settings and session keys.</summary>
    public string Id { get; }

    /// <summary>owner/name on GitHub.</summary>
    public string GitHubName { get; }

    /// <summary>
    /// Names it had before (lab-projects and services-quotes, renamed in October 2026). A clone made
    /// then still names them in its remote; GitHub redirects them, and the app updates the remote.
    /// </summary>
    public IReadOnlyList<string> FormerGitHubNames { get; }

    /// <summary>How the UI refers to it, in a sentence: "the lab projects".</summary>
    public string DisplayName { get; }

    /// <summary>Folder name for a new clone.</summary>
    public string DefaultFolderName { get; }

    /// <summary>The engine, relative to the repository root (the projects engine is now in the app; its script remains for the fallback).</summary>
    public string EngineScript { get; }

    /// <summary>
    /// A file only a clone of this repository has, relative to its root: the engine script, or for
    /// the projects, whose engine moved into the app, the project template.
    /// </summary>
    public string CloneMarker { get; }

    /// <summary>The folder holding every item (quote, project or protocol) and nothing else.</summary>
    public string RootFolder { get; }

    /// <summary>Path segments from the repository root to an item folder: quotes/Group/year/number is 4, protocols/id is 2.</summary>
    public int ItemDepth { get; }

    /// <summary>Files inside an item folder that the engine regenerates and nobody merges by hand.</summary>
    public IReadOnlyList<string> GeneratedFileNames { get; }

    /// <summary>
    /// True when every commit must first pass <c>check --staged</c>, which keeps identifying
    /// sample information out of git history (the projects repository) and published protocol
    /// versions unchanged (the protocols repository).
    /// </summary>
    public bool ChecksCommits { get; }

    public string Url => $"https://github.com/{GitHubName}";

    /// <summary>Where setup offers to put a new clone.</summary>
    public string DefaultClonePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DefaultFolderName);

    /// <summary>
    /// Files the app regenerates and therefore never asks a person to merge. A name starting with
    /// "*" matches the end of a file name (<c>*-SOW.md</c> for <c>&lt;number&gt;-SOW.md</c>).
    /// </summary>
    public bool IsGenerated(string path) =>
        path == "README.md"
        || (path.StartsWith(RootFolder + "/", StringComparison.Ordinal)
            && GeneratedFileNames.Any(name => name.StartsWith('*')
                ? path.EndsWith(name[1..], StringComparison.Ordinal) && !path.EndsWith("/" + name[1..], StringComparison.Ordinal)
                : path.EndsWith("/" + name, StringComparison.Ordinal)));

    /// <summary>The item folder (quotes/Group/year/number, projects/Lab/Project) of any path inside one.</summary>
    public string ItemFolder(string path) => string.Join('/', path.Split('/').Take(ItemDepth));

    /// <summary>True when the path is inside an item folder rather than elsewhere in the repository.</summary>
    public bool IsItemPath(string path) =>
        path.StartsWith(RootFolder + "/", StringComparison.Ordinal) && path.Split('/').Length > ItemDepth;

    /// <summary>A folder that is a clone of this repository, judging by its contents.</summary>
    public bool LooksLikeClone(string? path) =>
        path is not null
        && Directory.Exists(Path.Combine(path, ".git"))
        && File.Exists(Path.Combine(path, CloneMarker.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>True for the https and ssh forms of this repository's URL, under its name or a former one.</summary>
    public bool IsRemote(string url) => NameIn(url) is not null;

    /// <summary>
    /// The same URL under the repository's current name, when <paramref name="url"/> uses a former
    /// one (https://github.com/uw-maccosslab/lab-projects.git becomes .../LabOps-Projects.git);
    /// null when it already uses the current name or is not this repository.
    /// </summary>
    public string? RenamedRemote(string url)
    {
        if (NameIn(url) is not { } name || string.Equals(name, GitHubName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = url.Trim().TrimEnd('/');
        var at = trimmed.LastIndexOf(name, StringComparison.OrdinalIgnoreCase);
        return trimmed[..at] + GitHubName + trimmed[(at + name.Length)..];
    }

    /// <summary>Which of the repository's names (current or former) the URL uses, or null.</summary>
    private string? NameIn(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return new[] { GitHubName }.Concat(FormerGitHubNames).FirstOrDefault(name =>
            normalized.EndsWith("github.com/" + name, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("github.com:" + name, StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString() => Id;
}
