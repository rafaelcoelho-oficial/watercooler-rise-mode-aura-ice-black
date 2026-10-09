# Rise Mode Aura Ice Black — Windows driver

Driver não oficial para exibir a temperatura da CPU no visor do Rise Mode Aura Ice Black.

## Estado atual

Esta primeira fatia implementa:

- protocolo de temperatura do display;
- descoberta e escrita no dispositivo USB HID `VID 0xAA88 / PID 0x8666`;
- leitura de sensores de temperatura da CPU usando `LibreHardwareMonitorLib`;
- utilitário de diagnóstico para testar a comunicação no Windows.

O serviço Windows atualiza a temperatura continuamente. A interface WPF fica na bandeja do sistema e abre um widget compacto para acompanhar a leitura e ajustar o intervalo. O menu Iniciar inclui atalhos para abrir e desinstalar o Aura Ice Controller. O instalador WiX instala também o driver oficial PawnIO necessário para a leitura das temperaturas da CPU.

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

O `AuraIce-Setup.exe` instala o driver PawnIO oficial quando necessário e, em seguida, o Aura Ice Controller. A pessoa que instala só precisa executar esse arquivo e aprovar a solicitação do Windows. O instalador final baixa o PawnIO durante a instalação; o build valida previamente a versão esperada por hash e assinatura.

Para recompilar somente o instalador usando os binários já existentes em `publish/`:

```powershell
.\scripts\build-installer.ps1 -SkipPublish
```

Para publicar apenas os executáveis:

```powershell
.\scripts\publish.ps1
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
