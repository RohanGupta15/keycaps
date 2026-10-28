namespace ShutdownTimer.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"keycaps-settings-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(path);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = Settings.Load(path);
        Assert.True(s.CloseToTray);
        Assert.Equal(5, s.WarnMinutes);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    public void Corrupt_file_gives_defaults(string content)
    {
        File.WriteAllText(path, content);
        Assert.Equal(5, Settings.Load(path).WarnMinutes);
    }

    [Fact]
    public void Round_trips()
    {
        new Settings { CloseToTray = false, WarnMinutes = 10 }.Save(path);
        var s = Settings.Load(path);
        Assert.False(s.CloseToTray);
        Assert.Equal(10, s.WarnMinutes);
    }

    [Fact]
    public void Unknown_warning_time_falls_back_to_default()
    {
        File.WriteAllText(path, """{"WarnMinutes":-3}""");
        Assert.Equal(5, Settings.Load(path).WarnMinutes);
    }
}
