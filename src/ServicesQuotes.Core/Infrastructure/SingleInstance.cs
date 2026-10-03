namespace ServicesQuotes.Core.Infrastructure;

/// <summary>
/// Ensures one Services Quotes window per signed-in user, and lets a second launch reach the first.
/// </summary>
/// <remarks>
/// <para>
/// Two copies would share one git clone. Each would commit, rebase and push on its own schedule,
/// and a rebase in one can rewrite files the other is in the middle of building, which is the
/// kind of tangle a non-expert user cannot get out of. So a second launch brings the first
/// window forward and exits.
/// </para>
/// <para>
/// Exclusion is a locked file in the per-user data directory, not a named mutex, as in
/// PanoramaBridge: the kernel's <c>Local\</c> namespace is per terminal session, while the clone
/// is shared by the account across sessions. The operating system releases the lock when the
/// process dies, so a crash cannot leave a stale lock behind.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream? _lock;
    private readonly EventWaitHandle? _wakeExisting;
    private readonly ManualResetEventSlim _stopping = new(false);

    private Thread? _listener;
    private bool _disposed;

    private SingleInstance(bool isFirst, FileStream? heldLock, EventWaitHandle? wakeExisting)
    {
        IsFirst = isFirst;
        _lock = heldLock;
        _wakeExisting = wakeExisting;
    }

    /// <summary>True when this process is the only one running.</summary>
    public bool IsFirst { get; }

    /// <summary>Claims the instance for this process.</summary>
    /// <remarks>
    /// A failure to create either handle reports <see cref="IsFirst"/> true. Refusing to start
    /// because the check itself could not be made would be worse than the duplicate it guards
    /// against.
    /// </remarks>
    public static SingleInstance Acquire(string name, string lockFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFile);

        EventWaitHandle? wake = null;

        try
        {
            wake = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.wake");
        }
        catch (Exception)
        {
            // Only costs the ability to raise the running window; exclusion does not depend on it.
        }

        try
        {
            var held = new FileStream(
                lockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            return new SingleInstance(isFirst: true, held, wake);
        }
        catch (IOException)
        {
            // Another copy holds it.
            return new SingleInstance(isFirst: false, heldLock: null, wake);
        }
        catch (Exception)
        {
            // An unwritable directory or a policy denying the open: start anyway (see remarks).
            return new SingleInstance(isFirst: true, heldLock: null, wake);
        }
    }

    /// <summary>
    /// Runs <paramref name="show"/> when another launch asks for the window. First instance only.
    /// </summary>
    /// <remarks>
    /// A background thread blocked on a wait handle rather than a timer, so it costs nothing idle.
    /// </remarks>
    public void ListenForSecondLaunch(Action show)
    {
        ArgumentNullException.ThrowIfNull(show);

        if (!IsFirst || _wakeExisting is null || _listener is not null || _disposed)
        {
            return;
        }

        _listener = new Thread(() =>
        {
            // Two handles, so shutdown is a signal rather than an abort.
            var handles = new[] { _wakeExisting, _stopping.WaitHandle };

            while (!_disposed)
            {
                if (WaitHandle.WaitAny(handles) != 0)
                {
                    return;
                }

                show();
            }
        })
        {
            IsBackground = true,
            Name = "Services Quotes second-launch listener",
        };

        _listener.Start();
    }

    /// <summary>Asks the running instance to show itself.</summary>
    /// <returns>True when there was one to ask.</returns>
    public bool SignalExisting()
    {
        if (IsFirst || _wakeExisting is null)
        {
            return false;
        }

        try
        {
            return _wakeExisting.Set();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));

        try
        {
            // DeleteOnClose removes the file as the handle closes.
            _lock?.Dispose();
        }
        catch (IOException)
        {
            // The lock is released by the handle closing either way.
        }

        _wakeExisting?.Dispose();
        _stopping.Dispose();
    }
}
