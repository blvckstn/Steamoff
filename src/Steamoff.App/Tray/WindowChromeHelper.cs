using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Steamoff.App.Tray;

/// <summary>
/// Switches a window's native title bar into dark mode via DWM so the stock
/// Windows chrome matches Steamoff's dark neumorphic theme instead of clashing
/// </summary>
public static class WindowChromeHelper
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int attributeSize);

    public static void ApplyDarkTitleBar(Window window)
    {
        void Apply()
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            if (DwmSetWindowAttribute(helper.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(helper.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }
        }

        if (window.IsLoaded)
        {
            Apply();
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply();
        }
    }
}
