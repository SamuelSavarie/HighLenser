using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace HighLenser.Mac;

public sealed class MacSelectionWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;
    private string _candidate = "";
    private string _lastSent = "";
    private DateTime _candidateSince;

    public event EventHandler<string>? SelectionReady;
    public bool IsTrusted => OperatingSystem.IsMacOS() && MacAccessibility.IsTrusted();

    public MacSelectionWatcher()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Poll()
    {
        string selected = MacAccessibility.ReadSelectedText();
        if (selected.Length is < 2 or > 12_000) return;
        if (!string.Equals(selected, _candidate, StringComparison.Ordinal))
        {
            _candidate = selected;
            _candidateSince = DateTime.UtcNow;
            return;
        }
        if (selected == _lastSent || DateTime.UtcNow - _candidateSince < TimeSpan.FromMilliseconds(650)) return;
        _lastSent = selected;
        SelectionReady?.Invoke(this, selected);
    }

    public void Dispose() => _timer.Stop();
}

internal static class MacAccessibility
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;

    [DllImport(ApplicationServices)] private static extern IntPtr AXUIElementCreateSystemWide();
    [DllImport(ApplicationServices)] private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);
    [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(IntPtr value);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(IntPtr value, StringBuilder buffer, nint bufferSize, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);

    public static bool IsTrusted()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try { return AXIsProcessTrusted(); } catch { return false; }
    }

    public static string ReadSelectedText()
    {
        if (!OperatingSystem.IsMacOS() || !IsTrusted()) return "";
        IntPtr system = IntPtr.Zero, focused = IntPtr.Zero, selected = IntPtr.Zero;
        IntPtr focusedKey = IntPtr.Zero, selectedKey = IntPtr.Zero;
        try
        {
            system = AXUIElementCreateSystemWide();
            focusedKey = CFStringCreateWithCString(IntPtr.Zero, "AXFocusedUIElement", Utf8);
            selectedKey = CFStringCreateWithCString(IntPtr.Zero, "AXSelectedText", Utf8);
            if (AXUIElementCopyAttributeValue(system, focusedKey, out focused) != 0 || focused == IntPtr.Zero) return "";
            if (AXUIElementCopyAttributeValue(focused, selectedKey, out selected) != 0 || selected == IntPtr.Zero) return "";
            nint length = CFStringGetLength(selected);
            nint size = CFStringGetMaximumSizeForEncoding(length, Utf8) + 1;
            var buffer = new StringBuilder((int)Math.Min(size, 48_000));
            return CFStringGetCString(selected, buffer, buffer.Capacity, Utf8) ? buffer.ToString().Trim() : "";
        }
        catch { return ""; }
        finally
        {
            if (selected != IntPtr.Zero) CFRelease(selected);
            if (focused != IntPtr.Zero) CFRelease(focused);
            if (system != IntPtr.Zero) CFRelease(system);
            if (focusedKey != IntPtr.Zero) CFRelease(focusedKey);
            if (selectedKey != IntPtr.Zero) CFRelease(selectedKey);
        }
    }
}
