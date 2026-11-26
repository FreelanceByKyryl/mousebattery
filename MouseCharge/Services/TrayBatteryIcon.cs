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

    private static readonly Palette Light = new(
        Color.FromArgb(0x14, 0x1b, 0x22), Color.FromArgb(0x4b, 0x5c, 0x6e),
        Color.FromArgb(0xe1, 0xe7, 0xee), Color.FromArgb(0xf4, 0xf6, 0xf9),
        Color.FromArgb(0x52, 0x66, 0x78), Color.FromArgb(0x79, 0x90, 0xa5));

    private static readonly Palette Dark = new(
        Color.FromArgb(0xf3, 0xf6, 0xf9), Color.FromArgb(0xb6, 0xc4, 0xd1),
        Color.FromArgb(0x34, 0x41, 0x54), Color.FromArgb(0x22, 0x2e, 0x3d),
        Color.FromArgb(0xb5, 0xc4, 0xd2), Color.FromArgb(0x84, 0x96, 0xa8));

    /// <summary>The caller owns the returned icon. Missing readings are drawn as a dash.</summary>
    public static Icon? Create(int? percent, bool stale)
    {
        int? level = percent is >= 0 and <= 100 ? percent : null;
        bool highContrast = Forms.SystemInformation.HighContrast;
        var palette = highContrast
            ? new Palette(SystemColors.WindowText, SystemColors.WindowText, SystemColors.GrayText,
                SystemColors.Window, SystemColors.GrayText, SystemColors.GrayText)
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
                FillRoundedRectangle(graphics, background, new RectangleF(0, 0, DesignSize, 9.4f), 0.8f);
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
            FillRoundedRectangle(graphics, brush, new RectangleF(5.5f, 4.4f, 5f, 0.8f), 0.4f);
            return;
        }

        using var font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var family = font.FontFamily;
        using var format = StringFormat.GenericTypographic;
        using var path = new GraphicsPath();
        path.AddString(value.ToString(CultureInfo.InvariantCulture) + "%", family, (int)font.Style,
            font.Size, PointF.Empty, format);

        // Fit the actual ink bounds, keeping the whole 100% label inside a native 16px icon.
        var bounds = path.GetBounds();
        float sy = 8.4f / bounds.Height;
        float sx = Math.Min(sy, 15.7f / bounds.Width);
        using var fit = new Matrix(sx, 0, 0, sy,
            (DesignSize - bounds.Width * sx) / 2 - bounds.Left * sx, 0.5f - bounds.Top * sy);
        path.Transform(fit);
        graphics.FillPath(brush, path);
    }

    private static void DrawGauge(Graphics graphics, int? percent, bool stale, bool highContrast, Palette palette)
    {
        using var outline = new SolidBrush(palette.Outline);
        using var interior = new SolidBrush(palette.Interior);
        using var track = new SolidBrush(palette.Track);
        Color charge = stale ? palette.MutedCharge : highContrast
            ? SystemColors.Highlight : ChargeColor(percent.GetValueOrDefault());
        using var fill = new SolidBrush(charge);

        FillRoundedRectangle(graphics, outline, new RectangleF(0.55f, 10.1f, 13.55f, 5.5f), 1.6f);
        FillRoundedRectangle(graphics, interior, new RectangleF(1.38f, 10.93f, 11.89f, 3.84f), 0.77f);
        FillRoundedRectangle(graphics, outline, new RectangleF(14.35f, 11.7f, 1.3f, 2.4f), 0.5f);
        for (int step = 0; step < 5; step++)
        {
            float left = 1.85f + step * 2.23f;
            FillRoundedRectangle(graphics, track, new RectangleF(left, 11.55f, 1.72f, 2.7f), 0.32f);
            float fraction = Math.Clamp((percent.GetValueOrDefault() - step * 20) / 20f, 0, 1);
            if (fraction > 0)
            {
                FillRoundedRectangle(graphics, fill,
                    new RectangleF(left, 11.55f, 1.72f * fraction, 2.7f), 0.32f * fraction);
            }
        }
    }

    private static Color ChargeColor(int percent) => percent switch
    {
        >= 90 => Color.FromArgb(0x59, 0xac, 0x42),
        >= 70 => Color.FromArgb(0x8c, 0xbe, 0x50),
        >= 50 => Color.FromArgb(0xbd, 0xbe, 0x3d),
        >= 30 => Color.FromArgb(0xd3, 0x9b, 0x41),
        _ => Color.FromArgb(0xe2, 0x6c, 0x5e)
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

    private sealed record Palette(Color Text, Color Outline, Color Track, Color Interior,
        Color MutedText, Color MutedCharge);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
