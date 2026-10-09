$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-environment.ps1")

$root = $BuildRepoRoot
$publishRoot = Join-Path $root "publish"

if (Test-Path $publishRoot) {
    Remove-Item $publishRoot -Recurse -Force
}

Invoke-DotnetCommand -Arguments @(
    "publish",
    (Join-Path $root "src\AuraIce.Service\AuraIce.Service.csproj"),
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:RestoreConfigFile=$BuildNuGetConfig",
    "-o", (Join-Path $publishRoot "service")
) -FailureMessage "A publicação do serviço falhou."

Invoke-DotnetCommand -Arguments @(
    "publish",
    (Join-Path $root "src\AuraIce.App\AuraIce.App.csproj"),
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:RestoreConfigFile=$BuildNuGetConfig",
    "-o", (Join-Path $publishRoot "app")
) -FailureMessage "A publicação da interface falhou."

foreach ($requiredFile in @(
    (Join-Path $publishRoot "service\AuraIce.Service.exe"),
    (Join-Path $publishRoot "app\AuraIce.App.exe")
)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Arquivo publicado não encontrado: $requiredFile"
    }
}

Write-Host "Publicação concluída em $publishRoot"
