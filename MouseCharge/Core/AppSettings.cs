namespace MouseCharge.Core;

public sealed class AppSettings
{
    public string? DeviceId { get; set; }
    public string? LastAutomaticDeviceId { get; set; }
    public string? LastAutomaticDeviceName { get; set; }
    public int RefreshSeconds { get; set; } = 60;
    public int LowBatteryThreshold { get; set; } = 15;
    public bool NotificationsEnabled { get; set; } = true;
    public double WidgetOpacity { get; set; } = 1;
    public double WidgetScale { get; set; } = 1;
    public string Theme { get; set; } = "Dark";
    public bool TransparentBackground { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool PositionLocked { get; set; }
    public bool ClickThrough { get; set; }
    public bool StartWithWindows { get; set; }
    public bool DockToTaskbar { get; set; } = true;
    public bool TaskbarWidgetVisible { get; set; } = true;
    public bool WidgetVisible { get; set; } = true;
    public double? DesktopX { get; set; }
    public double? DesktopY { get; set; }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? null : DeviceId;
        RefreshSeconds = RefreshSeconds is 30 or 60 or 120 or 300 ? RefreshSeconds : 60;
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 1, 100);
        WidgetOpacity = double.IsFinite(WidgetOpacity) ? Math.Clamp(WidgetOpacity, 0.25, 1) : 1;
        WidgetScale = double.IsFinite(WidgetScale) ? Math.Clamp(WidgetScale, 0.75, 2) : 1;
        Theme = string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

        if (!IsDesktopCoordinate(DesktopX) || !IsDesktopCoordinate(DesktopY))
        {
            DesktopX = null;
            DesktopY = null;
        }
    }

    private static bool IsDesktopCoordinate(double? value) => value is >= int.MinValue and <= int.MaxValue;
}
