using System.Globalization;
using MouseCharge.Core;
using MouseCharge.Devices;

namespace MouseCharge.Services;

internal sealed class BatteryNotificationService(BatteryHistory history, TrayService tray)
{
    public void Observe(MouseDevice device, BatteryReading reading, AppSettings settings)
    {
        var known = new LastKnownBattery(
            reading.Percentage, reading.IsApproximate, reading.IsCharging, reading.ReadAt, reading.Provider);
        if (!history.RecordReading(device.Id, known, settings.LowBatteryThreshold, settings.NotificationsEnabled))
            return;

        var percent = reading.Percentage.ToString(CultureInfo.InvariantCulture) + "%";
        var amount = reading.IsApproximate ? $"about {percent}" : percent;
        tray.ShowWarning("MouseCharge · Low battery", $"{device.Name} is at {amount}.");
    }
}
