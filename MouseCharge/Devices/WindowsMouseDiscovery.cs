using System.Globalization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;

namespace MouseCharge.Devices;

internal static class WindowsMouseDiscovery
{
    private const string ContainerId = "System.Devices.ContainerId";
    private const string LocalMachine = "System.Devices.LocalMachine";
    private const string Parent = "System.Devices.Parent";
    private const string Present = "System.Devices.Present";
    private const string HardwareIds = "System.Devices.HardwareIds";
    private const string DeviceInstanceId = "System.Devices.DeviceInstanceId";
    private const string DeviceManufacturer = "System.Devices.DeviceManufacturer";
    private const string Manufacturer = "System.Devices.Manufacturer";
    private const string ModelName = "System.Devices.ModelName";
    private const string Connected = "System.Devices.Connected";
    private const string AepContainerId = "System.Devices.Aep.ContainerId";
    private const string AepAddress = "System.Devices.Aep.DeviceAddress";
    private const string AepConnected = "System.Devices.Aep.IsConnected";
    private const string AepManufacturer = "System.Devices.Aep.Manufacturer";
    private const string AepModelName = "System.Devices.Aep.ModelName";
    private const string BluetoothAppearance = "System.Devices.Aep.Bluetooth.Le.Appearance";
    private const string BluetoothManufacturer = "System.DeviceInterface.Bluetooth.Manufacturer";
    private const string BluetoothModel = "System.DeviceInterface.Bluetooth.ModelNumber";
    private const string HidInterfaceClass = "{4D1E55B2-F16F-11CF-88CB-001111000030}";

    private static readonly Guid SystemContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    private static readonly string[] BatteryKeys =
    [
        WindowsPropertyBatteryProvider.BatteryLife,
        WindowsPropertyBatteryProvider.ChargingState,
        WindowsPropertyBatteryProvider.BatteryPlusChargingText
    ];

    private static readonly string[] DeviceKeys =
        [ContainerId, Parent, Present, DeviceManufacturer, HardwareIds, .. BatteryKeys];
    private static readonly string[] InterfaceKeys =
        [ContainerId, DeviceInstanceId, BluetoothManufacturer, BluetoothModel, .. BatteryKeys];
    private static readonly string[] ContainerKeys =
        [LocalMachine, Manufacturer, ModelName, Connected, .. BatteryKeys];
    private static readonly string[] EndpointKeys =
        [AepContainerId, AepAddress, AepConnected, AepManufacturer, AepModelName, BluetoothAppearance, .. BatteryKeys];

    public static async Task<MouseDiscovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var queryToken = timeout.Token;
        var mouseNodesTask = QueryAsync("Windows mouse devices",
            "System.Devices.ClassGuid:=\"{4D36E96F-E325-11CE-BFC1-08002BE10318}\"",
            DeviceKeys, DeviceInformationKind.Device, queryToken);
        var mouseHidTask = QueryAsync("HID mouse interfaces", HidDevice.GetDeviceSelector(1, 2),
            InterfaceKeys, DeviceInformationKind.DeviceInterface, queryToken);
        var leTask = QueryAsync("Paired Bluetooth LE endpoints", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            EndpointKeys, DeviceInformationKind.AssociationEndpoint, queryToken);
        var classicTask = QueryAsync("Paired Bluetooth endpoints", BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            EndpointKeys, DeviceInformationKind.AssociationEndpoint, queryToken);

