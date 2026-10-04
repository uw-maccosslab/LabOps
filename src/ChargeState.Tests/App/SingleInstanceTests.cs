using ChargeState.Core.Infrastructure;
using ChargeState.Tests.TestSupport;

namespace ChargeState.Tests.App;

/// <summary>
/// One copy at a time, and a second launch that can tell a live copy from a stuck one. A stuck
/// copy (window closed, process alive) used to leave the app impossible to start again.
/// </summary>
public sealed class SingleInstanceTests
{
    // A unique name per test, so the named events never meet another test's or a running app's.
    private static string Name() => "ChargeStateTest-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public void The_first_copy_records_its_process_and_a_second_is_refused()
    {
        using var temp = new TempDirectory();
        var name = Name();
        var lockFile = temp.Combine("instance.lock");

        using var first = SingleInstance.Acquire(name, lockFile);
        using var second = SingleInstance.Acquire(name, lockFile);

        first.IsFirst.ShouldBeTrue();
        second.IsFirst.ShouldBeFalse();
        second.ExistingProcessId().ShouldBe(Environment.ProcessId);
    }

    [Fact]
    public void A_live_copy_answers_a_second_launch()
    {
        using var temp = new TempDirectory();
        var name = Name();
        var lockFile = temp.Combine("instance.lock");
        using var first = SingleInstance.Acquire(name, lockFile);
        var shown = 0;
        first.ListenForSecondLaunch(() =>
        {
            Interlocked.Increment(ref shown);
            first.Acknowledge();
        });

        using var second = SingleInstance.Acquire(name, lockFile);

        second.SignalExisting(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        shown.ShouldBe(1);
    }

    [Fact]
    public void A_stuck_copy_does_not_answer()
    {
        using var temp = new TempDirectory();
        var name = Name();
        var lockFile = temp.Combine("instance.lock");
        using var first = SingleInstance.Acquire(name, lockFile);
        // Its listener runs, but the UI thread that would answer never gets to (it is blocked).
        first.ListenForSecondLaunch(() => { });

        using var second = SingleInstance.Acquire(name, lockFile);

        second.SignalExisting(TimeSpan.FromMilliseconds(500)).ShouldBeFalse();
    }

    [Fact]
    public void An_old_answer_does_not_count_for_a_new_launch()
    {
        using var temp = new TempDirectory();
        var name = Name();
        var lockFile = temp.Combine("instance.lock");
        using var first = SingleInstance.Acquire(name, lockFile);
        first.Acknowledge(); // left over, with nobody waiting
        first.ListenForSecondLaunch(() => { });

        using var second = SingleInstance.Acquire(name, lockFile);

        second.SignalExisting(TimeSpan.FromMilliseconds(500)).ShouldBeFalse();
    }

    [Fact]
    public void Closing_releases_the_lock_and_the_process_record()
    {
        using var temp = new TempDirectory();
        var name = Name();
        var lockFile = temp.Combine("instance.lock");

        SingleInstance.Acquire(name, lockFile).Dispose();

        File.Exists(temp.Combine("instance.pid")).ShouldBeFalse();
        using var again = SingleInstance.Acquire(name, lockFile);
        again.IsFirst.ShouldBeTrue();
    }

    [Fact]
    public void Ending_a_stuck_copy_never_ends_this_process_or_another_program()
    {
        // This process's own ID, and the ID of a process that is not ChargeState, are both left alone.
        SingleInstance.EndStuckCopy(Environment.ProcessId);
        using var other = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            SingleInstance.EndStuckCopy(other.Id);
            other.HasExited.ShouldBeFalse();
        }
        finally
        {
            other.Kill(entireProcessTree: true);
        }
    }
}
