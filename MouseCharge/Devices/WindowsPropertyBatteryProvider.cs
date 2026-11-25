namespace MouseCharge.Devices;

internal sealed class WindowsPropertyBatteryProvider : IBatteryProvider
{
    internal const string BatteryLife = "System.Devices.BatteryLife";
    internal const string ChargingState = "System.Devices.ChargingState";
    internal const string BatteryPlusChargingText = "System.Devices.BatteryPlusChargingText";

    public string Name => "Windows battery property";

    public Task<BatteryProbe> ReadAsync(MouseDevice device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var percentage = Number(device.Properties.GetValueOrDefault(BatteryLife));
        var charging = Number(device.Properties.GetValueOrDefault(ChargingState)) switch
        {
            0 => false,
            1 => true,
            _ => (bool?)null
        };
        var probe = percentage switch
        {
            >= 0 and <= 100 => new BatteryProbe(percentage, true, charging,
                "Windows reports a cached battery level. The precision and age are unavailable."),
            null => BatteryProbe.Unavailable("Windows did not expose a numeric System.Devices.BatteryLife for this device."),
            _ => BatteryProbe.Unavailable($"Windows reported an unknown or invalid battery value ({percentage}).")
        };
        return Task.FromResult(probe);
    }

    internal static int? Number(object? value) => value switch
    {
        byte number => number,
        ushort number => number,
        int number => number,
        uint number when number <= int.MaxValue => (int)number,
        _ => null
    };
}
