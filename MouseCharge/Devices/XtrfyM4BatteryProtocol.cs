namespace MouseCharge.Devices;

internal static class XtrfyM4BatteryProtocol
{
    // Protocol observations verified on M4 Wireless firmware V3.18 and receiver 13.34:
    // https://github.com/Xurify/xtrfy-m4-control-software/blob/cf71362c06f0c8a911078298bd89af4046e821c6/README.md#protocol-notes
    public const int ReportLength = 17;
    internal const byte FeatureReportId = 0x08;
    internal const byte InputReportId = 0x09;

    private const byte ReadConnection = 0x03;
    private const byte ReadBattery = 0x04;

    public static byte[] BuildBatteryQuery() => BuildQuery(ReadBattery);
    public static byte[] BuildConnectionQuery() => BuildQuery(ReadConnection);

    public static bool TryParseBatteryReply(ReadOnlySpan<byte> report, out int percentage, out bool isCharging)
    {
        percentage = default;
        isCharging = default;
        if (!IsReply(report, ReadBattery) || report[6] > 100 || report[7] > 1)
            return false;

        percentage = report[6];
        isCharging = report[7] == 1;
        return true;
    }

    public static bool TryParseConnectionReply(ReadOnlySpan<byte> report, out bool isConnected)
    {
        isConnected = default;
        if (!IsReply(report, ReadConnection) || report[6] > 1)
            return false;

        isConnected = report[6] == 1;
        return true;
    }

    private static byte[] BuildQuery(byte operation)
    {
        var frame = new byte[ReportLength];
        frame[0] = FeatureReportId;
        frame[1] = operation;
        frame[^1] = CalculateChecksum(frame.AsSpan(0, ReportLength - 1));
        return frame;
    }

    private static bool IsReply(ReadOnlySpan<byte> frame, byte operation) =>
        frame.Length == ReportLength && frame[0] == InputReportId && frame[1] == operation
        && frame[2] == 0 && CalculateChecksum(frame[..^1]) == frame[^1];

    private static byte CalculateChecksum(ReadOnlySpan<byte> payload)
    {
        int checksum = 0x55;
        foreach (byte value in payload)
            checksum -= value;
        return unchecked((byte)checksum);
    }
}
