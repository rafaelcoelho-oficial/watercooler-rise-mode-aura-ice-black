using AuraIce.Core.Configuration;
using AuraIce.Core.Status;
using AuraIce.Core.Telemetry;
using AuraIce.Hardware;
using AuraIce.Telemetry;

namespace AuraIce.Service;

public sealed class AuraIceWorker : BackgroundService
{
    private readonly ILogger<AuraIceWorker> _logger;
    private readonly AuraIceSettingsStore _settingsStore;
    private readonly RiseModeAuraIceDisplay _display;
    private readonly LibreHardwareMonitorCpuTemperatureProvider _temperatureProvider;
    private readonly AuraIceStatusState _state;

    public AuraIceWorker(
        ILogger<AuraIceWorker> logger,
        AuraIceSettingsStore settingsStore,
        RiseModeAuraIceDisplay display,
        LibreHardwareMonitorCpuTemperatureProvider temperatureProvider,
        AuraIceStatusState state)
    {
        _logger = logger;
        _settingsStore = settingsStore;
        _display = display;
        _temperatureProvider = temperatureProvider;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AuraIceSettings settings = await _settingsStore.LoadAsync(stoppingToken);
        bool connected = false;

        _logger.LogInformation(
            "Aura Ice service iniciado. Intervalo: {Interval} ms; VID: 0x{VendorId:X4}; PID: 0x{ProductId:X4}",
            settings.UpdateIntervalMilliseconds,
            settings.VendorId,
            settings.ProductId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                settings = await _settingsStore.LoadAsync(stoppingToken);

                if (!connected)
                {
                    connected = _display.TryConnect(out string connectionError);
                    if (connected)
                    {
                        _logger.LogInformation("Display Rise Mode conectado.");
                    }
                    else
                    {
                        _logger.LogDebug("Display ainda não disponível: {Error}", connectionError);
                        _state.Update(_state.Snapshot with
                        {
                            DeviceConnected = false,
                            LastError = connectionError,
                            Timestamp = DateTimeOffset.UtcNow
                        });
                    }
                }

                CpuTelemetrySnapshot telemetry = _temperatureProvider.ReadSnapshot(settings.PreferredSensorIdentifier);
                CpuMetricReading temperature = telemetry.Temperature;
                if (connected && temperature is { IsAvailable: true, Value: { } value })
                {
                    _display.WriteTemperature(value);
                }

                string? telemetryError = temperature.IsAvailable
                    ? null
                    : temperature.Error ?? "Nenhum sensor de temperatura da CPU disponível.";
                _state.Update(new AuraIceStatusSnapshot(
                    DeviceConnected: connected,
                    TemperatureCelsius: temperature.Value,
                    SensorName: temperature.SensorName,
                    LastError: connected ? telemetryError : _state.Snapshot.LastError ?? telemetryError,
                    Timestamp: telemetry.Timestamp)
                {
                    Telemetry = telemetry
                });

                _logger.LogDebug(
                    "Snapshot CPU atualizado. Temperatura: {Temperature}; Uso: {Usage}; Frequência: {Frequency}; Tensão: {Voltage}; Potência: {Power}",
                    FormatMetric(telemetry.Temperature),
                    FormatMetric(telemetry.Usage),
                    FormatMetric(telemetry.Frequency),
                    FormatMetric(telemetry.Voltage),
                    FormatMetric(telemetry.Power));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                connected = false;
                _display.Disconnect();
                _state.Update(_state.Snapshot with
                {
                    DeviceConnected = false,
                    LastError = exception.Message,
                    Telemetry = CpuTelemetrySnapshot.CreateUnavailable(DateTimeOffset.UtcNow, exception.Message),
                    Timestamp = DateTimeOffset.UtcNow
                });
                _logger.LogWarning(exception, "Falha no ciclo do display; será feita nova tentativa.");
            }
            catch (Exception exception)
            {
                _state.Update(_state.Snapshot with
                {
                    LastError = exception.Message,
                    Telemetry = CpuTelemetrySnapshot.CreateUnavailable(DateTimeOffset.UtcNow, exception.Message),
                    Timestamp = DateTimeOffset.UtcNow
                });
                _logger.LogError(exception, "Falha inesperada no ciclo do serviço.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(settings.UpdateIntervalMilliseconds, 250, 10_000)), stoppingToken);
        }
    }

    public override void Dispose()
    {
        _display.Dispose();
        _temperatureProvider.Dispose();
        base.Dispose();
    }

    private static string FormatMetric(CpuMetricReading reading)
    {
        return reading.Value is { } value
            ? $"{value:0.##} {reading.Unit}"
            : "N/D";
    }
}
