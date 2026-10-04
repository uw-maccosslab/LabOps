namespace ChargeState.App.Services;

/// <summary>
/// Ends the process a few seconds after the window has closed, if it has not ended by itself.
/// </summary>
/// <remarks>
/// A process that outlives its window holds the single-instance lock, and the app then will not
/// start again until it is ended in Task Manager. Whatever might hang during the last cleanup
/// (disposing a service, a child process that will not exit), this makes sure that cannot
/// happen. The timer runs on a pool thread, so a blocked UI thread cannot stop it.
/// </remarks>
public static class ExitWatchdog
{
    private static Timer? _timer;

    public static void Arm(int exitCode, TimeSpan grace)
    {
        _timer = new Timer(_ =>
        {
            Serilog.Log.Warning("Shutdown did not finish within {Grace}; ending the process.", grace);
            Serilog.Log.CloseAndFlush();
            Environment.Exit(exitCode);
        }, null, grace, Timeout.InfiniteTimeSpan);
    }
}
