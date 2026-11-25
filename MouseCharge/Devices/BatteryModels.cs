namespace MouseCharge.Devices;

internal sealed record MouseDevice(
    string Id,
    string Name,
    string? Manufacturer,
    string? Model,
    string ConnectionType,
    bool IsConnected,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> HidInterfaces,
    IReadOnlyDictionary<string, object?> Properties,
    string? BluetoothId)
{
    public ulong? BluetoothAddress { get; init; }
    public XtrfyM4UsbDevice? XtrfyM4 { get; init; }
}

internal sealed record XtrfyM4UsbDevice(ushort ProductId, IReadOnlyList<string> VendorInterfaces);

internal sealed record MouseDiscovery(IReadOnlyList<MouseDevice> Devices, IReadOnlyList<string> Diagnostics);

internal sealed record BatteryReading(
    int Percentage, bool IsApproximate, bool? IsCharging, DateTimeOffset ReadAt, string Provider);

internal sealed record ProviderDiagnostic(string Provider, string Detail);

internal sealed record BatteryScan(BatteryReading? Reading, IReadOnlyList<ProviderDiagnostic> Diagnostics);

internal sealed record BatteryProbe(int? Percentage, bool IsApproximate, bool? IsCharging, string Detail)
{
    public static BatteryProbe Unavailable(string detail) => new(null, false, null, detail);
}

internal interface IBatteryProvider
{
    string Name { get; }
    Task<BatteryProbe> ReadAsync(MouseDevice device, CancellationToken cancellationToken);
}
