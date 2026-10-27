using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ShutdownTimer;

// Schedules the shutdown with Windows' own `shutdown /s /t <seconds>`, so it still happens if this
// app exits. While a shutdown is pending the app also keeps the PC awake (from the tray), and
// because Windows' countdown pauses while the PC sleeps, it re-checks the wall clock after every
// resume and corrects the countdown (see ShutdownPlanner).
public partial class MainWindow : Window
{
    static string StateFile => Path.Combine(ShutdownCommand.DataFolder, "scheduled.txt");

    const double RingSize = 232, RingStroke = 8;

    /// <summary>A gap this long between 1-second ticks means the PC (or this process) was suspended.</summary>
    static readonly TimeSpan ResumeGap = TimeSpan.FromSeconds(5);

    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly KeepAwake keepAwake = new();
    readonly TrayIcon tray;

    int hour12 = 3;   // 1..12
    int minute;       // 0..59
    bool pm;
    DateTime? scheduledAt;
    DateTime scheduledFrom;   // when it was scheduled, for the progress ring
    DateTime? lateFor;        // original target, while running a late shutdown after sleep
    int repeatDays;           // ShutdownPlanner day mask: picked in the window, saved with the schedule. 0 = once
    TimeSpan repeatTime;      // time of day of the repeating schedule (a snoozed target may differ)
    bool warned;              // the heads-up before this target was shown
    DateTime lastTickUtc = DateTime.UtcNow;
    bool exiting;

    public MainWindow()
    {
        InitializeComponent();

        tray = new TrayIcon(ShowFromTray, CancelShutdown, SkipNext, ExitApp);

        tick.Tick += (_, _) => OnTick();
        tick.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        LoadState();
        SyncDayChips();
        Activated += (_, _) => RefreshAutoStartBar();   // it may have been switched in Task Manager
        if (scheduledAt is not null)
            Resync();   // the PC may have slept, or restarted, while the app was closed
        else
            OnScheduleChanged();
    }

    // Windows' last boot time. The tick count keeps counting through sleep, so this stays stable.
    static DateTime LastBoot => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    // ---------- Mica window chrome ----------

    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    const int GWL_STYLE = -16;
    const int WS_SYSMENU = 0x00080000;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_CAPTION_COLOR = 35;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    const int DWMSBT_TABBEDWINDOW = 4; // Mica Alt
    const int DWMWCP_ROUND = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        ApplyDarkMode(hwnd);

