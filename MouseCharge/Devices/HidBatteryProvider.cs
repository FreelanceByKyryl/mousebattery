using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MouseCharge.Devices;

internal sealed class HidBatteryProvider : IBatteryProvider
{
    private const ushort DesktopPage = 0x01;
    private const ushort GenericDevicePage = 0x06;
    private const ushort BatteryStrength = 0x20;
    private const ushort PowerDevicePage = 0x84;
    private const ushort BatterySystemPage = 0x85;
    private const ushort RelativeStateOfCharge = 0x64;
    private const ushort Charging = 0x44;

    public string Name => "HID battery reports";

    public async Task<BatteryProbe> ReadAsync(MouseDevice device, CancellationToken cancellationToken)
    {
        var details = new HashSet<string>(StringComparer.Ordinal);
        bool bluetooth = device.ConnectionType is "Bluetooth" or "Bluetooth LE";
        foreach (string path in device.HidInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probe = await ReadCollectionAsync(path, bluetooth, cancellationToken).ConfigureAwait(false);
            if (probe.Percentage.HasValue)
                return probe;

            details.Add(probe.Detail);
        }

        return Unavailable(details.Count == 0
            ? "Windows did not expose a HID interface for this mouse."
            : string.Join(" ", details));
    }

    private static async Task<BatteryProbe> ReadCollectionAsync(
        string path, bool bluetooth, CancellationToken cancellationToken)
    {
        using var handle = Native.Open(path, readInput: false);
        if (handle.IsInvalid)
            return Unavailable($"The HID collection could not be opened (Win32 {Marshal.GetLastPInvokeError()}).");

        if (!Native.HidD_GetPreparsedData(handle, out var descriptor))
            return Unavailable($"Windows could not read the HID descriptor (Win32 {Marshal.GetLastPInvokeError()}).");

        using (descriptor)
        {
            if (Native.HidP_GetCaps(descriptor, out var caps) != Native.HidpStatusSuccess)
                return Unavailable("Windows could not parse the HID descriptor.");

            // A USB receiver can contain several peripherals. Other top-level collections
            // in that receiver do not establish which peripheral owns the battery report.
            bool mouse = caps.UsagePage == DesktopPage && caps.Usage is 0x01 or 0x02;
            bool bluetoothBattery = bluetooth && caps.UsagePage is GenericDevicePage or PowerDevicePage or BatterySystemPage;
            if (!mouse && !bluetoothBattery)
                return Unavailable("The HID collection could not be attributed to the selected mouse.");

            var featureValues = GetValueCaps(ReportType.Feature, caps.FeatureValueCaps, descriptor);
            var featureReports = BatteryReports(featureValues);
            string? featureFailure = null;
            foreach (var group in featureReports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (caps.FeatureReportLength == 0)
                    break;

                var report = new byte[caps.FeatureReportLength];
                report[0] = group.Key;
                if (!Native.HidD_GetFeature(handle, report, report.Length))
                {
                    featureFailure = $"Windows could not read the HID battery feature report (Win32 {Marshal.GetLastPInvokeError()}).";
                    continue;
                }

                var probe = ParseReport(ReportType.Feature, group, featureValues, caps.FeatureButtonCaps, descriptor, report);
                if (probe is not null)
                    return probe;

                featureFailure = "The HID battery feature report did not contain a valid capacity.";
            }

            var inputValues = GetValueCaps(ReportType.Input, caps.InputValueCaps, descriptor);
            var inputReports = BatteryReports(inputValues).ToDictionary(group => group.Key);
            if (caps.InputReportLength > 0 && inputReports.Count > 0)
            {
                var input = await ReadInputAsync(path, caps, inputValues, inputReports, descriptor, cancellationToken)
                    .ConfigureAwait(false);
                if (input.Percentage.HasValue || featureFailure is null)
                    return input;

                return Unavailable($"{featureFailure} {input.Detail}");
            }

            return Unavailable(featureFailure
                ?? "The HID descriptor exposes no supported Battery Strength or Relative State of Charge report.");
        }
    }

