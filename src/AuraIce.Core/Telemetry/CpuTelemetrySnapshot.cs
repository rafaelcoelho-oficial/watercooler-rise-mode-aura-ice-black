namespace AuraIce.Core.Telemetry;

public enum CpuMetricKind
{
    Temperature,
    Usage,
    Frequency,
    Voltage,
    Power
}

public sealed record CpuMetricReading(
    CpuMetricKind Kind,
    decimal? Value,
    string Unit,
    string? SensorIdentifier,
    string? SensorName,
    string Source,
    DateTimeOffset Timestamp,
    bool IsAvailable,
    bool IsEstimated = false,
    string? Error = null)
{
    public static CpuMetricReading Unavailable(
        CpuMetricKind kind,
        string unit,
        DateTimeOffset timestamp,
        string? error = null) => new(
            kind,
            null,
            unit,
            null,
            null,
            "LibreHardwareMonitor",
            timestamp,
            false,
            false,
            error);
}

public sealed record CpuTelemetrySnapshot(
    CpuMetricReading Temperature,
    CpuMetricReading Usage,
    CpuMetricReading Frequency,
    CpuMetricReading Voltage,
    CpuMetricReading Power,
    DateTimeOffset Timestamp,
    string? Error = null)
{
    public static CpuTelemetrySnapshot Empty { get; } = CreateUnavailable(DateTimeOffset.UtcNow);

    public static CpuTelemetrySnapshot CreateUnavailable(DateTimeOffset timestamp, string? error = null) => new(
        CpuMetricReading.Unavailable(CpuMetricKind.Temperature, "°C", timestamp, error),
        CpuMetricReading.Unavailable(CpuMetricKind.Usage, "%", timestamp, error),
        CpuMetricReading.Unavailable(CpuMetricKind.Frequency, "GHz", timestamp, error),
        CpuMetricReading.Unavailable(CpuMetricKind.Voltage, "V", timestamp, error),
        CpuMetricReading.Unavailable(CpuMetricKind.Power, "W", timestamp, error),
        timestamp,
        error);
}
