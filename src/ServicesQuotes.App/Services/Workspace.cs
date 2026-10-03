using ServicesQuotes.Core.Claude;
using ServicesQuotes.Core.GitHub;
using ServicesQuotes.Core.Infrastructure;
using ServicesQuotes.Core.Quotes;
using ServicesQuotes.Core.Sync;

namespace ServicesQuotes.App.Services;

/// <summary>
/// The state the whole app shares: which clone it works on, who is signed in, and the settings
/// that come from the repository.
/// </summary>
public sealed class Workspace : IAsyncDisposable, IDisposable
{
    private readonly SettingsStore _store;
    private readonly QuoteEngine _engine;
    private readonly GitClient _git;
    private readonly AppTools _tools;
    private AppToolServer? _toolServer;

    public Workspace(AppSettings settings, SettingsStore store, QuoteEngine engine, GitClient git, AppTools tools)
    {
        Settings = settings;
        _store = store;
        _engine = engine;
        _git = git;
        _tools = tools;
    }

    public AppSettings Settings { get; }

    public string? RepositoryPath { get; private set; }

    public RepoConfig Config { get; private set; } = RepoConfig.Default;

    public GitHubUser? User { get; set; }

    /// <summary>Whether this user sees the Send button (config/app.yaml approvers).</summary>
    public bool CanSend => Config.IsApprover(User?.Login);

    /// <summary>True when the quotes repository needs a newer app than this one.</summary>
    public bool AppTooOld => Config.RequiresNewerThan(AppInfo.Version);

    public void UseRepository(string path)
    {
        RepositoryPath = path;
        _engine.RepositoryPath = path;
        _git.RepositoryPath = path;
        Settings.RepositoryPath = path;
        SaveSettings();
        ReloadConfig();
    }

    public void ReloadConfig()
    {
        if (RepositoryPath is not null)
        {
            Config = RepoConfig.Load(RepositoryPath);
        }
    }

    public void SaveSettings() => _store.Save(Settings);

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
