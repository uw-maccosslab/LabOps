using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using LabOps.App.Services;
using LabOps.App.Views;
using LabOps.Core.Claude;
using LabOps.Core.Processes;
using LabOps.Core.Repositories;
using LabOps.Core.Setup;

namespace LabOps.App.ViewModels;

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
    private readonly SetupService _setup;
    private readonly ClaudeLogin _login;
    private readonly ILogger<ChatViewModel> _log;
    private readonly Dispatcher _dispatcher;

    private ClaudeSession? _session;
    private Repository? _repository;
    private QuoteReport? _turnReport;
    private bool _isNew;
    // How the conversation started and what was last sent, so Try again (after signing in to
    // Claude again) can start it again.
    private StartRequest? _started;
    private string? _lastSent;
    private bool _answered;
    private bool _signInOffered;

    public ChatViewModel(
        Workspace workspace, ClaudeLauncher launcher, AppTools tools, PermissionMemory permissions, SetupService setup,
        ClaudeLogin login, ILogger<ChatViewModel> log)
    {
        _workspace = workspace;
        _launcher = launcher;
        _tools = tools;
        _permissions = permissions;
        _setup = setup;
        _login = login;
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

    /// <summary>The quote, project or protocol this conversation is about; a report names a new quote.</summary>
    public string? Item
    {
        get => _item;
        private set => SetProperty(ref _item, value);
    }

    private string? _item;

    /// <summary>
    /// Set when the person is looking at a different quote, project or protocol from the one this
    /// conversation is about, so a message meant for that one does not go to this one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMismatch), nameof(MismatchText), nameof(TalkText))]
    public partial ConversationMismatch? Mismatch { get; set; }

    public bool HasMismatch => Mismatch is not null;

    public string MismatchText => Mismatch is { } m ? $"This conversation is about {m.About}. You are looking at {m.Viewing}." : "";

    public string TalkText => Mismatch is { } m ? $"Talk about {m.Viewing}" : "";

    /// <summary>Starts (or continues) a conversation about what the person is looking at.</summary>
    [RelayCommand]
    private void TalkAboutViewed()
    {
        if (Mismatch?.Talk is { } talk && talk.CanExecute(null))
        {
            talk.Execute(null);
        }
    }

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
        _repository = repository;
        OnPropertyChanged(nameof(Repository));
        Item = item;
        _isNew = isNew;
        _started = new StartRequest(repository, title, item, prompt, isNew, resume);
        IsWaitingForAnswer = false;
        _answered = false;
        _signInOffered = false;
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
        if (!ConfirmedDespiteMismatch())
        {
            return;
        }

        var text = Input.Trim();
        Input = "";
        // While Claude waits on a question, what is typed here is the answer.
        if (PendingQuestion is { } question)
        {
            question.AnswerWith(text);
            return;
        }

        if (_session is null || _session.HasEnded)
        {
            Items.Add(new NoticeItem("This conversation has ended. Start a new one from the quote or project.", isError: true));
            return;
        }

        await SendCoreAsync(text, show: true).ConfigureAwait(true);
    }

    /// <summary>
    /// Before a message goes to a conversation about something other than what the person is
    /// looking at, they confirm it; otherwise the text stays in the box.
    /// </summary>
    private bool ConfirmedDespiteMismatch()
    {
        if (Mismatch is not { } m)
        {
            return true;
        }

        return ConfirmWindow.Ask(System.Windows.Application.Current.MainWindow, $"Send this about {m.About}?",
            $"This conversation is about {m.About}, but you are looking at {m.Viewing}. Claude will take your message to be "
            + $"about {m.About}. To talk about {m.Viewing} instead, choose Cancel, then {TalkText} at the top of the conversation.",
            $"Send about {m.About}");
    }

    /// <summary>Claude is working on a turn, unless it is waiting for an answer, which is typed below.</summary>
    private bool CanSend() => (!IsBusy || IsWaitingForAnswer) && !string.IsNullOrWhiteSpace(Input);

    /// <summary>The question Claude is waiting on, if any.</summary>
    private QuestionItem? PendingQuestion => Items.OfType<QuestionItem>().LastOrDefault(q => !q.IsAnswered);

    /// <summary>Claude asked a question and is waiting for the answer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    public partial bool IsWaitingForAnswer { get; set; }

    /// <summary>Under the conversation while Claude has the turn.</summary>
    public string BusyText => IsWaitingForAnswer
        ? "Claude is waiting for your answer: choose one above, or type it here and press Enter."
        : "Claude is working...";

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

        _lastSent = text;
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
                // Claude Code reports a failed request as a message of its own ("API Error: 401 ...").
                if (ClaudeSignIn.IsProblem(text.Text))
                {
                    OfferSignIn();
                }

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
                    if (ClaudeSignIn.IsProblem(finished.Result))
                    {
                        OfferSignIn();
                    }
                }
                else
                {
                    _answered = true;
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

                if (ClaudeSignIn.IsProblem(ended.ErrorOutput))
                {
                    OfferSignIn();
                }

                break;
        }
    }

    /// <summary>
    /// Offers Sign in to Claude, once per conversation. Setup cannot see this problem, because
    /// <c>claude auth status</c> still reports a session that can no longer be refreshed as signed in.
    /// </summary>
    private void OfferSignIn()
    {
        if (_signInOffered)
        {
            return;
        }

        _signInOffered = true;
        Items.Add(new SignInItem(SignInToClaudeAsync, TryAgainAsync));
    }

    /// <summary>
    /// Signs in to Claude in the browser, with no console window (<see cref="ClaudeLogin"/>). If that
    /// does not finish, offers the console window Setup used to open, where Claude Code can ask for
    /// a code to paste.
    /// </summary>
    private async Task<bool> SignInToClaudeAsync(SignInItem item, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _login.SignInAsync(link => _dispatcher.InvokeAsync(() => item.Link = link), cancellationToken)
                .ConfigureAwait(true);
            if (result.Succeeded || result.Cancelled)
            {
                return result.Succeeded;
            }

            _log.LogWarning("claude auth login did not finish: {Output}", result.Output);
            if (System.Windows.MessageBox.Show(
                    "The sign-in did not finish. Sign in with a console window instead? It shows what Claude asks for, and you "
                    + "close it when it says you are signed in.",
                    Core.Infrastructure.AppInfo.ProductName, System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes
                || _setup.ConsoleFix(SetupStep.ClaudeSignIn) is not { } command)
            {
                return false;
            }

            await Shell.RunInConsoleAsync(command).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is ToolMissingException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Items.Add(new NoticeItem($"The sign-in could not start: {ex.Message}", isError: true));
            return false;
        }
    }

    /// <summary>
    /// Starts the conversation again with the new sign-in: from its first request when Claude never
    /// answered, otherwise continuing it with the last message sent.
    /// </summary>
    private Task TryAgainAsync()
    {
        if (_started is not { } s)
        {
            return Task.CompletedTask;
        }

        return _answered && Item is not null
            ? StartAsync(s.Repository, Title, Item, _lastSent ?? s.Prompt, isNew: false, resume: true)
            : StartAsync(s.Repository, s.Title, s.Item, s.Prompt, s.IsNew, s.Resume);
    }

    /// <summary>What a conversation was started with.</summary>
    private sealed record StartRequest(Repository Repository, string Title, string? Item, string Prompt, bool IsNew, bool Resume);

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
            q.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(QuestionItem.IsAnswered))
                {
                    IsWaitingForAnswer = PendingQuestion is not null;
                }
            };
            Items.Add(q);
            IsWaitingForAnswer = true;
            return q;
        });

        // On the UI thread, since abandoning it updates what the window shows.
        using var registration = cancellationToken.Register(() => _dispatcher.InvokeAsync(item.Abandon));
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
