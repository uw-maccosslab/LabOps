using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ChargeState.App.Services;
using ChargeState.App.Views;
using ChargeState.Core.Claude;
using ChargeState.Core.Processes;
using ChargeState.Core.Repositories;

namespace ChargeState.App.ViewModels;

/// <summary>What the main window needs to know when Claude finishes a turn.</summary>
/// <param name="Repository">The repository the conversation works in; its changes are saved there.</param>
/// <param name="Item">The quote number or project the conversation is about, when known.</param>
/// <param name="Report">Claude's quote summary, if it gave one this turn.</param>
/// <param name="IsNew">True when the conversation started a new quote or project.</param>
public sealed record ChatTurnResult(Repository Repository, string? Item, QuoteReport? Report, bool IsNew);

/// <summary>
/// The chat pane: one Claude Code conversation, and the app's side of Claude's tools.
/// </summary>
/// <remarks>
/// Claude's events arrive on a background thread and are marshalled to the UI thread here.
/// Questions and permission prompts block Claude's tool call until the user answers, which is
/// what the MCP server's long-held requests are for.
/// </remarks>
public sealed partial class ChatViewModel : ObservableObject, IClaudeHostUi
{
    private readonly Workspace _workspace;
    private readonly ClaudeLauncher _launcher;
    private readonly AppTools _tools;
    private readonly PermissionMemory _permissions;
    private readonly ILogger<ChatViewModel> _log;
    private readonly Dispatcher _dispatcher;

    private ClaudeSession? _session;
    private Repository? _repository;
    private QuoteReport? _turnReport;
    private bool _isNew;

    public ChatViewModel(Workspace workspace, ClaudeLauncher launcher, AppTools tools, PermissionMemory permissions, ILogger<ChatViewModel> log)
    {
        _workspace = workspace;
        _launcher = launcher;
        _tools = tools;
        _permissions = permissions;
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Title = "Claude";
        Input = "";
    }

    /// <summary>Raised on the UI thread when Claude finishes responding.</summary>
    public event Func<ChatTurnResult, Task>? TurnCompleted;

