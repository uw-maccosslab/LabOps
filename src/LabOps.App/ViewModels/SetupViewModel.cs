using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LabOps.App.Services;
using LabOps.Core.Claude;
using LabOps.Core.Engines;
using LabOps.Core.Infrastructure;
using LabOps.Core.Projects;
using LabOps.Core.Protocols;
using LabOps.Core.Quotes;
using LabOps.Core.Repositories;
using LabOps.Core.Setup;

namespace LabOps.App.ViewModels;

/// <summary>One row of the setup checklist.</summary>
public sealed partial class SetupRowViewModel(SetupItem item) : ObservableObject
{
    public SetupItem Item { get; } = item;

    public string Title => Item.Title;

    public string Detail => Item.Detail;

    public bool Done => Item.Done;

    public string? FixLabel => Item.FixLabel;

    public bool CanFix => Item.CanFix;

    public string? AlternateLabel => Item.AlternateLabel;

    public bool HasAlternate => Item.HasAlternate;
}

/// <summary>The first-run checklist: everything the app needs, each with a button to fix it.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly SetupService _setup;
    private readonly Workspace _workspace;
    private readonly QuoteEngine _quoteEngine;
    private readonly ProtocolEngine _protocolEngine;
    private readonly ClaudeSignInFlow _signIn;
    private CancellationTokenSource? _waiting;

    public SetupViewModel(
        SetupService setup, Workspace workspace, QuoteEngine quoteEngine, ProtocolEngine protocolEngine,
        ClaudeSignInFlow signIn)
    {
        _setup = setup;
        _workspace = workspace;
        _quoteEngine = quoteEngine;
        _protocolEngine = protocolEngine;
        _signIn = signIn;
        Claude = new ClaudeSettingsViewModel(workspace.Settings, workspace.SaveSettings, ClaudeChoices.ReadClaudeDefaults());
        Status = "Checking...";
    }

    public ObservableCollection<SetupRowViewModel> Rows { get; } = [];

    /// <summary>The model and effort Claude uses in LabOps, which this person chooses for themselves.</summary>
    public ClaudeSettingsViewModel Claude { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlternateCommand))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    public partial bool IsWorking { get; set; }

    /// <summary>
    /// Done waits for the step under way: closed partway, Setup left that step and the ones after
    /// it for the person to find again. The window's close box still closes it.
    /// </summary>
    public bool CanClose => !IsWorking;

    /// <summary>Waiting for the person to finish signing in to Claude in the browser.</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    [RelayCommand]
    private void StopWaiting() => _waiting?.Cancel();

    /// <summary>
    /// Signs in to Claude in the browser, with no console window; if that does not finish, offers
    /// the console window, where Claude Code can ask for a code to paste.
    /// </summary>
    private async Task SignInToClaudeAsync()
    {
        Status = "Finish in your browser: sign in with your lab Claude account and choose Authorize. If the page says the "
            + "window is too small, make it larger.";
        using var waiting = new CancellationTokenSource();
        _waiting = waiting;
        IsWaiting = true;
        try
        {
            await _signIn.SignInAsync(null, explanation =>
            {
                IsWaiting = false;
                Status = explanation;
            }, waiting.Token).ConfigureAwait(true);
        }
        finally
        {
            IsWaiting = false;
            _waiting = null;
        }
    }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool AllDone { get; set; }

    public async Task RefreshAsync()
    {
        IsWorking = true;
        try
        {
            foreach (var profile in RepositoryProfile.All)
            {
                if (_workspace.Get(profile) is null
                    && SetupService.FindExistingClone(profile, _workspace.ConfiguredPath(profile)) is { } found)
                {
                    _workspace.Open(profile, found);
                }
            }

            var items = await _setup.CheckAsync(_workspace.Projects?.Path, _workspace.Quotes?.Path, _workspace.Protocols?.Path)
                .ConfigureAwait(true);
            Rows.Clear();
            foreach (var item in items)
            {
                Rows.Add(new SetupRowViewModel(item));
            }

            AllDone = SetupService.AllDone(items);
            Status = SetupService.Summary(items);
            Checked = true;
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>
    /// The first check has finished. Until then the checklist is empty, and the Claude settings
    /// under it were all the window showed, for the seconds the sign-ins take to check.
    /// </summary>
    [ObservableProperty]
    public partial bool Checked { get; set; }

    /// <summary>Prepares an engine, showing each stage uv reports and how long it has taken.</summary>
    /// <remarks>
    /// The first time downloads Python and every package, which on a new computer can take
    /// minutes; a status line that never changed looked stuck, and Setup was closed before the
    /// steps after it. The clock moves even while uv is quiet in the middle of a download.
    /// </remarks>
    private async Task PrepareEngineAsync(string name, Func<IProgress<string>, Task> prepare)
    {
        var stage = "starting";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Show() => Status = $"Preparing the {name} ({clock.Elapsed:m\\:ss}): {stage}. The first time downloads Python "
            + "and its packages, which can take a few minutes. Leave this window open.";

        // Progress<T> reports on the thread it was made on, this one, so Status is set on the UI thread.
        var progress = new Progress<string>(reported =>
        {
            stage = reported;
            Show();
        });
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Show();
        Show();
        timer.Start();
        try
        {
            await prepare(progress).ConfigureAwait(true);
        }
        finally
        {
            timer.Stop();
        }
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task FixAsync(SetupRowViewModel row)
    {
        IsWorking = true;
        try
        {
            switch (row.Item.Step)
            {
                case SetupStep.ProjectsRepository or SetupStep.ProtocolsRepository or SetupStep.QuotesRepository:
                    await CloneAsync(row.Item.Profile!).ConfigureAwait(true);
                    break;
                case SetupStep.GitIdentity:
                    Status = "Setting your name on saved changes...";
                    foreach (var repository in _workspace.OpenRepositories())
                    {
                        await _setup.SetIdentityFromGitHubAsync(repository.Path).ConfigureAwait(true);
                    }

                    break;
                case SetupStep.ProtocolsEngine:
                    await PrepareEngineAsync("protocol engine", p => _protocolEngine.EnsureEnvironmentAsync(p)).ConfigureAwait(true);
                    break;
                case SetupStep.ClaudeSignIn:
                    await SignInToClaudeAsync().ConfigureAwait(true);
                    break;
                case SetupStep.QuotesEngine:
                    await PrepareEngineAsync("quote engine", p => _quoteEngine.EnsureEnvironmentAsync(p)).ConfigureAwait(true);
                    break;
                default:
                    if (_setup.ConsoleFix(row.Item.Step) is { } command)
                    {
                        Status = command.Explanation;
                        await Shell.RunInConsoleAsync(command).ConfigureAwait(true);
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or EngineException or IOException
            or System.ComponentModel.Win32Exception or Core.Processes.ToolMissingException)
        {
            System.Windows.MessageBox.Show(ex.Message, AppInfo.ProductName, System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsWorking = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private bool CanFix(SetupRowViewModel? row) => !IsWorking && row is { CanFix: true };

    /// <summary>
    /// The second button on a row: on a repository, use a copy the user already has. On the
    /// GitHub and Claude sign-ins and the git identity, it does the step again once it is done:
    /// sign in with a different GitHub account (gh adds it and makes it the one in use), sign in
    /// to Claude again (an expired session still reports as signed in), or take the name from the
    /// GitHub account now signed in.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAlternate))]
    private async Task AlternateAsync(SetupRowViewModel row)
    {
        if (row.Item.Step is SetupStep.GitHubSignIn or SetupStep.ClaudeSignIn or SetupStep.GitIdentity)
        {
            await FixAsync(row).ConfigureAwait(true);
            return;
        }

        if (row.Item.Profile is not { } profile)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = $"Choose your existing copy of the {profile.DisplayName} (the folder that contains {profile.CloneMarker.Replace('/', '\\')})",
            InitialDirectory = _workspace.Get(profile)?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsWorking = true;
        try
        {
            var path = await _setup.UseExistingCloneAsync(profile, dialog.FolderName).ConfigureAwait(true);
            _workspace.Open(profile, path);
        }
        catch (InvalidOperationException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, AppInfo.ProductName, System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsWorking = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private bool CanAlternate(SetupRowViewModel? row) => !IsWorking && row is { HasAlternate: true };

    private async Task CloneAsync(RepositoryProfile profile)
    {
        var dialog = new OpenFolderDialog
        {
            Title = $"Choose where to keep the {profile.DisplayName} (a new folder named {profile.DefaultFolderName} is created inside it)",
            InitialDirectory = Path.GetDirectoryName(profile.DefaultClonePath),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var target = Path.Combine(dialog.FolderName, profile.DefaultFolderName);
        Status = $"Downloading the {profile.DisplayName} to {target}...";
        var path = await _setup.CloneAsync(profile, target).ConfigureAwait(true);
        _workspace.Open(profile, path);
    }
}
