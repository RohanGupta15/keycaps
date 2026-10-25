using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ShutdownTimer;

/// <summary>System tray icon, shown for as long as the app runs. Closing or minimizing the window hides it here.</summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ToolStripItem cancelItem;

    public TrayIcon(Action open, Action cancelShutdown, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Shutdown Timer", null, (_, _) => open());
        cancelItem = menu.Items.Add("Cancel shutdown", null, (_, _) => cancelShutdown());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit (the shutdown stays scheduled)", null, (_, _) => exit());

        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/brand/shutdown-timer.ico")).Stream;
        icon = new Forms.NotifyIcon
        {
            Icon = new Icon(stream, Forms.SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Text = "Shutdown Timer",
            Visible = true,
        };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void Update(string status, bool pending)
    {
        icon.Text = $"Shutdown Timer: {status}"; // tooltip max is 127 chars
        cancelItem.Enabled = pending;
    }

    public void Notify(string title, string text) =>
        icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Icon?.Dispose();
        icon.Dispose();
    }
}
