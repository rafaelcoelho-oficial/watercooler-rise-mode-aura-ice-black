namespace AuraIce.Core.Protocol;

public static class TemperatureDisplayPayload
{
    public const int Length = 24;
    public const int MinimumLength = 2;
    public const int HidReportIdLength = 1;
    public const int MinimumHidReportLength = MinimumLength + HidReportIdLength;

    public static byte[] Create(decimal celsius)
    {
        decimal rounded = decimal.Round(celsius, 1, MidpointRounding.AwayFromZero);
        if (rounded is < 0 or > 255.9m)
        {
            throw new ArgumentOutOfRangeException(nameof(celsius), celsius, "A temperatura não cabe no protocolo do display.");
        }

        int integerPart = decimal.ToInt32(decimal.Truncate(rounded));
        int decimalPart = decimal.ToInt32(decimal.Truncate((rounded - integerPart) * 10));
        byte[] payload = new byte[Length];
        payload[0] = checked((byte)integerPart);
        payload[1] = checked((byte)decimalPart);
        return payload;
    }

    public static byte[] CreateHidOutputReport(decimal celsius, int reportLength)
    {
        if (reportLength < MinimumHidReportLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportLength),
                reportLength,
                $"O relatório HID precisa ter ao menos {MinimumHidReportLength} bytes, incluindo o byte de Report ID.");
        }

        byte[] payload = Create(celsius);
        byte[] report = new byte[reportLength];

        // HidSharp inclui o byte Report ID no início do buffer. Este dispositivo
        // usa relatório sem ID; nesse caso, o primeiro byte precisa ser zero.
        int payloadLength = Math.Min(payload.Length, report.Length - HidReportIdLength);
        Buffer.BlockCopy(payload, 0, report, HidReportIdLength, payloadLength);
        return report;
    }
}
