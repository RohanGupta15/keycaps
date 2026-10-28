using System.IO;
using System.Text.Json;

namespace ShutdownTimer;

/// <summary>User preferences, saved to settings.json next to the schedule. A missing or corrupt file means the defaults.</summary>
sealed class Settings
{
    public static readonly int[] WarnChoices = [0, 1, 5, 10, 15];

    /// <summary>Close button hides to the tray. When false it quits, unless a shutdown is pending (that keeps the PC awake).</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Minutes before a shutdown to warn, with Snooze. 0 = no warning.</summary>
    public int WarnMinutes { get; set; } = 5;

    static string FilePath => Path.Combine(ShutdownCommand.DataFolder, "settings.json");

    public static Settings Load() => Load(FilePath);

    public static Settings Load(string path)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
            if (!WarnChoices.Contains(s.WarnMinutes))
                s.WarnMinutes = new Settings().WarnMinutes;
            return s;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save() => Save(FilePath);

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (IOException) { }                 // best effort: the change still applies until the app exits
        catch (UnauthorizedAccessException) { }
    }
}
