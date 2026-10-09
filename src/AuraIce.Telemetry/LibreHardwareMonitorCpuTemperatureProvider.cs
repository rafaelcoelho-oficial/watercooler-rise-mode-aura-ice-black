using AuraIce.Core.Telemetry;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace AuraIce.Telemetry;

public sealed class LibreHardwareMonitorCpuTemperatureProvider : ICpuTemperatureProvider
{
    private const string ProviderName = "LibreHardwareMonitor";

    private readonly Computer _computer;
    private bool _opened;

    public static bool IsPawnIoInstalled => PawnIo.IsInstalled;

    public static Version? PawnIoVersion => PawnIo.IsInstalled ? PawnIo.Version : null;

    public LibreHardwareMonitorCpuTemperatureProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsMotherboardEnabled = true
        };
    }

    public IReadOnlyList<TemperatureSensorInfo> DiscoverSensors()
    {
        EnsureOpened();
        _computer.Accept(new UpdateVisitor());

        return EnumerateSensors(SensorType.Temperature)
            .Where(candidate => GetTemperaturePriority(candidate.Hardware.HardwareType, candidate.Sensor.Name) > 0)
            .Select(sensor => new TemperatureSensorInfo(
                sensor.Sensor.Identifier.ToString(),
                sensor.Sensor.Name,
                sensor.Sensor.Value is { } value ? Convert.ToDecimal(value) : null,
                GetTemperaturePriority(sensor.Hardware.HardwareType, sensor.Sensor.Name)))
            .OrderByDescending(sensor => sensor.Priority)
            .ThenBy(sensor => sensor.Name)
            .ToArray();
    }

    public TemperatureReading? ReadTemperature(string? preferredSensorIdentifier = null)
    {
        CpuTelemetrySnapshot snapshot = ReadSnapshot(preferredSensorIdentifier);
        CpuMetricReading reading = snapshot.Temperature;
        return reading is { IsAvailable: true, Value: { } value, SensorIdentifier: { } identifier, SensorName: { } name }
            ? new TemperatureReading(value, identifier, name, reading.Timestamp)
            : null;
    }

    /// <summary>
    /// Reads all CPU metrics after a single hardware update. Individual values may be unavailable
    /// because sensor support differs by processor, firmware and permissions.
    /// </summary>
    public CpuTelemetrySnapshot ReadSnapshot(string? preferredTemperatureSensorIdentifier = null)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        EnsureOpened();
        _computer.Accept(new UpdateVisitor());

        CpuMetricReading temperature = ReadMetric(
            () => SelectTemperature(preferredTemperatureSensorIdentifier, timestamp),
            CpuMetricKind.Temperature,
            "°C",
            timestamp);
        CpuMetricReading usage = ReadMetric(
            () => SelectUsage(timestamp),
            CpuMetricKind.Usage,
            "%",
            timestamp);
        CpuMetricReading frequency = ReadMetric(
            () => SelectFrequency(timestamp),
            CpuMetricKind.Frequency,
            "GHz",
            timestamp);
        CpuMetricReading voltage = ReadMetric(
            () => SelectVoltage(timestamp),
            CpuMetricKind.Voltage,
            "V",
            timestamp);
        CpuMetricReading power = ReadMetric(
            () => SelectPower(timestamp),
            CpuMetricKind.Power,
            "W",
            timestamp);

        return new CpuTelemetrySnapshot(temperature, usage, frequency, voltage, power, timestamp);
    }

    private static CpuMetricReading ReadMetric(
        Func<CpuMetricReading> read,
        CpuMetricKind kind,
        string unit,
        DateTimeOffset timestamp)
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            return CpuMetricReading.Unavailable(kind, unit, timestamp, exception.Message);
        }
    }

    private CpuMetricReading SelectTemperature(string? preferredIdentifier, DateTimeOffset timestamp)
    {
        var candidates = EnumerateSensors(SensorType.Temperature)
            .Where(candidate => IsUsable(candidate.Sensor, allowZero: false))
            .Select(candidate => new SensorCandidate(candidate.Hardware, candidate.Sensor,
                GetTemperaturePriority(candidate.Hardware.HardwareType, candidate.Sensor.Name)))
            .Where(candidate => candidate.Priority > 0)
            .ToArray();

        SensorCandidate? selected = preferredIdentifier is null
            ? null
            : candidates.FirstOrDefault(candidate => candidate.Sensor.Identifier.ToString() == preferredIdentifier);
        selected ??= candidates
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Sensor.Name)
            .FirstOrDefault();

        return CreateReading(selected, CpuMetricKind.Temperature, "°C", timestamp, divisor: 1m);
    }

    private CpuMetricReading SelectUsage(DateTimeOffset timestamp)
    {
        SensorCandidate? selected = CpuSensors(SensorType.Load)
            .Where(candidate => IsUsable(candidate.Sensor, allowZero: true))
            .Select(candidate => new SensorCandidate(candidate.Hardware, candidate.Sensor,
                GetUsagePriority(candidate.Sensor.Name)))
            .Where(candidate => candidate.Priority > 0)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Sensor.Name)
            .FirstOrDefault();

        return CreateReading(selected, CpuMetricKind.Usage, "%", timestamp, divisor: 1m);
    }

    private CpuMetricReading SelectFrequency(DateTimeOffset timestamp)
    {
        SensorCandidate? selected = CpuSensors(SensorType.Clock)
            .Where(candidate => IsUsable(candidate.Sensor, allowZero: false))
            .Select(candidate => new SensorCandidate(candidate.Hardware, candidate.Sensor,
                GetFrequencyPriority(candidate.Sensor.Name)))
            .Where(candidate => candidate.Priority > 0)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Sensor.Name)
            .FirstOrDefault();

        return CreateReading(selected, CpuMetricKind.Frequency, "GHz", timestamp, divisor: 1000m);
    }

    private CpuMetricReading SelectVoltage(DateTimeOffset timestamp)
    {
        SensorCandidate? selected = CpuSensors(SensorType.Voltage)
            .Where(candidate => IsUsable(candidate.Sensor, allowZero: false))
            .Select(candidate => new SensorCandidate(candidate.Hardware, candidate.Sensor,
                GetVoltagePriority(candidate.Sensor.Name)))
            .Where(candidate => candidate.Priority > 0)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Sensor.Name)
            .FirstOrDefault();

        bool isEstimated = selected is not null && IsVid(selected.Sensor.Name);
        return CreateReading(selected, CpuMetricKind.Voltage, "V", timestamp, divisor: 1m, isEstimated);
    }

    private CpuMetricReading SelectPower(DateTimeOffset timestamp)
    {
        SensorCandidate? selected = CpuSensors(SensorType.Power)
            .Where(candidate => IsUsable(candidate.Sensor, allowZero: true))
            .Select(candidate => new SensorCandidate(candidate.Hardware, candidate.Sensor,
                GetPowerPriority(candidate.Sensor.Name)))
            .Where(candidate => candidate.Priority > 0)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Sensor.Name)
            .FirstOrDefault();

        return CreateReading(selected, CpuMetricKind.Power, "W", timestamp, divisor: 1m);
    }

    private CpuMetricReading CreateReading(
        SensorCandidate? candidate,
        CpuMetricKind kind,
        string unit,
        DateTimeOffset timestamp,
        decimal divisor,
        bool isEstimated = false)
    {
        if (candidate is null || candidate.Sensor.Value is not { } rawValue || !float.IsFinite(rawValue))
        {
            return CpuMetricReading.Unavailable(kind, unit, timestamp, $"Sensor de {kind} indisponível.");
        }

        return new CpuMetricReading(
            kind,
            Convert.ToDecimal(rawValue) / divisor,
            unit,
            candidate.Sensor.Identifier.ToString(),
            candidate.Sensor.Name,
            ProviderName,
            timestamp,
            true,
            isEstimated);
    }

    private IEnumerable<(IHardware Hardware, ISensor Sensor)> CpuSensors(SensorType sensorType)
    {
        return EnumerateSensors(sensorType)
            .Where(candidate => candidate.Hardware.HardwareType == HardwareType.Cpu);
    }

    private IEnumerable<(IHardware Hardware, ISensor Sensor)> EnumerateSensors(SensorType sensorType)
    {
        foreach (IHardware hardware in _computer.Hardware)
        {
            foreach (IHardware candidateHardware in Flatten(hardware))
            {
                foreach (ISensor sensor in candidateHardware.Sensors)
                {
                    if (sensor.SensorType == sensorType)
                    {
                        yield return (candidateHardware, sensor);
                    }
                }
            }
        }
    }

    private static bool IsUsable(ISensor sensor, bool allowZero)
    {
        return sensor.Value is { } value &&
               float.IsFinite(value) &&
               (allowZero ? value >= 0 : value > 0);
    }

    private static int GetTemperaturePriority(HardwareType hardwareType, string name)
    {
        string normalized = Normalize(name);
        if (hardwareType == HardwareType.Cpu)
        {
            if (normalized.Contains("package")) return 100;
            if (normalized.Contains("tdie")) return 95;
            if (normalized.Contains("tctl")) return 90;
            if (normalized.Contains("cpu")) return 80;
            if (normalized.Contains("core average")) return 70;
            if (normalized.Contains("core")) return 30;
        }

        if (normalized.Contains("package") || normalized.Contains("cpu")) return 85;
        return 0;
    }

    private static int GetUsagePriority(string name)
    {
        string normalized = Normalize(name);
        if (normalized.Contains("total")) return 100;
        if (normalized is "cpu" or "cpu usage" or "processor") return 95;
        if (normalized.Contains("core average")) return 90;
        if (normalized.Contains("core")) return 30;
        return 0;
    }

    private static int GetFrequencyPriority(string name)
    {
        string normalized = Normalize(name);
        if (normalized.Contains("core average")) return 100;
        if (normalized.Contains("cpu core")) return 90;
        if (normalized.Contains("core")) return 80;
        if (normalized.Contains("package")) return 60;
        return 0;
    }

    private static int GetVoltagePriority(string name)
    {
        string normalized = Normalize(name);
        if (IsVid(name)) return 20;
        if (normalized.Contains("cpu core") || normalized.Contains("core voltage")) return 100;
        if (normalized.Contains("vcore")) return 95;
        if (normalized.Contains("cpu")) return 85;
        return 0;
    }

    private static int GetPowerPriority(string name)
    {
        string normalized = Normalize(name);
        if (normalized.Contains("package")) return 100;
        if (normalized.Contains("cpu core")) return 90;
        if (normalized.Contains("cpu")) return 80;
        return 0;
    }

    private static bool IsVid(string name) => Normalize(name).Contains("vid");

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static IEnumerable<IHardware> Flatten(IHardware hardware)
    {
        yield return hardware;
        foreach (IHardware child in hardware.SubHardware)
        {
            foreach (IHardware nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    private void EnsureOpened()
    {
        if (_opened)
        {
            return;
        }

        _computer.Open();
        _opened = true;
    }

    public void Dispose()
    {
        if (_opened)
        {
            _computer.Close();
            _opened = false;
        }
    }

    private sealed record SensorCandidate(IHardware Hardware, ISensor Sensor, int Priority);

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware child in hardware.SubHardware)
            {
                child.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
