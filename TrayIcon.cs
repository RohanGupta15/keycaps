using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ShutdownTimer;

/// <summary>System tray icon, shown for as long as the app runs. Closing or minimizing the window hides it here.</summary>
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ToolStripItem cancelItem, exitItem;

    public TrayIcon(Action open, Action cancelShutdown, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Shutdown Timer", null, (_, _) => open());
        cancelItem = menu.Items.Add("Cancel shutdown", null, (_, _) => cancelShutdown());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var autoStartItem = new Forms.ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart())
        {
            Enabled = AutoStart.Available,
        };
        menu.Items.Add(autoStartItem);
        // Read on every open: it can also be switched in Task Manager while the app runs.
        menu.Opening += (_, _) =>
        {
            autoStartItem.Checked = AutoStart.Enabled;
            autoStartItem.Text = AutoStart.DisabledInTaskManager
                ? "Start with Windows (turned off in Task Manager)"
                : "Start with Windows";
        };
        menu.Items.Add(new Forms.ToolStripSeparator());
        exitItem = menu.Items.Add("Exit", null, (_, _) => exit());

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

    void ToggleAutoStart()
    {
        // A blocked registry write (policy, antivirus) must not crash an app that may be holding a shutdown.
        try { AutoStart.Set(!AutoStart.Enabled); }
        catch (Exception e) when (AutoStart.IsRegistryFailure(e))
        {
            Notify("Couldn't change Start with Windows", "Windows didn't allow the change. You can set it in Task Manager → Startup apps.");
        }
    }

    public void Update(string status, bool pending)
    {
        icon.Text = $"Shutdown Timer: {status}"; // tooltip max is 127 chars
        cancelItem.Enabled = pending;
        exitItem.Text = pending ? "Exit (the shutdown stays scheduled)" : "Exit";
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
