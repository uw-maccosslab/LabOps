using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LabOps.Core.Infrastructure;
using LabOps.Core.Processes;
using LabOps.Core.Quotes;
using LabOps.Core.Sync;

namespace LabOps.Core.Repositories;

/// <summary>
/// One open clone: its profile, its own git client and sync service, and the settings it shares
/// with everyone (config/app.yaml).
/// </summary>
/// <remarks>
/// Each repository owns its git client. Sharing one between repositories would let a background
/// fetch of one run in the other's folder.
/// </remarks>
public sealed class Repository
{
    internal Repository(RepositoryProfile profile, string path, GitClient git, SyncService sync)
    {
        Profile = profile;
        Path = path;
        Git = git;
        Sync = sync;
        ReloadConfig();
    }

    public RepositoryProfile Profile { get; }

    /// <summary>The clone's folder.</summary>
    public string Path { get; }

    public GitClient Git { get; }

    public SyncService Sync { get; }

    /// <summary>config/app.yaml as of the last <see cref="ReloadConfig"/>.</summary>
    public RepoConfig Config { get; private set; } = RepoConfig.Default;

    /// <summary>True when this repository needs a newer app than the one running.</summary>
    public bool AppTooOld => Config.RequiresNewerThan(AppInfo.Version);

    public void ReloadConfig() => Config = RepoConfig.Load(Path);

    /// <summary>A path inside the clone from a repository-relative one (forward slashes).</summary>
    public string Combine(string relative) =>
        System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
}

/// <summary>Builds a <see cref="Repository"/> with its own git client and sync service.</summary>
public sealed class RepositoryFactory
{
    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;
    private readonly ILoggerFactory _loggers;

    public RepositoryFactory(IProcessRunner runner, ToolLocator tools, ILoggerFactory? loggers = null)
    {
        _runner = runner;
        _tools = tools;
        _loggers = loggers ?? NullLoggerFactory.Instance;
    }

    /// <param name="profile">Which repository this is.</param>
    /// <param name="path">The clone's folder.</param>
    /// <param name="rebuilder">Rebuilds generated files during a conflict.</param>
    /// <param name="check">The pre-commit check, required when the profile checks commits.</param>
    public Repository Open(RepositoryProfile profile, string path, IGeneratedFileRebuilder rebuilder, IPreCommitCheck? check)
    {
        var git = new GitClient(_runner, _tools) { RepositoryPath = path };
        var sync = new SyncService(git, profile, rebuilder, check, _loggers.CreateLogger<SyncService>());
        return new Repository(profile, path, git, sync);
    }
}
