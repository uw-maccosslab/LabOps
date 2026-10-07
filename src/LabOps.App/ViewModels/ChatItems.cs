using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabOps.Core.Claude;

namespace LabOps.App.ViewModels;

/// <summary>A file attached to the next message: its copy in the attachments folder, and what the app checked of it.</summary>
public sealed record ChatAttachment(string Name, string Path, string? Note);

/// <summary>One entry in the chat pane.</summary>
public abstract class ChatItem : ObservableObject;

/// <summary>The conversation is about one thing and the person is looking at another.</summary>
/// <param name="About">What the conversation is about, as the person knows it.</param>
/// <param name="Viewing">What the person is looking at now.</param>
/// <param name="Talk">Starts (or offers to continue) a conversation about what they are looking at.</param>
public sealed record ConversationMismatch(string About, string Viewing, IRelayCommand Talk);

public sealed class UserMessageItem(string text) : ChatItem
{
    public string Text { get; } = text;
}

public sealed class AssistantMessageItem(string text) : ChatItem
{
    public string Text { get; } = text;
}

/// <summary>A step Claude took, in plain words ("Building the quote").</summary>
public sealed partial class ActivityItem(string toolUseId, string text) : ChatItem
{
    public string ToolUseId { get; } = toolUseId;

    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool Failed { get; set; }
}

public sealed class NoticeItem(string text, bool isError) : ChatItem
{
    public string Text { get; } = text;

    public bool IsError { get; } = isError;
}

/// <summary>
/// Claude Code's sign-in on this computer stopped working: sign in again, then try again. Shown in
/// place of a bare "Failed to authenticate", which says what went wrong but not what to do.
/// </summary>
/// <param name="signIn">Signs in (in the browser, with no console window); true when it worked.</param>
/// <param name="tryAgain">Starts the conversation again.</param>
public sealed partial class SignInItem(Func<SignInItem, CancellationToken, Task<bool>> signIn, Func<Task> tryAgain) : ChatItem
{
    [ObservableProperty]
    public partial string Text { get; set; } =
        "Claude's sign-in on this computer has expired. Sign in again with your lab Claude account, then try again.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(TryAgainCommand))]
    public partial bool IsWorking { get; set; }

    /// <summary>Signed in again, so trying again can work.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TryAgainCommand))]
    public partial bool SignedIn { get; set; }

    /// <summary>The sign-in page, in case the browser did not open on its own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    public partial string? Link { get; set; }

    public bool HasLink => Link is not null && IsWorking;

    partial void OnIsWorkingChanged(bool value) => OnPropertyChanged(nameof(HasLink));

    [RelayCommand(CanExecute = nameof(CanSignIn), IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        IsWorking = true;
        Link = null;
        Text = "Finish in your browser: sign in with your lab Claude account and choose Authorize. If the page says the "
            + "window is too small, make it larger.";
        try
        {
            SignedIn = await signIn(this, cancellationToken).ConfigureAwait(true);
            Text = SignedIn ? "Signed in. Choose Try again."
                : cancellationToken.IsCancellationRequested ? "Stopped waiting. Choose Sign in to Claude to start again."
                : "The sign-in did not finish. Choose Sign in to Claude to try again.";
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanSignIn() => !IsWorking;

    [RelayCommand]
    private void OpenLink()
    {
        if (Link is not null)
        {
            Services.Shell.Open(Link);
        }
    }

    [RelayCommand(CanExecute = nameof(CanTryAgain))]
    private async Task TryAgainAsync()
    {
        IsWorking = true;
        try
        {
            await tryAgain().ConfigureAwait(true);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanTryAgain() => SignedIn && !IsWorking;
}

/// <summary>
/// A question from Claude, answered with one of its buttons or by typing in the chat's own box at
/// the bottom (the chat sends what is typed there to the question while it waits).
/// </summary>
public sealed partial class QuestionItem : ChatItem
{
    private readonly TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public QuestionItem(string question, IReadOnlyList<string> options)
    {
        Question = question;
        Options = options;
    }

    public string Question { get; }

    public IReadOnlyList<string> Options { get; }

    public bool HasOptions => Options.Count > 0;

    /// <summary>What to do, under the question while it waits.</summary>
    public string Hint => HasOptions ? "Choose an answer, or type your own in the box below." : "Type your answer in the box below.";

    public Task<string> Response => _answer.Task;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnswerCommand))]
    public partial bool IsAnswered { get; set; }

    [ObservableProperty]
    public partial string? Given { get; set; }

    /// <summary>One of the question's buttons.</summary>
    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private void Answer(string? option) => AnswerWith(option);

    private bool CanAnswer(string? option) => !IsAnswered && !string.IsNullOrWhiteSpace(option);

    /// <summary>Answers with <paramref name="text"/>; false when it is empty or the question was already answered.</summary>
    public bool AnswerWith(string? text)
    {
        if (IsAnswered || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Given = text.Trim();
        IsAnswered = true;
        _answer.TrySetResult(Given);
        return true;
    }

    /// <summary>Ends the wait if the conversation closes, or Claude stops, before an answer.</summary>
    public void Abandon()
    {
        if (!IsAnswered)
        {
            Given = "(no answer)";
            IsAnswered = true;
        }

        _answer.TrySetResult("");
    }
}

/// <summary>Claude's summary of the quote, shown as a card.</summary>
public sealed class ReportItem : ChatItem
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public ReportItem(QuoteReport report)
    {
        Report = report;
        Headline = $"{report.QuoteNumber}: {report.Total.ToString("C2", Us)} "
            + $"({report.PerSample.ToString("C2", Us)} per sample, {report.StudySamples:0.##} study samples)";

        var sections = new List<ReportSection>();
        void Add(string title, IReadOnlyList<string> lines)
        {
            // Claude sometimes reports "none" instead of an empty list; a section saying only
            // that is noise on the card.
            var real = lines.Where(l => !IsNothing(l)).ToList();
            if (real.Count > 0)
            {
                sections.Add(new ReportSection(title, real));
            }
        }

        if (!string.IsNullOrWhiteSpace(report.ChangeSummary))
        {
            sections.Add(new ReportSection("What changed", [report.ChangeSummary]));
        }

        Add("Flags", report.Flags);
        Add("Rate overrides", report.RateOverrides);
        Add("Not priced", report.NotPriced);
        Add("Assumptions", report.Assumptions);
        Add("Questions for Mike", report.Questions);
        Sections = sections;
    }

    public QuoteReport Report { get; }

    /// <summary>"none", "None.", "none: the email asked for nothing else", and the like.</summary>
    internal static bool IsNothing(string line)
    {
        var text = line.Trim().TrimStart('-', ' ').Trim();
        // "none" then punctuation, never "none of the samples ...", which is a real statement.
        string[] markers = ["none.", "none:", "none,", "none;", "none -", "none —", "none –", "none ("];
        return text.Equals("none", StringComparison.OrdinalIgnoreCase)
            || markers.Any(m => text.StartsWith(m, StringComparison.OrdinalIgnoreCase));
    }

    public string Headline { get; }

    public IReadOnlyList<ReportSection> Sections { get; }
}

public sealed record ReportSection(string Title, IReadOnlyList<string> Lines);
