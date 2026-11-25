using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MouseCharge.Services;

internal static class TrayBatteryIcon
{
    private const int Size = 16;
    private const int ReadingTop = 8;

    // A sideways top view keeps the shell and scroll wheel distinct from the reading.
    private static readonly string[] Mouse =
    [
        "  ##########  ",
        " ##........## ",
        "#.......++::.#",
        "#.......++::.#",
        " ##........## ",
        "  ##########  "
    ];

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

    private static readonly string[] Percent = ["#..#", "...#", "..#.", "..#.", ".#..", "#...", "#..#"];
    private static readonly string[] CompactPercent = ["#.#", "..#", "..#", ".#.", "#..", "#..", "#.#"];
    private static readonly string[] CompactZero = [".#.", "#.#", "#.#", "#.#", "#.#", "#.#", ".#."];
    private static readonly string[] Unavailable = ["......", "......", "......", "######", "......", "......", "......"];

    private static readonly Color Outline = Color.FromArgb(110, 153, 255);
    private static readonly Color Shell = Color.FromArgb(32, 48, 74);
    private static readonly Color Wheel = Color.FromArgb(197, 217, 255);
    private static readonly Color Split = Color.FromArgb(69, 85, 114);
    private static readonly Color Backdrop = Color.FromArgb(242, 21, 31, 44);
    private static readonly Color Green = Color.FromArgb(105, 236, 164);
    private static readonly Color Red = Color.FromArgb(255, 132, 148);
    private static readonly Color Gray = Color.FromArgb(193, 205, 223);

    /// <summary>The caller owns the returned icon. Missing readings are drawn as a dash.</summary>
    public static Icon? Create(int? percent, bool stale)
    {
        bool available = percent is >= 0 and <= 100;
        var ink = stale || !available ? Gray : percent < 20 ? Red : Green;

        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        DrawMouse(bitmap);
        DrawReading(bitmap, available ? percent : null, ink);

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
                    ':' => Split,
                    _ => Color.Empty
                };
                if (!color.IsEmpty)
                    bitmap.SetPixel(x + 1, y, color);
            }
        }
    }

    private static void DrawReading(Bitmap bitmap, int? percent, Color ink)
    {
        string[][] glyphs = percent switch
        {
            null => [Unavailable],
            100 => [Digits[1], CompactZero, CompactZero, CompactPercent],
            int value => [.. value.ToString(CultureInfo.InvariantCulture).Select(digit => Digits[digit - '0']), Percent]
        };
        int width = glyphs.Sum(glyph => glyph[0].Length + 1) - 1;
        int left = (Size - width) / 2;
        for (int y = ReadingTop - 1; y < Size; y++)
        {
            int inset = y == ReadingTop - 1 || y == Size - 1 ? 2 : 1;
            for (int x = inset; x < Size - inset; x++)
                bitmap.SetPixel(x, y, Backdrop);
        }

        foreach (var glyph in glyphs)
        {
            for (int y = 0; y < glyph.Length; y++)
            {
                for (int x = 0; x < glyph[y].Length; x++)
                {
                    if (glyph[y][x] == '#')
                        bitmap.SetPixel(left + x, ReadingTop + y, ink);
                }
            }
            left += glyph[0].Length + 1;
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
