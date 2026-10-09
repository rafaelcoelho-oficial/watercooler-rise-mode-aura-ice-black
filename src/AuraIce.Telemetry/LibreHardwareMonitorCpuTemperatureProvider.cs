using AuraIce.Core.Telemetry;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace AuraIce.Telemetry;

public sealed class LibreHardwareMonitorCpuTemperatureProvider : ICpuTemperatureProvider
{
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

        return EnumerateTemperatureSensors()
            .Where(candidate => GetPriority(candidate.Hardware.HardwareType, candidate.Sensor.Name) > 0)
            .Select(sensor => new TemperatureSensorInfo(
                sensor.Sensor.Identifier.ToString(),
                sensor.Sensor.Name,
                sensor.Sensor.Value is { } value ? Convert.ToDecimal(value) : null,
                GetPriority(sensor.Hardware.HardwareType, sensor.Sensor.Name)))
            .OrderByDescending(sensor => sensor.Priority)
            .ThenBy(sensor => sensor.Name)
            .ToArray();
    }

    public TemperatureReading? ReadTemperature(string? preferredSensorIdentifier = null)
    {
        EnsureOpened();
        _computer.Accept(new UpdateVisitor());

        var candidates = EnumerateTemperatureSensors()
            .Where(candidate => candidate.Sensor.Value is > 0)
            .Where(candidate => GetPriority(candidate.Hardware.HardwareType, candidate.Sensor.Name) > 0)
            .ToArray();

        (IHardware Hardware, ISensor Sensor)? selected = null;
        if (preferredSensorIdentifier is not null)
        {
            var preferred = candidates.FirstOrDefault(
                candidate => candidate.Sensor.Identifier.ToString() == preferredSensorIdentifier);
            if (preferred.Sensor is not null)
            {
                selected = preferred;
            }
        }

        if (selected is null)
        {
            var best = candidates
                .OrderByDescending(candidate => GetPriority(candidate.Hardware.HardwareType, candidate.Sensor.Name))
                .FirstOrDefault();
            if (best.Sensor is not null)
            {
                selected = best;
            }
        }

        if (selected is null || selected.Value.Sensor.Value is not { } value)
        {
            return null;
        }

        return new TemperatureReading(
            Convert.ToDecimal(value),
            selected.Value.Sensor.Identifier.ToString(),
            selected.Value.Sensor.Name,
            DateTimeOffset.UtcNow);
    }

    private IEnumerable<(IHardware Hardware, ISensor Sensor)> EnumerateTemperatureSensors()
    {
        foreach (IHardware hardware in _computer.Hardware)
        {
            foreach (IHardware candidateHardware in Flatten(hardware))
            {
                foreach (ISensor sensor in candidateHardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature)
                    {
                        yield return (candidateHardware, sensor);
                    }
                }
            }
        }
    }

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

    private static int GetPriority(HardwareType hardwareType, string name)
    {
        string normalized = name.ToLowerInvariant();
        if (hardwareType == HardwareType.Cpu)
        {
            if (normalized.Contains("package")) return 100;
            if (normalized.Contains("tdie")) return 95;
            if (normalized.Contains("tctl")) return 90;
            if (normalized.Contains("cpu")) return 80;
            if (normalized.Contains("core average")) return 70;
            if (normalized.Contains("core")) return 30;
        }

        // Algumas placas-mãe expõem a temperatura do processador pelo
        // controlador Super I/O em vez do sensor interno do CPU.
        if (normalized.Contains("package") || normalized.Contains("cpu")) return 85;

        return 0;
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
