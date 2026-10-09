# Status da implementação

## Já implementado

- leitura de temperatura de CPU via LibreHardwareMonitor;
- comunicação HID com VID `0xAA88` e PID `0x8666`;
- payload de temperatura de 24 bytes;
- serviço Windows com intervalo configurável e estado exposto por named pipe local;
- aplicativo WPF/tray para acompanhar conexão, temperatura, sensor e erros;
- configuração persistente em `%ProgramData%\AuraIce\config.json`;
- publicação self-contained para `win-x64`;
- projeto WiX 4 para MSI por máquina;
- inicialização automática do app pelo Startup comum do Windows;
- recuperação automática do serviço após falhas.

## Validação obrigatória no Windows com o dispositivo conectado

1. Fechar o software oficial da Rise Mode para liberar o HID.
2. Publicar e instalar o MSI como administrador.
3. Confirmar em `services.msc` que `AuraIceTemperatureService` está em execução.
4. Executar `AuraIce.Probe.exe` como administrador e confirmar a enumeração do dispositivo.
5. Conferir no visor se a temperatura permanece atualizada por pelo menos cinco minutos.
6. Reiniciar o Windows e confirmar que serviço, app e visor retornam automaticamente.

O teste físico identificou um relatório HID de saída de 11 bytes. No HidSharp, esse tamanho inclui o byte de Report ID; o display usa relatório sem ID, então o buffer enviado no Windows começa com `0x00`, seguido pelos bytes da parte inteira e do primeiro decimal da temperatura. O payload conhecido tem 24 bytes de dados e é truncado para caber no relatório HID, com zeros no preenchimento.
