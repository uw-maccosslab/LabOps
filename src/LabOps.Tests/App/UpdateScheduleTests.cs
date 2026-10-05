using LabOps.App.Services;

namespace LabOps.Tests.App;

/// <summary>When a running copy looks for a new release.</summary>
public sealed class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 15, 0, 0, TimeSpan.FromHours(-7));
    private static readonly TimeSpan Interval = UpdateService.CheckInterval;

    [Fact]
    public void Checks_every_four_hours() => Interval.ShouldBe(TimeSpan.FromHours(4));

    [Fact]
    public void Due_when_never_checked_or_the_interval_has_passed()
    {
        UpdateService.IsCheckDue(new UpdateStatus(UpdateStage.Idle), null, Now, Interval).ShouldBeTrue();
        UpdateService.IsCheckDue(new UpdateStatus(UpdateStage.UpToDate), Now.AddHours(-4), Now, Interval).ShouldBeTrue();
        UpdateService.IsCheckDue(new UpdateStatus(UpdateStage.Failed), Now.AddHours(-5), Now, Interval).ShouldBeTrue();
    }

    [Fact]
    public void Not_due_before_the_interval_has_passed() =>
        UpdateService.IsCheckDue(new UpdateStatus(UpdateStage.UpToDate), Now.AddHours(-3.9), Now, Interval).ShouldBeFalse();

    [Theory]
    [InlineData(UpdateStage.Checking)]
    [InlineData(UpdateStage.Downloading)]
    [InlineData(UpdateStage.ReadyToApply)]
    [InlineData(UpdateStage.NotInstalled)]
    public void Never_while_busy_staged_or_unmanaged(UpdateStage stage) =>
        UpdateService.IsCheckDue(new UpdateStatus(stage), Now.AddDays(-2), Now, Interval).ShouldBeFalse();

    [Fact]
    public void A_restart_that_left_the_update_waiting_did_not_install_it()
    {
        UpdateService.InstallFailed(restartedByInstaller: true, updateStillWaiting: true).ShouldBeTrue();
        // Installed: the update is the version running now, and nothing newer is waiting.
        UpdateService.InstallFailed(restartedByInstaller: true, updateStillWaiting: false).ShouldBeFalse();
        // Started by hand with an update downloaded: the ordinary "ready" case.
        UpdateService.InstallFailed(restartedByInstaller: false, updateStillWaiting: true).ShouldBeFalse();
    }

    [Fact]
    public void The_button_says_when_an_update_did_not_install_and_why()
    {
        LabOps.App.ViewModels.MainViewModel.UpdateTextFor(new UpdateStatus(UpdateStage.ReadyToApply, "26.8.2", 100))
            .ShouldBe("Update 26.8.2 ready: restart to install");
        var why = UpdateService.InstallFailedMessage("26.8.2");
        LabOps.App.ViewModels.MainViewModel.UpdateTextFor(new UpdateStatus(UpdateStage.ReadyToApply, "26.8.2", 100, why))
            .ShouldBe("Update 26.8.2 did not install: see why");
        why.ShouldContain("another program was still using LabOps's folder");
        why.ShouldContain("Close those (or restart Windows), then try again.");
    }
}
