using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoMergeTool.App.Theming;

/// <summary>
/// Without this the title bar stays white while the rest of the window goes dark, which reads as
/// a broken window rather than a dark theme.
/// </summary>
internal static class WindowChrome
{
    private const int UseImmersiveDarkMode = 20;

    /// <summary>Windows 10 builds before 20H1 used attribute 19 for the same thing.</summary>
    private const int UseImmersiveDarkModeLegacy = 19;

    public static void ApplyTitleBarTheme(Window window, bool isDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int)) == 0)
        {
            return;
        }

        // Older builds only know the legacy attribute; if that fails too the title bar simply
        // keeps the system default, which is not worth surfacing to the user.
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkModeLegacy, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
