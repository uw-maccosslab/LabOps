using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using ChargeState.Core.Engines;
using ChargeState.Core.Infrastructure;
using ChargeState.Core.Processes;
using ChargeState.Core.Repositories;
using ChargeState.Core.Sync;

namespace ChargeState.App.Services;

/// <summary>
/// The one piece of work the app is doing at a time, shared by the Projects and Quotes areas so
/// neither starts something while the other is busy.
/// </summary>
public sealed partial class WorkTracker(ILogger<WorkTracker> log) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsWorking { get; private set; }

    [ObservableProperty]
    public partial string? WorkingText { get; private set; }

    /// <summary>The latest word from work done in the background, such as a wiki page update.</summary>
    [ObservableProperty]
    public partial string? Notice { get; set; }

    /// <summary>
    /// How recent a sync with GitHub makes a fetch before the next change unnecessary. After a
    /// save the app shares in the background, so a person clicking through steps gets no fetch
    /// between them; the rebase before each share still brings in anything newer.
    /// </summary>
    public static readonly TimeSpan RecentSync = TimeSpan.FromSeconds(60);

    /// <summary>Runs <paramref name="work"/>, showing <paramref name="text"/>, and reports failures to the user.</summary>
    public async Task RunAsync(string text, Func<Task> work)
    {
        IsWorking = true;
        WorkingText = text;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await work().ConfigureAwait(true);
            log.LogDebug("{Work} took {Milliseconds} ms", text, (int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is EngineException or GitException or ToolMissingException or InvalidOperationException or IOException)
        {
            log.LogWarning(ex, "{Work} failed.", text);
            MessageBox.Show(ex.Message, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsWorking = false;
            WorkingText = null;
        }
    }

    /// <summary>
    /// Brings in others' work before a change, so it starts from the latest. Waits for any share
    /// still running, and skips the fetch when this copy synced in the last minute.
    /// </summary>
    /// <returns>The sync's result, or null when none was needed.</returns>
    public static async Task<SaveResult?> PullFirstAsync(Repository repository)
    {
        await repository.Sync.WhenIdleAsync().ConfigureAwait(true);
        return repository.Sync.SyncedWithin(RecentSync) ? null : await repository.Sync.SyncAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Shares a saved change with GitHub while the person carries on, and reports what that ran
    /// into. Being offline is left to the status bar: the commit waits on this computer, and the
    /// next sync shares it.
    /// </summary>
    public static async Task<SaveResult?> ShareInBackgroundAsync(Repository repository)
    {
        var result = await repository.Sync.ShareAsync().ConfigureAwait(true);
        return result.Error is not null && result.Conflict is null && repository.Sync.Status.State == SyncState.Offline ? null : result;
    }

    /// <summary>
    /// Tells the user what a save or sync ran into. After a conflict, offers to set their version
    /// aside so syncing works again.
    /// </summary>
    /// <returns>The set-aside branch and the item it concerns, when the user chose to set aside.</returns>
    public static async Task<(string Branch, string? Item)?> HandleSaveResultAsync(Repository repository, SaveResult result)
    {
        if (result.Conflict is { } conflict)
        {
            var answer = MessageBox.Show(
                $"{conflict.Message}\n\nSet your version aside and use theirs? Your version is kept on this computer, and Claude can redo your change on top of theirs.",
                AppInfo.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return null;
            }

            var branch = await repository.Sync.SetAsideAsync().ConfigureAwait(true);
            return (branch, conflict.Items.FirstOrDefault());
        }

        if (result.Refused is { Count: > 0 } refused)
        {
            MessageBox.Show(
                "Nothing was saved. The check found information that must not go into the shared repository:\n\n- "
                + string.Join("\n- ", refused)
                + "\n\nFix these in the files, then try again.",
                AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (result.Error is { } error)
        {
            MessageBox.Show(error, AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return null;
    }
}
