using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MouseCharge.Services;

internal static class TrayBatteryIcon
{
    private const int Size = 16;
    private const int DigitTop = 5;

    // Match the original icon's mouse outline and scroll wheel on a charcoal surface.
    private static readonly string[] Mouse =
    [
        "      ####      ",
        "    ##....##    ",
        "   #...++...#   ",
        "  #....++....#  ",
        " #............# ",
        " #............# ",
        " #............# ",
        " #............# ",
        " #............# ",
        " #............# ",
        " #............# ",
        " #............# ",
        " #............# ",
        "  #..........#  ",
        "   ##......##   ",
        "     ######     "
    ];

    // A one-pixel stroke keeps the numbers sharp at the smallest tray size.
    private static readonly string[][] Digits =
    [
        [".##.", "#..#", "#..#", "#..#", "#..#", "#..#", ".##."],
        [".#", "##", ".#", ".#", ".#", ".#", ".#"],
        [".##.", "#..#", "...#", "..#.", ".#..", "#...", "####"],
        ["###.", "...#", "...#", ".##.", "...#", "...#", "###."],
        ["#..#", "#..#", "#..#", "####", "...#", "...#", "...#"],
        ["####", "#...", "#...", "###.", "...#", "...#", "###."],
        [".##.", "#...", "#...", "###.", "#..#", "#..#", ".##."],
        ["####", "...#", "..#.", "..#.", ".#..", ".#..", ".#.."],
        [".##.", "#..#", "#..#", ".##.", "#..#", "#..#", ".##."],
        [".##.", "#..#", "#..#", ".###", "...#", "...#", ".##."]
    ];

    private static readonly Color Surface = Color.FromArgb(27, 31, 38);
    private static readonly Color Outline = Color.FromArgb(144, 157, 173);
    private static readonly Color Wheel = Color.FromArgb(180, 192, 208);
    private static readonly Color Green = Color.FromArgb(132, 233, 183);
    private static readonly Color Yellow = Color.FromArgb(255, 222, 141);
    private static readonly Color Red = Color.FromArgb(255, 160, 170);
    private static readonly Color Gray = Color.FromArgb(213, 221, 232);

    /// <summary>The caller owns the returned icon. A null result uses the default mouse icon.</summary>
    public static Icon? Create(int? percent, bool approximate, bool stale)
    {
        if (percent is not (>= 0 and <= 100))
        {
            return null;
        }

        var ink = stale ? Gray : percent.Value switch
        {
            >= 50 => Green,
            >= 20 => Yellow,
            _ => Red
        };
        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        for (var y = 0; y < Mouse.Length; y++)
        {
            for (var x = 0; x < Mouse[y].Length; x++)
            {
                switch (Mouse[y][x])
                {
                    case '#':
                        bitmap.SetPixel(x, y, Outline);
                        break;
                    case '+':
                        bitmap.SetPixel(x, y, Wheel);
                        break;
                    case '.':
                        bitmap.SetPixel(x, y, Surface);
                        break;
                }
            }
        }

        var digits = percent.Value.ToString(CultureInfo.InvariantCulture);
        var width = digits.Length - 1;
        foreach (var digit in digits)
        {
            width += Digits[digit - '0'][0].Length;
        }
        var left = (Size - width) / 2;
        foreach (var digit in digits)
        {
            var shape = Digits[digit - '0'];
            DrawInk(bitmap, shape, left, DigitTop, ink);
            left += shape[0].Length + 1;
        }

        if (approximate)
        {
            DrawInk(bitmap, [".##.#", "#.##."], 6, 12, ink);
        }

        var handle = bitmap.GetHicon();
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

    private static void DrawInk(Bitmap bitmap, string[] mask, int left, int top, Color ink)
    {
        for (var y = 0; y < mask.Length; y++)
        {
            for (var x = 0; x < mask[y].Length; x++)
            {
                if (mask[y][x] == '#')
                {
                    bitmap.SetPixel(left + x, top + y, ink);
                }
            }
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
