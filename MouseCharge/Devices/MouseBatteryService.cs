namespace MouseCharge.Devices;

internal sealed class MouseBatteryService
{
    private readonly IBatteryProvider[] providers =
    [
        new BluetoothBatteryProvider(),
        new XtrfyM4BatteryProvider(),
        new HidBatteryProvider(),
        new WindowsPropertyBatteryProvider()
    ];

    public async Task<IReadOnlyList<MouseDevice>> DiscoverAsync(CancellationToken cancellationToken) =>
        (await DiscoverWithDiagnosticsAsync(cancellationToken).ConfigureAwait(false)).Devices;

    public Task<MouseDiscovery> DiscoverWithDiagnosticsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => WindowsMouseDiscovery.DiscoverAsync(cancellationToken), cancellationToken);

    public Task<BatteryScan> ReadAsync(MouseDevice device, CancellationToken cancellationToken) =>
        Task.Run(() => ReadProvidersAsync(device, cancellationToken), cancellationToken);

    private async Task<BatteryScan> ReadProvidersAsync(MouseDevice device, CancellationToken cancellationToken)
    {
        if (!device.IsConnected)
            return new(null, [new("Device connection", "Windows reports the mouse is disconnected.")]);

        var diagnostics = new List<ProviderDiagnostic>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatteryProbe result;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                result = await provider.ReadAsync(device, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = BatteryProbe.Unavailable("The battery read timed out.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = BatteryProbe.Unavailable($"{ex.Message} (0x{ex.HResult:X8})");
            }
            cancellationToken.ThrowIfCancellationRequested();

            diagnostics.Add(new(provider.Name, result.Detail));
            if (result.Percentage is >= 0 and <= 100)
            {
                var reading = new BatteryReading(result.Percentage.Value, result.IsApproximate,
                    result.IsCharging, DateTimeOffset.Now, provider.Name);
                return new(reading, diagnostics);
            }
        }

        return new(null, diagnostics);
    }
}
