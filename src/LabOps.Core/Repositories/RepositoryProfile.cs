namespace LabOps.Core.Repositories;

/// <summary>The repositories the app works on.</summary>
public enum RepositoryKind
{
    Projects,
    Quotes,
}

/// <summary>
/// Everything that differs between the repositories the app works on: where each lives on
/// GitHub, its engine, how its folders are laid out, which files are generated, and whether
/// commits must pass the engine's identifier check first.
/// </summary>
/// <remarks>
/// Lab members work in the projects repository; only people who prepare quotes have the quotes
/// repository, so the app must run with either one alone. Each open repository gets its own git
/// client and sync service built from its profile (see <see cref="Repository"/>).
/// </remarks>
public sealed class RepositoryProfile
{
    private RepositoryProfile(
        RepositoryKind kind, string id, string gitHubName, string displayName, string defaultFolderName,
        string engineScript, string rootFolder, int itemDepth, IReadOnlyList<string> generatedFileNames, bool checksCommits)
    {
        Kind = kind;
        Id = id;
        GitHubName = gitHubName;
        DisplayName = displayName;
        DefaultFolderName = defaultFolderName;
        EngineScript = engineScript;
        RootFolder = rootFolder;
        ItemDepth = itemDepth;
        GeneratedFileNames = generatedFileNames;
        ChecksCommits = checksCommits;
    }

    /// <summary>uw-maccosslab/lab-projects: labs, their projects and experiments, open to the whole lab.</summary>
    public static RepositoryProfile Projects { get; } = new(
        RepositoryKind.Projects, "projects", "uw-maccosslab/lab-projects", "lab projects", "lab-projects",
        "scripts/project.py", "projects", itemDepth: 3, generatedFileNames: [], checksCommits: true);

    /// <summary>uw-maccosslab/services-quotes: Proteomics Services quotes, for the people who prepare them.</summary>
    public static RepositoryProfile Quotes { get; } = new(
        RepositoryKind.Quotes, "quotes", "uw-maccosslab/services-quotes", "quotes", "services-quotes",
        "scripts/quote.py", "quotes", itemDepth: 4, generatedFileNames: ["calculation.md", "quote.md"], checksCommits: false);

    public static IReadOnlyList<RepositoryProfile> All { get; } = [Projects, Quotes];

    public RepositoryKind Kind { get; }

    /// <summary>Short stable key, used in settings and session keys.</summary>
    public string Id { get; }

    /// <summary>owner/name on GitHub.</summary>
    public string GitHubName { get; }

    /// <summary>How the UI refers to it, in a sentence: "the lab projects".</summary>
    public string DisplayName { get; }

    /// <summary>Folder name for a new clone.</summary>
    public string DefaultFolderName { get; }

    /// <summary>The engine, relative to the repository root.</summary>
    public string EngineScript { get; }

    /// <summary>The folder holding every item (quote or project) and nothing else.</summary>
    public string RootFolder { get; }

    /// <summary>Path segments from the repository root to an item folder: quotes/Group/year/number is 4.</summary>
    public int ItemDepth { get; }

    /// <summary>Files inside an item folder that the engine regenerates and nobody merges by hand.</summary>
    public IReadOnlyList<string> GeneratedFileNames { get; }

    /// <summary>
    /// True when every commit must first pass <c>check --staged</c>, which keeps identifying
    /// sample information out of git history (the projects repository).
    /// </summary>
    public bool ChecksCommits { get; }

    public string Url => $"https://github.com/{GitHubName}";

    /// <summary>Where setup offers to put a new clone.</summary>
    public string DefaultClonePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DefaultFolderName);

    /// <summary>Files the app regenerates and therefore never asks a person to merge.</summary>
    public bool IsGenerated(string path) =>
        path == "README.md"
        || (path.StartsWith(RootFolder + "/", StringComparison.Ordinal)
            && GeneratedFileNames.Any(name => path.EndsWith("/" + name, StringComparison.Ordinal)));

    /// <summary>The item folder (quotes/Group/year/number, projects/Lab/Project) of any path inside one.</summary>
    public string ItemFolder(string path) => string.Join('/', path.Split('/').Take(ItemDepth));

    /// <summary>True when the path is inside an item folder rather than elsewhere in the repository.</summary>
    public bool IsItemPath(string path) =>
        path.StartsWith(RootFolder + "/", StringComparison.Ordinal) && path.Split('/').Length > ItemDepth;

    /// <summary>A folder that is a clone of this repository, judging by its contents.</summary>
    public bool LooksLikeClone(string? path) =>
        path is not null
        && Directory.Exists(Path.Combine(path, ".git"))
        && File.Exists(Path.Combine(path, EngineScript.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>True for the https and ssh forms of this repository's URL.</summary>
    public bool IsRemote(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.EndsWith("github.com/" + GitHubName, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("github.com:" + GitHubName, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Id;
}
