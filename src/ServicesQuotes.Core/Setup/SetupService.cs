using System.Text.Json;
using ServicesQuotes.Core.GitHub;
using ServicesQuotes.Core.Infrastructure;
using ServicesQuotes.Core.Processes;
using ServicesQuotes.Core.Quotes;

namespace ServicesQuotes.Core.Setup;

/// <summary>One thing the app needs, in the order setup walks through them.</summary>
public enum SetupStep
{
    Git,
    GitHubCli,
    GitHubSignIn,
    Claude,
    ClaudeSignIn,
    Repository,
    GitIdentity,
    PythonEnvironment,
}

/// <summary>A command that needs a visible console, because it asks the user something.</summary>
/// <param name="FileName">Program to run.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="Explanation">What the user will see and do, shown before the console opens.</param>
public sealed record ConsoleCommand(string FileName, IReadOnlyList<string> Arguments, string Explanation);

/// <summary>The state of one setup step.</summary>
public sealed record SetupItem(SetupStep Step, string Title, bool Done, string Detail, string? FixLabel, string? AlternateLabel = null)
{
    public bool CanFix => !Done && FixLabel is not null;

    /// <summary>A second way to complete the step, offered even once it is done (for example, switching clones).</summary>
    public bool HasAlternate => AlternateLabel is not null;
}

/// <summary>
/// Checks what the app needs and offers to fix what is missing, so a new user goes from the
/// installer to a working app without opening a terminal themselves.
/// </summary>
/// <remarks>
/// Installs use the official sources only: winget for Git and the GitHub CLI, and Anthropic's
/// installer for Claude Code. Sign-ins open each tool's own browser flow in a console window,
/// because they show a one-time code the user has to see; the app never handles a password.
/// </remarks>
public sealed class SetupService
{
    private readonly IProcessRunner _runner;
    private readonly ToolLocator _tools;
    private readonly GitHubCli _gh;

    public SetupService(IProcessRunner runner, ToolLocator tools, GitHubCli gh)
    {
        _runner = runner;
        _tools = tools;
        _gh = gh;
    }

    public async Task<IReadOnlyList<SetupItem>> CheckAsync(string? repositoryPath, CancellationToken cancellationToken = default)
    {
        var items = new List<SetupItem>();
        var git = _tools.Find(Tool.Git);
        items.Add(new(SetupStep.Git, "Git", git is not null,
            git is not null ? "Installed." : "Git keeps the quotes in sync with GitHub.", "Install Git"));

        var gh = _tools.Find(Tool.GitHubCli);
        items.Add(new(SetupStep.GitHubCli, "GitHub command-line tool", gh is not null,
            gh is not null ? "Installed." : "Used to sign in to GitHub once for everything.", "Install"));

        var signedIn = gh is not null && await _gh.IsSignedInAsync(cancellationToken).ConfigureAwait(false);
        var user = signedIn ? await _gh.GetUserAsync(cancellationToken).ConfigureAwait(false) : null;
        items.Add(new(SetupStep.GitHubSignIn, "GitHub sign-in", signedIn,
            signedIn ? $"Signed in as {user?.Login}." : "Sign in with the GitHub account that has access to the quotes.",
            gh is null ? null : "Sign in"));

        var claude = _tools.Find(Tool.Claude);
        items.Add(new(SetupStep.Claude, "Claude Code", claude is not null,
            claude is not null ? "Installed." : "Claude drafts and revises quotes.", "Install Claude Code"));

        var claudeAuth = claude is not null ? await ClaudeStatusAsync(claude, cancellationToken).ConfigureAwait(false) : null;
        items.Add(new(SetupStep.ClaudeSignIn, "Claude sign-in", claudeAuth?.LoggedIn == true,
            claudeAuth?.LoggedIn == true
                ? $"Signed in{(claudeAuth.Email is null ? "" : $" as {claudeAuth.Email}")}{(claudeAuth.OrgName is null ? "" : $" ({claudeAuth.OrgName})")}."
                : "Sign in with your lab Claude account.",
            claude is null ? null : "Sign in"));

        var repoReady = repositoryPath is not null && Directory.Exists(Path.Combine(repositoryPath, ".git"))
            && File.Exists(Path.Combine(repositoryPath, "scripts", "quote.py"));
        items.Add(new(SetupStep.Repository, "Quotes repository", repoReady,
            repoReady ? repositoryPath! : "A copy of the quotes on this computer. Download one, or point to a copy you already have.",
            signedIn ? "Download the quotes" : null,
            git is null ? null : repoReady ? "Use a different copy" : "Use a copy I already have"));

        var identity = repoReady && git is not null ? await GitIdentityAsync(git, repositoryPath!, cancellationToken).ConfigureAwait(false) : null;
        items.Add(new(SetupStep.GitIdentity, "Git identity", identity is not null,
            identity is not null ? $"Changes are recorded as {identity}." : "Your name on the changes you save.",
            repoReady && signedIn ? "Use my GitHub name" : null));

        var python = repoReady && Directory.Exists(Path.Combine(repositoryPath!, ".venv"));
        items.Add(new(SetupStep.PythonEnvironment, "Quote engine", python,
            python ? "Ready." : "Python and the packages quote.py needs (downloaded once).",
            repoReady ? "Prepare" : null));

        return items;
    }

    public static bool AllDone(IEnumerable<SetupItem> items) => items.All(i => i.Done);