    public ObservableCollection<ChatItem> Items { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The quote or project this conversation is about; a report names a new quote.</summary>
    public string? Item { get; private set; }

    /// <summary>The repository this conversation works in.</summary>
    public Repository? Repository => _repository;

    public bool HasSession => _session is { HasEnded: false };

    /// <summary>True when an earlier conversation about this item can be continued.</summary>
    public bool HasEarlierConversation(RepositoryProfile profile, string item) =>
        _workspace.Settings.ClaudeSessions.ContainsKey(Workspace.SessionKey(profile, item));

    /// <summary>Starts a conversation in <paramref name="repository"/>, replacing any previous one.</summary>
    /// <param name="repository">Where Claude works, and where its changes are saved.</param>
    /// <param name="title">The chat pane's title.</param>
    /// <param name="item">The quote number or project, when known.</param>
    /// <param name="prompt">The first message, not shown in the pane.</param>
    /// <param name="isNew">True when the conversation creates a new quote or project.</param>
    /// <param name="resume">Continue the last conversation about the item.</param>
    public async Task StartAsync(Repository repository, string title, string? item, string prompt, bool isNew, bool resume = false)
    {
        await EndSessionAsync().ConfigureAwait(true);

        Items.Clear();
        Title = title;
        Item = item;
        _repository = repository;
        _isNew = isNew;
        IsOpen = true;

        var server = await _workspace.ToolServerAsync().ConfigureAwait(true);
        string? resumeId = null;
        if (resume && item is not null)
        {
            _workspace.Settings.ClaudeSessions.TryGetValue(Workspace.SessionKey(repository.Profile, item), out resumeId);
        }

        ClaudeSession session;
        try
        {
            var options = _launcher.CreateOptions(repository.Profile, repository.Path, server,
                _workspace.User?.Name ?? _workspace.User?.Login, resumeId, _workspace.Settings.ClaudeModel);
            session = _launcher.Start(options);
        }
        catch (Exception ex) when (ex is ToolMissingException or System.ComponentModel.Win32Exception)
        {
            Items.Add(new NoticeItem($"Claude Code could not start: {ex.Message}", isError: true));
            return;
        }

        _session = session;
        _tools.Ui = this;
        session.EventReceived += e => _dispatcher.InvokeAsync(() => OnEvent(session, e));

        if (resumeId is not null)
        {
            Items.Add(new NoticeItem($"Continuing your earlier conversation about {item}.", isError: false));
        }

        // Shown before sending: Claude's first events can arrive while the send is awaited.
        Items.Add(new UserMessageItem(title));
        await SendCoreAsync(prompt, show: false).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        Input = "";
        if (_session is null || _session.HasEnded)
        {
            Items.Add(new NoticeItem("This conversation has ended. Start a new one from the quote or project.", isError: true));
            return;
        }

        await SendCoreAsync(text, show: true).ConfigureAwait(true);
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop()
    {
        _session?.Stop();
        Items.Add(new NoticeItem("Stopped. Anything Claude already changed has been kept; review it.", isError: false));
    }

    /// <summary>
    /// Names the item a conversation turned out to be about (a new project once Claude has
    /// created it), so it can be continued later.
    /// </summary>
    public void AdoptItem(string item)
    {
        if (Item is not null)
        {
            return;
        }

        Item = item;
        Title = item;
        RememberSession(_session?.SessionId);
    }

    /// <summary>Puts text in the message box for the user to send or edit, for example after a refused save.</summary>
    public void Suggest(string text) => Input = text;

    [RelayCommand]
    private async Task CloseAsync()
    {
        await EndSessionAsync().ConfigureAwait(true);
        IsOpen = false;
    }

    /// <summary>
    /// Ends Claude's process at once, without waiting for anything. For App.OnExit, where nothing
    /// may wait on the UI thread; a normal close ends the session properly first.
    /// </summary>
    public void KillSession() => _session?.Stop();

    public async Task EndSessionAsync()
    {
        foreach (var question in Items.OfType<QuestionItem>())
        {
            question.Abandon();
        }

        if (_session is not null)
        {
            var session = _session;
            _session = null;
            await session.DisposeAsync().ConfigureAwait(true);
        }

        if (_tools.Ui == this)
        {
            _tools.Ui = null;
        }

        IsBusy = false;
    }

    private async Task SendCoreAsync(string text, bool show)
    {
        if (show)
        {
            Items.Add(new UserMessageItem(text));
        }

        _turnReport = null;
        IsBusy = true;
        try
        {
            // A button's change may still be sharing with GitHub, which rebases the folder Claude
            // works in. Let it finish before Claude starts editing.
            if (_repository is not null)
            {
                await _repository.Sync.WhenIdleAsync().ConfigureAwait(true);
            }

            await _session!.SendAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            IsBusy = false;
            Items.Add(new NoticeItem("Claude is no longer running. Start a new conversation from the quote or project.", isError: true));
        }
    }

    private async Task OnEvent(ClaudeSession session, ClaudeEvent e)
    {
        if (session != _session)
        {
            return; // An event from a conversation that has since been replaced.
        }

        switch (e)
        {
            case SessionStarted started:
                RememberSession(started.SessionId);
                if (!started.McpServers.Any(s => s.Name == AppTools.ServerName && s.Connected))
                {
                    _log.LogWarning("Claude did not connect to the app's tools: {Servers}",
                        string.Join(", ", started.McpServers.Select(s => $"{s.Name}={s.Status}")));
                }

                break;

            case AssistantText text:
                Items.Add(new AssistantMessageItem(text.Text.Trim()));
                break;

            case ToolStarted tool when tool.Describe() is { Length: > 0 } description:
                Items.Add(new ActivityItem(tool.Id, description));
                break;

            case ToolFinished { IsError: true } finished:
                if (Items.OfType<ActivityItem>().LastOrDefault(a => a.ToolUseId == finished.ToolUseId) is { } activity)
                {
                    activity.Failed = true;
                }

                break;

            case TurnFinished finished:
                IsBusy = false;
                RememberSession(finished.SessionId);
                if (finished.IsError)
                {
                    Items.Add(new NoticeItem(finished.Result ?? "Claude stopped with an error.", isError: true));
                }

                if (TurnCompleted is { } handler && _repository is not null)
                {
                    await handler(new ChatTurnResult(_repository, Item, _turnReport, _isNew)).ConfigureAwait(true);
                }

                break;

            case SessionEnded ended:
                IsBusy = false;
                if (ended.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(ended.ErrorOutput) ? "" : $"\n\n{ended.ErrorOutput}";
                    Items.Add(new NoticeItem($"Claude stopped unexpectedly (exit code {ended.ExitCode}).{detail}", isError: true));
                }

                break;
        }
    }

    private void RememberSession(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && Item is not null && _repository is not null)
        {
            _workspace.Settings.ClaudeSessions[Workspace.SessionKey(_repository.Profile, Item)] = sessionId;
            _workspace.SaveSettings();
        }
    }

    // -- Claude's app tools ---------------------------------------------------------------------

    public async Task<string> AskUserAsync(string question, IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        var item = await _dispatcher.InvokeAsync(() =>
        {
            var q = new QuestionItem(question, options);
            Items.Add(q);
            return q;
        });

        using var registration = cancellationToken.Register(item.Abandon);
        return await item.Response.ConfigureAwait(false);
    }

    public Task ShowReportAsync(QuoteReport report, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            _turnReport = report;
            if (Item is null)
            {
                Item = report.QuoteNumber;
                Title = report.QuoteNumber;
                RememberSession(_session?.SessionId);
            }

            Items.Add(new ReportItem(report));
        }).Task;

    public async Task<PermissionDecision> RequestPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        var repository = _repository?.Profile.Id ?? "";
        if (_permissions.IsAllowed(repository, request))
        {
            return new PermissionDecision(true);
        }

        var (allow, remember) = await _dispatcher.InvokeAsync(() => PermissionWindow.Ask(request, PermissionMemory.Describe(request)));
        if (allow && remember)
        {
            _permissions.Remember(repository, request);
        }

        await _dispatcher.InvokeAsync(() => Items.Add(new NoticeItem(
            $"{(allow ? "Allowed" : "Declined")}: {request.Description}", isError: !allow)));

        return allow ? new PermissionDecision(true) : new PermissionDecision(false, "The user declined this step. Continue without it if you can, or explain what you needed.");
    }
}
