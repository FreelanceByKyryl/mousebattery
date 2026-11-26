using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MouseCharge.Services;

internal static class TrayBatteryIcon
{
    private const int DesignSize = 16;
    private const int Supersampling = 8;
    private const int DigitHeight = 12;
    private const int GaugeWidth = 3;
    private const int ReadingLeft = GaugeWidth + 1;

    private static readonly Palette Light = new(
        Color.Black, Color.FromArgb(0xaf, 0xb7, 0xbf),
        Color.FromArgb(0x45, 0x50, 0x5c), Color.FromArgb(0x69, 0x79, 0x89));

    private static readonly Palette Dark = new(
        Color.White, Color.FromArgb(0x46, 0x53, 0x62),
        Color.FromArgb(0xcc, 0xd5, 0xdf), Color.FromArgb(0x91, 0xa1, 0xb2));

    /// <summary>The caller owns the returned icon. Missing readings are drawn as a dash.</summary>
    public static Icon? Create(int? percent, bool stale)
    {
        int? level = percent is >= 0 and <= 100 ? percent : null;
        bool highContrast = Forms.SystemInformation.HighContrast;
        var palette = highContrast
            ? new Palette(SystemColors.WindowText, SystemColors.GrayText,
                SystemColors.WindowText, SystemColors.GrayText)
            : IsLightSystemTheme() ? Light : Dark;
        var size = Forms.SystemInformation.SmallIconSize;
        if (size.Width <= 0 || size.Height <= 0)
            size = new Size(DesignSize, DesignSize);

        using var artwork = new Bitmap(size.Width * Supersampling, size.Height * Supersampling,
            PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(artwork))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(artwork.Width / (float)DesignSize, artwork.Height / (float)DesignSize);
            if (highContrast)
            {
                using var background = new SolidBrush(SystemColors.Window);
                graphics.FillRectangle(background, 0, 0, DesignSize, DesignSize);
            }
            DrawReading(graphics, level, stale || level is null ? palette.MutedText : palette.Text);
            DrawGauge(graphics, level, stale, highContrast, palette);
        }

        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(artwork, new Rectangle(Point.Empty, size));
        }

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

    private static void DrawReading(Graphics graphics, int? percent, Color ink)
    {
        using var brush = new SolidBrush(ink);
        if (percent is not int value)
        {
            FillRoundedRectangle(graphics, brush,
                new RectangleF(ReadingLeft + 3, 7, 6, 1.5f), 0.5f);
            return;
        }

        string digits = value.ToString(CultureInfo.InvariantCulture);
        int digitsWidth = digits.Sum(digit => DigitWidth(digit, digits.Length)) + digits.Length - 1;
        int left = ReadingLeft + (DesignSize - ReadingLeft - digitsWidth) / 2;
        int right = left + digitsWidth;
        using var font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var family = font.FontFamily;
        using var format = StringFormat.GenericTypographic;
        // Anchor each digit to pixel columns so its stems survive reduction to 16px.
        using var edge = new Pen(ink, 0.45f) { LineJoin = LineJoin.Round };
        foreach (char digit in digits)
        {
            int width = DigitWidth(digit, digits.Length);
            using var path = new GraphicsPath();
            path.AddString(digit.ToString(), family, (int)font.Style, font.Size, PointF.Empty, format);
            var bounds = path.GetBounds();
            float sx = (width - edge.Width) / bounds.Width;
            float sy = (DigitHeight - edge.Width) / bounds.Height;
            using var fit = new Matrix(sx, 0, 0, sy,
                left + edge.Width / 2 - bounds.Left * sx, edge.Width / 2 - bounds.Top * sy);
            path.Transform(fit);
            graphics.FillPath(brush, path);
            graphics.DrawPath(edge, path);
            left += width + 1;
        }

        // The unit sits below the last digit so even 100 keeps its full digit width.
        using var slash = new Pen(ink, 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(slash, right - 4.5f, 15.5f, right - 0.5f, 12.75f);
        graphics.FillEllipse(brush, right - 5, 12.4f, 1.4f, 1.4f);
        graphics.FillEllipse(brush, right - 1.4f, 14.6f, 1.4f, 1.4f);
    }

    private static int DigitWidth(char digit, int count) => count switch
    {
        1 => digit == '1' ? 4 : 7,
        2 => digit == '1' ? 3 : 5,
        _ => digit == '1' ? 2 : 4
    };

    private static void DrawGauge(Graphics graphics, int? percent, bool stale, bool highContrast, Palette palette)
    {
        using var track = new SolidBrush(palette.Track);
        Color charge = stale ? palette.MutedCharge : highContrast
            ? SystemColors.Highlight : ChargeColor(percent.GetValueOrDefault());
        using var fill = new SolidBrush(charge);

        for (int step = 0; step < 5; step++)
        {
            int bottom = DesignSize - 1 - step * 3;
            FillRoundedRectangle(graphics, track, new RectangleF(0, bottom - 2, GaugeWidth, 2), 0.4f);
            float fraction = Math.Clamp((percent.GetValueOrDefault() - step * 20) / 20f, 0, 1);
            if (fraction > 0)
            {
                float height = 2 * fraction;
                FillRoundedRectangle(graphics, fill,
                    new RectangleF(0, bottom - height, GaugeWidth, height), 0.4f * fraction);
            }
        }
    }

    private static Color ChargeColor(int percent) => percent switch
    {
        >= 90 => Color.FromArgb(0x3d, 0xd9, 0x70),
        >= 70 => Color.FromArgb(0x85, 0xd7, 0x43),
        >= 50 => Color.FromArgb(0xf0, 0xc5, 0x32),
        >= 30 => Color.FromArgb(0xff, 0x9c, 0x39),
        _ => Color.FromArgb(0xfa, 0x63, 0x57)
    };

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    private static bool IsLightSystemTheme()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return personalize?.GetValue("SystemUsesLightTheme") is 1;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private sealed record Palette(Color Text, Color Track, Color MutedText, Color MutedCharge);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
