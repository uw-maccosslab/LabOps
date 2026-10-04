using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabOps.Core.Claude;

namespace LabOps.App.ViewModels;

/// <summary>One entry in the chat pane.</summary>
public abstract class ChatItem : ObservableObject;

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

/// <summary>A question from Claude, answered with a button or by typing.</summary>
public sealed partial class QuestionItem : ChatItem
{
    private readonly TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public QuestionItem(string question, IReadOnlyList<string> options)
    {
        Question = question;
        Options = options;
        Typed = "";
    }

    public string Question { get; }

    public IReadOnlyList<string> Options { get; }

    public Task<string> Response => _answer.Task;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnswerCommand))]
    public partial string Typed { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnswerCommand))]
    public partial bool IsAnswered { get; set; }

    [ObservableProperty]
    public partial string? Given { get; set; }

    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private void Answer(string? option)
    {
        var text = string.IsNullOrWhiteSpace(option) ? Typed.Trim() : option;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Given = text;
        IsAnswered = true;
        _answer.TrySetResult(text);
    }

    private bool CanAnswer(string? option) => !IsAnswered && (!string.IsNullOrWhiteSpace(option) || !string.IsNullOrWhiteSpace(Typed));

    /// <summary>Ends the wait if the conversation closes before an answer.</summary>
    public void Abandon() => _answer.TrySetResult("");
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
