using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace MouseCharge.Views;

public partial class WidgetWindow : Window
{
    private const double DefaultWidth = 300;
    private const double DefaultHeight = 139;
    private const double EdgeInset = 18;

    private static readonly Palette Dark = new(
        Brush("#F21B1F26"), Brush("#73E3A7"), Brush("#A9B3C2"));

    private static readonly Palette Light = new(
        Brush("#F8F8FAFD"), Brush("#206D45"), Brush("#515D6D"));

    private static readonly Brush TransparentSurface = Brush("#01000000");
    private readonly Effect _shadow;
    private Palette _palette = Dark;
    private IntPtr _handle;
    private (double? X, double? Y) _initialPosition;
    private bool _dragging;
    private bool _locked;
    private bool _clickThrough;
    private bool _transparent;
    private bool _placementQueued;
    private bool _connected;
    private bool _outdated;
    private int? _percentage;

    public WidgetWindow()
    {
        InitializeComponent();
        _shadow = Surface.Effect;
        ApplyPalette();
    }

    public event EventHandler? PositionChanged;

    // Absolute virtual-desktop coordinates in physical pixels, including negative origins.
    public double? DesktopX { get; private set; }
    public double? DesktopY { get; private set; }

    public void ApplyPreferences(double opacity, double scale, string theme,
        bool transparentBackground, bool alwaysOnTop, bool positionLocked, bool clickThrough)
    {
        Opacity = Math.Clamp(opacity, 0.25, 1);
        var size = Math.Clamp(scale, 0.75, 2);
        var width = DefaultWidth * size;
        if (Width != width)
        {
            Width = width;
            Height = DefaultHeight * size;
            QueuePlacementCheck();
        }

        Topmost = alwaysOnTop;
        _locked = positionLocked;
        _clickThrough = clickThrough;
        UpdateWindowStyle();
        _palette = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? Light : Dark;
        _transparent = transparentBackground;
        ApplyPalette();
    }

