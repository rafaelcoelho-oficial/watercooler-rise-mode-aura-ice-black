using AuraIce.Core.Configuration;
using AuraIce.Hardware;
using AuraIce.Service;
using AuraIce.Telemetry;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AuraIceTemperatureService";
});

builder.Services.AddSingleton<AuraIceSettingsStore>();
builder.Services.AddSingleton<RiseModeAuraIceDisplay>();
builder.Services.AddSingleton<LibreHardwareMonitorCpuTemperatureProvider>();
builder.Services.AddSingleton<AuraIceStatusState>();
builder.Services.AddHostedService<AuraIceWorker>();
builder.Services.AddHostedService<AuraIceStatusServer>();

await builder.Build().RunAsync();
