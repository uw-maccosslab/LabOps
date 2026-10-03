using ServicesQuotes.Core.Infrastructure;
using ServicesQuotes.Core.Processes;

namespace ServicesQuotes.Core.Claude;

/// <summary>Starts Claude Code sessions configured for quote work.</summary>
/// <remarks>
/// The allowlist is what Claude may do without asking: read and edit files, run quote.py, and
/// look at (not change) git history. Anything else goes to the app's permission dialog. Git
/// commands that change history are refused outright, because the app alone commits and syncs;
/// two writers to one clone is how a non-expert ends up with a broken repository.
/// </remarks>
public sealed class ClaudeLauncher
{
    public static readonly IReadOnlyList<string> AllowedTools =
    [
        "Read", "Edit", "Write", "Glob", "Grep", "Skill", "TodoWrite",
        "Bash(uv run python scripts/quote.py:*)",
        "Bash(python scripts/quote.py:*)",
        "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)",
        .. AppTools.ModelToolNames,
    ];

    public static readonly IReadOnlyList<string> DisallowedTools =
    [
        "Bash(git commit:*)", "Bash(git push:*)", "Bash(git pull:*)", "Bash(git fetch:*)",
        "Bash(git rebase:*)", "Bash(git reset:*)", "Bash(git checkout:*)", "Bash(git switch:*)",
        "Bash(git merge:*)", "Bash(git stash:*)", "Bash(git clean:*)", "Bash(git restore:*)",
        // Requests are untrusted input; nothing in one should be able to send Claude to a website.
        "WebFetch", "WebSearch",
    ];

    private readonly ToolLocator _tools;
    private readonly ProcessRunner _runner;
    private readonly AppPaths _paths;

    public ClaudeLauncher(ToolLocator tools, ProcessRunner runner, AppPaths paths)
    {
        _tools = tools;
        _runner = runner;
        _paths = paths;
    }

    public ClaudeSessionOptions CreateOptions(string repositoryPath, AppToolServer server, string? userName, string? resumeSessionId, string? model) =>
        new()
        {
            ClaudePath = _tools.Require(Tool.Claude),
            WorkingDirectory = repositoryPath,
            ResumeSessionId = resumeSessionId,
            Model = string.IsNullOrWhiteSpace(model) ? null : model,
            McpConfigPath = server.WriteMcpConfig(_paths.SessionDirectory),
            PermissionPromptTool = AppTools.PermissionToolName,
            AllowedTools = AllowedTools,
            DisallowedTools = DisallowedTools,
            AppendSystemPrompt = SystemPrompt(userName),
            Environment = new Dictionary<string, string?>
            {
                // A tool call that waits for the user (a question, a permission dialog) must not
                // time out while they read. One hour is far longer than anyone takes.
                ["MCP_TOOL_TIMEOUT"] = "3600000",
            },
        };

    public ClaudeSession Start(ClaudeSessionOptions options) => ClaudeSession.Start(options, _runner);

    internal static string SystemPrompt(string? userName) =>
        $"""
        You are running inside the Services Quotes desktop app{(userName is null ? "" : $", working with {userName}")}.
        The person you are talking to may not be technical: write plainly and briefly, and do not show them commands or file paths unless they ask. Do not use em dashes.
        Commands already run in the quotes repository folder: run them as written, without a cd prefix, so they match what is pre-approved.
        Use the new-quote skill to draft a quote and the revise-quote skill to change one.
        Ask questions with the ask_user tool, and finish by calling report_quote_summary.
        The app commits and syncs the quote folder when you finish. Never commit, push, or change git history yourself.
        """;
}
