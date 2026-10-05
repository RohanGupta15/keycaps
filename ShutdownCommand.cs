using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace ShutdownTimer;

/// <summary>Runs shutdown.exe, or with --dry-run only logs what it would run.</summary>
static class ShutdownCommand
{
    public static bool DryRun { get; set; }

    /// <summary>Per-mode data folder, so a dry run never touches the real schedule.</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DryRun ? "ShutdownTimer-dryrun" : "ShutdownTimer");

    /// <summary>Starts Windows' shutdown countdown. /d p:0:0 logs it as a planned shutdown.</summary>
    public static (int code, string output) Schedule(TimeSpan delay, DateTime target)
    {
        var seconds = Math.Clamp((long)Math.Ceiling(delay.TotalSeconds), 1, 315_360_000);
        var label = target.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return Run($"/s /t {seconds} /d p:0:0 /c \"Scheduled shutdown at {label} (Shutdown Timer).\"");
    }

    /// <summary>Cancels a pending shutdown. Fails harmlessly (exit 1116) when none is pending.</summary>
    public static void Abort() => Run("/a");

    static (int code, string output) Run(string args)
    {
        if (DryRun)
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(Path.Combine(DataFolder, "dry-run.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  shutdown {args}{Environment.NewLine}");
            return (0, "");
        }

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
}
