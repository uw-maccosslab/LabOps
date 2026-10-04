using LabOps.Core.Claude;
using LabOps.Core.GitHub;
using LabOps.Core.Infrastructure;
using LabOps.Core.Projects;
using LabOps.Core.Quotes;
using LabOps.Core.Repositories;
using LabOps.Core.Sync;

namespace LabOps.App.Services;

/// <summary>
/// The state the whole app shares: which clones it works on, who is signed in, and the settings
/// that come from each repository.
/// </summary>
/// <remarks>
/// The projects repository is for the whole lab; the quotes repository only for the people who
/// prepare quotes. Either can be open without the other, each with its own git client and sync.
/// </remarks>
public sealed class Workspace : IAsyncDisposable, IDisposable
{
    private readonly SettingsStore _store;
    private readonly RepositoryFactory _factory;
    private readonly QuoteEngine _quoteEngine;
    private readonly ProjectEngine _projectEngine;
    private readonly AppTools _tools;
    private AppToolServer? _toolServer;

    public Workspace(
        AppSettings settings, SettingsStore store, RepositoryFactory factory, QuoteEngine quoteEngine,
        ProjectEngine projectEngine, AppTools tools)
    {
        Settings = settings;
        _store = store;
        _factory = factory;
        _quoteEngine = quoteEngine;
        _projectEngine = projectEngine;
        _tools = tools;
    }

    /// <summary>Raised when a repository is opened (or reopened at a different folder).</summary>
    public event Action<Repository>? RepositoryOpened;

    public AppSettings Settings { get; }

    public Repository? Projects { get; private set; }

    public Repository? Quotes { get; private set; }

    public GitHubUser? User { get; set; }

    /// <summary>Whether this user sees the Send button (config/app.yaml approvers in the quotes repository).</summary>
    public bool CanSend => Quotes?.Config.IsApprover(User?.Login) == true;

    public Repository? Get(RepositoryProfile profile) => profile.Kind == RepositoryKind.Projects ? Projects : Quotes;

    /// <summary>The folder remembered for a repository, whether or not it is open.</summary>
    public string? ConfiguredPath(RepositoryProfile profile) =>
        profile.Kind == RepositoryKind.Projects ? Settings.ProjectsRepositoryPath : Settings.RepositoryPath;

    /// <summary>Opens a clone, remembers it, and points its engine at it.</summary>
    public Repository Open(RepositoryProfile profile, string path)
    {
        if (Get(profile) is { } open && string.Equals(open.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return open;
        }

        Repository repository;
        if (profile.Kind == RepositoryKind.Projects)
        {
            _projectEngine.RepositoryPath = path;
            repository = _factory.Open(profile, path, new NoGeneratedFiles(), _projectEngine);
            Projects = repository;
            Settings.ProjectsRepositoryPath = path;
        }
        else
        {
            _quoteEngine.RepositoryPath = path;
            repository = _factory.Open(profile, path, new EngineRebuilder(_quoteEngine), check: null);
            Quotes = repository;
            Settings.RepositoryPath = path;
        }

        SaveSettings();
        RepositoryOpened?.Invoke(repository);
        return repository;
    }

    public void SaveSettings() => _store.Save(Settings);

    /// <summary>The key a Claude conversation about an item is remembered under.</summary>
    public static string SessionKey(RepositoryProfile profile, string item) =>
        profile.Kind == RepositoryKind.Quotes ? item : $"{profile.Id}:{item}";

    /// <summary>The MCP server for Claude's app tools, started on first use.</summary>
    public async Task<AppToolServer> ToolServerAsync() =>
        _toolServer ??= await AppToolServer.StartAsync(_tools).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        var server = Interlocked.Exchange(ref _toolServer, null);
        if (server is not null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronous too, because the service container is disposed with a plain using in
    /// Program.Main, and a container holding an async-only service throws from Dispose.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
