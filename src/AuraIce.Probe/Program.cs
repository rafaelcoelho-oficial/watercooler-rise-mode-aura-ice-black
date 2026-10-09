using AuraIce.Hardware;
using AuraIce.Telemetry;
using System.Security.Principal;

Console.WriteLine("Aura Ice Probe — diagnóstico do display");
Console.WriteLine("Feche o software oficial da Rise Mode antes de continuar.\n");

bool isAdministrator = new WindowsPrincipal(WindowsIdentity.GetCurrent())
    .IsInRole(WindowsBuiltInRole.Administrator);
if (!isAdministrator)
{
    Console.WriteLine("Aviso: o Probe não está executando como Administrador.");
    Console.WriteLine("Alguns sensores podem aparecer como 0,0 °C. Execute AuraIce.Probe.exe como Administrador.\n");
}

using var display = new RiseModeAuraIceDisplay();
using var temperatureProvider = new LibreHardwareMonitorCpuTemperatureProvider();

if (LibreHardwareMonitorCpuTemperatureProvider.IsPawnIoInstalled)
{
    Console.WriteLine($"PawnIO instalado (versão {LibreHardwareMonitorCpuTemperatureProvider.PawnIoVersion}).\n");
}
else
{
    Console.WriteLine("PawnIO AUSENTE. Sem ele, o LibreHardwareMonitor pode listar sensores, mas não conseguir ler a temperatura da CPU.");
    Console.WriteLine("Instale-o pelo LibreHardwareMonitor oficial, executado como Administrador; reinicie o Windows se for solicitado.\n");
}

Console.WriteLine("Dispositivos HID encontrados:");
IReadOnlyList<HidDisplayInfo> devices = display.Discover();
if (devices.Count == 0)
{
    Console.WriteLine("  nenhum dispositivo VID 0xAA88 / PID 0x8666 foi encontrado.");
}
else
{
    foreach (HidDisplayInfo device in devices)
    {
        Console.WriteLine($"  {device.Path} — saída: {device.OutputReportLength} bytes");
    }
}

Console.WriteLine("\nSensores de temperatura da CPU:");
try
{
    foreach (var sensor in temperatureProvider.DiscoverSensors())
    {
        Console.WriteLine($"  [{sensor.Priority}] {sensor.Name}: {sensor.CurrentCelsius?.ToString("F1") ?? "--"} °C");
    }
}
catch (Exception exception)
{
    Console.WriteLine($"  falha ao acessar sensores: {exception.Message}");
    Console.WriteLine("  tente executar o diagnóstico como Administrador.");
}

if (!display.TryConnect(out string connectionError))
{
    Console.WriteLine($"\nDisplay indisponível: {connectionError}");
    return;
}

Console.WriteLine("\nDisplay conectado. Pressione Ctrl+C para sair.");
while (true)
{
    try
    {
        var reading = temperatureProvider.ReadTemperature();
        if (reading is null)
        {
            Console.WriteLine("Nenhuma temperatura de CPU disponível.");
        }
        else
        {
            display.WriteTemperature(reading.Celsius);
            Console.WriteLine($"{DateTime.Now:T} — {reading.SensorName}: {reading.Celsius:F1} °C enviado");
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Falha no ciclo: {exception.Message}");
        display.TryConnect(out _);
    }

    await Task.Delay(TimeSpan.FromSeconds(1));
}
