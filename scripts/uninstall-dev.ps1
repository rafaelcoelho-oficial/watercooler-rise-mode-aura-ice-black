$ErrorActionPreference = "Stop"
$serviceName = "AuraIceTemperatureService"
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -ne $existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $serviceName -Force
    }
    & sc.exe delete $serviceName | Out-Host
}

Remove-ItemProperty `
    -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "AuraIceController" `
    -ErrorAction SilentlyContinue

Write-Host "Serviço e inicialização removidos. Os binários e a configuração foram preservados."
