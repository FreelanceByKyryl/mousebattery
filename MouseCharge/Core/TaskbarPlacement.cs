using System.Drawing;

namespace MouseCharge.Core;

internal static class TaskbarPlacement
{
    public static (int X, int Y)? Find(Rectangle monitor, Rectangle taskbar, Rectangle? notificationArea,
        double widthDip, double heightDip, uint dpi)
    {
        if (dpi == 0 || !double.IsFinite(widthDip) || !double.IsFinite(heightDip) ||
            widthDip <= 0 || heightDip <= 0)
        {
            return null;
        }

        double scale = dpi / 96.0;
        double pixelWidth = Math.Ceiling(widthDip * scale);
        double pixelHeight = Math.Ceiling(heightDip * scale);
        if (pixelWidth > monitor.Width || pixelHeight > monitor.Height)
        {
            return null;
        }

        int width = (int)pixelWidth;
        int height = (int)pixelHeight;
        int gap = (int)Math.Ceiling(6 * scale);
        int inset = (int)Math.Ceiling(2 * scale);
        var visibleBar = Rectangle.Intersect(monitor, taskbar);
        var tray = notificationArea is { } bounds ? Rectangle.Intersect(visibleBar, bounds) : Rectangle.Empty;
        if (visibleBar.Width < width + 2 * inset || visibleBar.Height < height + 2 * inset)
        {
            // An auto-hidden taskbar may leave only a reveal strip inside the monitor.
            return null;
        }

        bool hasTray = tray.Width > 0 && tray.Height > 0;
        if (taskbar.Width >= taskbar.Height)
        {
            bool onTop = visibleBar.Top - monitor.Top <= gap;
            bool onBottom = monitor.Bottom - visibleBar.Bottom <= gap;
            if ((!onTop && !onBottom) || visibleBar.Height < taskbar.Height - gap)
                return null;

            int reserved = (int)Math.Ceiling(264 * scale);
            int trayStart = hasTray ? tray.Left : visibleBar.Right - reserved;
            int x = trayStart - width - gap;
            if (x < visibleBar.Left + inset)
                return null;

            int y = visibleBar.Top + (visibleBar.Height - height) / 2;
            return (x, y);
        }

        bool onLeft = visibleBar.Left - monitor.Left <= gap;
        bool onRight = monitor.Right - visibleBar.Right <= gap;
        if ((!onLeft && !onRight) || visibleBar.Width < taskbar.Width - gap)
            return null;

        int reservedHeight = (int)Math.Ceiling(200 * scale);
        int trayTop = hasTray ? tray.Top : visibleBar.Bottom - reservedHeight;
        int yAlongBar = trayTop - height - gap;
        if (yAlongBar < visibleBar.Top + inset)
            return null;

        int xAcrossBar = visibleBar.Left + (visibleBar.Width - width) / 2;
        return (xAcrossBar, yAlongBar);
    }
}
