using System.Threading;
using System.Windows;

namespace ShutdownTimer;

public partial class App : Application
{
    Mutex? instance;
    EventWaitHandle? showRequest;

    protected override void OnStartup(StartupEventArgs e)
    {
        // --dry-run logs shutdown commands instead of running them and uses its own data folder.
        ShutdownCommand.DryRun = e.Args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));

        // One instance only: it may be keeping the PC awake from the tray. A second launch just shows it.
        var name = ShutdownCommand.DryRun ? "Keycaps.ShutdownTimer.DryRun" : "Keycaps.ShutdownTimer";
        instance = new Mutex(true, $@"Local\{name}", out var first);
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
        if (!first)
        {
            showRequest.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => Dispatcher.BeginInvoke(window.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        showRequest?.Dispose();
        base.OnExit(e);
    }
}
