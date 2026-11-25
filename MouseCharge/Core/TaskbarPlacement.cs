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
        int gap = (int)Math.Ceiling(8 * scale);
        var visibleBar = Rectangle.Intersect(monitor, taskbar);
        var tray = notificationArea is { } bounds ? Rectangle.Intersect(visibleBar, bounds) : Rectangle.Empty;
        if (visibleBar.Width < gap || visibleBar.Height < gap)
        {
            // An auto-hidden taskbar may leave only a reveal strip inside the monitor.
            return null;
        }

        bool hasTray = tray.Width > 0 && tray.Height > 0;
        if (taskbar.Width >= taskbar.Height)
        {
            bool onTop = visibleBar.Top - monitor.Top <= gap;
            bool onBottom = monitor.Bottom - visibleBar.Bottom <= gap;
            if (!onTop && !onBottom)
                return null;

            int reserved = Math.Max((int)Math.Ceiling(320 * scale), monitor.Width / 4);
            int trayStart = hasTray ? tray.Left : visibleBar.Right - reserved;
            int x = trayStart - width - gap;
            if (x < monitor.Left || x + width > monitor.Right)
                return null;

            int y = DockAcrossEdge(onTop, monitor.Top, monitor.Bottom, visibleBar.Top, visibleBar.Bottom, height);
            return (x, y);
        }

        bool onLeft = visibleBar.Left - monitor.Left <= gap;
        bool onRight = monitor.Right - visibleBar.Right <= gap;
        if (!onLeft && !onRight)
            return null;

        int reservedHeight = Math.Max((int)Math.Ceiling(240 * scale), monitor.Height / 4);
        int trayTop = hasTray ? tray.Top : visibleBar.Bottom - reservedHeight;
        int yAlongBar = trayTop - height - gap;
        if (yAlongBar < monitor.Top || yAlongBar + height > monitor.Bottom)
            return null;

        int xAcrossBar = DockAcrossEdge(onLeft, monitor.Left, monitor.Right,
            visibleBar.Left, visibleBar.Right, width);
        return (xAcrossBar, yAlongBar);
    }

    private static int DockAcrossEdge(bool atStart, int screenStart, int screenEnd, int barStart, int barEnd,
        int cardSize)
    {
        int barThickness = atStart ? barEnd - screenStart : screenEnd - barStart;
        // Tall taskbars should still leave a portion of the card protruding into the work area.
        int overlap = Math.Min(barThickness, Math.Max(1, cardSize * 2 / 3));
        int start = atStart ? barEnd - overlap : barStart + overlap - cardSize;
        return Math.Clamp(start, screenStart, screenEnd - cardSize);
    }
}
