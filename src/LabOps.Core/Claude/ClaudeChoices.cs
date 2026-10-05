using System.Text.Json;

namespace LabOps.Core.Claude;

/// <summary>A model or effort level to run Claude with; a null <see cref="Value"/> leaves it to the person's Claude Code settings.</summary>
public sealed record ClaudeChoice(string? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The models and effort levels a person can choose, in Setup, for LabOps's conversations with
/// Claude. Both change how much of their own Claude plan a conversation uses, so each person
/// chooses for themselves; until they do, LabOps passes neither, and Claude Code uses the same
/// settings as in a terminal.
/// </summary>
public static class ClaudeChoices
{
    private const string Default = "Your Claude Code default";

    /// <summary>
    /// Models by their full names, so the version shown is the one used: versions of the same model
    /// differ a lot, and the bare aliases ("opus") move to each new version unannounced. Add a
    /// model here when it comes out.
    /// </summary>
    private static readonly ClaudeChoice[] KnownModels =
    [
        new("claude-opus-5-5", "Opus 5.5: the most capable; uses your plan the fastest"),
        new("claude-opus-5", "Opus 5: the previous Opus"),
        new("claude-sonnet-5-5", "Sonnet 5.5: capable and quicker; uses less of your plan"),
        new("claude-sonnet-5", "Sonnet 5: the previous Sonnet"),
        new("claude-haiku-4-5", "Haiku 4.5: the quickest; uses the least, for small changes"),
    ];

    /// <summary>The aliases LabOps 26.8.1 and 26.8.2 offered, still kept for whoever chose one.</summary>
    private static readonly Dictionary<string, string> AliasLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opus"] = "Opus, the latest version",
        ["sonnet"] = "Sonnet, the latest version",
        ["haiku"] = "Haiku, the latest version",
    };

    /// <summary>The levels Claude Code takes for <c>--effort</c>.</summary>
    private static readonly ClaudeChoice[] KnownEfforts =
    [
        new("low", "Low: the quickest; uses the least"),
        new("medium", "Medium"),
        new("high", "High"),
        new("xhigh", "Extra high: more thorough; uses more"),
        new("max", "Max: the most thorough; uses the most"),
    ];

    /// <param name="current">The model chosen so far, if any; one LabOps does not list is kept as a choice of its own.</param>
    /// <param name="claudeDefault">The model in the person's Claude Code settings, to name in the default's label.</param>
    public static IReadOnlyList<ClaudeChoice> Models(string? current, string? claudeDefault) =>
        Choices(KnownModels, current, claudeDefault);

    /// <param name="current">The effort chosen so far, if any.</param>
    /// <param name="claudeDefault">The effort in the person's Claude Code settings, to name in the default's label.</param>
    public static IReadOnlyList<ClaudeChoice> Efforts(string? current, string? claudeDefault) =>
        Choices(KnownEfforts, current, claudeDefault);

    /// <summary>The choice for a stored value: the default when there is none.</summary>
    public static ClaudeChoice Find(IReadOnlyList<ClaudeChoice> choices, string? value) =>
        choices.FirstOrDefault(c => string.Equals(c.Value, Normalize(value), StringComparison.OrdinalIgnoreCase)) ?? choices[0];

    /// <summary>
    /// The model and effort in the person's own Claude Code settings (<c>~/.claude/settings.json</c>,
    /// or under <c>CLAUDE_CONFIG_DIR</c>), when they set them there; nulls when not, or when the file
    /// cannot be read.
    /// </summary>
    public static (string? Model, string? Effort) ReadClaudeDefaults(string? settingsFile = null)
    {
        settingsFile ??= Path.Combine(
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir
                ? dir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            "settings.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsFile));
            string? Text(string name) =>
                doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var v)
                && v.ValueKind == JsonValueKind.String ? Normalize(v.GetString()) : null;
            return (Text("model"), Text("effortLevel"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    private static IReadOnlyList<ClaudeChoice> Choices(ClaudeChoice[] known, string? current, string? claudeDefault)
    {
        var choices = new List<ClaudeChoice> { new(null, Normalize(claudeDefault) is { } d ? $"{Default} ({d})" : Default) };
        choices.AddRange(known);
        if (Normalize(current) is { } value && !known.Any(c => string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new ClaudeChoice(value, AliasLabels.GetValueOrDefault(value, value)));
        }

        return choices;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
