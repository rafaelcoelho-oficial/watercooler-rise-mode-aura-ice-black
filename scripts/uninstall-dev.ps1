[CmdletBinding()]
param(
    [string]$InstallRoot = "$env:ProgramFiles\AuraIce"
)

$ErrorActionPreference = "Stop"
$serviceName = "AuraIceTemperatureService"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$programDataRoot = Join-Path $env:ProgramData "AuraIce"
$tempBundleRoot = Join-Path $env:TEMP ".net"

function Get-SafeFullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Caminho vazio não pode ser removido."
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ([System.IO.Path]::GetPathRoot($fullPath).TrimEnd('\') -eq $fullPath.TrimEnd('\')) {
        throw "O caminho de remoção não pode ser a raiz de uma unidade: $fullPath"
    }

    return $fullPath
}

function Stop-AuraIceProcesses {
    Get-Process -Name "AuraIce.App", "AuraIce.Service" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

Stop-AuraIceProcesses

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if ($existingService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }

    & sc.exe delete $serviceName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível remover o serviço '$serviceName'. Código: $LASTEXITCODE"
    }

    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        if ($null -eq (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 200
    }
}

Remove-ItemProperty -LiteralPath $runKey -Name "AuraIceController" -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "HKCU:\Software\AuraIce" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath "HKLM:\Software\AuraIce" -Recurse -Force -ErrorAction SilentlyContinue

$pathsToRemove = @(
    (Get-SafeFullPath $InstallRoot),
    (Get-SafeFullPath $programDataRoot),
    (Get-SafeFullPath (Join-Path $tempBundleRoot "AuraIce.App")),
    (Get-SafeFullPath (Join-Path $tempBundleRoot "AuraIce.Service"))
)

foreach ($path in $pathsToRemove | Select-Object -Unique) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        Write-Host "Removido: $path"
    }
}

Write-Host "Aura Ice removido: processos, serviço, inicialização, registros, binários, configuração e cache temporário foram limpos."
