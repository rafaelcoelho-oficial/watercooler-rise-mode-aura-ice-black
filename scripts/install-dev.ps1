param(
    [string]$InstallRoot = "$env:ProgramFiles\AuraIce"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root "publish"
$serviceSource = Join-Path $publishRoot "service"
$appSource = Join-Path $publishRoot "app"
$serviceInstall = Join-Path $InstallRoot "service"
$appInstall = Join-Path $InstallRoot "app"
$serviceName = "AuraIceTemperatureService"

if (-not (Test-Path (Join-Path $serviceSource "AuraIce.Service.exe"))) {
    throw "A publicação não foi encontrada. Execute scripts\publish.ps1 primeiro."
}

New-Item -ItemType Directory -Force -Path $serviceInstall, $appInstall | Out-Null
Copy-Item (Join-Path $serviceSource "*") $serviceInstall -Recurse -Force
Copy-Item (Join-Path $appSource "*") $appInstall -Recurse -Force

$dataRoot = Join-Path $env:ProgramData "AuraIce"
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

# A interface e o serviço compartilham apenas configuração não sensível.
# O instalador definitivo deverá substituir esta ACL por uma regra MSI explícita.
& icacls.exe $dataRoot /grant "Users:(OI)(CI)M" /T | Out-Host

$serviceExe = Join-Path $serviceInstall "AuraIce.Service.exe"
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $existingService) {
    New-Service -Name $serviceName `
        -BinaryPathName "`"$serviceExe`"" `
        -DisplayName "Aura Ice Temperature Service" `
        -Description "Envia a temperatura da CPU para o visor Rise Mode Aura Ice." `
        -StartupType Automatic | Out-Null
}
else {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
    }
    & sc.exe config $serviceName binPath= "`"$serviceExe`"" start= auto | Out-Host
}

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name "AuraIceController" -Value "`"$(Join-Path $appInstall 'AuraIce.App.exe')`""

Start-Service -Name $serviceName
Write-Host "Aura Ice instalado em $InstallRoot"
