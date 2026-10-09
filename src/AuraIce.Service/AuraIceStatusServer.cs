using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AuraIce.Core.Status;

namespace AuraIce.Service;

public sealed class AuraIceStatusServer : BackgroundService
{
    private readonly AuraIceStatusState _state;
    private readonly ILogger<AuraIceStatusServer> _logger;

    public AuraIceStatusServer(AuraIceStatusState state, ILogger<AuraIceStatusServer> logger)
    {
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = NamedPipeServerStreamAcl.Create(
                    AuraIceIpc.PipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    CreatePipeSecurity());

                await pipe.WaitForConnectionAsync(stoppingToken);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(_state.Snapshot));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                _logger.LogDebug(exception, "Cliente do status desconectado.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Falha no servidor de status.");
            }
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.Read | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        return security;
    }
}
