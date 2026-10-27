namespace ShutdownTimer.Tests;

public class RepeatTests
{
    static readonly TimeSpan ElevenThirtyPm = new(23, 30, 0);
    static readonly DateTime Tuesday = new(2026, 10, 27, 12, 0, 0);
    const int Mon = 1 << (int)DayOfWeek.Monday, Wed = 1 << (int)DayOfWeek.Wednesday;

    [Fact]
    public void Every_day_fires_later_today() =>
        Assert.Equal(Tuesday.Date + ElevenThirtyPm, ShutdownPlanner.NextRepeat(Tuesday, ElevenThirtyPm, ShutdownPlanner.EveryDay));

    [Fact]
    public void Every_day_after_todays_time_fires_tomorrow() =>
        Assert.Equal(Tuesday.Date.AddDays(1) + ElevenThirtyPm,
            ShutdownPlanner.NextRepeat(Tuesday.Date + ElevenThirtyPm, ElevenThirtyPm, ShutdownPlanner.EveryDay));

    [Fact]
    public void Chosen_days_skip_to_the_next_one() =>
        Assert.Equal(new DateTime(2026, 11, 2) + ElevenThirtyPm, // Wednesday after the time → next Monday
            ShutdownPlanner.NextRepeat(new DateTime(2026, 10, 28, 23, 45, 0), ElevenThirtyPm, Mon | Wed));

    [Fact]
    public void Weekdays_skip_the_weekend() =>
        Assert.Equal(new DateTime(2026, 11, 2) + ElevenThirtyPm, // Friday after the time → Monday
            ShutdownPlanner.NextRepeat(new DateTime(2026, 10, 30, 23, 31, 0), ElevenThirtyPm, ShutdownPlanner.Weekdays));

    [Fact]
    public void Time_after_midnight_counts_from_the_next_day() =>
        Assert.Equal(new DateTime(2026, 10, 28, 1, 0, 0),
            ShutdownPlanner.NextRepeat(new DateTime(2026, 10, 27, 23, 0, 0), new TimeSpan(1, 0, 0), ShutdownPlanner.EveryDay));

    [Fact]
    public void Keeps_wall_clock_time_across_dst_change() => // EU clocks went back on Sun 25 Oct 2026
        Assert.Equal(new DateTime(2026, 10, 25) + ElevenThirtyPm,
            ShutdownPlanner.NextRepeat(new DateTime(2026, 10, 24, 23, 45, 0), ElevenThirtyPm, ShutdownPlanner.EveryDay));

    [Fact]
    public void No_days_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ShutdownPlanner.NextRepeat(Tuesday, ElevenThirtyPm, 0));

    [Theory]
    [InlineData(0, "Once")]
    [InlineData(ShutdownPlanner.EveryDay, "Every day")]
    [InlineData(ShutdownPlanner.Weekdays, "Weekdays")]
    [InlineData(ShutdownPlanner.Weekends, "Weekends")]
    [InlineData(Mon | Wed | 1, "Mon, Wed, Sun")]
    public void Describes_days(int days, string text) => Assert.Equal(text, ShutdownPlanner.DescribeDays(days));
}
