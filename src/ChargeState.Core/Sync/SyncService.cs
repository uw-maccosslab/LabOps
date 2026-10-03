using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChargeState.Core.Sync;

/// <summary>Where the local copy stands relative to GitHub.</summary>
public enum SyncState
{
    Unknown,
    UpToDate,
    Behind,
    Ahead,
    Diverged,
    Syncing,
    Conflict,
    Offline,
    Error,
}

/// <summary>Everything the status bar shows about syncing.</summary>
public sealed record SyncStatus(
    SyncState State,
    int Ahead = 0,
    int Behind = 0,
    IReadOnlyList<string>? LocalChanges = null,
    string? Message = null,
    DateTimeOffset? LastSynced = null)
{
    public IReadOnlyList<string> Changes => LocalChanges ?? [];
}

/// <summary>Someone else changed the same quote. Nothing was lost; the user's commit is kept locally.</summary>
public sealed record SyncConflict(IReadOnlyList<string> Files, IReadOnlyList<string> QuoteNumbers, string? OtherAuthor)
{
    public string Message
    {
        get
        {
            var who = string.IsNullOrWhiteSpace(OtherAuthor) ? "Someone else" : OtherAuthor;
            var what = QuoteNumbers.Count > 0 ? string.Join(", ", QuoteNumbers) : string.Join(", ", Files);
            return $"{who} changed {what} at the same time. Your change is saved on this computer but "
                + "has not been shared. You can redo your change on top of the latest version.";
        }
    }
}

/// <summary>The outcome of saving and syncing.</summary>
public sealed record SaveResult(bool Committed, bool Pushed, SyncConflict? Conflict = null, string? Error = null)
{
    public bool Succeeded => Conflict is null && Error is null;
}

