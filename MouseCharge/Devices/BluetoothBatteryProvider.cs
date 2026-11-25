using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace MouseCharge.Devices;

internal sealed class BluetoothBatteryProvider : IBatteryProvider
{
    public string Name => "Bluetooth LE Battery Service";

    public async Task<BatteryProbe> ReadAsync(MouseDevice device, CancellationToken cancellationToken)
    {
        if (device.ConnectionType != "Bluetooth LE")
            return BatteryProbe.Unavailable("The mouse does not use a Bluetooth LE connection.");
        if (device.BluetoothId is null && device.BluetoothAddress is null)
            return BatteryProbe.Unavailable("Windows did not identify a Bluetooth LE endpoint for this mouse.");

        using var bluetooth = await OpenAsync(device, cancellationToken).ConfigureAwait(false);
        if (bluetooth is null)
            return BatteryProbe.Unavailable("Windows could not open the Bluetooth LE endpoint.");
        if (bluetooth.ConnectionStatus != BluetoothConnectionStatus.Connected)
            return BatteryProbe.Unavailable("The Bluetooth LE device is asleep or disconnected.");

        var services = await bluetooth.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (services.Status != GattCommunicationStatus.Success)
            return BatteryProbe.Unavailable($"Battery Service discovery failed ({services.Status}).");
        if (services.Services.Count == 0)
            return BatteryProbe.Unavailable("The Bluetooth device has no standard Battery Service (0x180F).");

        try
        {
            var detail = "The Battery Service does not expose a readable Battery Level characteristic (0x2A19).";
            foreach (var service in services.Services)
            {
                var levels = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (levels.Status != GattCommunicationStatus.Success)
                {
                    detail = $"Battery Level discovery failed ({levels.Status}).";
                    continue;
                }

                foreach (var level in levels.Characteristics)
                {
                    if ((level.CharacteristicProperties & GattCharacteristicProperties.Read) == 0)
                        continue;
                    var value = await level.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    if (value.Status != GattCommunicationStatus.Success)
                    {
                        detail = $"Battery Level read failed ({value.Status}).";
                        continue;
                    }
                    using var reader = DataReader.FromBuffer(value.Value);
                    if (reader.UnconsumedBufferLength == 1 && reader.ReadByte() is var percentage and <= 100)
                        return new(percentage, false, null, "Read Battery Level (0x2A19) directly from the mouse.");
                    detail = "Battery Level returned a malformed or out-of-range value.";
                }
            }
            return BatteryProbe.Unavailable(detail);
        }
        finally
        {
            foreach (var service in services.Services)
                service.Dispose();
        }
    }

    private static async Task<BluetoothLEDevice?> OpenAsync(MouseDevice device, CancellationToken cancellationToken)
    {
        Task<BluetoothLEDevice> Open() => device.BluetoothId is { } id
            ? BluetoothLEDevice.FromIdAsync(id).AsTask(cancellationToken)
            : BluetoothLEDevice.FromBluetoothAddressAsync(device.BluetoothAddress!.Value).AsTask(cancellationToken);

        // Windows may require the endpoint creation call on a user interface thread.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        var operation = dispatcher is null || dispatcher.CheckAccess()
            ? Open()
            : await dispatcher.InvokeAsync(Open, System.Windows.Threading.DispatcherPriority.Background, cancellationToken)
                .Task.ConfigureAwait(false);
        return await operation.ConfigureAwait(false);
    }
}
