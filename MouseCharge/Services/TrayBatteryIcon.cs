using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MouseCharge.Services;

internal static class TrayBatteryIcon
{
    private const int Size = 16;
    private const int CaseWidth = 15;
    private const int CaseTop = 2;
    private const int CaseBottom = 13;
    private const int ReadingTop = 4;

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

    private static readonly string[] Percent = ["#.#", "..#", "..#", ".#.", "#..", "#..", "#.#"];
    private static readonly string[] CompactOne = ["#", "#", "#", "#", "#", "#", "#"];
    private static readonly string[] CompactZero = [".#.", "#.#", "#.#", "#.#", "#.#", "#.#", ".#."];
    private static readonly string[] Unavailable = ["......", "......", "......", "######", "......", "......", "......"];

    private static readonly Color Outline = Color.FromArgb(128, 147, 172);
    private static readonly Color Interior = Color.FromArgb(21, 31, 44);
    private static readonly Color Green = Color.FromArgb(105, 236, 164);
    private static readonly Color Red = Color.FromArgb(255, 132, 148);
    private static readonly Color Gray = Color.FromArgb(193, 205, 223);

    /// <summary>The caller owns the returned icon. Missing readings are drawn as a dash.</summary>
    public static Icon? Create(int? percent, bool stale)
    {
        bool available = percent is >= 0 and <= 100;
        var ink = stale || !available ? Gray : percent < 20 ? Red : Green;

        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        DrawCase(bitmap);
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

    private static void DrawCase(Bitmap bitmap)
    {
        for (int x = 1; x < CaseWidth - 1; x++)
        {
            bitmap.SetPixel(x, CaseTop, Outline);
            bitmap.SetPixel(x, CaseBottom, Outline);
        }

        for (int y = CaseTop + 1; y < CaseBottom; y++)
        {
            bitmap.SetPixel(0, y, Outline);
            bitmap.SetPixel(CaseWidth - 1, y, Outline);
            for (int x = 1; x < CaseWidth - 1; x++)
                bitmap.SetPixel(x, y, Interior);
        }

        for (int y = 6; y <= 9; y++)
            bitmap.SetPixel(CaseWidth, y, Outline);
    }

    private static void DrawReading(Bitmap bitmap, int? percent, Color ink)
    {
        string[][] glyphs = percent switch
        {
            null => [Unavailable],
            100 => [CompactOne, CompactZero, CompactZero, Percent],
            int value => [.. value.ToString(CultureInfo.InvariantCulture).Select(digit => Digits[digit - '0']), Percent]
        };
        int width = glyphs.Sum(glyph => glyph[0].Length + 1) - 1;
        int left = (CaseWidth - width) / 2;

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
