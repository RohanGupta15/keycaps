namespace ShutdownTimer;

public enum PlanAction
{
    /// <summary>The target is still ahead: restart Windows' countdown with the exact time left.</summary>
    Rearm,
    /// <summary>The target passed while the PC slept, but only just: shut down after a short warning.</summary>
    ShutDownSoon,
    /// <summary>The target passed too long ago to shut down without surprising the user: report it.</summary>
    Missed,
    /// <summary>Windows restarted after the target, so the shutdown (or a manual one) already happened.</summary>
    AlreadyDone,
}

public readonly record struct Plan(PlanAction Action, TimeSpan Delay);

/// <summary>
/// Decides what to do with a scheduled shutdown when the app can't trust Windows' countdown:
/// after the PC resumes from sleep, when the app starts, or when the target is overdue.
/// `shutdown /t` counts seconds that pause while the PC sleeps, so the wall clock is the source of truth.
/// </summary>
public static class ShutdownPlanner
{
    /// <summary>How late a shutdown may still run after waking. Beyond this it's reported as missed,
    /// so opening the lid the next morning never powers the PC off.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    /// <summary>Warning given before a late shutdown, so the user can cancel it.</summary>
    public static readonly TimeSpan Warning = TimeSpan.FromSeconds(60);

    /// <summary>How long before a shutdown the app warns, with Cancel and Snooze.</summary>
    public static readonly TimeSpan HeadsUp = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Snooze = TimeSpan.FromMinutes(15);

    /// <summary>Repeat days as a bit mask: bit n is <see cref="DayOfWeek"/> n. 0 means once.</summary>
    public const int EveryDay = 0b111_1111, Weekdays = 0b011_1110, Weekends = 0b100_0001;

    /// <summary>
    /// The first wall-clock time of day <paramref name="time"/>, on a repeat day, strictly after <paramref name="after"/>.
    /// Local wall-clock time, so a DST change doesn't move it; callers convert to UTC for the countdown.
    /// </summary>
    public static DateTime NextRepeat(DateTime after, TimeSpan time, int days)
    {
        if ((days & EveryDay) == 0)
            throw new ArgumentOutOfRangeException(nameof(days), "No repeat days.");
        for (var day = after.Date; ; day = day.AddDays(1))
            if ((days >> (int)day.DayOfWeek & 1) != 0 && day + time > after)
                return day + time;
    }

    public static string DescribeDays(int days) => days switch
    {
        0 => "Once",
        EveryDay => "Every day",
        Weekdays => "Weekdays",
        Weekends => "Weekends",
        _ => string.Join(", ", Enumerable.Range(0, 7).Select(i => (DayOfWeek)((i + 1) % 7)) // Monday first
            .Where(d => (days >> (int)d & 1) != 0).Select(d => d.ToString()[..3])),
    };

    public static Plan Decide(DateTime now, DateTime target, DateTime lastBoot)
    {
        if (lastBoot > target)
            return new(PlanAction.AlreadyDone, TimeSpan.Zero);

        var left = target - now;
        if (left > TimeSpan.Zero)
            return new(PlanAction.Rearm, left);

        return -left <= Grace
            ? new(PlanAction.ShutDownSoon, Warning)
            : new(PlanAction.Missed, TimeSpan.Zero);
    }
}
