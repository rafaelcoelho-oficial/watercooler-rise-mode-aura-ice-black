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
                        _state.Update(new AuraIceStatusSnapshot(
                            DeviceConnected: true,
                            TemperatureCelsius: null,
                            SensorName: null,
                            LastError: null,
                            Timestamp: DateTimeOffset.UtcNow));
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

                TemperatureReading? reading = _temperatureProvider.ReadTemperature(settings.PreferredSensorIdentifier);
                if (reading is null)
                {
                    _logger.LogWarning("Nenhum sensor de temperatura da CPU disponível.");
                    _state.Update(_state.Snapshot with
                    {
                        DeviceConnected = connected,
                        LastError = "Nenhum sensor de temperatura da CPU disponível.",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    if (connected)
                    {
                        _display.WriteTemperature(reading.Celsius);
                    }

                    _state.Update(new AuraIceStatusSnapshot(
                        DeviceConnected: connected,
                        TemperatureCelsius: reading.Celsius,
                        SensorName: reading.SensorName,
                        LastError: connected ? null : _state.Snapshot.LastError,
                        Timestamp: reading.Timestamp));
                    _logger.LogDebug("Temperatura enviada: {Temperature:F1} °C ({Sensor})", reading.Celsius, reading.SensorName);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                connected = false;
                _display.Disconnect();
                _state.Update(_state.Snapshot with
                {
                    DeviceConnected = false,
                    LastError = exception.Message,
                    Timestamp = DateTimeOffset.UtcNow
                });
                _logger.LogWarning(exception, "Falha no ciclo do display; será feita nova tentativa.");
            }
            catch (Exception exception)
            {
                _state.Update(_state.Snapshot with
                {
                    LastError = exception.Message,
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
}
