using System.Diagnostics;
using System.Globalization;

namespace ChargeState.Core.Infrastructure;

/// <summary>
/// Ensures one ChargeState window per signed-in user, and lets a second launch reach the first.
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
/// <para>
/// A process that is alive but stuck (its window closed, its UI thread blocked) still holds the
/// lock. So the running copy acknowledges a second launch from its UI thread, and a launch that
/// gets no answer can offer to end the stuck copy, whose process ID is kept beside the lock.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream? _lock;
    private readonly string _pidFile;
    private readonly EventWaitHandle? _wakeExisting;
    private readonly EventWaitHandle? _acknowledged;
    private readonly ManualResetEventSlim _stopping = new(false);

    private Thread? _listener;
    private bool _disposed;

    private SingleInstance(bool isFirst, FileStream? heldLock, string pidFile, EventWaitHandle? wakeExisting, EventWaitHandle? acknowledged)
    {
        IsFirst = isFirst;
        _lock = heldLock;
        _pidFile = pidFile;
        _wakeExisting = wakeExisting;
        _acknowledged = acknowledged;
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

        var pidFile = Path.ChangeExtension(lockFile, ".pid");
        var wake = Event($@"Local\{name}.wake");
        var acknowledged = Event($@"Local\{name}.ack");

        try
        {
            var held = new FileStream(
                lockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);

            try
            {
                File.WriteAllText(pidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only costs a later launch the ability to end exactly this process if it gets stuck.
            }

            return new SingleInstance(isFirst: true, held, pidFile, wake, acknowledged);
        }
        catch (IOException)
        {
            // Another copy holds it.
            return new SingleInstance(isFirst: false, heldLock: null, pidFile, wake, acknowledged);
        }
        catch (Exception)
        {
            // An unwritable directory or a policy denying the open: start anyway (see remarks).
            return new SingleInstance(isFirst: true, heldLock: null, pidFile, wake, acknowledged);
        }
    }

    private static EventWaitHandle? Event(string name)
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
        catch (Exception)
        {
            // Only costs the ability to raise the running window; exclusion does not depend on it.
            return null;
        }
    }

    /// <summary>
    /// Runs <paramref name="show"/> when another launch asks for the window. First instance only.
    /// <paramref name="show"/> should call <see cref="Acknowledge"/> once the window is shown.
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
            Name = "ChargeState second-launch listener",
        };

        _listener.Start();
    }

    /// <summary>Tells a second launch that this copy is alive and has come forward.</summary>
    public void Acknowledge()
    {
        try
        {
            _acknowledged?.Set();
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    /// <summary>Asks the running instance to show itself, and waits for it to answer.</summary>
    /// <returns>True when the running copy answered within <paramref name="timeout"/>.</returns>
    public bool SignalExisting(TimeSpan timeout)
    {
        if (IsFirst || _wakeExisting is null || _acknowledged is null)
        {
            return false;
        }

        try
        {
            // An answer left over from an earlier launch must not count for this one.
            _acknowledged.Reset();
            return _wakeExisting.Set() && _acknowledged.WaitOne(timeout);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The process ID the running copy recorded, if it did (copies before 26.3.0 did not).</summary>
    public int? ExistingProcessId()
    {
        try
        {
            return int.TryParse(File.ReadAllText(_pidFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                ? pid
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ends a stuck copy of the app: the recorded process, else (a copy from before the ID was
    /// recorded) every other process of this executable. Waits a few seconds for each to go.
    /// </summary>
    public static void EndStuckCopy(int? processId)
    {
        using var self = Process.GetCurrentProcess();
        if (processId == self.Id)
        {
            return;
        }

        var targets = new List<Process>();
        if (processId is { } id)
        {
            try
            {
                var p = Process.GetProcessById(id);
                if (string.Equals(p.ProcessName, self.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    targets.Add(p);
                }
                else
                {
                    p.Dispose();
                }
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }
        else
        {
            targets.AddRange(Process.GetProcessesByName(self.ProcessName).Where(p => p.Id != self.Id && p.SessionId == self.SessionId));
        }

        foreach (var p in targets)
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited meanwhile, or not ours to end; the next Acquire reports it.
            }
            finally
            {
                p.Dispose();
            }
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
            if (_lock is not null)
            {
                File.Delete(_pidFile);
            }

            // DeleteOnClose removes the file as the handle closes.
            _lock?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The lock is released by the handle closing either way.
        }

        _wakeExisting?.Dispose();
        _acknowledged?.Dispose();
        _stopping.Dispose();
    }
}