    private static async Task<BatteryProbe> ReadInputAsync(
        string path, HidCaps caps, HidValueCaps[] values, Dictionary<byte, IGrouping<byte, HidValueCaps>> reports,
        SafeHidDescriptor descriptor, CancellationToken cancellationToken)
    {
        using var handle = Native.Open(path, readInput: true);
        if (handle.IsInvalid)
            return Unavailable($"The driver did not allow reading HID input reports (Win32 {Marshal.GetLastPInvokeError()}).");

        // Sample ordinary reports without requesting an input report from the device.
        // Windows usually reserves the system mouse collection for its own input stack.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(300));
        var buffer = new byte[caps.InputReportLength];
        try
        {
            using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: true);
            for (int i = 0; i < 32; i++)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
                if (count == 0)
                    break;

                if (count == buffer.Length && reports.TryGetValue(buffer[0], out var fields))
                {
                    var probe = ParseReport(ReportType.Input, fields, values, caps.InputButtonCaps, descriptor, buffer);
                    if (probe is not null)
                        return probe;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The device may be asleep or only report battery changes.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Unavailable($"The HID input report stream was unavailable (0x{ex.HResult:X8}).");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Unavailable("No valid HID battery input report arrived during passive sampling.");
    }

    private static IGrouping<byte, HidValueCaps>[] BatteryReports(HidValueCaps[] caps) =>
        caps.Where(cap => cap.IsAbsolute != 0 && cap.IsAlias == 0 && cap.Units == 0 && cap.UnitsExponent == 0
            && cap.BitSize is > 0 and <= 32 && cap.ReportCount == 1
            && cap.LogicalMin >= 0 && cap.LogicalMax > cap.LogicalMin
            && (cap.IsUsage(BatterySystemPage, RelativeStateOfCharge) || cap.IsUsage(GenericDevicePage, BatteryStrength)))
            .OrderByDescending(cap => cap.IsUsage(BatterySystemPage, RelativeStateOfCharge))
            .GroupBy(cap => cap.ReportId)
            .ToArray();

    private static BatteryProbe? ParseReport(
        ReportType type, IEnumerable<HidValueCaps> fields, HidValueCaps[] values, ushort buttonCount,
        SafeHidDescriptor descriptor, byte[] report)
    {
        foreach (var cap in fields)
        {
            bool relativeCharge = cap.IsUsage(BatterySystemPage, RelativeStateOfCharge);
            ushort usage = relativeCharge ? RelativeStateOfCharge : BatteryStrength;
            if (Native.HidP_GetUsageValue(type, cap.UsagePage, cap.LinkCollection, usage,
                    out uint raw, descriptor, report, report.Length) != Native.HidpStatusSuccess
                || raw < cap.LogicalMin || raw > cap.LogicalMax)
                continue;

            bool percent = cap.LogicalMin == 0 && cap.LogicalMax == 100;
            // Relative State of Charge has a defined percent scale. Accept a non-percent
            // logical encoding of it only if its declared physical scale is 0–100.
            if (relativeCharge && !percent && (cap.PhysicalMin != 0 || cap.PhysicalMax != 100))
                continue;

            double capacity = 100d * (raw - cap.LogicalMin) / (cap.LogicalMax - cap.LogicalMin);
            bool? charging = ReadCharging(type, cap.LinkCollection, values, buttonCount, descriptor, report);
            string field = relativeCharge ? "Relative State of Charge" : "Battery Strength";
            string reportType = type == ReportType.Feature ? "feature" : "input";
            return new BatteryProbe((int)Math.Round(capacity, MidpointRounding.AwayFromZero),
                !percent, charging, $"HID {field} in {reportType} report {report[0]}.");
        }

        return null;
    }

    private static bool? ReadCharging(
        ReportType type, ushort link, HidValueCaps[] values, ushort buttonCount, SafeHidDescriptor descriptor, byte[] report)
    {
        if (values.Any(cap => cap.ReportId == report[0] && cap.LinkCollection == link && cap.IsAbsolute != 0
            && cap.LogicalMin == 0 && cap.LogicalMax == 1 && cap.IsUsage(BatterySystemPage, Charging))
            && Native.HidP_GetUsageValue(type, BatterySystemPage, link, Charging,
                out uint value, descriptor, report, report.Length) == Native.HidpStatusSuccess
            && value <= 1)
            return value == 1;

        if (buttonCount == 0)
            return null;

        var buttons = new HidButtonCaps[buttonCount];
        if (Native.HidP_GetButtonCaps(type, buttons, ref buttonCount, descriptor) != Native.HidpStatusSuccess
            || !buttons.Take(buttonCount).Any(button => button.IsCharging(report[0], link)))
            return null;

        int count = Native.HidP_MaxUsageListLength(type, BatterySystemPage, descriptor);
        if (count <= 0)
            return null;

        var activeUsages = new ushort[count];
        if (Native.HidP_GetUsages(type, BatterySystemPage, link, activeUsages, ref count,
                descriptor, report, report.Length) != Native.HidpStatusSuccess)
            return null;

        return activeUsages.AsSpan(0, count).Contains(Charging);
    }

    private static HidValueCaps[] GetValueCaps(ReportType type, ushort count, SafeHidDescriptor descriptor)
    {
        if (count == 0)
            return [];

        var values = new HidValueCaps[count];
        if (Native.HidP_GetValueCaps(type, values, ref count, descriptor) != Native.HidpStatusSuccess)
            return [];

        return values[..count];
    }

    private static BatteryProbe Unavailable(string detail) => new(null, false, null, detail);

    private enum ReportType { Input = 0, Feature = 2 }

    // Fixed offsets from the Windows WDK's hidpi.h. The native structures contain no pointers.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct HidCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputReportLength;
        [FieldOffset(8)] public ushort FeatureReportLength;
        [FieldOffset(46)] public ushort InputButtonCaps;
        [FieldOffset(48)] public ushort InputValueCaps;
        [FieldOffset(58)] public ushort FeatureButtonCaps;
        [FieldOffset(60)] public ushort FeatureValueCaps;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HidValueCaps
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(3)] public byte IsAlias;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(32)] public uint UnitsExponent;
        [FieldOffset(36)] public uint Units;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(48)] public int PhysicalMin;
        [FieldOffset(52)] public int PhysicalMax;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;

        public readonly bool IsUsage(ushort page, ushort usage) =>
            UsagePage == page && (IsRange == 0 ? UsageMin == usage : usage >= UsageMin && usage <= UsageMax);
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HidButtonCaps
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;

        public readonly bool IsCharging(byte reportId, ushort link) =>
            UsagePage == BatterySystemPage && ReportId == reportId && LinkCollection == link && IsAbsolute != 0
            && (IsRange == 0 ? UsageMin == Charging : Charging >= UsageMin && Charging <= UsageMax);
    }

    private sealed class SafeHidDescriptor() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => Native.HidD_FreePreparsedData(handle);
    }

    private static class Native
    {
        public const int HidpStatusSuccess = 0x00110000;

        private const uint GenericRead = 0x80000000;
        private const uint ShareReadWrite = 3;
        private const uint OpenExisting = 3;
        private const uint Overlapped = 0x40000000;

        public static SafeFileHandle Open(string path, bool readInput) => CreateFile(
            path, readInput ? GenericRead : 0, ShareReadWrite, IntPtr.Zero, OpenExisting,
            readInput ? Overlapped : 0, IntPtr.Zero);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle file, out SafeHidDescriptor descriptor);

        [DllImport("hid.dll")]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_FreePreparsedData(IntPtr descriptor);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetFeature(SafeFileHandle file, [In, Out] byte[] report, int length);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(SafeHidDescriptor descriptor, out HidCaps caps);

        [DllImport("hid.dll")]
        public static extern int HidP_GetValueCaps(
            ReportType type, [Out] HidValueCaps[] caps, ref ushort count, SafeHidDescriptor descriptor);

        [DllImport("hid.dll")]
        public static extern int HidP_GetButtonCaps(
            ReportType type, [Out] HidButtonCaps[] caps, ref ushort count, SafeHidDescriptor descriptor);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsageValue(ReportType type, ushort page, ushort link, ushort usage,
            out uint value, SafeHidDescriptor descriptor, [In] byte[] report, int length);

        [DllImport("hid.dll")]
        public static extern int HidP_MaxUsageListLength(ReportType type, ushort page, SafeHidDescriptor descriptor);

        [DllImport("hid.dll")]
        public static extern int HidP_GetUsages(ReportType type, ushort page, ushort link, [Out] ushort[] usages,
            ref int count, SafeHidDescriptor descriptor, [In] byte[] report, int length);
    }
}
