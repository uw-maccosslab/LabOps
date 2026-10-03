using ChargeState.App.Services;

namespace ChargeState.Tests.App;

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
}