        // With the frame extended, DWM still paints its caption band (in the accent colour when
        // "Show accent colour on title bars" is on) and its own caption buttons over ours.
        // COLOR_NONE stops the band; dropping WS_SYSMENU stops the buttons. Alt+F4 still works.
        int captionNone = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionNone, sizeof(int));
        SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);

        int backdrop = DWMSBT_TABBEDWINDOW;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0)
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush"); // pre-22H2: solid fallback

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        };
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.Invoke(() => ApplyDarkMode(new WindowInteropHelper(this).Handle));
    }

    // Mica's tint follows this flag, so keep it in sync with the Windows app theme.
    static void ApplyDarkMode(IntPtr hwnd)
    {
        var light = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int v && v != 0;
        int dark = light ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // ---------- Time picking ----------

    TimeSpan PickedTime => new((hour12 % 12) + (pm ? 12 : 0), minute, 0);

    // Next occurrence of the picked clock time on a picked day; once means today if still ahead, otherwise tomorrow.
    DateTime NextOccurrence() =>
        ShutdownPlanner.NextRepeat(DateTime.Now, PickedTime, repeatDays == 0 ? ShutdownPlanner.EveryDay : repeatDays);

    void Day_Click(object sender, RoutedEventArgs e)
    {
        repeatDays ^= 1 << int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        Refresh();
    }

    void SyncDayChips()
    {
        foreach (var chip in DayChips.Children.OfType<ToggleButton>())
            chip.IsChecked = (repeatDays >> int.Parse((string)chip.Tag, CultureInfo.InvariantCulture) & 1) != 0;
    }

    void SetPicked(DateTime t)
    {
        pm = t.Hour >= 12;
        hour12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        minute = t.Minute;
    }

    void Step(string segment, int delta)
    {
        switch (segment)
        {
            case "hour": hour12 = Wrap(hour12 - 1 + delta, 12) + 1; break;
            case "minute": minute = Wrap(minute + delta, 60); break;
            case "ampm": pm = !pm; break;
        }
        Refresh();
    }

    static int Wrap(int value, int size) => ((value % size) + size) % size;

    void Step_Click(object sender, RoutedEventArgs e)
    {
        var parts = ((string)((FrameworkElement)sender).Tag).Split(':');
        Step(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    void Segment_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Step((string)((FrameworkElement)sender).Tag, e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        var minutes = int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        // Round up to the next whole minute so the preview never reads "29m 59s".
        var t = DateTime.Now.AddMinutes(minutes);
        SetPicked(t.AddSeconds(60 - t.Second));
        Refresh();
    }

    // ---------- Display ----------

    void OnTick()
    {
        var nowUtc = DateTime.UtcNow;
        var gap = nowUtc - lastTickUtc;
        lastTickUtc = nowUtc;

        // Works even when Windows doesn't send this app a resume notification (Modern Standby).
        if (gap > ResumeGap && scheduledAt is not null)
            Resync();
        // Safety net: Windows should have shut down by now, so its countdown was off.
        else if (scheduledAt is { } at && DateTime.Now - at > TimeSpan.FromSeconds(15))
            Resync();
        else if (scheduledAt is { } due && !warned && lateFor is null
                 && due > DateTime.Now && due - DateTime.Now <= ShutdownPlanner.HeadsUp)
            WarnShortly(due);

        Refresh();
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Mode == PowerModes.Resume && scheduledAt is not null)
            Resync();
        Refresh();   // StatusChange: plugged in or unplugged
    });

    void Refresh()
    {
        HourText.Text = hour12.ToString("00", CultureInfo.InvariantCulture);
        MinuteText.Text = minute.ToString("00", CultureInfo.InvariantCulture);
        AmPmText.Text = pm ? "PM" : "AM";

        var next = NextOccurrence();
        PreviewText.Text = $"{DayWord(next)} · in {FormatSpan(next - DateTime.Now)}";
        RepeatText.Text = ShutdownPlanner.DescribeDays(repeatDays);

        if (scheduledAt is { } t)
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            ScheduledPanel.Visibility = Visibility.Visible;

            var left = t - DateTime.Now;
            CountdownText.Text = FormatCountdown(left);
            TargetText.Text = $"{Clock(t)} · {DayWord(t)}";

            var total = (t - scheduledFrom).TotalSeconds;
            ProgressArc.Data = RingArc(total > 0 ? left.TotalSeconds / total : 1);

            ScheduledTitle.Text = lateFor is not null ? "Shutting down"
                : repeatDays != 0 ? $"Shutdown · {ShutdownPlanner.DescribeDays(repeatDays)}"
                : "Shutdown scheduled";
            SkipButton.Visibility = repeatDays != 0 && lateFor is null ? Visibility.Visible : Visibility.Collapsed;
            if (lateFor is { } missed)
                SetAwakeStatus("", $"Your PC was asleep at {Clock(missed)}", caution: false);
            else if (SystemParameters.PowerLineStatus == PowerLineStatus.Offline)
                SetAwakeStatus("", "On battery: plug in so your PC stays awake", caution: true);
            else if (keepAwake.IsHeld)
                SetAwakeStatus("", "Keeping your PC awake until then", caution: false);
            else
                SetAwakeStatus("", "Couldn't keep your PC awake", caution: true);
        }
        else
        {
            IdlePanel.Visibility = Visibility.Visible;
            ScheduledPanel.Visibility = Visibility.Collapsed;
        }
    }

    void SetAwakeStatus(string glyph, string text, bool caution)
    {
        AwakeIcon.Text = glyph;
        AwakeText.Text = text;
        AwakeIcon.SetResourceReference(TextBlock.ForegroundProperty,
            caution ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
    }

    // Clockwise arc from 12 o'clock covering `fraction` of the ring (the time remaining).
    static Geometry RingArc(double fraction)
    {
        fraction = Math.Clamp(fraction, 0.0005, 0.9999);
        double r = (RingSize - RingStroke) / 2, c = RingSize / 2;
        double angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    static string Clock(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);

    static string DayWord(DateTime t) =>
        t.Date == DateTime.Today ? "Today" :
        t.Date == DateTime.Today.AddDays(1) ? "Tomorrow" :
        t.ToString("dddd", CultureInfo.CurrentCulture);

    // ---------- Scheduling ----------

    void Schedule_Click(object sender, RoutedEventArgs e)
    {
        repeatTime = PickedTime;
        if (Arm(NextOccurrence()))
            MissedBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Replaces any pending shutdown with one at <paramref name="target"/>. The delay is counted in UTC,
    /// so a DST change between now and then doesn't move the shutdown by an hour.
    /// </summary>
    bool Arm(DateTime target)
    {
        var now = DateTime.Now;
        ShutdownCommand.Abort();   // replace any shutdown that is already pending
        var (code, output) = ShutdownCommand.Schedule(target.ToUniversalTime() - DateTime.UtcNow, target);
        if (code != 0)
        {
            // The old shutdown was already cancelled above, so nothing is pending now.
            ClearState();
            OnScheduleChanged();
            MessageBox.Show(this, $"Windows refused to schedule the shutdown (code {code}).\n\n{output}",
                "Shutdown Timer", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        scheduledAt = target;
        scheduledFrom = now;
        lateFor = null;
        warned = target - now <= ShutdownPlanner.HeadsUp;   // set this close: the user already knows
        WarningBar.Visibility = Visibility.Collapsed;
        SaveState();
        OnScheduleChanged();
        return true;
    }

    void ArmNextRepeat(DateTime after) => Arm(ShutdownPlanner.NextRepeat(after, repeatTime, repeatDays));

    void Cancel_Click(object sender, RoutedEventArgs e) => CancelShutdown();

    void CancelShutdown()
    {
        ShutdownCommand.Abort();
        ClearState();
        repeatDays = 0;   // cancel stops the repeat too; Skip is for one occurrence
        SyncDayChips();
        OnScheduleChanged();
    }

    void Skip_Click(object sender, RoutedEventArgs e) => SkipNext();

    void SkipNext()
    {
        if (repeatDays != 0 && scheduledAt is { } t)
            ArmNextRepeat(t);
    }

    void Snooze_Click(object sender, RoutedEventArgs e) => Arm(DateTime.Now + ShutdownPlanner.Snooze);

    void WarnShortly(DateTime due)
    {
        warned = true;
        WarningText.Text = $"Your PC shuts down at {Clock(due)}. Save your work, or snooze it for {ShutdownPlanner.Snooze.TotalMinutes:0} minutes.";
        WarningBar.Visibility = Visibility.Visible;
        ShowFromTray();
        Topmost = true;    // in front of whatever the user is doing, without staying on top
        Topmost = false;
        tray.Notify($"Shutting down at {Clock(due)}", "Open Shutdown Timer to cancel or snooze.");
    }

    // Repeating needs the app to start with Windows: after each shutdown, that's what sets up the next one.
    void RefreshAutoStartBar() =>
        AutoStartBar.Visibility = repeatDays != 0 && scheduledAt is not null && AutoStart.Available && !AutoStart.Enabled
            ? Visibility.Visible : Visibility.Collapsed;

    void TurnOnAutoStart_Click(object sender, RoutedEventArgs e)
    {
        try { AutoStart.Set(true); }
        catch (Exception ex) when (AutoStart.IsRegistryFailure(ex)) { }
        RefreshAutoStartBar();
    }

    /// <summary>
    /// Brings Windows' countdown back in line with the wall clock. Called after the PC resumes, when
    /// the app starts with a saved schedule, and when the target is overdue.
    /// </summary>
    void Resync()
    {
        if (scheduledAt is not { } target)
            return;

        var now = DateTime.Now;
        var plan = ShutdownPlanner.Decide(DateTime.UtcNow, target.ToUniversalTime(), LastBoot.ToUniversalTime());
        switch (plan.Action)
        {
            case PlanAction.Rearm:
                ShutdownCommand.Abort();
                ShutdownCommand.Schedule(plan.Delay, target);
                break;

            case PlanAction.ShutDownSoon:
                // Missed by a little while asleep: shut down after a warning the user can cancel.
                ShutdownCommand.Abort();
                var soon = now + plan.Delay;
                ShutdownCommand.Schedule(plan.Delay, soon);
                lateFor ??= target;
                WarningBar.Visibility = Visibility.Collapsed;   // the late-shutdown status replaces it
                scheduledAt = soon;
                scheduledFrom = now;
                SaveState();
                ShowFromTray();
                break;

            case PlanAction.Missed:
                // Too late to surprise the user with a shutdown: cancel the stale countdown and say so.
                ShutdownCommand.Abort();
                ClearState();
                var missed = lateFor ?? target;
                var when = missed.Date == DateTime.Today ? "today"
                    : missed.Date == DateTime.Today.AddDays(-1) ? "yesterday"
                    : "on " + missed.ToString("dddd", CultureInfo.CurrentCulture);
                MissedText.Text = $"Your PC was asleep at {Clock(missed)} {when}, so it didn't shut down.";
                if (repeatDays != 0)
                {
                    tray.Notify("Shutdown skipped", MissedText.Text);
                    ArmNextRepeat(now);
                    return;
                }
                MissedBar.Visibility = Visibility.Visible;
                ShowFromTray();
                break;

            case PlanAction.AlreadyDone:
                // Windows restarted after the target: the shutdown happened. Forget it, or set up the next one.
                ClearState();
                if (repeatDays != 0)
                {
                    ArmNextRepeat(now);
                    return;
                }
                break;
        }
        OnScheduleChanged();
    }

    /// <summary>Keep-awake request and tray icon follow whether a shutdown is pending.</summary>
    void OnScheduleChanged()
    {
        if (scheduledAt is { } t)
        {
            keepAwake.Hold($"Shutdown Timer: keeping the PC awake for a shutdown at {Clock(t)}");
            tray.Update(repeatDays != 0 ? $"Next: {t:ddd} {Clock(t)}" : $"Shutdown at {Clock(t)}",
                pending: true, repeating: repeatDays != 0);
        }
        else
        {
            lateFor = null;
            keepAwake.Release();
            tray.Update("No shutdown scheduled", pending: false, repeating: false);
        }
        RefreshAutoStartBar();
        Refresh();
    }

    void DismissMissed_Click(object sender, RoutedEventArgs e) => MissedBar.Visibility = Visibility.Collapsed;

    // ---------- Window and tray ----------

    static string HiddenNoticeFile => Path.Combine(ShutdownCommand.DataFolder, "tray-notice-shown");

    protected override void OnClosing(CancelEventArgs e)
    {
        // Close hides to the tray; only Exit in the tray menu quits. A pending shutdown keeps the PC awake meanwhile.
        if (!exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    void HideToTray()
    {
        Hide();
        if (File.Exists(HiddenNoticeFile))
            return;
        tray.Notify("Shutdown Timer is still running",
            scheduledAt is { } t
                ? $"It's keeping your PC awake until {Clock(t)}. Open it from the tray to cancel."
                : "Open it from the tray, or right-click the tray icon to exit.");
        try
        {
            Directory.CreateDirectory(ShutdownCommand.DataFolder);
            File.WriteAllText(HiddenNoticeFile, "");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnClosed(EventArgs e)
    {
        keepAwake.Dispose();
        tray.Dispose();
        base.OnClosed(e);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    // ---------- Persistence: "<scheduledAt>|<scheduledFrom>|<repeatDays>|<repeatTime>" ----------

    void LoadState()
    {
        try
        {
            var parts = File.ReadAllText(StateFile).Trim().Split('|');
            if (!TryParseDate(parts[0], out var at))
            {
                ClearState();
                return;
            }
            // Past targets are kept too: Resync decides whether they were done, are due, or were missed.
            scheduledAt = at;
            scheduledFrom = parts.Length > 1 && TryParseDate(parts[1], out var from) ? from : DateTime.Now;
            SetPicked(at);
            // Files from before repeating have no repeat fields: a one-off schedule.
            if (parts.Length > 3 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var days)
                && TimeSpan.TryParse(parts[3], CultureInfo.InvariantCulture, out var time)
                && days is > 0 and <= ShutdownPlanner.EveryDay)
            {
                repeatDays = days;
                repeatTime = time;
                SetPicked(at.Date + time);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool TryParseDate(string s, out DateTime value) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);

    void SaveState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, string.Create(CultureInfo.InvariantCulture,
            $"{scheduledAt!.Value:o}|{scheduledFrom:o}|{repeatDays}|{repeatTime:c}"));
    }

    void ClearState()
    {
        scheduledAt = null;
        try { File.Delete(StateFile); } catch (IOException) { }
    }

    static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:D2}m"
            : $"{span.Minutes}m {span.Seconds:D2}s";
    }

    static string FormatCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }
}
