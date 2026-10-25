using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ThunderbirdAccounts.App;

internal static class WindowTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        // Unsupported attributes return HRESULTs on older Windows, without changing the window.
        int dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        int caption = 0x00131110; // COLORREF: charcoal, matching the application canvas.
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        int text = 0x00F4F1F1;
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        int corners = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
    }
}
