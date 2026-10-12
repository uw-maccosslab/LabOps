namespace LabOps.Core.Infrastructure;

/// <summary>
/// Runs an operation one at a time: asked again while it runs, it hands back the run in progress
/// instead of starting a second one. Once that run ends, the next request starts afresh.
/// </summary>
public sealed class OneAtATime<T>
{
    private readonly Lock _gate = new();
    private Task<T>? _running;

    /// <summary>The run in progress, or a new one started with <paramref name="start"/>.</summary>
    public Task<T> Run(Func<Task<T>> start)
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false } running)
            {
                return running;
            }

            return _running = start();
        }
    }
}
