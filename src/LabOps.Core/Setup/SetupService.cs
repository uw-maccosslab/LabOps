using System.Text.Json;
using LabOps.Core.GitHub;
using LabOps.Core.Processes;
using LabOps.Core.Repositories;

namespace LabOps.Core.Setup;

/// <summary>One thing the app needs, in the order setup walks through them.</summary>
public enum SetupStep
{
    Git,
    GitHubCli,
    GitHubSignIn,
    Claude,
    ClaudeSignIn,
    ProjectsRepository,
    ProjectsEngine,
    ProtocolsRepository,
    ProtocolsEngine,
    QuotesRepository,
    QuotesEngine,
    GitIdentity,
}

/// <summary>A command that needs a visible console, because it asks the user something.</summary>
/// <param name="FileName">Program to run.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="Explanation">What the user will see and do, shown before the console opens.</param>
public sealed record ConsoleCommand(string FileName, IReadOnlyList<string> Arguments, string Explanation);

/// <summary>The state of one setup step.</summary>
/// <param name="Step">Which step.</param>
/// <param name="Title">Its name in the checklist.</param>
/// <param name="Done">Whether it is complete.</param>
/// <param name="Detail">What it is for, or how it stands.</param>
/// <param name="FixLabel">The button that completes it, or null when nothing can be done yet.</param>
/// <param name="AlternateLabel">A second button, offered even once it is done.</param>
/// <param name="Optional">Not needed to use the app (the quotes, for people who do not prepare them).</param>
public sealed record SetupItem(
    SetupStep Step, string Title, bool Done, string Detail, string? FixLabel, string? AlternateLabel = null, bool Optional = false)
{
    public bool CanFix => !Done && FixLabel is not null;

    /// <summary>The repository a step belongs to, if any.</summary>
    public RepositoryProfile? Profile => Step switch
    {
        SetupStep.ProjectsRepository or SetupStep.ProjectsEngine => RepositoryProfile.Projects,
        SetupStep.ProtocolsRepository or SetupStep.ProtocolsEngine => RepositoryProfile.Protocols,
        SetupStep.QuotesRepository or SetupStep.QuotesEngine => RepositoryProfile.Quotes,
        _ => null,
    };

    /// <summary>A second way to complete the step, offered even once it is done (for example, switching clones).</summary>
    public bool HasAlternate => AlternateLabel is not null;
}

