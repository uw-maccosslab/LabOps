using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LabOps.App.Services;
using LabOps.App.Views;
using LabOps.Core.Engines;
using LabOps.Core.GitHub;
using LabOps.Core.Infrastructure;
using LabOps.Core.Processes;
using LabOps.Core.Quotes;
using LabOps.Core.Repositories;
using LabOps.Core.Setup;
using LabOps.Core.Sync;

namespace LabOps.App.ViewModels;

/// <summary>The two halves of the app.</summary>
public enum AppArea
{
    Projects,
    Quotes,
}

/// <summary>
/// The main window: which area is showing, the Quotes area (the quote list, the selected quote
/// and its actions), and the status bar for both repositories. The Projects area has its own
/// view model, <see cref="ProjectsViewModel"/>.
/// </summary>
/// <remarks>
/// Every action that changes a quote ends with a save and sync, so the user never has to think
/// about git: what they see is what everyone else sees a moment later.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private readonly Workspace _workspace;
    private readonly QuoteEngine _engine;
    private readonly GitHubCli _gh;
    private readonly SetupService _setup;
    private readonly QuoteSearch _search;
    private readonly UpdateService _updates;
    private readonly WorkTracker _work;
    private readonly ILogger<MainViewModel> _log;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<RepositoryKind, SyncStatus> _syncStatus = [];
    private readonly Dictionary<RepositoryKind, CheckRun?> _checks = [];
    private DispatcherTimer? _timer;
    private List<QuoteSummary> _all = [];

    public MainViewModel(
        Workspace workspace, QuoteEngine engine, GitHubCli gh, SetupService setup, QuoteSearch search,
        UpdateService updates, WorkTracker work, ChatViewModel chat, ProjectsViewModel projects, ILogger<MainViewModel> log)
    {
        _workspace = workspace;
        _engine = engine;
        _gh = gh;
        _setup = setup;
        _search = search;
        _updates = updates;
        _work = work;
        _log = log;
        Chat = chat;
        Projects = projects;
        _dispatcher = Dispatcher.CurrentDispatcher;

        Query = "";
        Filter = QuoteFilter.Current;
        Area = AppArea.Projects;
        PreviewHtml = MarkdownRenderer.ToHtml(null, "Select a quote to see it here.");
        SyncText = "Starting...";

        Chat.TurnCompleted += OnClaudeTurnCompletedAsync;
        _workspace.RepositoryOpened += OnRepositoryOpened;
        _updates.StatusChanged += s => _dispatcher.InvokeAsync(() => ShowUpdate(s));
        _work.PropertyChanged += OnWorkChanged;
    }

    public ChatViewModel Chat { get; }

    public ProjectsViewModel Projects { get; }

    public ObservableCollection<QuoteSummary> Quotes { get; } = [];

    public IReadOnlyList<QuoteFilter> Filters { get; } = Enum.GetValues<QuoteFilter>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectsArea), nameof(IsQuotesArea))]
    public partial AppArea Area { get; set; }

    public bool IsProjectsArea => Area == AppArea.Projects;

    public bool IsQuotesArea => Area == AppArea.Quotes;

    /// <summary>The Quotes area exists only for people with a copy of the quotes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowQuotesCommand))]
    public partial bool HasQuotes { get; set; }

    [ObservableProperty] public partial string Query { get; set; }

    [ObservableProperty] public partial QuoteFilter Filter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedHeadline), nameof(SelectedDetail), nameof(SelectedIssues),
        nameof(CanSendSelected), nameof(IsSentSelected), nameof(IsAcceptedSelected))]
    [NotifyCanExecuteChangedFor(nameof(AskClaudeCommand), nameof(SendCommand), nameof(MarkAcceptedCommand),
        nameof(MarkInvoicedCommand), nameof(MarkDeclinedCommand), nameof(ReviseCommand), nameof(DraftPdfCommand),
        nameof(OpenFolderCommand), nameof(OpenPdfCommand), nameof(OpenSpreadsheetCommand), nameof(OpenOnGitHubCommand),
        nameof(StatementOfWorkCommand))]
    public partial QuoteSummary? Selected { get; set; }

    [ObservableProperty] public partial bool ShowCalculation { get; set; }

    [ObservableProperty] public partial string PreviewHtml { get; set; }

    [ObservableProperty] public partial string SyncText { get; set; }

    [ObservableProperty] public partial bool SyncProblem { get; set; }

    [ObservableProperty] public partial string? CheckText { get; set; }

    [ObservableProperty] public partial bool CheckFailed { get; set; }

    [ObservableProperty] public partial string? CheckUrl { get; set; }

    [ObservableProperty] public partial string? UpdateText { get; set; }

    [ObservableProperty] public partial bool UpdateReady { get; set; }

    [ObservableProperty] public partial string? Banner { get; set; }

    [ObservableProperty] public partial string? UserText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewQuoteCommand), nameof(AskClaudeCommand), nameof(SendCommand),
        nameof(MarkAcceptedCommand), nameof(MarkInvoicedCommand), nameof(MarkDeclinedCommand), nameof(ReviseCommand),
        nameof(DraftPdfCommand), nameof(SyncNowCommand), nameof(StatementOfWorkCommand))]
    public partial bool IsWorking { get; set; }

    [ObservableProperty] public partial string? WorkingText { get; set; }

    [ObservableProperty] public partial string? Notice { get; set; }

    public bool HasSelection => Selected is not null;

    public bool CanSendSelected => _workspace.CanSend && Selected is { IsDraft: true };

    public bool IsSentSelected => Selected?.Status == "sent";

    public bool IsAcceptedSelected => Selected?.Status == "accepted";

    public string SelectedHeadline => Selected is null ? "" : $"{Selected.QuoteNumber}  ({Selected.Status})";

    public string SelectedDetail
    {
        get
        {
            if (Selected is null)
            {
                return "";
            }

            var q = Selected;
            var lines = new List<string>
            {
                $"{q.Project}",
                $"{q.Client}{(string.IsNullOrWhiteSpace(q.Institution) ? "" : $", {q.Institution}")}",
                $"{q.Total.ToString("C2", Us)} total, {q.PerSample.ToString("C2", Us)} per sample, {q.StudySamples:0.##} study samples",
            };
            var dates = new[] { ("Issued", q.Issued), ("Valid until", q.ValidUntil), ("Sent", q.Sent), ("Accepted", q.Accepted),
                                ("Invoiced", q.Invoiced), ("Declined", q.Declined), ("PO", q.Po),
                                ("Last changed", q.Modified?.ToLocalTime().ToString("yyyy-MM-dd", Us)) }
                .Where(d => !string.IsNullOrWhiteSpace(d.Item2)).Select(d => $"{d.Item1} {d.Item2}");
            lines.Add(string.Join("   ", dates));
            return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }
    }

    public IReadOnlyList<string> SelectedIssues =>
        Selected is null ? [] : [.. Selected.Issues.Select(i => $"{i.Level}: {i.Message}"), .. Selected.Error is null ? [] : new[] { Selected.Error }];

    private Repository? QuotesRepository => _workspace.Quotes;

    private string? QuotesPath => _workspace.Quotes?.Path;

    private IEnumerable<Repository> OpenRepositories() => new[] { _workspace.Projects, _workspace.Quotes }.OfType<Repository>();

    // -- startup -----------------------------------------------------------------------------

    /// <summary>Runs setup if needed, then loads and syncs. Called once the window is shown.</summary>
    public async Task InitializeAsync(Window owner)
    {
        LegacyApp.OfferRemoval(owner, App.Services.GetRequiredService<AppSettings>(), App.Services.GetRequiredService<SettingsStore>());
        foreach (var profile in RepositoryProfile.All)
        {
            if (SetupService.FindExistingClone(profile, _workspace.ConfiguredPath(profile)) is { } path)
            {
                _workspace.Open(profile, path);
            }
        }

        var items = await _setup.CheckAsync(_workspace.Projects?.Path, _workspace.Quotes?.Path).ConfigureAwait(true);
        if (!SetupService.AllDone(items))
        {
            SetupWindow.Show(owner, App.Services);
        }

        if (!OpenRepositories().Any())
        {
            Banner = "Setup is not finished. Open Setup to download the lab projects.";
            SyncText = "Not set up";
            return;
        }

        await StartWorkingAsync().ConfigureAwait(true);
    }

    private void OnRepositoryOpened(Repository repository)
    {
        var kind = repository.Profile.Kind;
        repository.Sync.StatusChanged += s => _dispatcher.InvokeAsync(() => ShowSync(kind, s));
        repository.Sync.RepositoryUpdated += () => _dispatcher.InvokeAsync(async () =>
        {
            if (kind == RepositoryKind.Quotes)
            {
                await ReloadQuotesAsync().ConfigureAwait(true);
            }
            else
            {
                await Projects.ReloadAsync().ConfigureAwait(true);
            }
        });

        HasQuotes = _workspace.Quotes is not null;
        if (_workspace.Projects is null && HasQuotes)
        {
            Area = AppArea.Quotes;
        }
    }

    private async Task StartWorkingAsync()
    {
        foreach (var repository in OpenRepositories())
        {
            repository.ReloadConfig();
            // Before the first sync, so it fetches from the repository's current name.
            if (await repository.UpdateRenamedRemoteAsync().ConfigureAwait(true) is { } renamed)
            {
                _log.LogInformation("The {Repository} clone's origin now points at {Url}.", repository.Profile.Id, renamed);
            }
        }

        _workspace.User = await _gh.GetUserAsync().ConfigureAwait(true);
        UserText = _workspace.User is null ? "Not signed in to GitHub" : $"Signed in as {_workspace.User.Login}";
        ShowBanner();

        await _work.RunAsync("Getting the latest changes...", async () =>
        {
            foreach (var repository in OpenRepositories())
            {
                await repository.Sync.SyncAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);

        await Projects.ReloadAsync().ConfigureAwait(true);
        await ReloadQuotesAsync().ConfigureAwait(true);
        if (Area == AppArea.Quotes && !HasQuotes)
        {
            Area = AppArea.Projects;
        }

        _timer ??= CreateTimer();
        _ = RefreshChecksAsync();
        _ = _updates.CheckAsync();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Math.Max(1, _workspace.Settings.FetchIntervalMinutes)) };
        timer.Tick += async (_, _) =>
        {
            CheckForUpdatesIfDue();
            await BackgroundSyncAsync().ConfigureAwait(true);
        };
        timer.Start();
        return timer;
    }

    /// <summary>
    /// Every few hours, look for a new release. Riding on the sync timer costs one comparison per
    /// tick; the download runs in the background, and installing still waits for the user.
    /// Checked even while the user is busy, because nothing it does interrupts them.
    /// </summary>
    private void CheckForUpdatesIfDue()
    {
        if (UpdateService.IsCheckDue(_updates.Status, _updates.LastChecked, DateTimeOffset.Now, UpdateService.CheckInterval))
        {
            _ = _updates.CheckAsync();
        }
    }

    /// <summary>Brings in others' work quietly, unless the user is in the middle of something.</summary>
    private async Task BackgroundSyncAsync()
    {
        foreach (var repository in OpenRepositories().ToList())
        {
            if (IsWorking || Chat.IsBusy)
            {
                return;
            }

            var status = await repository.Sync.RefreshAsync(fetch: true).ConfigureAwait(true);
            if (status.Behind > 0 && status.Changes.Count == 0)
            {
                await repository.Sync.SyncAsync().ConfigureAwait(true);
            }
        }

        await RefreshChecksAsync().ConfigureAwait(true);
    }

    private void ShowBanner()
    {
        var old = OpenRepositories().Where(r => r.AppTooOld).ToList();
        Banner = old.Count == 0
            ? null
            : $"This version of {AppInfo.ProductName} is too old for the {string.Join(" and ", old.Select(r => r.Profile.DisplayName))} "
              + $"(it needs {old.Max(r => r.Config.MinAppVersion)}). Update the app to make changes.";
        OnPropertyChanged(nameof(CanSendSelected));
        Projects.RefreshCommands();
    }

    private void OnWorkChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsWorking = _work.IsWorking;
        WorkingText = _work.WorkingText;
        Notice = _work.Notice;
    }

    /// <summary>
    /// Ends Claude and stops the tool server, while the UI thread still runs. A share still
    /// running gets a few seconds to finish; one that does not leaves its commit on this computer,
    /// and the next start shares it.
    /// </summary>
    public async Task ShutdownAsync()
    {
        _timer?.Stop();
        var sharing = Task.WhenAll(OpenRepositories().Select(r => r.Sync.WhenIdleAsync()));
        await Task.WhenAny(sharing, Task.Delay(TimeSpan.FromSeconds(6))).ConfigureAwait(true);
        await Chat.EndSessionAsync().ConfigureAwait(true);
        await _workspace.DisposeAsync().ConfigureAwait(true);
    }

    // -- areas -------------------------------------------------------------------------------

    [RelayCommand]
    private void ShowProjects() => Area = AppArea.Projects;

    [RelayCommand(CanExecute = nameof(HasQuotes))]
    private void ShowQuotes() => Area = AppArea.Quotes;

    // -- list --------------------------------------------------------------------------------

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnFilterChanged(QuoteFilter value) => ApplyFilter();

    partial void OnSelectedChanged(QuoteSummary? value) => ShowPreview();

    partial void OnShowCalculationChanged(bool value) => ShowPreview();

    /// <summary>Re-reads both areas from this computer, without contacting GitHub.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await Projects.ReloadAsync().ConfigureAwait(true);
        await ReloadQuotesAsync().ConfigureAwait(true);
    }

    private async Task ReloadQuotesAsync(string? select = null)
    {
        if (QuotesRepository is not { } repository)
        {
            return;
        }

        var keep = select ?? Selected?.QuoteNumber;
        try
        {
            var quotes = await _engine.ListAsync().ConfigureAwait(true);
            var modified = await ItemHistory.LastModifiedAsync(repository).ConfigureAwait(true);
            _all = [.. quotes.Select(q => q with { Modified = modified.TryGetValue(q.Folder, out var t) ? t : null })];
            var path = repository.Path;
            var snapshot = _all;
            await Task.Run(() => _search.Index(path, snapshot)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is EngineException or ToolMissingException or GitException)
        {
            Banner = $"The quotes could not be loaded: {ex.Message}";
            return;
        }

        repository.ReloadConfig();
        ShowBanner();
        ApplyFilter();

        if (keep is not null && Quotes.FirstOrDefault(q => q.QuoteNumber == keep) is { } match)
        {
            Selected = match;
        }
        else if (keep is not null && _all.FirstOrDefault(q => q.QuoteNumber == keep) is { } hidden)
        {
            // The quote is filtered out (for example it just became sent): show everything.
            Filter = QuoteFilter.All;
            Selected = Quotes.FirstOrDefault(q => q.QuoteNumber == hidden.QuoteNumber);
        }

        ShowPreview();
    }

    private void ApplyFilter()
    {
        var selected = Selected?.QuoteNumber;
        Quotes.Clear();
        foreach (var quote in _search.Filter(_all, Query, Filter))
        {
            Quotes.Add(quote);
        }

        Selected = Quotes.FirstOrDefault(q => q.QuoteNumber == selected);
    }

    private void ShowPreview()
    {
        if (Selected is null || QuotesPath is null)
        {
            PreviewHtml = MarkdownRenderer.ToHtml(null, "Select a quote to see it here.");
            return;
        }

        var folder = Selected.FolderPath(QuotesPath);
        var file = ShowCalculation || Selected.IsHistorical ? "calculation.md" : "quote.md";
        var path = Path.Combine(folder, file);
        var text = File.Exists(path) ? File.ReadAllText(path) : null;
        PreviewHtml = MarkdownRenderer.ToHtml(text, $"{file} has not been generated yet.");
    }

    // -- actions -----------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task NewQuoteAsync()
    {
        var form = NewQuoteWindow.Ask(Application.Current.MainWindow);
        if (form is null)
        {
            return;
        }

        await PullFirstAsync().ConfigureAwait(true);
        await Chat.StartAsync(QuotesRepository!, form.Title, item: null, form.BuildPrompt(), isNew: true).ConfigureAwait(true);
    }

    private bool CanEdit() => !IsWorking && QuotesRepository is { AppTooOld: false };

    [RelayCommand(CanExecute = nameof(CanAskClaude))]
    private async Task AskClaudeAsync()
    {
        var quote = Selected!;
        if (quote.IsFrozen)
        {
            var answer = MessageBox.Show(
                $"{quote.QuoteNumber} has been {quote.Status}, so it cannot be changed. Make a revision ({quote.QuoteNumber}-R...) and change that instead?",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            quote = await ReviseCoreAsync(quote).ConfigureAwait(true);
            if (quote is null)
            {
                return;
            }
        }

        var resume = Chat.HasEarlierConversation(RepositoryProfile.Quotes, quote.QuoteNumber) && MessageBox.Show(
            $"Continue your earlier conversation with Claude about {quote.QuoteNumber}? Choose No to start fresh.",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        await PullFirstAsync().ConfigureAwait(true);
        var prompt = $"Use the revise-quote skill on quote {quote.QuoteNumber} ({quote.Folder}). "
            + "Ask me what I want to change with the ask_user tool, then make the change.";
        await Chat.StartAsync(QuotesRepository!, quote.QuoteNumber, quote.QuoteNumber, prompt, isNew: false, resume).ConfigureAwait(true);
    }

    private bool CanAskClaude() => CanEdit() && Selected is { IsHistorical: false, Error: null };

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var quote = Selected!;
        if (!ConfirmWindow.Ask(Application.Current.MainWindow, "Send this quote?",
                $"{quote.QuoteNumber}\n{quote.Client}\n{quote.Project}\n\n{quote.Total.ToString("C2", Us)} total, "
                + $"{quote.PerSample.ToString("C2", Us)} per sample\n\n"
                + "This writes the final PDF and spreadsheet, dates the quote today, and marks it sent. "
                + "It cannot be undone; later changes need a revision. You then email the PDF to the client yourself.",
                "Send"))
        {
            return;
        }

        SendResult? result = null;
        await _work.RunAsync($"Sending {quote.QuoteNumber}...", async () =>
        {
            await PullFirstAsync().ConfigureAwait(true);
            result = await _engine.SendAsync(quote.QuoteNumber, keepIssued: false).ConfigureAwait(true);
            await SaveAsync([quote.Folder], $"{quote.QuoteNumber}: sent").ConfigureAwait(true);
        }).ConfigureAwait(true);

        await ReloadQuotesAsync(quote.QuoteNumber).ConfigureAwait(true);
        if (result is not null)
        {
            var warnings = result.Warnings.Count == 0 ? "" : "\n\nNote: " + string.Join("; ", result.Warnings);
            if (MessageBox.Show($"{quote.QuoteNumber} is marked sent.{warnings}\n\nShow the PDF in its folder so you can attach it to your email?",
                    AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Shell.Reveal(result.PdfPath);
            }
        }
    }

    private bool CanSend() => CanEdit() && CanSendSelected && Selected is { HasErrors: false };

    [RelayCommand(CanExecute = nameof(CanMarkAccepted))]
    private async Task MarkAcceptedAsync()
    {
        var quote = Selected!;
        var po = InputWindow.Ask(Application.Current.MainWindow, "Purchase order received",
            $"Purchase order number for {quote.QuoteNumber} (leave empty if there is none yet):");
        if (po is null)
        {
            return;
        }

        await ChangeStatusAsync(quote, QuoteStatusChange.Accepted, po).ConfigureAwait(true);
    }

    private bool CanMarkAccepted() => CanEdit() && Selected?.Status == "sent";

    [RelayCommand(CanExecute = nameof(CanMarkInvoiced))]
    private async Task MarkInvoicedAsync() => await ChangeStatusAsync(Selected!, QuoteStatusChange.Invoiced, null).ConfigureAwait(true);

    private bool CanMarkInvoiced() => CanEdit() && Selected?.Status == "accepted";

    [RelayCommand(CanExecute = nameof(CanMarkDeclined))]
    private async Task MarkDeclinedAsync()
    {
        if (MessageBox.Show($"Mark {Selected!.QuoteNumber} as declined by the client?", AppInfo.ProductName,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            await ChangeStatusAsync(Selected!, QuoteStatusChange.Declined, null).ConfigureAwait(true);
        }
    }

    private bool CanMarkDeclined() => CanEdit() && Selected?.Status is "draft" or "sent" or "accepted";

    private async Task ChangeStatusAsync(QuoteSummary quote, QuoteStatusChange change, string? po)
    {
        var word = change.ToString().ToLowerInvariant();
        await _work.RunAsync($"Marking {quote.QuoteNumber} {word}...", async () =>
        {
            await PullFirstAsync().ConfigureAwait(true);
            await _engine.SetStatusAsync(quote.QuoteNumber, change, po).ConfigureAwait(true);
            await SaveAsync([quote.Folder], $"{quote.QuoteNumber}: {word}{(string.IsNullOrWhiteSpace(po) ? "" : $" (PO {po.Trim()})")}").ConfigureAwait(true);
        }).ConfigureAwait(true);
        await ReloadQuotesAsync(quote.QuoteNumber).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRevise))]
    private async Task ReviseAsync()
    {
        var revised = await ReviseCoreAsync(Selected!).ConfigureAwait(true);
        if (revised is not null)
        {
            Selected = Quotes.FirstOrDefault(q => q.QuoteNumber == revised.QuoteNumber) ?? Selected;
        }
    }

    private bool CanRevise() => CanEdit() && Selected is { IsFrozen: true };

    private async Task<QuoteSummary?> ReviseCoreAsync(QuoteSummary quote)
    {
        QuoteSummary? revised = null;
        await _work.RunAsync($"Making a revision of {quote.QuoteNumber}...", async () =>
        {
            await PullFirstAsync().ConfigureAwait(true);
            revised = await _engine.ReviseAsync(quote.QuoteNumber).ConfigureAwait(true);
            await SaveAsync([quote.Folder, revised.Folder], $"{revised.QuoteNumber}: revision of {quote.QuoteNumber}").ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (revised is not null)
        {
            Filter = QuoteFilter.Current;
            await ReloadQuotesAsync(revised.QuoteNumber).ConfigureAwait(true);
        }

        return revised;
    }

    [RelayCommand(CanExecute = nameof(CanDraftPdf))]
    private async Task DraftPdfAsync()
    {
        var quote = Selected!;
        string? path = null;
        await _work.RunAsync("Making a draft PDF...", async () =>
            path = await _engine.DraftPdfAsync(quote.QuoteNumber).ConfigureAwait(true)).ConfigureAwait(true);
        if (path is not null)
        {
            Shell.Open(path);
        }

        // The new draft PDF exists now; Open PDF can use it.
        OpenPdfCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Drafts only: a sent quote's PDF is final and is opened with Open PDF.</summary>
    private bool CanDraftPdf() => !IsWorking && Selected is { IsDraft: true, Error: null };

    /// <summary>
    /// Makes the statement of work (Exhibit A) priced at the sample counts the user confirms,
    /// saves and shares it with the quote, and opens it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMakeStatementOfWork))]
    private async Task StatementOfWorkAsync()
    {
        var quote = Selected!;
        var replaces = QuotesPath is not null && quote.ExistingSow(QuotesPath) is not null
            ? " It replaces the statement of work already in the quote folder." : "";
        var answer = InputWindow.Ask(Application.Current.MainWindow, "Statement of work",
            $"Price {quote.QuoteNumber} at these sample counts (up to six, separated by commas). The quote's own count, "
            + $"{quote.StudySamples:0.##}, prices exactly as the quote; other counts keep its scope.{replaces}",
            string.Join(", ", quote.DefaultSowCounts()), "Make it");
        if (answer is null)
        {
            return;
        }

        var counts = ParseCounts(answer);
        if (counts is null)
        {
            MessageBox.Show("Give whole numbers of samples separated by commas, for example 20, 40, 60, 80.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? path = null;
        await _work.RunAsync($"Making the statement of work for {quote.QuoteNumber}...", async () =>
        {
            await PullFirstAsync().ConfigureAwait(true);
            path = await _engine.StatementOfWorkAsync(quote.QuoteNumber, counts).ConfigureAwait(true);
            await SaveAsync([quote.Folder], $"{quote.QuoteNumber}: statement of work").ConfigureAwait(true);
        }).ConfigureAwait(true);

        await ReloadQuotesAsync(quote.QuoteNumber).ConfigureAwait(true);
        if (path is not null)
        {
            Shell.Open(path);
        }
    }

    /// <summary>Not for historical estimates, which are records rather than quotes.</summary>
    private bool CanMakeStatementOfWork() => CanEdit() && Selected is { IsHistorical: false, HasErrors: false };

    /// <summary>"20, 40 60" as [20, 40, 60]; null when anything is not a positive whole number.</summary>
    internal static IReadOnlyList<int>? ParseCounts(string text)
    {
        var parts = text.Split([',', ' ', ';', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var counts = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 1)
            {
                return null;
            }

            counts.Add(n);
        }

        return counts.Count == 0 ? null : [.. counts.Distinct().Order()];
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder() => Shell.Open(Selected!.FolderPath(QuotesPath!));

    /// <summary>The sent PDF, or for a draft the latest draft PDF.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPdf))]
    private void OpenPdf() => Shell.Open(Selected!.ExistingPdf(QuotesPath!)!);

    private bool CanOpenPdf() => QuotesPath is not null && Selected?.ExistingPdf(QuotesPath) is not null;

    [RelayCommand(CanExecute = nameof(CanOpenSpreadsheet))]
    private void OpenSpreadsheet() => Shell.Open(Selected!.ExistingSpreadsheet(QuotesPath!)!);

    private bool CanOpenSpreadsheet() => QuotesPath is not null && Selected?.ExistingSpreadsheet(QuotesPath) is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenOnGitHub() =>
        Shell.Open($"{RepositoryProfile.Quotes.Url}/tree/main/{Uri.EscapeDataString(Selected!.Folder).Replace("%2F", "/", StringComparison.Ordinal)}");

    /// <summary>Syncs every open repository: brings in others' work and shares this computer's.</summary>
    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        await _work.RunAsync("Syncing with GitHub...", async () =>
        {
            foreach (var repository in OpenRepositories().ToList())
            {
                var result = await repository.Sync.SyncAsync().ConfigureAwait(true);
                if (repository.Profile.Kind == RepositoryKind.Quotes)
                {
                    await HandleSaveResultAsync(result).ConfigureAwait(true);
                }
                else
                {
                    await Projects.HandleSaveResultAsync(result).ConfigureAwait(true);
                }
            }
        }).ConfigureAwait(true);
        await RefreshChecksAsync().ConfigureAwait(true);
    }

    private bool CanSyncNow() => !IsWorking && OpenRepositories().Any();

    [RelayCommand]
    private async Task OpenSetupAsync()
    {
        SetupWindow.Show(Application.Current.MainWindow, App.Services);
        if (OpenRepositories().Any())
        {
            await StartWorkingAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ApplyUpdate()
    {
        if (Chat.IsBusy || IsWorking)
        {
            MessageBox.Show("Wait until the current work finishes, then restart to update.", AppInfo.ProductName);
            return;
        }

        _updates.ApplyAndRestart();
    }

    [RelayCommand]
    private void OpenCheck()
    {
        if (CheckUrl is not null)
        {
            Shell.Open(CheckUrl);
        }
    }

    [RelayCommand]
    private void OpenLogs() => Shell.Open(App.Services.GetService(typeof(AppPaths)) is AppPaths p ? p.LogDirectory : ".");

    // -- Claude ------------------------------------------------------------------------------

    /// <summary>Saves and syncs what Claude changed when it finishes a turn, in the repository it worked in.</summary>
    private async Task OnClaudeTurnCompletedAsync(ChatTurnResult turn)
    {
        if (turn.Repository.Profile.Kind == RepositoryKind.Projects)
        {
            await Projects.OnClaudeTurnCompletedAsync(turn).ConfigureAwait(true);
            return;
        }

        var repository = turn.Repository;
        var root = repository.Profile.RootFolder + "/";
        var number = turn.Report?.QuoteNumber ?? turn.Item;
        var status = await repository.Sync.RefreshAsync(fetch: false).ConfigureAwait(true);
        var changed = status.Changes.Where(c => c.StartsWith(root, StringComparison.Ordinal)).ToList();
        var outside = status.Changes.Where(c => !c.StartsWith(root, StringComparison.Ordinal)).ToList();

        // Files outside the quotes folder (the engine, templates, rates) change how every quote is
        // built. A quote built with an engine change that stays on this computer fails GitHub's
        // check, so an approver is asked whether to share them too; anyone else keeps them local.
        var shareOutside = false;
        if (outside.Count > 0)
        {
            var list = string.Join("\n", outside.Select(f => "- " + f));
            shareOutside = _workspace.CanSend && MessageBox.Show(
                $"Claude also changed files outside the quotes folder:\n\n{list}\n\nThese change how quotes are built, for everyone. "
                + "Save and share them too? Choose No to keep them on this computer only; a quote that depends on them will then fail GitHub's check.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!shareOutside)
            {
                Chat.Items.Add(new NoticeItem(
                    "Claude also changed files outside the quotes folder. They were kept on this computer and not shared: " + string.Join(", ", outside),
                    isError: true));
            }
        }

        if (changed.Count > 0 || shareOutside)
        {
            var verb = turn.IsNew ? "draft" : "changed";
            string[] paths = shareOutside ? [repository.Profile.RootFolder, .. outside] : [repository.Profile.RootFolder];
            await _work.RunAsync("Saving Claude's changes...", async () =>
                await SaveAsync(paths, $"{number ?? "Quotes"}: {verb} with Claude", waitForGitHub: true).ConfigureAwait(true)).ConfigureAwait(true);
            Chat.Items.Add(new NoticeItem("Saved and shared.", isError: false));
        }

        await ReloadQuotesAsync(number).ConfigureAwait(true);
    }

    // -- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// Brings in others' quotes before changing anything (see <see cref="WorkTracker.PullFirstAsync"/>).
    /// A sync that brings in commits reloads the list itself (RepositoryUpdated).
    /// </summary>
    private async Task PullFirstAsync()
    {
        if (await WorkTracker.PullFirstAsync(QuotesRepository!).ConfigureAwait(true) is { } result)
        {
            await HandleSaveResultAsync(result).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Commits the change on this computer and shares it with GitHub in the background, or, with
    /// <paramref name="waitForGitHub"/>, before returning (after a Claude turn, which then says
    /// "Saved and shared").
    /// </summary>
    private async Task SaveAsync(IReadOnlyList<string> paths, string message, bool waitForGitHub = false)
    {
        var repository = QuotesRepository!;
        if (waitForGitHub)
        {
            await HandleSaveResultAsync(await repository.Sync.SaveAsync(paths, message).ConfigureAwait(true)).ConfigureAwait(true);
            return;
        }

        var result = await repository.Sync.SaveLocallyAsync(paths, message).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            await HandleSaveResultAsync(result).ConfigureAwait(true);
        }
        else if (result.Committed)
        {
            _ = ShareAsync(repository);
        }
    }

    private async Task ShareAsync(Repository repository)
    {
        if (await WorkTracker.ShareInBackgroundAsync(repository).ConfigureAwait(true) is { Succeeded: false } problem)
        {
            await HandleSaveResultAsync(problem).ConfigureAwait(true);
        }
    }

    private async Task HandleSaveResultAsync(SaveResult result)
    {
        if (await WorkTracker.HandleSaveResultAsync(QuotesRepository!, result).ConfigureAwait(true) is not { } aside)
        {
            return;
        }

        await ReloadQuotesAsync().ConfigureAwait(true);
        if (aside.Item is { } quote)
        {
            var prompt = $"My change to quote {quote} conflicted with someone else's change, so my version was set aside on the local git branch {aside.Branch}. "
                + $"Use `git show {aside.Branch}` to see what I changed, then use the revise-quote skill to apply the same change to the current version of {quote}. "
                + "If their change and mine disagree, ask me which to keep.";
            await Chat.StartAsync(QuotesRepository!, $"Redo my change to {quote}", quote, prompt, isNew: false).ConfigureAwait(true);
        }
    }

    private void ShowSync(RepositoryKind kind, SyncStatus status)
    {
        _syncStatus[kind] = status;
        SyncProblem = _syncStatus.Values.Any(s => s.State is SyncState.Conflict or SyncState.Error or SyncState.Offline);

        static string Describe(SyncStatus s)
        {
            var when = s.State == SyncState.UpToDate && s.LastSynced is { } t ? $" (synced {t:h:mm tt})" : "";
            return $"{s.Message ?? s.State.ToString()}{when}";
        }

        SyncText = _syncStatus.Count == 1
            ? Describe(_syncStatus.Values.Single())
            : string.Join("     ", _syncStatus.OrderBy(p => p.Key).Select(p => $"{(p.Key == RepositoryKind.Projects ? "Projects" : "Quotes")}: {Describe(p.Value)}"));
    }

    private async Task RefreshChecksAsync()
    {
        foreach (var repository in OpenRepositories().ToList())
        {
            try
            {
                _checks[repository.Profile.Kind] = await _gh.GetLatestCheckAsync(repository.Profile.GitHubName).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is ToolMissingException or System.Text.Json.JsonException)
            {
                _checks[repository.Profile.Kind] = null;
            }
        }

        var failed = _checks.Where(c => c.Value?.Failed == true).Select(c => (Kind: c.Key, Run: c.Value!)).FirstOrDefault();
        var running = _checks.Values.FirstOrDefault(r => r?.InProgress == true);
        var any = _checks.Values.OfType<CheckRun>().ToList();
        CheckFailed = failed.Run is not null;
        CheckUrl = failed.Run?.Url ?? running?.Url ?? any.FirstOrDefault()?.Url;
        CheckText = failed.Run is not null
            ? $"Checks failed on GitHub for the {(failed.Kind == RepositoryKind.Projects ? "projects" : "quotes")} (click for details)"
            : running is not null ? "Checks running on GitHub"
            : any.Count > 0 && any.All(r => r.Passed) ? "Checks passed"
            : null;
    }

    private void ShowUpdate(UpdateStatus status)
    {
        UpdateReady = status.Stage == UpdateStage.ReadyToApply;
        UpdateText = status.Stage switch
        {
            UpdateStage.Downloading => $"Downloading update {status.AvailableVersion} ({status.DownloadPercent}%)",
            UpdateStage.ReadyToApply => $"Update {status.AvailableVersion} ready: restart to install",
            _ => null,
        };
    }
}
