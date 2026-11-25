using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MouseCharge.Services;

internal static class TrayBatteryIcon
{
    private const int Size = 16;
    private const int GaugeLeft = 3;
    private const int GaugeWidth = 9;

    // A wide shell, scroll wheel and thumb recess survive at the native tray size.
    private static readonly string[] Mouse =
    [
        "   #####   ",
        "  ##...##  ",
        " #...+...# ",
        " #...+...# ",
        " #...:...# ",
        " #.......# ",
        "  #.o.o..# ",
        "  #..o...# ",
        " #.o.o.o.# ",
        "  #.....#  ",
        "   #####   "
    ];

    private static readonly Color Outline = Color.FromArgb(97, 137, 255);
    private static readonly Color Shell = Color.FromArgb(30, 48, 81);
    private static readonly Color Wheel = Color.FromArgb(184, 208, 255);
    private static readonly Color Detail = Color.FromArgb(13, 24, 46);
    private static readonly Color Track = Color.FromArgb(25, 35, 52);
    private static readonly Color GaugeEdge = Color.FromArgb(108, 132, 173);
    private static readonly Color Green = Color.FromArgb(93, 224, 153);
    private static readonly Color LowEdge = Color.FromArgb(208, 94, 106);
    private static readonly Color Red = Color.FromArgb(255, 122, 136);
    private static readonly Color NeutralEdge = Color.FromArgb(127, 140, 160);
    private static readonly Color Gray = Color.FromArgb(181, 194, 213);

    /// <summary>The caller owns the returned icon. A missing reading draws an empty, neutral gauge.</summary>
    public static Icon? Create(int? percent, bool stale)
    {
        bool available = percent is >= 0 and <= 100;
        int level = available ? percent.GetValueOrDefault() : 0;
        bool neutral = !available || stale;
        bool low = !neutral && level < 20;
        var edge = neutral ? NeutralEdge : low ? LowEdge : GaugeEdge;
        var ink = neutral ? Gray : low ? Red : Green;

        // The tooltip labels approximation. The icon only shows the coarse reading.
        int filled = (int)Math.Round(level * GaugeWidth / 100.0, MidpointRounding.AwayFromZero);
        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        DrawMouse(bitmap);
        DrawGauge(bitmap, filled, edge, ink);

        var handle = bitmap.GetHicon();
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawMouse(Bitmap bitmap)
    {
        for (int y = 0; y < Mouse.Length; y++)
        {
            for (int x = 0; x < Mouse[y].Length; x++)
            {
                var color = Mouse[y][x] switch
                {
                    '#' => Outline,
                    '+' => Wheel,
                    '.' => Shell,
                    ':' or 'o' => Detail,
                    _ => Color.Empty
                };
                if (!color.IsEmpty)
                    bitmap.SetPixel(x + 2, y, color);
            }
        }
    }

    private static void DrawGauge(Bitmap bitmap, int filled, Color edge, Color ink)
    {
        for (int column = 0; column < GaugeWidth; column++)
        {
            int x = GaugeLeft + column;
            var interior = column < filled ? ink : Track;
            bitmap.SetPixel(x, 12, edge);
            bitmap.SetPixel(x, 13, interior);
            bitmap.SetPixel(x, 14, interior);
            bitmap.SetPixel(x, 15, edge);
        }

        // The rounded case and its terminal leave a transparent row above the gauge.
        for (int y = 13; y <= 14; y++)
        {
            bitmap.SetPixel(GaugeLeft - 1, y, edge);
            bitmap.SetPixel(GaugeLeft + GaugeWidth, y, edge);
            bitmap.SetPixel(GaugeLeft + GaugeWidth + 1, y, edge);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
