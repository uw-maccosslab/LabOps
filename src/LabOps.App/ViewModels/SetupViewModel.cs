using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LabOps.App.Services;
using LabOps.Core.Engines;
using LabOps.Core.Infrastructure;
using LabOps.Core.Projects;
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
    private readonly ProjectEngine _projectEngine;

    public SetupViewModel(SetupService setup, Workspace workspace, QuoteEngine quoteEngine, ProjectEngine projectEngine)
    {
        _setup = setup;
        _workspace = workspace;
        _quoteEngine = quoteEngine;
        _projectEngine = projectEngine;
        Status = "Checking...";
    }

    public ObservableCollection<SetupRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixCommand))]
    [NotifyCanExecuteChangedFor(nameof(AlternateCommand))]
    public partial bool IsWorking { get; set; }

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

            var items = await _setup.CheckAsync(_workspace.Projects?.Path, _workspace.Quotes?.Path).ConfigureAwait(true);
            Rows.Clear();
            foreach (var item in items)
            {
                Rows.Add(new SetupRowViewModel(item));
            }

            AllDone = SetupService.AllDone(items);
            Status = AllDone ? "Everything is ready." : "Work down the list; each button opens what it needs.";
        }
        finally
        {
            IsWorking = false;
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
                case SetupStep.ProjectsRepository or SetupStep.QuotesRepository:
                    await CloneAsync(row.Item.Profile!).ConfigureAwait(true);
                    break;
                case SetupStep.GitIdentity:
                    Status = "Setting your name on saved changes...";
                    foreach (var repository in new[] { _workspace.Projects, _workspace.Quotes }.OfType<Repository>())
                    {
                        await _setup.SetIdentityFromGitHubAsync(repository.Path).ConfigureAwait(true);
                    }

                    break;
                case SetupStep.ProjectsEngine:
                    Status = "Preparing the project engine. The first time downloads Python, which takes a minute or two...";
                    await _projectEngine.EnsureEnvironmentAsync().ConfigureAwait(true);
                    break;
                case SetupStep.QuotesEngine:
                    Status = "Preparing the quote engine. The first time downloads Python, which takes a minute or two...";
                    await _quoteEngine.EnsureEnvironmentAsync().ConfigureAwait(true);
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

    /// <summary>The second button on a repository row: use a copy the user already has.</summary>
    [RelayCommand(CanExecute = nameof(CanAlternate))]
    private async Task AlternateAsync(SetupRowViewModel row)
    {
        if (row.Item.Profile is not { } profile)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = $"Choose your existing copy of the {profile.DisplayName} (the folder that contains {profile.EngineScript.Replace('/', '\\')})",
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
