using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MouseCharge.Views;

public partial class TaskbarIndicatorWindow : Window
{
    private static readonly Palette Dark = new(
        Brush("#9BCBFF"), Brush("#80E7AA"), Brush("#FF929B"), Brush("#BFC8D6"));

    private static readonly Palette Light = new(
        Brush("#275884"), Brush("#206D41"), Brush("#A42A43"), Brush("#4B5563"));

    private Palette _palette = Dark;
    private Placement? _placement;
    private IntPtr _handle;
    private int? _percentage;
    private bool _outdated;
    private bool _placementQueued;

    public TaskbarIndicatorWindow()
    {
        InitializeComponent();
        ApplySystemPalette();
        IsVisibleChanged += (_, _) => QueuePlacement();
    }

    public void SetReading(string mouseName, string status, int? percentage,
        bool approximate, bool outdated, bool? charging, DateTimeOffset? lastSuccessfulRead = null)
    {
        _percentage = percentage;
        _outdated = outdated;
        var amount = percentage is { } value ? value.ToString(CultureInfo.CurrentCulture) + "%" : null;
        LevelText.Text = amount is null ? "—" : (approximate ? "≈" : "") + amount;
        LevelText.Foreground = ReadingBrush();

        var details = new List<string> { mouseName, status };
        if (outdated)
        {
            details.Add("Outdated reading");
        }
        if (approximate && amount is not null)
        {
            details.Add("Approximate");
        }
        if (!outdated && charging is { } isCharging)
        {
            details.Add(isCharging ? "Charging" : "Not charging");
        }
        if (lastSuccessfulRead is { } reading)
        {
            details.Add($"Last read {reading.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)}");
        }

        AutomationProperties.SetName(LevelText, amount is null ? "Battery unavailable" : $"{amount} battery");
        AutomationProperties.SetHelpText(LevelText, string.Join(" · ", details));
    }

    // Coordinates are top-left on the physical virtual desktop, including negative monitor origins.
    public void PlaceOnTaskbar(int x, int y, IntPtr taskbarHandle)
    {
        _placement = new Placement(x, y, taskbarHandle);
        ApplyPlacement();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        _handle = source.Handle;
        source.AddHook(WindowMessage);
        var style = Native.GetWindowLong(_handle, Native.ExtendedStyle);
        Native.SetWindowLong(_handle, Native.ExtendedStyle,
            style | Native.Transparent | Native.ToolWindow | Native.NoActivateStyle);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplySystemPalette();
        ApplyPlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _handle = IntPtr.Zero;
        base.OnClosed(e);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int dpiChanged = 0x02E0;
        const int displayChanged = 0x007E;
        if (message is dpiChanged or displayChanged)
        {
            QueuePlacement();
        }

        return IntPtr.Zero;
    }

    private void QueuePlacement()
    {
        if (_handle == IntPtr.Zero || _placementQueued)
        {
            return;
        }

        _placementQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _placementQueued = false;
            ApplyPlacement();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyPlacement()
    {
        if (_handle == IntPtr.Zero || _placement is not { } target ||
            !Native.IsWindowVisible(target.Taskbar) || !Native.GetWindowRect(target.Taskbar, out var taskbar) ||
            !Native.GetWindowRect(_handle, out var current))
        {
            Indicator.Visibility = Visibility.Hidden;
            return;
        }

        var positionChanged = current.Left != target.X || current.Top != target.Y;
        var taskbarTopmost = (Native.GetWindowLong(target.Taskbar, Native.ExtendedStyle) & Native.Topmost) != 0;
        var windowAboveTaskbar = taskbarTopmost
            ? Native.GetWindow(target.Taskbar, Native.PreviousWindow)
            : IntPtr.Zero;
        var orderChanged = taskbarTopmost && windowAboveTaskbar != _handle;
        if (positionChanged || orderChanged)
        {
            // Place directly above the taskbar, leaving shell flyouts above the indicator.
            var insertAfter = windowAboveTaskbar == IntPtr.Zero ? Native.TopmostWindow : windowAboveTaskbar;
            var flags = Native.NoSize | Native.NoActivate | Native.NoOwnerZOrder;
            if (!orderChanged)
            {
                flags |= Native.NoZOrder;
            }
            if (!positionChanged)
            {
                flags |= Native.NoMove;
            }

            if (!Native.SetWindowPos(_handle, insertAfter, target.X, target.Y, 0, 0, flags) && positionChanged)
            {
                // The predecessor can disappear while the shell closes a flyout.
                Native.SetWindowPos(_handle, IntPtr.Zero, target.X, target.Y, 0, 0, flags | Native.NoZOrder);
            }
        }

        var contained = Native.GetWindowRect(_handle, out var placed) &&
            placed.Left >= taskbar.Left && placed.Top >= taskbar.Top &&
            placed.Right <= taskbar.Right && placed.Bottom <= taskbar.Bottom;
        Indicator.Visibility = contained ? Visibility.Visible : Visibility.Hidden;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(ApplySystemPalette, DispatcherPriority.Background);
    }

    private void ApplySystemPalette()
    {
        if (SystemParameters.HighContrast)
        {
            var text = SystemColors.WindowTextBrush;
            _palette = new Palette(text, text, text, text);
        }
        else
        {
            _palette = UsesLightTaskbar() ? Light : Dark;
        }

        MouseGlyph.Stroke = _palette.Mouse;
        LevelText.Foreground = ReadingBrush();
    }

    private Brush ReadingBrush() => _outdated ? _palette.Neutral : _percentage switch
    {
        >= 20 => _palette.Green,
        >= 0 => _palette.Red,
        _ => _palette.Neutral
    };

    private static bool UsesLightTaskbar()
    {
        try
        {
            const string personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            return Registry.GetValue(personalize, "SystemUsesLightTheme", 0) is 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed record Palette(Brush Mouse, Brush Green, Brush Red, Brush Neutral);
    private readonly record struct Placement(int X, int Y, IntPtr Taskbar);

    private static class Native
    {
        internal const int ExtendedStyle = -20;
        internal const int Topmost = 0x00000008;
        internal const int Transparent = 0x00000020;
        internal const int ToolWindow = 0x00000080;
        internal const int NoActivateStyle = 0x08000000;
        internal const uint PreviousWindow = 3;
        internal const uint NoSize = 0x0001;
        internal const uint NoMove = 0x0002;
        internal const uint NoZOrder = 0x0004;
        internal const uint NoActivate = 0x0010;
        internal const uint NoOwnerZOrder = 0x0200;
        internal static readonly IntPtr TopmostWindow = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr window, out Rect bounds);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);

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
