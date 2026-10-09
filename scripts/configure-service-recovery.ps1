[CmdletBinding()]
param(
    [string]$ServiceName = "AuraIceTemperatureService"
)

$ErrorActionPreference = "Stop"

# Restart after transient hardware/library failures. The final action is none
# so a persistent failure does not create an endless restart loop.
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/none/0
if ($LASTEXITCODE -ne 0) {
    throw "Não foi possível configurar a recuperação do serviço '$ServiceName'. Código: $LASTEXITCODE"
}

Write-Host "Recuperação configurada para $ServiceName."