/// <summary>Rebuilds a quote's generated files; used to resolve conflicts in them.</summary>
public interface IGeneratedFileRebuilder
{
    /// <summary>Rebuilds the quote in <paramref name="folder"/> (relative, forward slashes).</summary>
    Task RebuildAsync(string folder, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the local clone and GitHub in step the way pwiz-ai does: commit directly to main,
/// always rebase before pushing, never create a merge commit, never force-push.
/// </summary>
/// <remarks>
/// <para>
/// Quotes live in separate folders, so two people working on different quotes touch different
/// files and a rebase replays one person's commit on top of the other's with no conflict at all.
/// That is the normal case, and it needs nothing from the user.
/// </para>
/// <para>
/// Generated files (calculation.md and quote.md) can conflict when two people change the same
/// quote's inputs in compatible ways, or when quote.py itself changed. They are never merged by
/// hand: the conflict is resolved by rebuilding them from the merged quote.yaml, which is what
/// they would have contained anyway. The README index is taken from GitHub, where a workflow
/// regenerates it.
/// </para>
/// <para>
/// A conflict in anything a person edits, quote.yaml above all, is real, and guessing would lose
/// someone's work. The rebase is aborted, the user's commit stays on this computer, and the user
/// is told who changed the quote.
/// </para>
/// Every operation runs under one lock, so a background fetch can never overlap a save.
/// </remarks>
public sealed partial class SyncService
{
    private const string Remote = "origin";
    private const string Branch = "main";
    private const int MaxPushAttempts = 3;

    private readonly GitClient _git;
    private readonly IGeneratedFileRebuilder _rebuilder;
    private readonly ILogger<SyncService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncService(GitClient git, IGeneratedFileRebuilder rebuilder, ILogger<SyncService>? log = null)
    {
        _git = git;
        _rebuilder = rebuilder;
        _log = log ?? NullLogger<SyncService>.Instance;
    }

    /// <summary>Raised when the status changes. May fire on a background thread.</summary>
    public event Action<SyncStatus>? StatusChanged;

    /// <summary>Raised after a sync brought in other people's commits, so the list should reload.</summary>
    public event Action? RepositoryUpdated;

    public SyncStatus Status { get; private set; } = new(SyncState.Unknown);

    /// <summary>Re-reads ahead/behind and local changes, fetching first when asked.</summary>
    public async Task<SyncStatus> RefreshAsync(bool fetch, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (fetch)
            {
                var fetched = await _git.RunAsync(["fetch", "--quiet", Remote, Branch], cancellationToken).ConfigureAwait(false);
                if (!fetched.Succeeded)
                {
                    return Publish(new SyncStatus(OfflineOrError(fetched.ErrorText), Message: Friendly(fetched.ErrorText),
                        LastSynced: Status.LastSynced));
                }
            }

            return Publish(await ReadStatusAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Commits the given paths (if anything changed) and syncs with GitHub.</summary>
    /// <param name="paths">Paths relative to the repository, usually quote folders.</param>
    /// <param name="message">Commit message, for example <c>MacCoss-2026-NWU-SC: draft</c>.</param>
    public async Task<SaveResult> SaveAsync(IEnumerable<string> paths, string message, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(Status with { State = SyncState.Syncing, Message = "Saving..." });
            var committed = await CommitAsync(paths.ToList(), message, cancellationToken).ConfigureAwait(false);
            var result = await SyncCoreAsync(cancellationToken).ConfigureAwait(false);
            return result with { Committed = committed };
        }
        catch (GitException ex)
        {
            Publish(new SyncStatus(SyncState.Error, Message: Friendly(ex.Message), LastSynced: Status.LastSynced));
            return new SaveResult(false, false, Error: Friendly(ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Brings in other people's work and shares any local commits.</summary>
    public async Task<SaveResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SyncCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (GitException ex)
        {
            Publish(new SyncStatus(SyncState.Error, Message: Friendly(ex.Message), LastSynced: Status.LastSynced));
            return new SaveResult(false, false, Error: Friendly(ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// After a conflict: keeps the user's version on a local branch and moves main back to
    /// GitHub's version, so syncing works again and nothing is lost.
    /// </summary>
    /// <remarks>
    /// The branch stays on this computer. Its name goes to Claude, which can read the set-aside
    /// change with <c>git show</c> (allowed, read-only) and reapply it to the current quote.
    /// </remarks>
    /// <returns>The name of the branch holding the user's version.</returns>
    public async Task<string> SetAsideAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Anything not yet committed goes onto the set-aside branch too, before the reset.
            await CommitAsync(["quotes"], "Set aside after a conflict", cancellationToken).ConfigureAwait(false);

            var branch = $"set-aside/{DateTime.Now:yyyyMMdd-HHmmss}";
            await _git.RequireAsync(["branch", branch, "HEAD"], cancellationToken).ConfigureAwait(false);
            await _git.RequireAsync(["fetch", "--quiet", Remote, Branch], cancellationToken).ConfigureAwait(false);
            await _git.RequireAsync(["reset", "--hard", $"{Remote}/{Branch}"], cancellationToken).ConfigureAwait(false);
            _log.LogWarning("Set the local version aside on {Branch} and reset main to GitHub.", branch);

            Publish((await ReadStatusAsync(cancellationToken).ConfigureAwait(false)) with { LastSynced = DateTimeOffset.Now });
            RepositoryUpdated?.Invoke();
            return branch;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> CommitAsync(IReadOnlyList<string> paths, string message, CancellationToken ct)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        await _git.RequireAsync(["add", "-A", "--", .. paths], ct).ConfigureAwait(false);

        // Exit code 1 means something is staged; 0 means there is nothing to commit.
        var staged = await _git.RunAsync(["diff", "--cached", "--quiet"], ct).ConfigureAwait(false);
        if (staged.ExitCode == 0)
        {
            return false;
        }

        await _git.RequireAsync(["commit", "--quiet", "-m", message], ct).ConfigureAwait(false);
        _log.LogInformation("Committed: {Message}", message);
        return true;
    }

    private async Task<SaveResult> SyncCoreAsync(CancellationToken ct)
    {
        Publish(Status with { State = SyncState.Syncing, Message = "Syncing with GitHub..." });

        for (var attempt = 1; attempt <= MaxPushAttempts; attempt++)
        {
            var fetched = await _git.RunAsync(["fetch", "--quiet", Remote, Branch], ct).ConfigureAwait(false);
            if (!fetched.Succeeded)
            {
                Publish(new SyncStatus(OfflineOrError(fetched.ErrorText), Message: Friendly(fetched.ErrorText), LastSynced: Status.LastSynced));
                return new SaveResult(false, false, Error: Friendly(fetched.ErrorText));
            }

            var before = (await _git.RequireAsync(["rev-parse", "HEAD"], ct).ConfigureAwait(false)).Trim();
            var conflict = await RebaseAsync(ct).ConfigureAwait(false);
            if (conflict is not null)
            {
                var status = await ReadStatusAsync(ct).ConfigureAwait(false);
                Publish(status with { State = SyncState.Conflict, Message = conflict.Message });
                return new SaveResult(false, false, conflict);
            }

            var after = (await _git.RequireAsync(["rev-parse", "HEAD"], ct).ConfigureAwait(false)).Trim();
            var upstreamChanged = await BroughtInCommitsAsync(before, ct).ConfigureAwait(false);

            var current = await ReadStatusAsync(ct).ConfigureAwait(false);
            if (current.Ahead == 0)
            {
                if (upstreamChanged || before != after)
                {
                    RepositoryUpdated?.Invoke();
                }

                Publish(current with { LastSynced = DateTimeOffset.Now });
                return new SaveResult(false, false);
            }

            var pushed = await _git.RunAsync(["push", "--quiet", Remote, $"HEAD:{Branch}"], ct).ConfigureAwait(false);
            if (pushed.Succeeded)
            {
                if (upstreamChanged)
                {
                    RepositoryUpdated?.Invoke();
                }

                Publish((await ReadStatusAsync(ct).ConfigureAwait(false)) with { LastSynced = DateTimeOffset.Now });
                return new SaveResult(false, true);
            }

            if (!IsRejectedBecauseBehind(pushed.ErrorText))
            {
                Publish(new SyncStatus(OfflineOrError(pushed.ErrorText), current.Ahead, current.Behind, current.Changes,
                    Friendly(pushed.ErrorText), Status.LastSynced));
                return new SaveResult(false, false, Error: Friendly(pushed.ErrorText));
            }

            // Someone pushed between our fetch and our push. Rebase onto their work and try again.
            _log.LogInformation("Push rejected because GitHub moved on; rebasing again (attempt {Attempt}).", attempt);
        }

        const string busy = "GitHub kept changing while syncing. Try Sync again in a moment.";
        Publish(Status with { State = SyncState.Error, Message = busy });
        return new SaveResult(false, false, Error: busy);
    }

    /// <summary>Rebases local commits onto GitHub's main, resolving generated-file conflicts.</summary>
    /// <returns>The conflict that stopped it, or null when the rebase finished.</returns>
    private async Task<SyncConflict?> RebaseAsync(CancellationToken ct)
    {
        var result = await _git.RunAsync(["rebase", "--autostash", $"{Remote}/{Branch}"], ct).ConfigureAwait(false);

        // A rebase of one commit can stop several times, once per conflicting commit.
        for (var round = 0; !result.Succeeded && _git.RebaseInProgress() && round < 50; round++)
        {
            var conflicted = Lines(await _git.RequireAsync(["diff", "--name-only", "--diff-filter=U"], ct).ConfigureAwait(false));
            if (conflicted.Count == 0)
            {
                // Stopped with nothing in conflict and nothing staged: after resolution this
                // commit's change was already on GitHub, so it has become empty. Skip it.
                var staged = await _git.RunAsync(["diff", "--cached", "--quiet"], ct).ConfigureAwait(false);
                result = staged.ExitCode == 0
                    ? await _git.RunAsync(["rebase", "--skip"], ct).ConfigureAwait(false)
                    : await _git.RunAsync(["rebase", "--continue"], ct).ConfigureAwait(false);
                continue;
            }

            if (!conflicted.All(IsGenerated))
            {
                return await AbortWithConflictAsync(conflicted, ct).ConfigureAwait(false);
            }

            foreach (var file in conflicted.Where(f => f == "README.md"))
            {
                // During a rebase "ours" is the branch being rebased onto: GitHub's version.
                await _git.RequireAsync(["checkout", "--ours", "--", file], ct).ConfigureAwait(false);
                await _git.RequireAsync(["add", "--", file], ct).ConfigureAwait(false);
            }

            foreach (var folder in conflicted.Where(f => f != "README.md").Select(QuoteFolder).Distinct())
            {
                _log.LogInformation("Rebuilding {Folder} to resolve a conflict in its generated files.", folder);
                foreach (var file in conflicted.Where(f => f.StartsWith(folder + "/", StringComparison.Ordinal)))
                {
                    // Any side will do; the rebuild below overwrites it from the merged quote.yaml.
                    await _git.RequireAsync(["checkout", "--theirs", "--", file], ct).ConfigureAwait(false);
                }

                await _rebuilder.RebuildAsync(folder, ct).ConfigureAwait(false);
                await _git.RequireAsync(["add", "-A", "--", folder], ct).ConfigureAwait(false);
            }

            result = await _git.RunAsync(["rebase", "--continue"], ct).ConfigureAwait(false);
        }

        if (!result.Succeeded && _git.RebaseInProgress())
        {
            return await AbortWithConflictAsync([], ct).ConfigureAwait(false);
        }

        if (!result.Succeeded)
        {
            throw new GitException(result.ErrorText);
        }

        return null;
    }

    private async Task<SyncConflict> AbortWithConflictAsync(IReadOnlyList<string> files, CancellationToken ct)
    {
        var personal = files.Where(f => !IsGenerated(f)).ToList();
        await _git.RunAsync(["rebase", "--abort"], ct).ConfigureAwait(false);

        string? author = null;
        if (personal.Count > 0)
        {
            var log = await _git.RunAsync(["log", "-1", "--format=%an", $"{Remote}/{Branch}", "--", personal[0]], ct).ConfigureAwait(false);
            author = log.Succeeded ? log.StandardOutput.Trim() : null;
        }

        var quotes = personal.Where(f => f.StartsWith("quotes/", StringComparison.Ordinal))
            .Select(f => QuoteFolder(f).Split('/').Last()).Distinct().ToList();
        _log.LogWarning("Sync stopped on a conflict in {Files}.", string.Join(", ", files));
        return new SyncConflict(personal.Count > 0 ? personal : files, quotes, author);
    }

    private async Task<bool> BroughtInCommitsAsync(string before, CancellationToken ct)
    {
        // True when origin/main has commits that the pre-rebase HEAD did not.
        var result = await _git.RunAsync(["rev-list", "--count", $"{before}..{Remote}/{Branch}"], ct).ConfigureAwait(false);
        return result.Succeeded && int.TryParse(result.StandardOutput.Trim(), out var n) && n > 0;
    }

    private async Task<SyncStatus> ReadStatusAsync(CancellationToken ct)
    {
        var output = await _git.RequireAsync(["status", "--porcelain=v1", "--branch"], ct).ConfigureAwait(false);
        var status = ParseStatus(output);
        return status with { LastSynced = Status.LastSynced };
    }

    /// <summary>Parses <c>git status --porcelain=v1 --branch</c>.</summary>
    internal static SyncStatus ParseStatus(string porcelain)
    {
        var lines = Lines(porcelain);
        int ahead = 0, behind = 0;
        if (lines.Count > 0 && lines[0].StartsWith("## ", StringComparison.Ordinal))
        {
            var header = lines[0];
            var a = AheadPattern().Match(header);
            var b = BehindPattern().Match(header);
            ahead = a.Success ? int.Parse(a.Groups[1].Value) : 0;
            behind = b.Success ? int.Parse(b.Groups[1].Value) : 0;
            lines = lines.Skip(1).ToList();
        }

        var changes = lines.Where(l => l.Length > 3).Select(l => l[3..].Trim('"')).ToList();
        var state = (ahead, behind) switch
        {
            (0, 0) => SyncState.UpToDate,
            (> 0, 0) => SyncState.Ahead,
            (0, > 0) => SyncState.Behind,
            _ => SyncState.Diverged,
        };

        var message = state switch
        {
            SyncState.UpToDate => changes.Count == 0 ? "Up to date" : $"{changes.Count} unsaved change(s)",
            SyncState.Ahead => $"{ahead} change(s) not yet shared",
            SyncState.Behind => $"{behind} new change(s) on GitHub",
            _ => $"{ahead} to share, {behind} to bring in",
        };

        return new SyncStatus(state, ahead, behind, changes, message);
    }

    /// <summary>Files the app regenerates and therefore never asks a person to merge.</summary>
    internal static bool IsGenerated(string path) =>
        path == "README.md"
        || (path.StartsWith("quotes/", StringComparison.Ordinal)
            && (path.EndsWith("/calculation.md", StringComparison.Ordinal) || path.EndsWith("/quote.md", StringComparison.Ordinal)));

    /// <summary>quotes/Group/year/number from any path inside a quote folder.</summary>
    internal static string QuoteFolder(string path) => string.Join('/', path.Split('/').Take(4));

    internal static bool IsRejectedBecauseBehind(string error) =>
        error.Contains("rejected", StringComparison.OrdinalIgnoreCase)
        && (error.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
            || error.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase));

    private static SyncState OfflineOrError(string error) =>
        error.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase)
        || error.Contains("unable to access", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)
            ? SyncState.Offline
            : SyncState.Error;

    /// <summary>Turns git's wording into something a non-expert can act on.</summary>
    internal static string Friendly(string error)
    {
        if (error.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub cannot be reached. Your work is saved on this computer and will be shared when you are back online.";
        }

        if (error.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
            || error.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
            || error.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub did not accept your sign-in. Open Setup and sign in to GitHub again.";
        }

        if (error.Contains("Please tell me who you are", StringComparison.OrdinalIgnoreCase))
        {
            return "Git does not know your name yet. Open Setup and complete the Git identity step.";
        }

        return error.Trim();
    }

    private SyncStatus Publish(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
        return status;
    }

    private static List<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    [GeneratedRegex(@"ahead (\d+)")]
    private static partial Regex AheadPattern();

    [GeneratedRegex(@"behind (\d+)")]
    private static partial Regex BehindPattern();
}
