namespace MouseCharge.Core;

public sealed class AppSettings
{
    public string? DeviceId { get; set; }
    public string? LastAutomaticDeviceId { get; set; }
    public string? LastAutomaticDeviceName { get; set; }
    public int RefreshSeconds { get; set; } = 60;
    public int LowBatteryThreshold { get; set; } = 15;
    public bool NotificationsEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool TaskbarIndicatorVisible { get; set; } = true;

    public AppSettings Copy() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? null : DeviceId;
        RefreshSeconds = RefreshSeconds is 30 or 60 or 120 or 300 ? RefreshSeconds : 60;
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 1, 100);
    }
}