/// <summary>
/// Checks what the app needs and offers to fix what is missing, so a new user goes from the
/// installer to a working app without opening a terminal themselves.
/// </summary>
/// <remarks>
/// Installs use the official sources only: winget for Git and the GitHub CLI, and Anthropic's
/// installer for Claude Code. GitHub's sign-in opens its browser flow in a console window, because
/// it shows a one-time code the user has to see. Claude's runs with no window
/// (<see cref="Claude.ClaudeLogin"/>), since its page sends the answer back by itself; the console
/// (<see cref="ConsoleFix"/>) is the fallback. The app never handles a password.
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

    /// <param name="projectsPath">The projects clone, if there is one.</param>
    /// <param name="quotesPath">The quotes clone, if there is one.</param>
    /// <param name="protocolsPath">The protocols clone, if there is one.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    public async Task<IReadOnlyList<SetupItem>> CheckAsync(
        string? projectsPath, string? quotesPath, string? protocolsPath = null, CancellationToken cancellationToken = default)
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
            signedIn ? $"Signed in as {user?.Login}." : "Sign in with the GitHub account you use for the lab.",
            gh is null ? null : "Sign in"));

        var claude = _tools.Find(Tool.Claude);
        items.Add(new(SetupStep.Claude, "Claude Code", claude is not null,
            claude is not null ? "Installed." : "Claude organizes sample metadata, updates projects and experiments, and drafts quotes.", "Install Claude Code"));

        var claudeAuth = claude is not null ? await ClaudeStatusAsync(claude, cancellationToken).ConfigureAwait(false) : null;
        // A sign-in whose session expired still reports as signed in, so Sign in again stays on
        // offer: it is how to fix "Failed to authenticate" from here.
        items.Add(new(SetupStep.ClaudeSignIn, "Claude sign-in", claudeAuth?.LoggedIn == true,
            claudeAuth?.LoggedIn == true
                ? $"Signed in{(claudeAuth.Email is null ? "" : $" as {claudeAuth.Email}")}{(claudeAuth.OrgName is null ? "" : $" ({claudeAuth.OrgName})")}."
                : "Sign in with your lab Claude account.",
            claude is null ? null : "Sign in",
            claude is not null && claudeAuth?.LoggedIn == true ? "Sign in again" : null));

        var projects = RepositoryProfile.Projects.LooksLikeClone(projectsPath);
        items.Add(new(SetupStep.ProjectsRepository, "Lab projects", projects,
            projects ? projectsPath! : "A copy of the lab's projects on this computer. Download one, or point to a copy you already have.",
            signedIn ? "Download the projects" : null,
            git is null ? null : projects ? "Use a different copy" : "Use a copy I already have"));
        items.Add(EngineItem(SetupStep.ProjectsEngine, "Project engine", "project.py", projects ? projectsPath : null));

        // The protocols are for the whole lab, but optional, so an app updated before anyone
        // downloads them still opens; the Protocols area offers the download.
        var protocols = RepositoryProfile.Protocols.LooksLikeClone(protocolsPath);
        var protocolAccess = protocols || !signedIn
            ? (bool?)protocols
            : await _gh.CanAccessRepositoryAsync(RepositoryProfile.Protocols.GitHubName, cancellationToken).ConfigureAwait(false);
        items.Add(new(SetupStep.ProtocolsRepository, "Lab protocols", protocols || protocolAccess == false,
            protocols ? protocolsPath!
                : protocolAccess == false ? "Your GitHub account cannot see the lab's protocols. Ask Mike to add you to the uw-maccosslab organization."
                : "The lab's protocols, to read, to write with Claude, and to link to a project's steps.",
            signedIn && protocolAccess != false ? "Download the protocols" : null,
            git is null || protocolAccess == false ? null : protocols ? "Use a different copy" : "Use a copy I already have",
            Optional: true));
        if (protocols)
        {
            items.Add(EngineItem(SetupStep.ProtocolsEngine, "Protocol engine", "protocol.py", protocolsPath) with { Optional = true });
        }

        // The quotes are for the people who prepare them. Ask GitHub before offering a download
        // that would fail, and leave the step out of "all done" either way.
        var quotes = RepositoryProfile.Quotes.LooksLikeClone(quotesPath);
        var access = quotes || !signedIn
            ? (bool?)quotes
            : await _gh.CanAccessRepositoryAsync(RepositoryProfile.Quotes.GitHubName, cancellationToken).ConfigureAwait(false);
        items.Add(new(SetupStep.QuotesRepository, "Quotes (optional)", quotes || access == false,
            quotes ? quotesPath!
                : access == false ? "Your GitHub account does not have access to the quotes, so the Quotes area is hidden. Ask Mike if you need it."
                : "Only for people who prepare Proteomics Services quotes.",
            signedIn && access != false ? "Download the quotes" : null,
            git is null || access == false ? null : quotes ? "Use a different copy" : "Use a copy I already have",
            Optional: true));
        if (quotes)
        {
            items.Add(EngineItem(SetupStep.QuotesEngine, "Quote engine", "quote.py", quotesPath) with { Optional = true });
        }

        var clones = new[] { projects ? projectsPath : null, protocols ? protocolsPath : null, quotes ? quotesPath : null }
            .OfType<string>().ToList();
        var identities = new List<string>();
        if (git is not null)
        {
            foreach (var clone in clones)
            {
                if (await GitIdentityAsync(git, clone, cancellationToken).ConfigureAwait(false) is { } identity)
                {
                    identities.Add(identity);
                }
            }
        }

        var named = clones.Count > 0 && identities.Count == clones.Count;
        items.Add(new(SetupStep.GitIdentity, "Git identity", named,
            named ? $"Changes are recorded as {identities[0]}." : "Your name on the changes you save.",
            clones.Count > 0 && signedIn ? "Use my GitHub name" : null));

        return items;
    }

    private static SetupItem EngineItem(SetupStep step, string title, string script, string? clone)
    {
        var ready = clone is not null && Directory.Exists(Path.Combine(clone, ".venv"));
        return new(step, title, ready, ready ? "Ready." : $"Python and the packages {script} needs (downloaded once).",
            clone is not null ? "Prepare" : null);
    }

    /// <summary>Whether the app can be used: every step done except the optional ones.</summary>
    public static bool AllDone(IEnumerable<SetupItem> items) => items.Where(i => !i.Optional).All(i => i.Done);

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
            "A console window opens and your browser opens Claude. Sign in with your lab account (if the page says the window is "
            + "too small, make it larger), choose Authorize, then close the console window."),
        _ => null,
    };

    private static ConsoleCommand PowerShell(string script, string explanation) => new(
        "powershell.exe",
        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script + "; Read-Host 'Done. Press Enter to close this window'"],
        explanation);

    private static string Quote(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>Clones a repository and configures it for rebase-only syncing.</summary>
    public async Task<string> CloneAsync(RepositoryProfile profile, string path, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(Path.Combine(path, ".git")))
        {
            return await UseExistingCloneAsync(profile, path, cancellationToken).ConfigureAwait(false);
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"{path} already contains other files. Choose an empty or new folder.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = await _gh.CloneRepositoryAsync(profile.GitHubName, path, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"The {profile.DisplayName} could not be downloaded: {result.ErrorText}");
        }

        await ConfigureRepositoryAsync(profile, path, cancellationToken).ConfigureAwait(false);
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

    /// <summary>The configured clone, else one in the default place, if either is a clone of the repository.</summary>
    public static string? FindExistingClone(RepositoryProfile profile, string? configured) =>
        new[] { configured, profile.DefaultClonePath }.FirstOrDefault(profile.LooksLikeClone);

    /// <summary>
    /// Adopts a clone the user already has (for example one they use from a terminal), after
    /// checking it really is the repository, so the app does not make a second copy.
    /// </summary>
    public async Task<string> UseExistingCloneAsync(RepositoryProfile profile, string path, CancellationToken cancellationToken = default)
    {
        if (!profile.LooksLikeClone(path))
        {
            throw new InvalidOperationException(
                $"{path} is not a copy of the {profile.DisplayName}. Choose the folder that contains {profile.EngineScript.Replace('/', '\\')}.");
        }

        var remote = await _runner.RunAsync(_tools.Require(Tool.Git), ["remote", "get-url", "origin"], path,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!remote.Succeeded || !profile.IsRemote(remote.StandardOutput))
        {
            throw new InvalidOperationException(
                $"{path} is a git repository, but not a copy of github.com/{profile.GitHubName} (its origin is {remote.StandardOutput.Trim()}).");
        }

        await ConfigureRepositoryAsync(profile, path, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private async Task ConfigureRepositoryAsync(RepositoryProfile profile, string path, CancellationToken cancellationToken)
    {
        // Settings local to this clone, so terminal users of the same folder sync the same way.
        // Line endings are not touched here: a fresh clone gets autocrlf=false from the clone
        // command itself, and an existing clone keeps whatever its owner chose.
        await GitConfigAsync(path, "pull.rebase", "true", cancellationToken).ConfigureAwait(false);
        await GitConfigAsync(path, "rebase.autoStash", "true", cancellationToken).ConfigureAwait(false);
        if (profile.ChecksCommits)
        {
            // The repository's pre-commit hook, so a commit from a terminal in this clone gets the
            // same identifier check the app runs before its own commits.
            await GitConfigAsync(path, "core.hooksPath", ".githooks", cancellationToken).ConfigureAwait(false);
        }
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
