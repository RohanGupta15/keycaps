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
