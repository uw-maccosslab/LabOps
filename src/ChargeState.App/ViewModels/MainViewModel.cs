using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ChargeState.App.Services;
using ChargeState.App.Views;
using ChargeState.Core.GitHub;
using ChargeState.Core.Infrastructure;
using ChargeState.Core.Processes;
using ChargeState.Core.Quotes;
using ChargeState.Core.Setup;
using ChargeState.Core.Sync;

namespace ChargeState.App.ViewModels;

/// <summary>
/// The main window: the quote list, the selected quote, its actions, and the status bar.
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
    private readonly SyncService _sync;
    private readonly GitHubCli _gh;
    private readonly SetupService _setup;
    private readonly QuoteSearch _search;
    private readonly UpdateService _updates;
    private readonly ILogger<MainViewModel> _log;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;
    private List<QuoteSummary> _all = [];

    public MainViewModel(
        Workspace workspace, QuoteEngine engine, SyncService sync, GitHubCli gh, SetupService setup,
        QuoteSearch search, UpdateService updates, ChatViewModel chat, ILogger<MainViewModel> log)
    {
        _workspace = workspace;
        _engine = engine;
        _sync = sync;
        _gh = gh;
        _setup = setup;
        _search = search;
        _updates = updates;
        _log = log;
        Chat = chat;
        _dispatcher = Dispatcher.CurrentDispatcher;

        Query = "";
        Filter = QuoteFilter.Current;
        PreviewHtml = MarkdownRenderer.ToHtml(null, "Select a quote to see it here.");
        SyncText = "Starting...";

        Chat.TurnCompleted += OnClaudeTurnCompletedAsync;
        _sync.StatusChanged += s => _dispatcher.InvokeAsync(() => ShowSync(s));
        _sync.RepositoryUpdated += () => _dispatcher.InvokeAsync(async () => await ReloadQuotesAsync().ConfigureAwait(true));
        _updates.StatusChanged += s => _dispatcher.InvokeAsync(() => ShowUpdate(s));
    }

    public ChatViewModel Chat { get; }

    public ObservableCollection<QuoteSummary> Quotes { get; } = [];

    public IReadOnlyList<QuoteFilter> Filters { get; } = Enum.GetValues<QuoteFilter>();

    [ObservableProperty] public partial string Query { get; set; }

    [ObservableProperty] public partial QuoteFilter Filter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedHeadline), nameof(SelectedDetail), nameof(SelectedIssues),
        nameof(CanSendSelected), nameof(IsSentSelected), nameof(IsAcceptedSelected))]
    [NotifyCanExecuteChangedFor(nameof(AskClaudeCommand), nameof(SendCommand), nameof(MarkAcceptedCommand),
        nameof(MarkInvoicedCommand), nameof(MarkDeclinedCommand), nameof(ReviseCommand), nameof(DraftPdfCommand),
        nameof(OpenFolderCommand), nameof(OpenPdfCommand), nameof(OpenSpreadsheetCommand), nameof(OpenOnGitHubCommand))]
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
        nameof(DraftPdfCommand), nameof(SyncNowCommand))]
    public partial bool IsWorking { get; set; }

    [ObservableProperty] public partial string? WorkingText { get; set; }

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
                                ("Invoiced", q.Invoiced), ("Declined", q.Declined), ("PO", q.Po) }
                .Where(d => !string.IsNullOrWhiteSpace(d.Item2)).Select(d => $"{d.Item1} {d.Item2}");
            lines.Add(string.Join("   ", dates));
            return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }
    }

    public IReadOnlyList<string> SelectedIssues =>
        Selected is null ? [] : [.. Selected.Issues.Select(i => $"{i.Level}: {i.Message}"), .. Selected.Error is null ? [] : new[] { Selected.Error }];

    // -- startup -----------------------------------------------------------------------------

    /// <summary>Runs setup if needed, then loads and syncs. Called once the window is shown.</summary>
    public async Task InitializeAsync(Window owner)
    {
        var repo = SetupService.FindExistingClone(_workspace.Settings.RepositoryPath);
        if (repo is not null)
        {
            _workspace.UseRepository(repo);
        }

        var items = await _setup.CheckAsync(repo).ConfigureAwait(true);
        if (!SetupService.AllDone(items))
        {
            SetupWindow.Show(owner, App.Services);
        }

        if (_workspace.RepositoryPath is null)
        {
            Banner = "Setup is not finished. Open Setup to download the quotes.";
            SyncText = "Not set up";
            return;
        }

        await StartWorkingAsync().ConfigureAwait(true);
    }

    private async Task StartWorkingAsync()
    {
        _workspace.ReloadConfig();
        _workspace.User = await _gh.GetUserAsync().ConfigureAwait(true);
        UserText = _workspace.User is null ? "Not signed in to GitHub" : $"Signed in as {_workspace.User.Login}";
        ShowBanner();

        await RunAsync("Getting the latest quotes...", async () =>
        {
            await _sync.SyncAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        await ReloadQuotesAsync().ConfigureAwait(true);

        _timer ??= CreateTimer();
        _ = RefreshCheckAsync();
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
        if (IsWorking || Chat.IsBusy || _workspace.RepositoryPath is null)
        {
            return;
        }

        var status = await _sync.RefreshAsync(fetch: true).ConfigureAwait(true);
        if (status.Behind > 0 && status.Changes.Count == 0)
        {
            await _sync.SyncAsync().ConfigureAwait(true);
        }

        await RefreshCheckAsync().ConfigureAwait(true);
    }

    private void ShowBanner()
    {
        Banner = _workspace.AppTooOld
            ? $"This version of {AppInfo.ProductName} is too old for the quotes repository (it needs {_workspace.Config.MinAppVersion}). Update the app to make changes."
            : null;
        OnPropertyChanged(nameof(CanSendSelected));
    }

    // -- list --------------------------------------------------------------------------------

    partial void OnQueryChanged(string value) => ApplyFilter();

    partial void OnFilterChanged(QuoteFilter value) => ApplyFilter();

    partial void OnSelectedChanged(QuoteSummary? value) => ShowPreview();

    partial void OnShowCalculationChanged(bool value) => ShowPreview();

    [RelayCommand]
    private async Task RefreshAsync() => await ReloadQuotesAsync().ConfigureAwait(true);

    private async Task ReloadQuotesAsync(string? select = null)
    {
        if (_workspace.RepositoryPath is null)
        {
            return;
        }

        var keep = select ?? Selected?.QuoteNumber;
        try
        {
            _all = [.. await _engine.ListAsync().ConfigureAwait(true)];
            var repo = _workspace.RepositoryPath;
            var snapshot = _all;
            await Task.Run(() => _search.Index(repo, snapshot)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is QuoteEngineException or ToolMissingException)
        {
            Banner = $"The quotes could not be loaded: {ex.Message}";
            return;
        }

        _workspace.ReloadConfig();
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
        if (Selected is null || _workspace.RepositoryPath is null)
        {
            PreviewHtml = MarkdownRenderer.ToHtml(null, "Select a quote to see it here.");
            return;
        }

        var folder = Selected.FolderPath(_workspace.RepositoryPath);
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
        await Chat.StartAsync(form.Title, quoteNumber: null, form.BuildPrompt(), isNewQuote: true).ConfigureAwait(true);
    }

    private bool CanEdit() => !IsWorking && _workspace.RepositoryPath is not null && !_workspace.AppTooOld;

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

        var hasEarlier = _workspace.Settings.ClaudeSessions.ContainsKey(quote.QuoteNumber);
        var resume = hasEarlier && MessageBox.Show(
            $"Continue your earlier conversation with Claude about {quote.QuoteNumber}? Choose No to start fresh.",
            AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        await PullFirstAsync().ConfigureAwait(true);
        var prompt = $"Use the revise-quote skill on quote {quote.QuoteNumber} ({quote.Folder}). "
            + "Ask me what I want to change with the ask_user tool, then make the change.";
        await Chat.StartAsync(quote.QuoteNumber, quote.QuoteNumber, prompt, isNewQuote: false, resume).ConfigureAwait(true);
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
        await RunAsync($"Sending {quote.QuoteNumber}...", async () =>
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
        await RunAsync($"Marking {quote.QuoteNumber} {word}...", async () =>
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
        await RunAsync($"Making a revision of {quote.QuoteNumber}...", async () =>
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
        await RunAsync("Making a draft PDF...", async () =>
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder() => Shell.Open(Selected!.FolderPath(_workspace.RepositoryPath!));

    /// <summary>The sent PDF, or for a draft the latest draft PDF.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPdf))]
    private void OpenPdf() => Shell.Open(Selected!.ExistingPdf(_workspace.RepositoryPath!)!);

    private bool CanOpenPdf() => _workspace.RepositoryPath is not null && Selected?.ExistingPdf(_workspace.RepositoryPath) is not null;

    [RelayCommand(CanExecute = nameof(CanOpenSpreadsheet))]
    private void OpenSpreadsheet() => Shell.Open(Selected!.ExistingSpreadsheet(_workspace.RepositoryPath!)!);

    private bool CanOpenSpreadsheet() => _workspace.RepositoryPath is not null && Selected?.ExistingSpreadsheet(_workspace.RepositoryPath) is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenOnGitHub() =>
        Shell.Open($"https://github.com/{AppInfo.QuotesRepository}/tree/main/{Uri.EscapeDataString(Selected!.Folder).Replace("%2F", "/", StringComparison.Ordinal)}");

    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        await RunAsync("Syncing with GitHub...", async () =>
        {
            var result = await _sync.SyncAsync().ConfigureAwait(true);
            await HandleSaveResultAsync(result).ConfigureAwait(true);
        }).ConfigureAwait(true);
        await RefreshCheckAsync().ConfigureAwait(true);
    }

    private bool CanSyncNow() => !IsWorking && _workspace.RepositoryPath is not null;

    [RelayCommand]
    private async Task OpenSetupAsync()
    {
        SetupWindow.Show(Application.Current.MainWindow, App.Services);
        if (_workspace.RepositoryPath is not null)
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

    /// <summary>Saves and syncs whatever Claude changed under quotes/ when it finishes a turn.</summary>
    private async Task OnClaudeTurnCompletedAsync(ChatTurnResult turn)
    {
        var number = turn.Report?.QuoteNumber ?? turn.QuoteNumber;
        var status = await _sync.RefreshAsync(fetch: false).ConfigureAwait(true);
        var changed = status.Changes.Where(c => c.StartsWith("quotes/", StringComparison.Ordinal)).ToList();
        var outside = status.Changes.Where(c => !c.StartsWith("quotes/", StringComparison.Ordinal)).ToList();

        if (outside.Count > 0)
        {
            Chat.Items.Add(new NoticeItem(
                "Claude also changed files outside the quotes folder. They were not saved: " + string.Join(", ", outside), isError: true));
        }

        if (changed.Count > 0)
        {
            var verb = turn.IsNewQuote ? "draft" : "changed";
            await RunAsync("Saving Claude's changes...", async () =>
                await SaveAsync(["quotes"], $"{number ?? "Quotes"}: {verb} with Claude").ConfigureAwait(true)).ConfigureAwait(true);
            Chat.Items.Add(new NoticeItem("Saved and shared.", isError: false));
        }

        await ReloadQuotesAsync(number).ConfigureAwait(true);
    }

    // -- helpers -----------------------------------------------------------------------------

    /// <summary>Brings in others' work before changing anything, so edits start from the latest.</summary>
    private async Task PullFirstAsync()
    {
        var result = await _sync.SyncAsync().ConfigureAwait(true);
        await HandleSaveResultAsync(result).ConfigureAwait(true);
        if (result.Conflict is null)
        {
            await ReloadQuotesAsync().ConfigureAwait(true);
        }
    }

    private async Task SaveAsync(IReadOnlyList<string> paths, string message)
    {
        var result = await _sync.SaveAsync(paths, message).ConfigureAwait(true);
        await HandleSaveResultAsync(result).ConfigureAwait(true);
    }

    private async Task HandleSaveResultAsync(SaveResult result)
    {
        if (result.Conflict is { } conflict)
        {
            var answer = MessageBox.Show(
                $"{conflict.Message}\n\nSet your version aside and use theirs? Your version is kept on this computer, and Claude can redo your change on top of theirs.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var branch = await _sync.SetAsideAsync().ConfigureAwait(true);
            await ReloadQuotesAsync().ConfigureAwait(true);
            var quote = conflict.QuoteNumbers.FirstOrDefault();
            if (quote is not null)
            {
                var prompt = $"My change to quote {quote} conflicted with someone else's change, so my version was set aside on the local git branch {branch}. "
                    + $"Use `git show {branch}` to see what I changed, then use the revise-quote skill to apply the same change to the current version of {quote}. "
                    + "If their change and mine disagree, ask me which to keep.";
                await Chat.StartAsync($"Redo my change to {quote}", quote, prompt, isNewQuote: false).ConfigureAwait(true);
            }
        }
        else if (result.Error is { } error)
        {
            MessageBox.Show(error, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RunAsync(string text, Func<Task> work)
    {
        IsWorking = true;
        WorkingText = text;
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is QuoteEngineException or GitException or ToolMissingException or InvalidOperationException)
        {
            _log.LogWarning(ex, "{Work} failed.", text);
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsWorking = false;
            WorkingText = null;
        }
    }

    private void ShowSync(SyncStatus status)
    {
        SyncProblem = status.State is SyncState.Conflict or SyncState.Error or SyncState.Offline;
        var when = status.LastSynced is { } t ? $" (synced {t:h:mm tt})" : "";
        SyncText = $"{status.Message ?? status.State.ToString()}{(status.State == SyncState.UpToDate ? when : "")}";
    }

    private async Task RefreshCheckAsync()
    {
        try
        {
            var run = await _gh.GetLatestCheckAsync().ConfigureAwait(true);
            CheckUrl = run?.Url;
            CheckFailed = run?.Failed == true;
            CheckText = run switch
            {
                null => null,
                { InProgress: true } => "Checks running on GitHub",
                { Passed: true } => "Checks passed",
                { Failed: true } => "Checks failed on GitHub (click for details)",
                _ => $"Checks: {run.Conclusion ?? run.Status}",
            };
        }
        catch (Exception ex) when (ex is ToolMissingException or System.Text.Json.JsonException)
        {
            CheckText = null;
        }
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
