using AuraIce.Core.Telemetry;

namespace AuraIce.Core.Status;

public sealed record AuraIceStatusSnapshot(
    bool DeviceConnected,
    decimal? TemperatureCelsius,
    string? SensorName,
    string? LastError,
    DateTimeOffset Timestamp)
{
    public CpuTelemetrySnapshot Telemetry { get; init; } = CpuTelemetrySnapshot.Empty;

    public static AuraIceStatusSnapshot Empty { get; } = new(
        DeviceConnected: false,
        TemperatureCelsius: null,
        SensorName: null,
        LastError: null,
        Timestamp: DateTimeOffset.UtcNow);
}