    /// <summary>The console command that fixes a step, or null for steps the app does itself.</summary>
    /// <remarks>
    /// Everything runs through PowerShell with single-quoted paths. cmd.exe strips the first and
    /// last quote of a /c command line, which breaks any tool path containing a space, such as
    /// C:\Program Files\GitHub CLI\gh.exe.
    /// </remarks>
    public ConsoleCommand? ConsoleFix(SetupStep step) => step switch
    {
        SetupStep.Git => PowerShell(
            "winget install --id Git.Git --exact --source winget",
            "A console window installs Git with winget, Windows' own installer. Accept any prompts it shows, then close it."),
        SetupStep.GitHubCli => PowerShell(
            "winget install --id GitHub.cli --exact --source winget",
            "A console window installs the GitHub command-line tool with winget. Accept any prompts it shows, then close it."),
        SetupStep.GitHubSignIn => PowerShell(
            $"& {Quote(_tools.Require(Tool.GitHubCli))} auth login --hostname github.com --web --git-protocol https; "
                + $"if ($?) {{ & {Quote(_tools.Require(Tool.GitHubCli))} auth setup-git }}",
            "A console window opens and shows a one-time code. Your browser opens GitHub: enter the code "
            + "and approve. Then close the console window."),
        SetupStep.Claude => PowerShell(
            "irm https://claude.ai/install.ps1 | iex",
            "A console window runs Anthropic's official Claude Code installer. Close it when it says the install finished."),
        SetupStep.ClaudeSignIn => PowerShell(
            $"& {Quote(_tools.Require(Tool.Claude))} auth login",
            "A console window opens and your browser opens Claude. Sign in with your lab account, then close the console window."),
        _ => null,
    };

    private static ConsoleCommand PowerShell(string script, string explanation) => new(
        "powershell.exe",
        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script + "; Read-Host 'Done. Press Enter to close this window'"],
        explanation);

    private static string Quote(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>Clones the quotes repository and configures it for rebase-only syncing.</summary>
    public async Task<string> CloneAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(Path.Combine(path, ".git")))
        {
            await ConfigureRepositoryAsync(path, cancellationToken).ConfigureAwait(false);
            return path;
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"{path} already contains other files. Choose an empty or new folder.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = await _gh.CloneQuotesRepositoryAsync(path, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"The quotes could not be downloaded: {result.ErrorText}");
        }

        await ConfigureRepositoryAsync(path, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>Sets this clone's commit identity from the GitHub account.</summary>
    public async Task SetIdentityFromGitHubAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var user = await _gh.GetUserAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Sign in to GitHub first.");
        await GitConfigAsync(repositoryPath, "user.name", string.IsNullOrWhiteSpace(user.Name) ? user.Login : user.Name!, cancellationToken).ConfigureAwait(false);
        await GitConfigAsync(repositoryPath, "user.email", user.NoReplyEmail, cancellationToken).ConfigureAwait(false);
    }

    public static string? FindExistingClone(string? configured) =>
        new[] { configured, AppPaths.DefaultClonePath }
            .FirstOrDefault(p => p is not null && File.Exists(Path.Combine(p, "scripts", "quote.py")) && Directory.Exists(Path.Combine(p, ".git")));

    /// <summary>
    /// Adopts a clone the user already has (for example one they use from a terminal), after
    /// checking it really is the quotes repository, so the app does not make a second copy.
    /// </summary>
    public async Task<string> UseExistingCloneAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(path, ".git")) || !File.Exists(Path.Combine(path, "scripts", "quote.py")))
        {
            throw new InvalidOperationException($"{path} is not a copy of the quotes repository. Choose the folder that contains scripts\\quote.py.");
        }

        var remote = await _runner.RunAsync(_tools.Require(Tool.Git), ["remote", "get-url", "origin"], path,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!remote.Succeeded || !IsQuotesRemote(remote.StandardOutput))
        {
            throw new InvalidOperationException(
                $"{path} is a git repository, but not a copy of github.com/{AppInfo.QuotesRepository} (its origin is {remote.StandardOutput.Trim()}).");
        }

        await ConfigureRepositoryAsync(path, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>True for the https and ssh forms of the quotes repository's URL.</summary>
    internal static bool IsQuotesRemote(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.EndsWith("github.com/" + AppInfo.QuotesRepository, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("github.com:" + AppInfo.QuotesRepository, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ConfigureRepositoryAsync(string path, CancellationToken cancellationToken)
    {
        // Settings local to this clone, so terminal users of the same folder sync the same way.
        // Line endings are not touched here: a fresh clone gets autocrlf=false from the clone
        // command itself, and an existing clone keeps whatever its owner chose.
        await GitConfigAsync(path, "pull.rebase", "true", cancellationToken).ConfigureAwait(false);
        await GitConfigAsync(path, "rebase.autoStash", "true", cancellationToken).ConfigureAwait(false);
    }

    private async Task GitConfigAsync(string repositoryPath, string key, string value, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(_tools.Require(Tool.Git), ["config", "--local", key, value], repositoryPath,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Git could not be configured: {result.ErrorText}");
        }
    }

    private async Task<string?> GitIdentityAsync(string git, string repositoryPath, CancellationToken cancellationToken)
    {
        var name = await _runner.RunAsync(git, ["config", "user.name"], repositoryPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        var email = await _runner.RunAsync(git, ["config", "user.email"], repositoryPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        return name.Succeeded && email.Succeeded && name.StandardOutput.Trim().Length > 0
            ? $"{name.StandardOutput.Trim()} <{email.StandardOutput.Trim()}>"
            : null;
    }

    private async Task<ClaudeAuth?> ClaudeStatusAsync(string claude, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(claude, ["auth", "status"], timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseClaudeAuth(result.StandardOutput);
    }

    internal static ClaudeAuth? ParseClaudeAuth(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new ClaudeAuth(
                root.TryGetProperty("loggedIn", out var l) && l.ValueKind == JsonValueKind.True,
                Text("email"), Text("orgName"), Text("subscriptionType"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record ClaudeAuth(bool LoggedIn, string? Email, string? OrgName, string? Subscription);
}
