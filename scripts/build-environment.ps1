$ErrorActionPreference = "Stop"

$BuildRepoRoot = Split-Path -Parent $PSScriptRoot
$BuildNuGetConfig = Join-Path $BuildRepoRoot "NuGet.Config"
$BuildCacheRoot = Join-Path $BuildRepoRoot ".nuget"
$BuildNuGetPackages = Join-Path $BuildCacheRoot "packages"
$BuildNuGetHttpCache = Join-Path $BuildCacheRoot "http-cache"
$BuildAppData = Join-Path ([System.IO.Path]::GetTempPath()) "AuraIce-NuGet"
$BuildDotnetCliHome = Join-Path ([System.IO.Path]::GetTempPath()) "AuraIce-DotNetCli"

if (-not (Test-Path -LiteralPath $BuildNuGetConfig)) {
    throw "NuGet.Config não encontrado: $BuildNuGetConfig"
}

New-Item -ItemType Directory -Force -Path @(
    $BuildCacheRoot,
    $BuildNuGetPackages,
    $BuildNuGetHttpCache,
    $BuildAppData,
    $BuildDotnetCliHome
) | Out-Null

# O projeto usa somente o NuGet.Config versionado. Isso evita depender de um
# arquivo global de usuário que pode estar bloqueado ou conter feeds diferentes.
$env:APPDATA = $BuildAppData
$env:DOTNET_CLI_HOME = $BuildDotnetCliHome
$env:NUGET_PACKAGES = $BuildNuGetPackages
$env:NUGET_HTTP_CACHE_PATH = $BuildNuGetHttpCache
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

function Invoke-DotnetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Código de saída: $LASTEXITCODE."
    }
}
