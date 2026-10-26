using Microsoft.Win32;

namespace ShutdownTimer;

/// <summary>
/// Start with Windows through the per-user Run key (no admin rights needed). Windows' Task Manager
/// "Startup apps" switch is kept separately in StartupApproved, so that is read here too.
/// </summary>
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "ShutdownTimer";
    public const string BackgroundArg = "--background";

    static string Command => $"\"{Environment.ProcessPath}\" {BackgroundArg}";

    /// <summary>Unavailable in --dry-run, so trying it out never changes what starts with Windows.</summary>
    public static bool Available => !ShutdownCommand.DryRun;

    static bool Registered
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(ValueName) is string;
        }
    }

    /// <summary>Registered, but switched off in Task Manager.</summary>
    public static bool DisabledInTaskManager
    {
        get
        {
            if (!Available || !Registered) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return !IsApproved(approved?.GetValue(ValueName) as byte[]);
        }
    }

    public static bool Enabled => Available && Registered && !DisabledInTaskManager;

    /// <summary>StartupApproved values start with an even byte (02, 06) when on and an odd one (03, 07) when off. No value means on.</summary>
    public static bool IsApproved(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static void Set(bool on)
    {
        if (!Available) return;
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) run.SetValue(ValueName, Command);
            else run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // Turning it on here is the user's explicit choice, so it also clears a Task Manager "off".
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Points the Run value at this exe again if it was moved since it was registered.</summary>
    public static void FixPath()
    {
        if (!Available) return;
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            run.SetValue(ValueName, Command);
    }
}
