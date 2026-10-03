using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ChargeState.App.Services;
using ChargeState.App.Views;
using ChargeState.Core.Claude;
using ChargeState.Core.Processes;

namespace ChargeState.App.ViewModels;

/// <summary>What the main window needs to know when Claude finishes a turn.</summary>
/// <param name="QuoteNumber">The quote the conversation is about, when known.</param>
/// <param name="Report">Claude's summary, if it gave one this turn.</param>
/// <param name="IsNewQuote">True when the conversation started as a new quote.</param>
public sealed record ChatTurnResult(string? QuoteNumber, QuoteReport? Report, bool IsNewQuote);

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
    private readonly ILogger<ChatViewModel> _log;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<string> _allowedForConversation = new(StringComparer.OrdinalIgnoreCase);

    private ClaudeSession? _session;
    private QuoteReport? _turnReport;
    private bool _isNewQuote;

    public ChatViewModel(Workspace workspace, ClaudeLauncher launcher, AppTools tools, ILogger<ChatViewModel> log)
    {
        _workspace = workspace;
        _launcher = launcher;
        _tools = tools;
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

    /// <summary>The quote this conversation is about; set by a report for a new quote.</summary>
    public string? QuoteNumber { get; private set; }

    public bool HasSession => _session is { HasEnded: false };

    /// <summary>Starts a conversation, replacing any previous one.</summary>
    public async Task StartAsync(string title, string? quoteNumber, string prompt, bool isNewQuote, bool resume = false)
    {
        await EndSessionAsync().ConfigureAwait(true);

        Items.Clear();
        _allowedForConversation.Clear();
        Title = title;
        QuoteNumber = quoteNumber;
        _isNewQuote = isNewQuote;
        IsOpen = true;

        var repo = _workspace.RepositoryPath ?? throw new InvalidOperationException("No quotes repository is set up yet.");
        var server = await _workspace.ToolServerAsync().ConfigureAwait(true);
        string? resumeId = null;
        if (resume && quoteNumber is not null)
        {
            _workspace.Settings.ClaudeSessions.TryGetValue(quoteNumber, out resumeId);
        }

        ClaudeSession session;
        try
        {
            var options = _launcher.CreateOptions(repo, server, _workspace.User?.Name ?? _workspace.User?.Login, resumeId, _workspace.Settings.ClaudeModel);
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
            Items.Add(new NoticeItem("Continuing your earlier conversation about this quote.", isError: false));
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
            Items.Add(new NoticeItem("This conversation has ended. Start a new one from the quote.", isError: true));
            return;
        }

        await SendCoreAsync(text, show: true).ConfigureAwait(true);
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop()
    {
        _session?.Stop();
        Items.Add(new NoticeItem("Stopped. Anything Claude already changed is still in the quote; review it before sending.", isError: false));
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        await EndSessionAsync().ConfigureAwait(true);
        IsOpen = false;
    }

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
            await _session!.SendAsync(text).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            IsBusy = false;
            Items.Add(new NoticeItem("Claude is no longer running. Start a new conversation from the quote.", isError: true));
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

                if (TurnCompleted is { } handler)
                {
                    await handler(new ChatTurnResult(QuoteNumber, _turnReport, _isNewQuote)).ConfigureAwait(true);
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
        if (!string.IsNullOrWhiteSpace(sessionId) && QuoteNumber is not null)
        {
            _workspace.Settings.ClaudeSessions[QuoteNumber] = sessionId;
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
            if (QuoteNumber is null)
            {
                QuoteNumber = report.QuoteNumber;
                Title = report.QuoteNumber;
                RememberSession(_session?.SessionId);
            }

            Items.Add(new ReportItem(report));
        }).Task;

    public async Task<PermissionDecision> RequestPermissionAsync(PermissionRequest request, CancellationToken cancellationToken)
    {
        var key = PermissionKey(request);
        if (_allowedForConversation.Contains(key))
        {
            return new PermissionDecision(true);
        }

        var (allow, remember) = await _dispatcher.InvokeAsync(() => PermissionWindow.Ask(request));
        if (allow && remember)
        {
            _allowedForConversation.Add(key);
        }

        await _dispatcher.InvokeAsync(() => Items.Add(new NoticeItem(
            $"{(allow ? "Allowed" : "Declined")}: {request.Description}", isError: !allow)));

        return allow ? new PermissionDecision(true) : new PermissionDecision(false, "The user declined this step. Continue without it if you can, or explain what you needed.");
    }

    /// <summary>"Allow for this conversation" covers the same tool, and for commands the same program.</summary>
    private static string PermissionKey(PermissionRequest request)
    {
        if (request.ToolName is "Bash" or "PowerShell"
            && request.Input.ValueKind == System.Text.Json.JsonValueKind.Object
            && request.Input.TryGetProperty("command", out var command))
        {
            var program = (command.GetString() ?? "").Trim().Split(' ', 2)[0];
            return $"{request.ToolName}:{program}";
        }

        return request.ToolName;
    }
}