    public void SetReading(string mouseName, string status, int? percentage,
        bool approximate, bool outdated, bool? charging, bool connected,
        DateTimeOffset? lastSuccessfulRead = null)
    {
        var levelChanged = _percentage != percentage;
        _percentage = percentage;
        _outdated = outdated;
        _connected = connected;
        LevelText.Text = percentage is { } value
            ? value.ToString(CultureInfo.CurrentCulture) + "%"
            : "—";

        var note = BatteryDescription(percentage, approximate, outdated);
        BatteryNote.Text = note;
        BatteryNote.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(LevelText, percentage.HasValue
            ? $"{LevelText.Text} battery"
            : "Battery unavailable");

        var details = new List<string> { mouseName, status };
        if (note.Length > 0)
        {
            details.Add(note);
        }
        if (!outdated && charging is { } isCharging)
        {
            details.Add(isCharging ? "Charging" : "Not charging");
        }
        if (lastSuccessfulRead is { } reading)
        {
            details.Add($"Last read {reading.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)}");
        }
        BatteryReading.ToolTip = string.Join(Environment.NewLine, details);
        AutomationProperties.SetHelpText(LevelText, string.Join(" · ", details));

        ApplyReadingColor();
        if (levelChanged && IsVisible && SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(200))
            {
                FillBehavior = FillBehavior.Stop
            };
            LevelText.BeginAnimation(OpacityProperty, animation);
        }
    }

    public void RestorePosition(double? x, double? y)
    {
        _initialPosition = (x, y);
        PlaceAt(x, y);
    }

    public void ResetPosition() => RestorePosition(null, null);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        _handle = source.Handle;
        source.AddHook(WindowMessage);
        UpdateWindowStyle();
        PlaceAt(_initialPosition.X, _initialPosition.Y);
    }

    protected override void OnClosed(EventArgs e)
    {
        _handle = IntPtr.Zero;
        base.OnClosed(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_locked || _clickThrough || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        e.Handled = true;
        _dragging = true;
        try
        {
            DragMove();
        }
        finally
        {
            _dragging = false;
            KeepOnScreen();
        }
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        const int dpiChanged = 0x02E0;
        const int displayChanged = 0x007E;
        const int settingsChanged = 0x001A;
        const int workAreaChanged = 0x002F;
        if (message is dpiChanged or displayChanged ||
            (message == settingsChanged && wParam.ToInt64() == workAreaChanged))
        {
            QueuePlacementCheck();
        }

        return IntPtr.Zero;
    }

    private void QueuePlacementCheck()
    {
        if (_handle == IntPtr.Zero || _placementQueued)
        {
            return;
        }

        _placementQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _placementQueued = false;
            if (!_dragging)
            {
                KeepOnScreen();
            }
        }, DispatcherPriority.Background);
    }

    private void KeepOnScreen()
    {
        if (Native.GetWindowRect(_handle, out var window))
        {
            PlaceAt(window.Left, window.Top, Native.MonitorFromRect(ref window, Native.NearestMonitor));
        }
    }

    private void PlaceAt(double? x, double? y, IntPtr monitor = default)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var hasPosition = IsScreenCoordinate(x) && IsScreenCoordinate(y);
        var point = hasPosition ? new Native.Point((int)x!.Value, (int)y!.Value) : default;
        if (monitor == IntPtr.Zero)
        {
            monitor = Native.MonitorFromPoint(point, hasPosition ? Native.NearestMonitor : Native.PrimaryMonitor);
        }

        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var dpiScale = Native.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0
            ? dpiX / 96.0
            : VisualTreeHelper.GetDpi(this).DpiScaleX;
        var area = info.WorkArea;
        var maxX = Math.Max(area.Left, area.Right - (int)Math.Ceiling(Width * dpiScale));
        var maxY = Math.Max(area.Top, area.Bottom - (int)Math.Ceiling(Height * dpiScale));
        var inset = (int)Math.Round(EdgeInset * dpiScale);

        var left = Math.Clamp(hasPosition ? point.X : maxX - inset, area.Left, maxX);
        var top = Math.Clamp(hasPosition ? point.Y : maxY - inset, area.Top, maxY);
        if (Native.GetWindowRect(_handle, out var current) && (left != current.Left || top != current.Top))
        {
            MoveTo(left, top);
        }

        if (Native.GetWindowRect(_handle, out var actual) && (DesktopX != actual.Left || DesktopY != actual.Top))
        {
            DesktopX = actual.Left;
            DesktopY = actual.Top;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsScreenCoordinate(double? value) => value is >= int.MinValue and <= int.MaxValue;

    private void MoveTo(int x, int y)
    {
        const uint noSize = 0x0001;
        const uint noZOrder = 0x0004;
        const uint noActivate = 0x0010;
        Native.SetWindowPos(_handle, IntPtr.Zero, x, y, 0, 0, noSize | noZOrder | noActivate);
    }

    private void UpdateWindowStyle()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        const int extendedStyle = -20;
        const int transparent = 0x00000020;
        const int toolWindow = 0x00000080;
        var current = Native.GetWindowLong(_handle, extendedStyle);
        var style = (current | toolWindow) & ~transparent;
        if (_clickThrough)
        {
            style |= transparent;
        }

        if (style != current)
        {
            Native.SetWindowLong(_handle, extendedStyle, style);
        }
    }

    private void ApplyPalette()
    {
        Surface.Background = _transparent ? TransparentSurface : _palette.Surface;
        Surface.Effect = _transparent ? null : _shadow;
        BatteryNote.Foreground = _palette.Neutral;
        ApplyReadingColor();
    }

    private void ApplyReadingColor()
    {
        LevelText.Foreground = _connected && !_outdated && _percentage.HasValue
            ? _palette.Green
            : _palette.Neutral;
    }

    private static string BatteryDescription(int? percentage, bool approximate, bool outdated)
    {
        if (!percentage.HasValue)
        {
            return "Battery unavailable";
        }

        if (outdated)
        {
            return approximate ? "Outdated · Approximate" : "Outdated reading";
        }

        return approximate ? "Approximate" : string.Empty;
    }

    private static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed record Palette(Brush Surface, Brush Green, Brush Neutral);

    private static class Native
    {
        internal const uint NearestMonitor = 2;
        internal const uint PrimaryMonitor = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;

            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo
        {
            public int Size;
            public Rect Area;
            public Rect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect bounds);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromRect(ref Rect bounds, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("shcore.dll")]
        internal static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        internal static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y,
            int width, int height, uint flags);
    }
}
