using Microsoft.Extensions.Logging;
using LabOps.Core.GitHub;
using LabOps.Core.Infrastructure;
using Velopack;
using Velopack.Sources;

namespace LabOps.App.Services;

/// <summary>Where the update workflow stands.</summary>
public enum UpdateStage
{
    Idle,
    NotInstalled,
    Checking,
    UpToDate,
    Downloading,
    ReadyToApply,
    Failed,
}

public sealed record UpdateStatus(UpdateStage Stage, string? AvailableVersion = null, int DownloadPercent = 0, string? Error = null);

/// <summary>
/// Checks GitHub Releases for a newer app, downloads it in the background, and tells the user.
/// </summary>
/// <remarks>
/// As in PanoramaBridge, it never restarts the app on its own (the user may be partway through
/// a quote with Claude) and never throws out of a check: an unreachable GitHub must not stop
/// anyone working. The app repository is internal, so the feed is read with the user's own
/// GitHub sign-in, taken from gh at check time and never stored or logged.
/// </remarks>
public sealed class UpdateService
{
    private readonly GitHubCli _gh;
    private readonly AppSettings _settings;
    private readonly ILogger<UpdateService> _log;
    private UpdateManager? _manager;
    private UpdateInfo? _staged;

    public UpdateService(GitHubCli gh, AppSettings settings, ILogger<UpdateService> log)
    {
        _gh = gh;
        _settings = settings;
        _log = log;
    }

    /// <summary>How often a running copy looks for a new release, as in PanoramaBridge.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);

    public event Action<UpdateStatus>? StatusChanged;

    public UpdateStatus Status { get; private set; } = new(UpdateStage.Idle);

    /// <summary>When the last check started, or null before the first.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>
    /// Whether a periodic check should run now. Never while one is running or downloading, never
    /// once an update is staged (it waits for the user's restart, and checking again would only
    /// download it a second time), and never for a copy Velopack does not manage, such as a
    /// development build.
    /// </summary>
    public static bool IsCheckDue(UpdateStatus status, DateTimeOffset? lastChecked, DateTimeOffset now, TimeSpan interval) =>
        status.Stage is not (UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.ReadyToApply or UpdateStage.NotInstalled)
        && (lastChecked is null || now - lastChecked.Value >= interval);

    public async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (Status.Stage == UpdateStage.ReadyToApply)
        {
            return Status;
        }

        LastChecked = DateTimeOffset.Now;
        try
        {
            var token = await _gh.GetTokenAsync(cancellationToken).ConfigureAwait(false);

            // prerelease: true so the channel, not GitHub's prerelease flag, decides who gets a
            // build (see PanoramaBridge's UpdateService for how that bit them).
            var source = new GithubSource(AppInfo.RepositoryUrl, accessToken: token, prerelease: true);
            _manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = _settings.BetaUpdates ? AppInfo.BetaUpdateChannel : AppInfo.UpdateChannel });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update manager could not be initialized; updates are disabled.");
            return Publish(new UpdateStatus(UpdateStage.Failed, Error: ex.Message));
        }

        if (!_manager.IsInstalled)
        {
            return Publish(new UpdateStatus(UpdateStage.NotInstalled));
        }

        Publish(new UpdateStatus(UpdateStage.Checking));
        UpdateInfo? update;
        try
        {
            update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed.");
            return Publish(new UpdateStatus(UpdateStage.Failed, Error: ex.Message));
        }

        if (update is null)
        {
            return Publish(new UpdateStatus(UpdateStage.UpToDate));
        }

        var version = update.TargetFullRelease.Version.ToString();
        _log.LogInformation("Update {Version} available; downloading.", version);
        try
        {
            await _manager.DownloadUpdatesAsync(update,
                percent => Publish(new UpdateStatus(UpdateStage.Downloading, version, percent)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Downloading update {Version} failed.", version);
            return Publish(new UpdateStatus(UpdateStage.Failed, version, Error: ex.Message));
        }

        _staged = update;
        return Publish(new UpdateStatus(UpdateStage.ReadyToApply, version, 100));
    }

    /// <summary>Applies the staged update and restarts. Call only when nothing is in progress.</summary>
    public bool ApplyAndRestart()
    {
        if (_manager is null || _staged is null)
        {
            return false;
        }

        _log.LogInformation("Applying update {Version} and restarting.", _staged.TargetFullRelease.Version);
        _manager.ApplyUpdatesAndRestart(_staged.TargetFullRelease);
        return true;
    }

    private UpdateStatus Publish(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
        return status;
    }
}
