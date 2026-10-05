using ShutdownTimer;

namespace ShutdownTimer.Tests;

public class ShutdownPlannerTests
{
    static readonly DateTime Target = new(2026, 9, 30, 3, 45, 0);
    static readonly DateTime BootedBefore = Target.AddDays(-1);

    [Fact]
    public void Target_still_ahead_rearms_with_exact_time_left()
    {
        var plan = ShutdownPlanner.Decide(Target.AddMinutes(-29).AddSeconds(-30), Target, BootedBefore);

        Assert.Equal(PlanAction.Rearm, plan.Action);
        Assert.Equal(TimeSpan.FromSeconds(29 * 60 + 30), plan.Delay);
    }

    [Theory]
    [InlineData(0)]     // woke exactly at the target
    [InlineData(1)]
    [InlineData(600)]   // 10 minutes late: still inside the grace window
    public void Woke_shortly_after_target_shuts_down_after_warning(int secondsLate)
    {
        var plan = ShutdownPlanner.Decide(Target.AddSeconds(secondsLate), Target, BootedBefore);

        Assert.Equal(PlanAction.ShutDownSoon, plan.Action);
        Assert.Equal(ShutdownPlanner.Warning, plan.Delay);
    }

    [Theory]
    [InlineData(601)]
    [InlineData(3 * 3600 + 13 * 60)]   // 30 Sep: target 3:45 AM, PC woke for good at 6:58 AM
    public void Woke_long_after_target_is_reported_missed_not_shut_down(int secondsLate)
    {
        var plan = ShutdownPlanner.Decide(Target.AddSeconds(secondsLate), Target, BootedBefore);

        Assert.Equal(PlanAction.Missed, plan.Action);
    }

    [Fact]
    public void Restart_after_target_means_shutdown_already_happened()
    {
        var bootedAfter = Target.AddMinutes(5);

        var plan = ShutdownPlanner.Decide(Target.AddHours(8), Target, bootedAfter);

        Assert.Equal(PlanAction.AlreadyDone, plan.Action);
    }

    [Fact]
    public void Restart_before_target_still_rearms()
    {
        // A reboot clears Windows' pending shutdown, so a future target must be re-armed.
        var plan = ShutdownPlanner.Decide(Target.AddHours(-1), Target, Target.AddHours(-2));

        Assert.Equal(PlanAction.Rearm, plan.Action);
        Assert.Equal(TimeSpan.FromHours(1), plan.Delay);
    }

    [Fact]
    public void Grace_window_is_ten_minutes_and_warning_one_minute()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), ShutdownPlanner.Grace);
        Assert.Equal(TimeSpan.FromSeconds(60), ShutdownPlanner.Warning);
    }
}