        var queries = await Task.WhenAll(mouseNodesTask, mouseHidTask, leTask, classicTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = queries.Select(query => query.Error).OfType<string>().ToList();
        var endpoints = queries[2].Devices.Select(device => new Endpoint(device, true))
            .Concat(queries[3].Devices.Select(device => new Endpoint(device, false))).ToArray();
        var mouseInterfaces = queries[1].Devices.ToLookup(
            device => GetString(device, DeviceInstanceId) ?? device.Id, StringComparer.OrdinalIgnoreCase);
        var nodes = queries[0].Devices.ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
        var containers = new Dictionary<Guid, DeviceInformation?>();
        var bluetoothHid = new Dictionary<Guid, IReadOnlyList<DeviceInformation>>();
        var m4Devices = new Dictionary<string, XtrfyM4UsbDevice>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<DeviceInformation>? usbHidInterfaces = null;
        var usedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new Dictionary<string, MouseDevice>(StringComparer.OrdinalIgnoreCase);
        var instances = nodes.Keys.Union(mouseInterfaces.Select(group => group.Key), StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interfaces = mouseInterfaces[instance].ToArray();
            var chain = await GetNodeChainAsync(nodes, instance, queryToken).ConfigureAwait(false);
            var node = chain.FirstOrDefault();
            var leafInterface = interfaces.FirstOrDefault();
            var containerId = GetContainerId(node, ContainerId) ?? GetContainerId(leafInterface, ContainerId);
            if (containerId.HasValue && !containers.ContainsKey(containerId.Value))
            {
                containers[containerId.Value] = await TryGetDeviceAsync(containerId.Value.ToString("B"),
                    ContainerKeys, DeviceInformationKind.DeviceContainer, queryToken).ConfigureAwait(false);
            }
            var container = containerId.HasValue ? containers[containerId.Value] : null;
            if (GetBool(container, LocalMachine) is true)
            {
                containerId = null;
                container = null;
            }

            var address = GetBluetoothAddress(chain);
            var transport = GetTransport(chain, container);
            var endpoint = transport switch
            {
                "Bluetooth LE" => FindEndpoint(endpoints.Where(item => item.IsLe), containerId, address),
                "Bluetooth" => FindEndpoint(endpoints.Where(item => !item.IsLe), containerId, address),
                "Other" => FindEndpoint(endpoints, containerId, address),
                _ => null
            };
            if (endpoint is not null)
            {
                usedEndpoints.Add(endpoint.Device.Id);
                transport = endpoint.IsLe ? "Bluetooth LE" : "Bluetooth";
            }

            var isBluetooth = transport.StartsWith("Bluetooth", StringComparison.Ordinal);
            var isUsb = transport.StartsWith("USB", StringComparison.Ordinal);
            var usbParent = isUsb ? chain.LastOrDefault() : null;
            var m4Product = usbParent is null ? null : XtrfyM4BatteryProvider.GetUsbProduct(usbParent.Id);
            XtrfyM4UsbDevice? m4 = null;
            if (usbParent is not null && m4Product is { } product)
            {
                transport = product == XtrfyM4BatteryProvider.ReceiverId ? "USB receiver" : "USB";
                if (!m4Devices.TryGetValue(usbParent.Id, out m4))
                {
                    if (usbHidInterfaces is null)
                    {
                        var (results, error) = await QueryAsync("Xtrfy M4 Wireless HID collections",
                            $"System.Devices.InterfaceClassGuid:=\"{HidInterfaceClass}\"",
                            InterfaceKeys, DeviceInformationKind.DeviceInterface, queryToken).ConfigureAwait(false);
                        if (error is not null)
                            diagnostics.Add(error);
                        usbHidInterfaces = results;
                    }

                    var paths = new List<string>();
                    foreach (var candidate in usbHidInterfaces.Where(item =>
                                 item.IsEnabled && XtrfyM4BatteryProvider.IsVendorInterface(item.Id, product)))
                    {
                        if (GetString(candidate, DeviceInstanceId) is not { } candidateInstance)
                            continue;
                        var candidateChain = await GetNodeChainAsync(nodes, candidateInstance, queryToken).ConfigureAwait(false);
                        if (string.Equals(candidateChain.LastOrDefault()?.Id, usbParent.Id, StringComparison.OrdinalIgnoreCase))
                            paths.Add(candidate.Id);
                    }
                    m4 = new XtrfyM4UsbDevice(product, paths);
                    m4Devices.Add(usbParent.Id, m4);
                }
            }
            // A compound USB receiver can expose mouse and keyboard functions in one container.
            // The container's battery cannot safely be attributed to an individual mouse.
            var properties = ReadBatteryProperties(endpoint?.Device, isBluetooth ? container : null, node, leafInterface);
            var hardwareIds = chain.SelectMany(device => GetStrings(device, HardwareIds))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var name = FirstSpecificName(endpoint?.Device.Name, container?.Name, node?.Name, leafInterface?.Name);
            var model = FirstSpecificName(GetString(endpoint?.Device, AepModelName), GetString(leafInterface, BluetoothModel),
                GetString(container, ModelName), name);
            var manufacturer = GetString(endpoint?.Device, AepManufacturer) ?? GetString(leafInterface, BluetoothManufacturer)
                ?? GetString(container, Manufacturer) ?? GetDeviceManufacturer(node)
                ?? (isUsb ? GetDeviceManufacturer(chain.LastOrDefault()) : null);
            if (m4 is not null)
            {
                name = "Xtrfy M4 Wireless";
                manufacturer = "Xtrfy";
                model = "M4 Wireless";
            }
            var instancePresent = GetBool(node, Present) ?? interfaces.Any(item => item.IsEnabled);
            var isConnected = GetBool(endpoint?.Device, AepConnected) ??
                (isBluetooth ? GetBool(container, Connected) ?? instancePresent : instancePresent);
            IReadOnlyList<DeviceInformation> associatedHid = interfaces;
            if (isBluetooth && endpoint is not null && containerId is { } bluetoothContainer)
            {
                if (!bluetoothHid.TryGetValue(bluetoothContainer, out var siblings))
                {
                    var (results, error) = await QueryAsync("Bluetooth HID collections",
                        $"System.Devices.ContainerId:=\"{bluetoothContainer:B}\" AND " +
                        $"System.Devices.InterfaceClassGuid:=\"{HidInterfaceClass}\"",
                        InterfaceKeys, DeviceInformationKind.DeviceInterface, queryToken).ConfigureAwait(false);
                    if (error is not null)
                        diagnostics.Add(error);
                    siblings = results;
                    bluetoothHid.Add(bluetoothContainer, siblings);
                }

                associatedHid = interfaces.Concat(siblings.Where(item => item.IsEnabled))
                    .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var id = endpoint?.Key ?? (isBluetooth ? containerId?.ToString("D") : null) ?? instance;
            var device = new MouseDevice(id, name ?? node?.Name ?? leafInterface?.Name ?? "Mouse", manufacturer, model,
                transport, isConnected, hardwareIds, associatedHid.Select(item => item.Id).ToArray(),
                properties, endpoint?.IsLe == true ? endpoint.Device.Id : null)
            {
                XtrfyM4 = m4,
                BluetoothAddress = transport == "Bluetooth LE" &&
                                   ulong.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)
                    ? number : null
            };
            Combine(devices, device);
        }

        // A paired LE mouse can disappear from Plug and Play when it sleeps. Appearance 0x03C2 is
        // the SIG-assigned Mouse value, so this fallback does not infer device type from its name.
        foreach (var endpoint in endpoints.Where(item => item.IsLe && !usedEndpoints.Contains(item.Device.Id)))
        {
            var info = endpoint.Device;
            if (WindowsPropertyBatteryProvider.Number(Get(info, BluetoothAppearance)) != 0x03C2)
                continue;
            var device = new MouseDevice(endpoint.Key, FirstSpecificName(info.Name) ?? "Bluetooth mouse",
                GetString(info, AepManufacturer), GetString(info, AepModelName) ?? FirstSpecificName(info.Name),
                "Bluetooth LE", GetBool(info, AepConnected) ?? false, [], [], ReadBatteryProperties(info), info.Id);
            Combine(devices, device);
        }

        if (queryToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            diagnostics.Add("Windows device enumeration reached its time limit. Device details may be incomplete.");

        return new(devices.Values.OrderByDescending(device => device.IsConnected)
            .ThenByDescending(device => device.ConnectionType.StartsWith("Bluetooth", StringComparison.Ordinal))
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), diagnostics);
    }

