namespace AuraIce.Core.Telemetry;

public sealed record TemperatureReading(
    decimal Celsius,
    string SensorIdentifier,
    string SensorName,
    DateTimeOffset Timestamp);

public interface ICpuTemperatureProvider : IDisposable
{
    IReadOnlyList<TemperatureSensorInfo> DiscoverSensors();
    TemperatureReading? ReadTemperature(string? preferredSensorIdentifier = null);
}

public sealed record TemperatureSensorInfo(
    string Identifier,
    string Name,
    decimal? CurrentCelsius,
    int Priority);
