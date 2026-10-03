using System.Text.Json;
using ChargeState.Core.Infrastructure;
using ChargeState.Core.Processes;

namespace ChargeState.Core.GitHub;

/// <summary>The latest run of the quotes repository's check workflow.</summary>
public sealed record CheckRun(string Status, string? Conclusion, string Url, string HeadSha, DateTimeOffset CreatedAt)
{
    public bool InProgress => Status is "queued" or "in_progress" or "waiting" or "pending" or "requested";

    public bool Passed => Status == "completed" && Conclusion == "success";

    public bool Failed => Status == "completed" && Conclusion is "failure" or "timed_out" or "startup_failure";
}

/// <summary>The signed-in GitHub user, as the commit identity is built from it.</summary>
public sealed record GitHubUser(string Login, string? Name, long Id)
{
    /// <summary>GitHub's private address for commits, so no personal email is published.</summary>
    public string NoReplyEmail => $"{Id}+{Login}@users.noreply.github.com";
}

/// <summary>
/// The GitHub command-line tool (gh). One browser sign-in through gh covers pulling and pushing
/// (gh is git's credential helper after setup), the API calls below, and the update feed.
/// </summary>
public sealed class GitHubCli
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;

    public GitHubCli(IProcessRunner runner, ToolLocator tools)
    {
        _runner = runner;
        _tools = tools;
    }

    public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default)
    {
        if (_tools.Find(Tool.GitHubCli) is null)
        {
            return false;
        }

        var result = await RunAsync(["auth", "status", "--hostname", "github.com"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<GitHubUser?> GetUserAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["api", "user"], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(result.StandardOutput);
        var root = doc.RootElement;
        return new GitHubUser(
            root.GetProperty("login").GetString()!,
            root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
            root.GetProperty("id").GetInt64());
    }

    /// <summary>The user's token, for reading the app's own release feed. Null when signed out.</summary>
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_tools.Find(Tool.GitHubCli) is null)
        {
            return null;
        }

        var result = await RunAsync(["auth", "token", "--hostname", "github.com"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    public async Task<bool> CanAccessQuotesRepositoryAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["repo", "view", AppInfo.QuotesRepository, "--json", "name"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    public async Task<CheckRun?> GetLatestCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["run", "list", "--repo", AppInfo.QuotesRepository, "--workflow", "check.yml", "--branch", "main",
             "--limit", "1", "--json", "status,conclusion,url,headSha,createdAt"],
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? ParseCheckRun(result.StandardOutput) : null;
    }

    public async Task<bool> RunChecksAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["workflow", "run", "check.yml", "--repo", AppInfo.QuotesRepository, "--ref", "main"],
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    /// <summary>Clones the quotes repository into <paramref name="path"/>.</summary>
    public async Task<ProcessResult> CloneQuotesRepositoryAsync(string path, CancellationToken cancellationToken = default) =>
        await _runner.RunAsync(
            _tools.Require(Tool.GitHubCli),
            // autocrlf off from the first checkout, so a global autocrlf=true (the Git for Windows
            // default) can never make files look changed when the clone is later set to false.
            ["repo", "clone", AppInfo.QuotesRepository, path, "--", "-c", "core.autocrlf=false"],
            Path.GetDirectoryName(path),
            timeout: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken).ConfigureAwait(false);

    internal static CheckRun? ParseCheckRun(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var run = doc.RootElement[0];
        return new CheckRun(
            run.GetProperty("status").GetString() ?? "",
            run.TryGetProperty("conclusion", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() != "" ? c.GetString() : null,
            run.GetProperty("url").GetString() ?? "",
            run.GetProperty("headSha").GetString() ?? "",
            run.GetProperty("createdAt").GetDateTimeOffset());
    }

    private Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(_tools.Require(Tool.GitHubCli), arguments, timeout: Timeout, cancellationToken: cancellationToken);
}
