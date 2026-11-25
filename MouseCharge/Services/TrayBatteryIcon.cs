using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MouseCharge.Services;

internal static class TrayBatteryIcon
{
    private const int Size = 16;
    private const int NumberLeft = 6;
    private const int NumberWidth = Size - NumberLeft;
    private const int DigitTop = 4;

    // The M4's thumb recess, offset buttons and perforated palm rest are visible at 16 px.
    private static readonly string[] Mouse =
    [
        "  ### ",
        " #.+##",
        "#..+.#",
        "#..:# ",
        "#...# ",
        " #...#",
        " b#.o#",
        " b#..#",
        " #o.o#",
        " #.o.#",
        "##o.o#",
        "#....#",
        " #####",
        "  ### "
    ];

    // Three-pixel-wide glyphs leave room for the mouse even at 100 percent.
    private static readonly string[][] Digits =
    [
        [".#.", "#.#", "#.#", "#.#", "#.#", "#.#", ".#."],
        [".#", "##", ".#", ".#", ".#", ".#", ".#"],
        ["##.", "..#", "..#", ".#.", "#..", "#..", "###"],
        ["##.", "..#", "..#", ".##", "..#", "..#", "##."],
        ["#.#", "#.#", "#.#", "###", "..#", "..#", "..#"],
        ["###", "#..", "#..", "##.", "..#", "..#", "##."],
        [".##", "#..", "#..", "##.", "#.#", "#.#", ".#."],
        ["###", "..#", "..#", ".#.", ".#.", "#..", "#.."],
        [".#.", "#.#", "#.#", ".#.", "#.#", "#.#", ".#."],
        [".#.", "#.#", "#.#", ".##", "..#", "..#", "##."]
    ];

    private static readonly Color Surface = Color.FromArgb(21, 31, 43);
    private static readonly Color Outline = Color.FromArgb(155, 215, 198);
    private static readonly Color Shell = Color.FromArgb(101, 169, 157);
    private static readonly Color Buttons = Color.FromArgb(99, 146, 160);
    private static readonly Color Wheel = Color.FromArgb(209, 241, 234);
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
        using (var graphics = Graphics.FromImage(bitmap))
        using (var background = new SolidBrush(Surface))
        {
            graphics.FillRectangle(background, NumberLeft + 1, 2, NumberWidth - 2, 12);
            graphics.FillRectangle(background, NumberLeft, 3, NumberWidth, 10);
        }

        for (var y = 0; y < Mouse.Length; y++)
        {
            for (var x = 0; x < Mouse[y].Length; x++)
            {
                var color = Mouse[y][x] switch
                {
                    '#' => Outline,
                    '+' => Wheel,
                    'b' => Buttons,
                    '.' => Shell,
                    ':' or 'o' => Surface,
                    _ => Color.Empty
                };
                if (!color.IsEmpty)
                    bitmap.SetPixel(x, y + 1, color);
            }
        }

        var digits = percent.Value.ToString(CultureInfo.InvariantCulture);
        var width = digits.Length - 1;
        foreach (var digit in digits)
        {
            width += Digits[digit - '0'][0].Length;
        }
        var left = NumberLeft + (NumberWidth - width + 1) / 2;
        foreach (var digit in digits)
        {
            var shape = Digits[digit - '0'];
            DrawInk(bitmap, shape, left, DigitTop, ink);
            left += shape[0].Length + 1;
        }

        if (approximate)
        {
            DrawInk(bitmap, [".##.#", "#.##."], 9, 12, ink);
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
