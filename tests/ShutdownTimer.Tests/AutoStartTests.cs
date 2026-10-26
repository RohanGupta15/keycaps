namespace ShutdownTimer.Tests;

public class AutoStartTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 0x02, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0 }, false)]
    public void Reads_task_manager_switch(byte[]? value, bool approved) =>
        Assert.Equal(approved, AutoStart.IsApproved(value));
}
