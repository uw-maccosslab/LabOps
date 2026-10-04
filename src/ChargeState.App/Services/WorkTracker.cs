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

    /// <summary>Runs <paramref name="work"/>, showing <paramref name="text"/>, and reports failures to the user.</summary>
    public async Task RunAsync(string text, Func<Task> work)
    {
        IsWorking = true;
        WorkingText = text;
        try
        {
            await work().ConfigureAwait(true);
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
