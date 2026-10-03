using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ServicesQuotes.App.Services;
using ServicesQuotes.Core.Infrastructure;
using ServicesQuotes.Core.Quotes;
using ServicesQuotes.Core.Setup;

namespace ServicesQuotes.App.ViewModels;

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
    private readonly QuoteEngine _engine;

    public SetupViewModel(SetupService setup, Workspace workspace, QuoteEngine engine)
    {
        _setup = setup;
        _workspace = workspace;
        _engine = engine;
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
            var repo = _workspace.RepositoryPath ?? SetupService.FindExistingClone(_workspace.Settings.RepositoryPath);
            if (repo is not null && _workspace.RepositoryPath != repo)
            {
                _workspace.UseRepository(repo);
            }

            var items = await _setup.CheckAsync(repo).ConfigureAwait(true);
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
                case SetupStep.Repository:
                    await CloneAsync().ConfigureAwait(true);
                    break;
                case SetupStep.GitIdentity:
                    Status = "Setting your name on saved changes...";
                    await _setup.SetIdentityFromGitHubAsync(_workspace.RepositoryPath!).ConfigureAwait(true);
                    break;
                case SetupStep.PythonEnvironment:
                    Status = "Preparing the quote engine. The first time downloads Python, which takes a minute or two...";
                    await _engine.EnsureEnvironmentAsync().ConfigureAwait(true);
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
        catch (Exception ex) when (ex is InvalidOperationException or QuoteEngineException or IOException
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

    /// <summary>The second button on a row; today only "use a copy I already have".</summary>
    [RelayCommand(CanExecute = nameof(CanAlternate))]
    private async Task AlternateAsync(SetupRowViewModel row)
    {
        if (row.Item.Step != SetupStep.Repository)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose your existing copy of the quotes (the folder that contains scripts\\quote.py)",
            InitialDirectory = _workspace.RepositoryPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsWorking = true;
        try
        {
            var path = await _setup.UseExistingCloneAsync(dialog.FolderName).ConfigureAwait(true);
            _workspace.UseRepository(path);
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

    private async Task CloneAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to keep the quotes (a new folder named services-quotes is created inside it)",
            InitialDirectory = Path.GetDirectoryName(AppPaths.DefaultClonePath),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var target = Path.Combine(dialog.FolderName, "services-quotes");
        Status = $"Downloading the quotes to {target}...";
        var path = await _setup.CloneAsync(target).ConfigureAwait(true);
        _workspace.UseRepository(path);
    }
}
