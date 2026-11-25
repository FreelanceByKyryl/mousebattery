using System.IO;

namespace MouseCharge.Core;

internal sealed record LastKnownBattery(int Percentage, bool IsApproximate, bool? IsCharging,
    DateTimeOffset ReadAt, string Provider);

internal sealed class BatteryHistory
{
    private sealed record DeviceHistory(LastKnownBattery? Reading, bool WarningSent);

    private readonly string _path;
    private readonly Dictionary<string, DeviceHistory> _devices = new(StringComparer.OrdinalIgnoreCase);

    public BatteryHistory() : this(LocalJson.AppDirectory)
    {
    }

    internal BatteryHistory(string directory)
    {
        _path = Path.Combine(directory, "battery-history.json");
        var saved = LocalJson.Load<Dictionary<string, DeviceHistory?>>(_path, out var issue);
        PersistenceIssue = issue;
        if (saved is null)
        {
            return;
        }

        foreach (var (deviceId, history) in saved)
        {
            if (history?.Reading is { Percentage: >= 0 and <= 100 } reading && reading.ReadAt != default)
            {
                _devices[deviceId] = history;
            }
        }
    }

    public string? PersistenceIssue { get; private set; }

    public LastKnownBattery? GetLastKnown(string deviceId) => _devices.GetValueOrDefault(deviceId)?.Reading;

    public bool RecordReading(string deviceId, LastKnownBattery reading, int warningThreshold, bool notificationsEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentOutOfRangeException.ThrowIfNegative(reading.Percentage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reading.Percentage, 100);

        var previous = _devices.GetValueOrDefault(deviceId);
        if (reading.ReadAt < previous?.Reading?.ReadAt)
        {
            return false;
        }

        var isLow = reading.Percentage < warningThreshold;
        var warningSent = previous?.WarningSent == true;
        var shouldWarn = isLow && notificationsEnabled && !warningSent;
        _devices[deviceId] = new DeviceHistory(reading, isLow && (warningSent || shouldWarn));

        try
        {
            LocalJson.Save(_path, _devices);
            PersistenceIssue = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            PersistenceIssue = $"Could not save {Path.GetFileName(_path)}. {error.Message}";
        }

        return shouldWarn;
    }
}
