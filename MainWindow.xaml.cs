using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ShutdownTimer;

// Schedules the shutdown with Windows' own `shutdown /s /t <seconds>`, so it still happens if this
// app is closed. While a shutdown is pending the app also keeps the PC awake (from the tray), and
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
    DateTime lastTickUtc = DateTime.UtcNow;
    bool exiting;

    public MainWindow()
    {
        InitializeComponent();

        tray = new TrayIcon(ShowFromTray, CancelShutdown, ExitApp);

        tick.Tick += (_, _) => OnTick();
        tick.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        LoadState();
        if (scheduledAt is not null)
            Resync();   // the PC may have slept, or restarted, while the app was closed
        Refresh();
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

    // Next occurrence of the picked clock time: today if still ahead, otherwise tomorrow.
    DateTime NextOccurrence()
    {
        var now = DateTime.Now;
        var target = now.Date + PickedTime;
        return target <= now ? target.AddDays(1) : target;
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

        if (scheduledAt is { } t)
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            ScheduledPanel.Visibility = Visibility.Visible;

            var left = t - DateTime.Now;
            CountdownText.Text = FormatCountdown(left);
            TargetText.Text = $"{Clock(t)} · {DayWord(t)}";

            var total = (t - scheduledFrom).TotalSeconds;
            ProgressArc.Data = RingArc(total > 0 ? left.TotalSeconds / total : 1);

            ScheduledTitle.Text = lateFor is null ? "Shutdown scheduled" : "Shutting down";
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
        var now = DateTime.Now;
        var target = NextOccurrence();

        ShutdownCommand.Abort();   // replace any shutdown that is already pending
        var (code, output) = ShutdownCommand.Schedule(target - now, target);
        if (code != 0)
        {
            MessageBox.Show(this, $"Windows refused to schedule the shutdown (code {code}).\n\n{output}",
                "Shutdown Timer", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        scheduledAt = target;
        scheduledFrom = now;
        lateFor = null;
        MissedBar.Visibility = Visibility.Collapsed;
        SaveState();
        OnScheduleChanged();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => CancelShutdown();

    void CancelShutdown()
    {
        ShutdownCommand.Abort();
        ClearState();
        OnScheduleChanged();
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
        var plan = ShutdownPlanner.Decide(now, target, LastBoot);
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
                MissedBar.Visibility = Visibility.Visible;
                ShowFromTray();
                break;

            case PlanAction.AlreadyDone:
                // Windows restarted after the target: the shutdown happened. Just forget it.
                ClearState();
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
            tray.Show($"Shutdown at {Clock(t)}");
        }
        else
        {
            lateFor = null;
            keepAwake.Release();
            tray.Hide();
        }
        Refresh();
    }

    void DismissMissed_Click(object sender, RoutedEventArgs e) => MissedBar.Visibility = Visibility.Collapsed;

    // ---------- Window and tray ----------

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing with a shutdown pending hides to the tray, so the PC stays awake until then.
        if (!exiting && scheduledAt is not null)
        {
            e.Cancel = true;
            Hide();
            tray.Notify("Shutdown Timer is still running",
                $"It's keeping your PC awake until {Clock(scheduledAt.Value)}. Open it from the tray to cancel.");
            return;
        }
        base.OnClosing(e);
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

    // ---------- Persistence: "<scheduledAt>|<scheduledFrom>" ----------

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
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool TryParseDate(string s, out DateTime value) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);

    void SaveState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, $"{scheduledAt!.Value:o}|{scheduledFrom:o}");
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
