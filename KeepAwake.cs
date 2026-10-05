using System.Runtime.InteropServices;

namespace ShutdownTimer;

/// <summary>
/// Holds a PowerRequestSystemRequired request so Windows doesn't idle into sleep before the shutdown.
/// Per Microsoft's PowerSetRequest docs, Windows still ends the request when the user sleeps the PC
/// (lid, power button, Start > Sleep), and on Modern Standby PCs on battery 5 minutes after the sleep
/// timeout. The reason string appears in `powercfg /requests`.
/// </summary>
sealed class KeepAwake : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PowerSetRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PowerClearRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
    const int PowerRequestSystemRequired = 1;
    static readonly IntPtr InvalidHandle = new(-1);

    IntPtr request = IntPtr.Zero;
    string? reason;

    public bool IsHeld => request != IntPtr.Zero;

    /// <summary>Takes (or re-labels) the request. Returns false if Windows refused it.</summary>
    public bool Hold(string why)
    {
        if (IsHeld && why == reason)
            return true;
        Release();

        var context = new ReasonContext
        {
            Version = POWER_REQUEST_CONTEXT_VERSION,
            Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            SimpleReasonString = why,
        };
        var handle = PowerCreateRequest(ref context);
        if (handle == IntPtr.Zero || handle == InvalidHandle)
            return false;
        if (!PowerSetRequest(handle, PowerRequestSystemRequired))
        {
            CloseHandle(handle);
            return false;
        }
        request = handle;
        reason = why;
        return true;
    }

    public void Release()
    {
        if (!IsHeld)
            return;
        PowerClearRequest(request, PowerRequestSystemRequired);
        CloseHandle(request);
        request = IntPtr.Zero;
        reason = null;
    }

    public void Dispose() => Release();
}
