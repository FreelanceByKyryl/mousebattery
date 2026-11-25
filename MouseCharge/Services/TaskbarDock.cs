using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using MouseCharge.Core;

namespace MouseCharge.Services;

internal static class TaskbarDock
{
    /// <summary>Returns the top-left corner in physical desktop pixels when the primary taskbar can be used.</summary>
    public static (int X, int Y)? FindPlacement(double widthDip, double heightDip)
    {
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (!TryGetVisibleBounds(taskbar, out var bar))
            return null;

        var monitor = Native.MonitorFromWindow(taskbar, Native.NullIfOffMonitor);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !Native.GetMonitorInfo(monitor, ref info))
            return null;

        var area = info.Monitor.ToRectangle();
        uint dpi = Native.GetDpiForWindow(taskbar);
        var child = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        Rectangle? tray = TryGetVisibleBounds(child, out var trayBounds) ? trayBounds : null;
        var placement = TaskbarPlacement.Find(area, bar, tray, widthDip, heightDip, dpi);
        if (placement is not { } point)
            return null;

        if (Native.SHQueryUserNotificationState(out var state) == 0 &&
            state is Native.NotificationState.Direct3DFullScreen or Native.NotificationState.PresentationMode)
        {
            return null;
        }

        var foreground = Native.GetForegroundWindow();
        if (foreground != taskbar && foreground != Native.GetShellWindow() &&
            TryGetVisibleBounds(foreground, out var foregroundBounds) &&
            Native.MonitorFromWindow(foreground, Native.NullIfOffMonitor) == monitor)
        {
            string className = GetClassName(foreground);
            if (IsFullScreen(foreground, foregroundBounds, area, className))
                return null;

            int width = (int)Math.Ceiling(widthDip * dpi / 96.0);
            int height = (int)Math.Ceiling(heightDip * dpi / 96.0);
            var card = new Rectangle(point.X, point.Y, width, height);
            if (IsShellOverlay(className) && card.IntersectsWith(foregroundBounds))
                return null;
        }

        return placement;
    }

    private static bool TryGetVisibleBounds(IntPtr window, out Rectangle bounds)
    {
        if (window != IntPtr.Zero && Native.IsWindowVisible(window) &&
            Native.GetWindowRect(window, out var nativeBounds))
        {
            bounds = nativeBounds.ToRectangle();
            return bounds.Width > 0 && bounds.Height > 0;
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private static bool IsFullScreen(IntPtr window, Rectangle bounds, Rectangle monitor, string className)
    {
        if (className is "Progman" or "WorkerW" || !bounds.Contains(monitor))
            return false;

        // A maximized desktop app can cover the full monitor when the taskbar is set to auto-hide.
        // A retained title bar distinguishes that case from borderless full-screen windows.
        const int caption = 0x00C00000;
        return !Native.IsZoomed(window) || (Native.GetWindowStyle(window, -16) & caption) != caption;
    }

    private static string GetClassName(IntPtr window)
    {
        var name = new StringBuilder(128);
        return Native.GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    private static bool IsShellOverlay(string className) => className is
        "#32768" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or
        "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow";

    private static class Native
    {
        internal const uint NullIfOffMonitor = 0;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
        }

        internal enum NotificationState
        {
            Direct3DFullScreen = 3,
            PresentationMode = 4
        }

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr FindWindow(string className, string? title);

        [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsZoomed(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
        internal static extern int GetWindowStyle(IntPtr window, int index);

        [DllImport("shell32.dll", ExactSpelling = true)]
        internal static extern int SHQueryUserNotificationState(out NotificationState state);
    }
}
