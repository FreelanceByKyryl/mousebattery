using System.Runtime.InteropServices;
using System.Text;

namespace MouseCharge.Devices;

internal static class MouseDiagnostics
{
    public static async Task<string> CreateReportAsync(MouseBatteryService service, CancellationToken cancellationToken)
    {
        var discovery = await service.DiscoverWithDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var report = new StringBuilder();
        report.AppendLine($"MouseCharge hardware diagnostics · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Windows: {Environment.OSVersion.VersionString} · {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($"Detected mouse devices: {discovery.Devices.Count}");
        foreach (var detail in discovery.Diagnostics)
            report.AppendLine(detail);

        foreach (var device in discovery.Devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.AppendLine().AppendLine(device.Name);
            report.AppendLine($"  Manufacturer: {device.Manufacturer ?? "Unavailable"}");
            report.AppendLine($"  Model: {device.Model ?? "Unavailable"}");
            report.AppendLine($"  Connection: {device.ConnectionType} · {(device.IsConnected ? "Windows device present" : "disconnected")}");
            report.AppendLine($"  Hardware IDs: {(device.HardwareIds.Count == 0 ? "Unavailable" : string.Join(", ", device.HardwareIds))}");
            report.AppendLine($"  Associated HID interfaces: {device.HidInterfaces.Count}");
            if (device.XtrfyM4 is { } m4)
                report.AppendLine($"  Xtrfy M4 Wireless vendor HID interfaces: {m4.VendorInterfaces.Count} (25A7:{m4.ProductId:X4})");

            var properties = device.Properties
                .Where(item => item.Key.Contains("battery", StringComparison.OrdinalIgnoreCase) ||
                               item.Key.Contains("charging", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Key);
            foreach (var (key, value) in properties)
                report.AppendLine($"  {key}: {value ?? "Unavailable"}");

            var scan = await service.ReadAsync(device, cancellationToken).ConfigureAwait(false);
            if (scan.Reading is { } reading)
            {
                var precision = reading.IsApproximate ? "approximately " : "";
                var charging = reading.IsCharging switch
                {
                    true => "charging",
                    false => "not charging",
                    _ => "charging status unavailable"
                };
                report.AppendLine($"  Battery: {precision}{reading.Percentage}% · {charging} · checked at {reading.ReadAt:HH:mm:ss}");
            }
            else
            {
                report.AppendLine("  Battery unavailable");
            }

            foreach (var result in scan.Diagnostics)
                report.AppendLine($"  {result.Provider}: {result.Detail}");

            if (device.ConnectionType == "USB receiver")
                report.AppendLine("  The USB receiver can stay present when the mouse is asleep or disconnected.");
            if (device.ConnectionType.StartsWith("USB", StringComparison.Ordinal) && scan.Reading is null)
                report.AppendLine("  A receiver must expose battery telemetry through Windows or a supported HID collection.");
        }

        return report.ToString();
    }
}
