using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MouseCharge.Devices;

internal sealed class XtrfyM4BatteryProvider : IBatteryProvider
{
    internal const ushort VendorId = 0x25A7;
    internal const ushort ReceiverId = 0xFA99;
    internal const ushort CableId = 0xFA92;
    private const ushort QueryUsagePage = 0xFF02;
    private const ushort ReplyUsagePage = 0xFF01;

    public string Name => "Xtrfy M4 Wireless";

    public async Task<BatteryProbe> ReadAsync(MouseDevice device, CancellationToken cancellationToken)
    {
        if (device.XtrfyM4 is not { ProductId: ReceiverId or CableId } m4)
            return BatteryProbe.Unavailable("Windows did not identify a supported M4 Wireless device.");
        cancellationToken.ThrowIfCancellationRequested();
        using var command = OpenCollection(m4, feature: true);
        using var reply = OpenCollection(m4, feature: false);
        if (command is null || reply is null)
            return BatteryProbe.Unavailable("The M4 Wireless vendor HID interfaces or report descriptors were unavailable.");

        using var stream = new FileStream(reply, FileAccess.Read, bufferSize: 1, isAsync: true);
        if (m4.ProductId == ReceiverId)
        {
            var connectionQuery = XtrfyM4BatteryProtocol.BuildConnectionQuery();
            var connection = await ExchangeAsync(command, stream, connectionQuery,
                static frame => XtrfyM4BatteryProtocol.TryParseConnectionReply(frame, out _),
                cancellationToken).ConfigureAwait(false);
            if (connection is null || !XtrfyM4BatteryProtocol.TryParseConnectionReply(connection, out var connected))
                return BatteryProbe.Unavailable("No valid connection reply arrived from the M4 Wireless receiver.");
            if (!connected)
                return BatteryProbe.Unavailable("The receiver is present, and the mouse is asleep or disconnected.");
        }

        var batteryQuery = XtrfyM4BatteryProtocol.BuildBatteryQuery();
        var battery = await ExchangeAsync(command, stream, batteryQuery,
            static frame => XtrfyM4BatteryProtocol.TryParseBatteryReply(frame, out _, out _),
            cancellationToken).ConfigureAwait(false);
        if (battery is null || !XtrfyM4BatteryProtocol.TryParseBatteryReply(battery, out var percentage, out var charging))
            return BatteryProbe.Unavailable("No valid M4 Wireless battery reply arrived.");

        return new(percentage, false, charging, "Read the device-reported percentage and charging state.");
    }

    private static SafeFileHandle? OpenCollection(XtrfyM4UsbDevice device, bool feature)
    {
        var usagePage = feature ? QueryUsagePage : ReplyUsagePage;
        var reportId = feature ? XtrfyM4BatteryProtocol.FeatureReportId : XtrfyM4BatteryProtocol.InputReportId;
        SafeFileHandle? match = null;
        try
        {
            foreach (string path in device.VendorInterfaces)
            {
                if (!IsVendorInterface(path, device.ProductId))
                    continue;

                SafeFileHandle? handle = HidBatteryProvider.OpenVendorCollection(path, feature);
                try
                {
                    if (handle.IsInvalid || !HidBatteryProvider.HasVendorReport(
                            handle, VendorId, device.ProductId, usagePage, reportId,
                            XtrfyM4BatteryProtocol.ReportLength, feature))
                        continue;
                    if (match is not null)
                        return null;

                    match = handle;
                    handle = null;
                }
                finally
                {
                    handle?.Dispose();
                }
            }

            var result = match;
            match = null;
            return result;
        }
        finally
        {
            match?.Dispose();
        }
    }

    private static async Task<byte[]?> ExchangeAsync(
        SafeFileHandle command, FileStream stream, byte[] query, Func<byte[], bool> isValidReply,
        CancellationToken cancellationToken)
    {
        var response = new byte[XtrfyM4BatteryProtocol.ReportLength];
        await DrainInputAsync(stream, response, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!HidBatteryProvider.SendFeature(command, query))
            throw new IOException($"Windows could not send the M4 Wireless status query (Win32 {Marshal.GetLastPInvokeError()}).");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(600));
        try
        {
            for (int i = 0; i < 32; i++)
            {
                int length = await stream.ReadAsync(response.AsMemory(), timeout.Token).ConfigureAwait(false);
                if (length == 0)
                    break;
                if (length == response.Length && isValidReply(response))
                    return response;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    private static async Task DrainInputAsync(FileStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        // Consume reports queued on this handle without flushing the collection for other readers.
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(TimeSpan.FromMilliseconds(20));
        try
        {
            for (var index = 0; index < 32; index++)
            {
                if (await stream.ReadAsync(buffer.AsMemory(), drain.Token).ConfigureAwait(false) == 0)
                    return;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal static ushort? GetUsbProduct(string id)
    {
        if (id.StartsWith($@"USB\VID_{VendorId:X4}&PID_{ReceiverId:X4}\", StringComparison.OrdinalIgnoreCase))
            return ReceiverId;
        if (id.StartsWith($@"USB\VID_{VendorId:X4}&PID_{CableId:X4}\", StringComparison.OrdinalIgnoreCase))
            return CableId;
        return null;
    }

    internal static bool IsVendorInterface(string path, ushort product)
    {
        string prefix = $@"\\?\HID#VID_{VendorId:X4}&PID_{product:X4}&MI_01";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            path.Length > prefix.Length && path[prefix.Length] is '&' or '#';
    }
}
