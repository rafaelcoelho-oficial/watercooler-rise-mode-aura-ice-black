# Rise Mode Aura Ice Black — Windows driver

Driver não oficial para exibir a temperatura da CPU no visor do Rise Mode Aura Ice Black.

## Estado atual

O projeto implementa:

- protocolo de temperatura do display;
- descoberta e escrita no dispositivo USB HID `VID 0xAA88 / PID 0x8666`;
- leitura real de temperatura, uso, frequência, tensão e potência da CPU usando `LibreHardwareMonitorLib`;
- utilitário de diagnóstico para testar a comunicação no Windows.

O serviço Windows coleta os cinco indicadores em um único ciclo compartilhado, com intervalo padrão de 1000 ms. A interface WPF fica na bandeja do sistema e abre um widget compacto com gauge vetorial, quatro cartões de telemetria, sparklines limitadas a 60 amostras e estados `N/D` quando um sensor não está disponível. O widget não acessa hardware diretamente: lê apenas o snapshot publicado pelo named pipe do serviço. O menu Iniciar inclui atalhos para abrir e desinstalar o Aura Ice Controller. O instalador WiX instala também o driver oficial PawnIO necessário para a leitura de sensores internos quando aplicável e inicia o aplicativo imediatamente após uma instalação, reinstalação ou reparo concluído.

### Fluxo de telemetria

`AuraIce.Service` mantém uma única instância do provider LibreHardwareMonitor. A cada ciclo, o provider atualiza o hardware uma vez e seleciona, preservando nome, identificador, fonte e timestamp:

- temperatura: package/Tctl/Tdie/CPU conforme a prioridade disponível;
- uso: sensor de carga total da CPU;
- frequência: clock atual do núcleo/CPU;
- tensão: leitura física preferencialmente, com VID identificado como estimado quando for a única opção;
- potência: sensor do pacote da CPU.

Nenhum valor é simulado ou inferido a partir de outro indicador. A ausência de um sensor não interrompe os demais: o cartão correspondente exibe `N/D`, o status do serviço continua separado do status do display e a série histórica deixa de receber amostras até a leitura voltar.

## Instalador para usuário final

Em um Windows x64 com o .NET SDK 8 ou superior e acesso à Internet, gere o instalador único a partir da raiz do repositório:

```powershell
.\scripts\build-installer.ps1
```

O comando executa estas etapas:

1. publica o serviço e a interface WPF como executáveis `win-x64` autocontidos em `publish/`;
2. compila o MSI WiX em `installer/bin/Release/pt-BR/AuraIce.Installer.msi`;
3. baixa o instalador oficial PawnIO 2.2.0, valida hash SHA-256 e assinatura Authenticode;
4. gera o instalador final em `installer/bin/Release/AuraIce-Setup.exe`.

O `AuraIce-Setup.exe` instala o driver PawnIO oficial quando necessário e, em seguida, o Aura Ice Controller. Ao concluir uma instalação, reinstalação ou reparo, o MSI inicia automaticamente o `AuraIce.App.exe` com privilégios normais do usuário; a desinstalação não inicia o aplicativo. A pessoa que instala só precisa executar esse arquivo e aprovar a solicitação do Windows. O instalador final baixa o PawnIO durante a instalação; o build valida previamente a versão esperada por hash e assinatura.

Para recompilar somente o instalador usando os binários já existentes em `publish/`:

```powershell
.\scripts\build-installer.ps1 -SkipPublish
```

Em CI, sandbox ou outra máquina sem o serviço Windows Installer (`msiserver`), a validação ICE do MSI não pode ser executada. Nesses ambientes, use explicitamente:

```powershell
.\scripts\build-installer.ps1 -SkipValidation
```

Essa opção deve ficar restrita a ambientes isolados; em uma máquina Windows comum, use o comando padrão para manter a validação completa do MSI. Ela pode ser combinada com `-SkipPublish` quando os binários de `publish/` já existirem.

Para publicar apenas os executáveis:

```powershell
.\scripts\publish.ps1
```

Para validar apenas a solução compilada, sem publicar executáveis:

```powershell
dotnet build .\AuraIce.sln --configuration Release --no-restore
```

Para remover uma instalação de desenvolvimento e limpar processos, serviço, inicialização, registros, configuração e caches temporários:

```powershell
.\scripts\uninstall-dev.ps1
```

Os scripts usam o `NuGet.Config` versionado do projeto, um cache local em `.nuget/` e verificam o código de saída de cada comando `dotnet`. Assim, o build não depende do `NuGet.Config` global do usuário. O restore dos pacotes WiX e .NET ainda exige acesso ao `https://api.nuget.org`.

Se o PowerShell bloquear a execução de scripts, libere somente para a sessão atual:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

O PawnIO é um driver de kernel de terceiros, necessário para que o LibreHardwareMonitor leia sensores internos. A instalação exige administrador e pode pedir reinicialização. O instalador não desativa Smart App Control, Secure Boot nem outras proteções do Windows. Consulte [as instruções detalhadas](docs/INSTALACAO-MSI.md).

## Instalação de desenvolvimento

Em um PowerShell elevado:

```powershell
.\scripts\publish.ps1
.\scripts\install-dev.ps1
```

Isso registra o serviço `AuraIceTemperatureService` como automático e inicia a interface no login do usuário. Esse fluxo é destinado a desenvolvimento; usuários finais devem usar `AuraIce-Setup.exe`.

## Requisitos de desenvolvimento

- Windows 10/11 x64;
- .NET SDK 8 ou superior;
- o watercooler conectado à placa-mãe por USB;
- execução elevada caso o sensor escolhido pelo LibreHardwareMonitor exija privilégios administrativos.

## Executar o diagnóstico

```powershell
.\src\AuraIce.Probe\bin\x64\Release\net8.0-windows\AuraIce.Probe.exe
```

O diagnóstico procura o HID, mostra os sensores de temperatura encontrados e envia o valor selecionado a cada segundo.

Feche o software oficial da Rise Mode antes do teste, pois dois processos não devem disputar o mesmo display.

## Licenças

O código deste projeto é separado das dependências de terceiros. A integração com `LibreHardwareMonitorLib` deve manter os avisos e a licença MPL-2.0 da biblioteca.
