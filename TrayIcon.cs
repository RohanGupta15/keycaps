using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ShutdownTimer;

/// <summary>System tray icon, shown only while a shutdown is pending (the app is keeping the PC awake).</summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;

    public TrayIcon(Action open, Action cancelShutdown, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Shutdown Timer", null, (_, _) => open());
        menu.Items.Add("Cancel shutdown", null, (_, _) => cancelShutdown());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit (the shutdown stays scheduled)", null, (_, _) => exit());

        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/brand/shutdown-timer.ico")).Stream;
        icon = new Forms.NotifyIcon
        {
            Icon = new Icon(stream, Forms.SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Text = "Shutdown Timer",
        };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void Show(string tooltip)
    {
        icon.Text = $"Shutdown Timer: {tooltip}"; // tooltip max is 127 chars
        icon.Visible = true;
    }

    public void Hide() => icon.Visible = false;

    public void Notify(string title, string text)
    {
        if (icon.Visible)
            icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Icon?.Dispose();
        icon.Dispose();
    }
}
