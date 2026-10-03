using ChargeState.Core.Processes;

namespace ChargeState.Core.Sync;

/// <summary>A git command failed; the message is git's own explanation.</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>Runs git in the quotes repository.</summary>
public sealed class GitClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    // Rebase and commit must never open an editor: there is nobody at a console to close it.
    private static readonly Dictionary<string, string?> NoEditor = new()
    {
        ["GIT_EDITOR"] = "true",
        ["GIT_SEQUENCE_EDITOR"] = "true",
    };

    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;

    public GitClient(IProcessRunner runner, ToolLocator tools)
    {
        _runner = runner;
        _tools = tools;
    }

    public string? RepositoryPath { get; set; }

    /// <summary>Runs git and returns the result whatever the exit code.</summary>
    public Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default) =>
        _runner.RunAsync(
            _tools.Require(Tool.Git),
            arguments,
            RepositoryPath ?? throw new GitException("No quotes repository is set up yet."),
            NoEditor,
            Timeout,
            cancellationToken);

    /// <summary>Runs git and returns its output, throwing <see cref="GitException"/> on failure.</summary>
    public async Task<string> RequireAsync(IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new GitException(result.ErrorText);
        }

        return result.StandardOutput;
    }

    /// <summary>True while a rebase is stopped partway (for example on a conflict).</summary>
    public bool RebaseInProgress()
    {
        var gitDir = Path.Combine(RepositoryPath ?? "", ".git");
        return Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDir, "rebase-apply"));
    }
}