    private static void Combine(Dictionary<string, MouseDevice> devices, MouseDevice device)
    {
        if (!devices.TryGetValue(device.Id, out var previous))
        {
            devices.Add(device.Id, device);
            return;
        }

        var preferred = previous.IsConnected && !device.IsConnected ? previous : device;
        devices[device.Id] = preferred with
        {
            IsConnected = previous.IsConnected || device.IsConnected,
            HardwareIds = previous.HardwareIds.Union(device.HardwareIds, StringComparer.OrdinalIgnoreCase).ToArray(),
            HidInterfaces = previous.HidInterfaces.Union(device.HidInterfaces, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static async Task<List<DeviceInformation>> GetNodeChainAsync(
        Dictionary<string, DeviceInformation> nodes, string instance, CancellationToken cancellationToken)
    {
        var chain = new List<DeviceInformation>();
        for (var index = 0; index < 5 && instance.Length > 0; index++)
        {
            if (instance.StartsWith(@"USB\ROOT", StringComparison.OrdinalIgnoreCase) ||
                instance.StartsWith(@"HTREE\", StringComparison.OrdinalIgnoreCase))
                break;

            if (!nodes.TryGetValue(instance, out var node))
            {
                node = await TryGetDeviceAsync(instance, DeviceKeys, DeviceInformationKind.Device,
                    cancellationToken).ConfigureAwait(false);
                if (node is null)
                    break;
                nodes.TryAdd(instance, node);
            }
            chain.Add(node);
            if (IsBluetoothAddressNode(node.Id))
                break;
            if (node.Id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) &&
                !node.Id.Split('\\')[1].Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                break;
            instance = GetString(node, Parent) ?? string.Empty;
        }
        return chain;
    }

    private static string GetTransport(IReadOnlyList<DeviceInformation> chain, DeviceInformation? container)
    {
        var nodeIds = chain.Select(node => node.Id).ToArray();
        if (nodeIds.Any(id => id.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase)))
            return "Bluetooth LE";
        if (nodeIds.Any(id => id.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase)))
            return "Bluetooth";
        if (nodeIds.Any(id => id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)))
        {
            var usbNames = chain.Select(node => node.Name).Append(container?.Name);
            return usbNames.Any(name => name?.Contains("receiver", StringComparison.OrdinalIgnoreCase) == true)
                ? "USB receiver" : "USB";
        }
        return "Other";
    }

    private static Endpoint? FindEndpoint(IEnumerable<Endpoint> endpoints, Guid? containerId, string? address)
    {
        var matches = endpoints.Where(endpoint => address is not null && endpoint.Address is { } endpointAddress
            ? endpointAddress == address
            : containerId.HasValue && GetContainerId(endpoint.Device, AepContainerId) == containerId);
        return matches.OrderByDescending(endpoint => GetBool(endpoint.Device, AepConnected) is true)
            .ThenByDescending(endpoint => endpoint.IsLe).FirstOrDefault();
    }

    private static string? GetBluetoothAddress(IEnumerable<DeviceInformation> chain)
    {
        foreach (var node in chain)
        {
            if (!IsBluetoothAddressNode(node.Id))
                continue;
            var devicePart = node.Id.Split('\\')[1];
            var address = NormalizeAddress(devicePart[4..]);
            if (address is not null)
                return address;
        }
        return null;
    }

    private static bool IsBluetoothAddressNode(string id) =>
        id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeAddress(string? value)
    {
        var address = value?.Replace(":", "").Replace("-", "");
        return address is { Length: 12 } && address.All(Uri.IsHexDigit)
            ? address.ToUpperInvariant() : null;
    }

    private static IReadOnlyDictionary<string, object?> ReadBatteryProperties(params DeviceInformation?[] sources)
    {
        var source = sources.FirstOrDefault(device =>
            WindowsPropertyBatteryProvider.Number(Get(device, WindowsPropertyBatteryProvider.BatteryLife)) is >= 0 and <= 100)
            ?? sources.FirstOrDefault(device => BatteryKeys.Any(key => Get(device, key) is not null));
        return BatteryKeys.ToDictionary(key => key, key => Get(source, key));
    }

    private static string? FirstSpecificName(params string?[] names) =>
        names.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) &&
            !name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("Mouse", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("USB Input Device", StringComparison.OrdinalIgnoreCase));

    private static string? GetDeviceManufacturer(DeviceInformation? node)
    {
        var value = GetString(node, DeviceManufacturer);
        return value is "Microsoft" or "Microsoft Corporation" or "(Standard mouse types)" ? null : value;
    }

    private static object? Get(DeviceInformation? device, string key) =>
        device is not null && device.Properties.TryGetValue(key, out var value) ? value : null;
    private static string? GetString(DeviceInformation? device, string key) =>
        Get(device, key) is string value && !string.IsNullOrWhiteSpace(value) ? value : null;
    private static bool? GetBool(DeviceInformation? device, string key) => Get(device, key) as bool?;
    private static IEnumerable<string> GetStrings(DeviceInformation? device, string key) =>
        Get(device, key) as IEnumerable<string> ?? [];
    private static Guid? GetContainerId(DeviceInformation? device, string key) =>
        Get(device, key) is Guid value && value != Guid.Empty && value != SystemContainer ? value : null;

    private static async Task<(IReadOnlyList<DeviceInformation> Devices, string? Error)> QueryAsync(
        string name, string selector, string[] properties, DeviceInformationKind kind, CancellationToken cancellationToken)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(selector, properties, kind)
                .AsTask(cancellationToken).ConfigureAwait(false);
            return (devices.ToArray(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ([], $"{name}: enumeration timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ([], $"{name}: {ex.Message} (0x{ex.HResult:X8})");
        }
    }

    private static async Task<DeviceInformation?> TryGetDeviceAsync(string id, string[] keys,
        DeviceInformationKind kind, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;
        try
        {
            return await DeviceInformation.CreateFromIdAsync(id, keys, kind).AsTask(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            return null;
        }
    }

    private sealed record Endpoint(DeviceInformation Device, bool IsLe)
    {
        public string? Address => NormalizeAddress(GetString(Device, AepAddress));
        public string Key => Address is { } address ? $"bluetooth:{address}" : Device.Id;
    }
}
