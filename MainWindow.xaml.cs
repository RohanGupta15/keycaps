using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ShutdownTimer;

// Schedules the shutdown with Windows' own `shutdown /s /t <seconds>`, so the
// shutdown still happens after this app is closed. The chosen time is saved to
// disk only so the app can show it again when reopened.
public partial class MainWindow : Window
{
    static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShutdownTimer", "scheduled.txt");

    const double RingSize = 232, RingStroke = 8;

    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };

    int hour12 = 3;   // 1..12
    int minute;       // 0..59
    bool pm;
    DateTime? scheduledAt;
    DateTime scheduledFrom;   // when it was scheduled, for the progress ring

    public MainWindow()
    {
        InitializeComponent();

        tick.Tick += (_, _) => Refresh();
        tick.Start();

        LoadState();
        Refresh();
    }

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
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
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

    void Refresh()
    {
        if (scheduledAt is { } at && at <= DateTime.Now)
            ClearState();

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
            TargetText.Text = $"{t.ToString("h:mm tt", CultureInfo.InvariantCulture)} · {DayWord(t)}";

            var total = (t - scheduledFrom).TotalSeconds;
            ProgressArc.Data = RingArc(total > 0 ? left.TotalSeconds / total : 1);
        }
        else
        {
            IdlePanel.Visibility = Visibility.Visible;
            ScheduledPanel.Visibility = Visibility.Collapsed;
        }
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

    static string DayWord(DateTime t) =>
        t.Date == DateTime.Today ? "Today" :
        t.Date == DateTime.Today.AddDays(1) ? "Tomorrow" :
        t.ToString("dddd", CultureInfo.CurrentCulture);

    // ---------- Scheduling ----------

    void Schedule_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        var target = NextOccurrence();
        var seconds = (int)Math.Ceiling((target - now).TotalSeconds);

        // Replace any shutdown that is already pending (this fails harmlessly if none is).
        RunShutdown("/a");

        var label = target.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var (code, output) = RunShutdown($"/s /t {seconds} /c \"Scheduled shutdown at {label} (Shutdown Timer).\"");
        if (code != 0)
        {
            MessageBox.Show(this, $"Windows refused to schedule the shutdown (code {code}).\n\n{output}",
                "Shutdown Timer", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        scheduledAt = target;
        scheduledFrom = now;
        SaveState();
        Refresh();
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        RunShutdown("/a");
        ClearState();
        Refresh();
    }

    static (int code, string output) RunShutdown(string args)
    {
        var psi = new ProcessStartInfo("shutdown.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output.Trim());
    }

    // ---------- Persistence: "<scheduledAt>|<scheduledFrom>" ----------

    void LoadState()
    {
        try
        {
            var parts = File.ReadAllText(StateFile).Trim().Split('|');
            if (!TryParseDate(parts[0], out var at) || at <= DateTime.Now)
                return;

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
