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

    /// <summary>While a lab repository needs a newer app, changes wait, so the update is looked for every few minutes.</summary>
    [Fact]
    public void A_needed_update_is_looked_for_every_ten_minutes()
    {
        UpdateService.NeededCheckInterval.ShouldBe(TimeSpan.FromMinutes(10));
        UpdateService.IsCheckDue(new UpdateStatus(UpdateStage.UpToDate), Now.AddMinutes(-10), Now, UpdateService.NeededCheckInterval).ShouldBeTrue();
        UpdateService.IsCheckDue(new UpdateStatus(UpdateStage.UpToDate), Now.AddMinutes(-9), Now, UpdateService.NeededCheckInterval).ShouldBeFalse();
    }

    /// <summary>
    /// The banner of an app too old for a repository says how the update is going, and its button
    /// looks for the update or restarts into it; nothing to press while it is on its way.
    /// </summary>
    [Theory]
    [InlineData(UpdateStage.Idle, "", "Check for updates")]
    [InlineData(UpdateStage.Checking, " Looking for it...", null)]
    [InlineData(UpdateStage.Downloading, " Downloading 26.14.0 (40%)...", null)]
    [InlineData(UpdateStage.ReadyToApply, " 26.14.0 is downloaded: restart to install it.", "Restart to update")]
    [InlineData(UpdateStage.UpToDate, " It has not reached the update feed yet; LabOps looks again every few minutes.", "Check for updates")]
    [InlineData(UpdateStage.Failed, " Looking for it failed: offline", "Check for updates")]
    [InlineData(UpdateStage.NotInstalled, " This copy cannot update itself; download the new version.", "Download the new version")]
    public void A_too_old_app_says_how_its_update_is_going_and_offers_the_next_step(UpdateStage stage, string news, string? button)
    {
        var status = new UpdateStatus(stage, "26.14.0", 40, stage == UpdateStage.Failed ? "offline" : null);

        LabOps.App.ViewModels.MainViewModel.TooOldText("lab projects", "26.14.0", status)
            .ShouldBe($"This version of LabOps is too old for the lab projects (it needs 26.14.0), so changes wait for the update.{news}");
        LabOps.App.ViewModels.MainViewModel.TooOldAction(status).ShouldBe(button);
    }
}
