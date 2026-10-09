[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-environment.ps1")
$repoRoot = $BuildRepoRoot
$iconScript = Join-Path $PSScriptRoot "build-icon.ps1"
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$installerProject = Join-Path $repoRoot "installer\AuraIce.Installer.wixproj"
$setupProject = Join-Path $repoRoot "installer\AuraIce.Setup.wixproj"
$msiPath = Join-Path $repoRoot "installer\bin\Release\pt-BR\AuraIce.Installer.msi"
$setupPath = Join-Path $repoRoot "installer\bin\Release\AuraIce-Setup.exe"
$pawnIoVersion = "2.2.0"
$pawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/$pawnIoVersion/PawnIO_setup.exe"
$pawnIoSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032"
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AuraIce-Installer-" + [guid]::NewGuid().ToString("N"))
$validationProperties = @()

if ($SkipValidation) {
    $validationProperties = @("-p:SuppressValidation=true")
    Write-Warning "A validação ICE do MSI foi desativada. Use esta opção somente em ambientes sem o serviço Windows Installer, como CI ou sandbox."
}

try {
    & $iconScript

    if (-not $SkipPublish) {
        & $publishScript
        if ($LASTEXITCODE -ne 0) {
            throw "A publicação dos binários falhou."
        }
    }

    if (-not (Test-Path $installerProject)) {
        throw "Projeto WiX não encontrado: $installerProject"
    }

    foreach ($requiredPublishedFile in @(
        (Join-Path $repoRoot "publish\service\AuraIce.Service.exe"),
        (Join-Path $repoRoot "publish\app\AuraIce.App.exe")
    )) {
        if (-not (Test-Path -LiteralPath $requiredPublishedFile)) {
            throw "Binário publicado não encontrado: $requiredPublishedFile. Execute sem -SkipPublish."
        }
    }

    $installerBuildArguments = @(
        "build",
        $installerProject,
        "--configuration", "Release",
        "--nologo",
        "-p:RestoreConfigFile=$BuildNuGetConfig"
    ) + $validationProperties
    Invoke-DotnetCommand -Arguments $installerBuildArguments -FailureMessage "A compilação do MSI falhou."

    if (-not (Test-Path $msiPath)) {
        throw "MSI não encontrado no caminho esperado: $msiPath"
    }

    if (-not (Test-Path $setupProject)) {
        throw "Projeto do instalador simples não encontrado: $setupProject"
    }

    New-Item -ItemType Directory -Path $tempDirectory | Out-Null
    $pawnIoPath = Join-Path $tempDirectory "PawnIO_setup.exe"
    Write-Host "Validando o instalador oficial PawnIO $pawnIoVersion para gerar o pacote..."
    Invoke-WebRequest -Uri $pawnIoUrl -OutFile $pawnIoPath -UseBasicParsing

    $actualSha256 = (Get-FileHash -LiteralPath $pawnIoPath -Algorithm SHA256).Hash
    if ($actualSha256 -ne $pawnIoSha256) {
        throw "O hash do instalador PawnIO não corresponde ao manifesto fixado. Nenhum pacote foi gerado."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $pawnIoPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $signature.SignerCertificate.Subject -notmatch "(^|,\s*)CN=namazso\.eu(,|$)") {
        throw "A assinatura Authenticode do instalador PawnIO não foi validada como namazso.eu. Nenhum pacote foi gerado."
    }

    $pawnIoSha512 = (Get-FileHash -LiteralPath $pawnIoPath -Algorithm SHA512).Hash
    $pawnIoSize = (Get-Item -LiteralPath $pawnIoPath).Length

    $setupBuildArguments = @(
        "build",
        $setupProject,
        "--configuration", "Release",
        "--nologo",
        "-p:RestoreConfigFile=$BuildNuGetConfig",
        "-p:MsiPath=$msiPath",
        "-p:PawnIOHash=$pawnIoSha512",
        "-p:PawnIOSize=$pawnIoSize"
    ) + $validationProperties
    Invoke-DotnetCommand -Arguments $setupBuildArguments -FailureMessage "A compilação do instalador AuraIce-Setup.exe falhou."

    if (-not (Test-Path $setupPath)) {
        throw "Instalador não encontrado no caminho esperado: $setupPath"
    }

    Write-Host "Instalador para usuário final: $setupPath"
    Write-Host "O pacote baixa o PawnIO oficial durante a instalação se ele estiver ausente ou desatualizado."
}
finally {
    if (Test-Path $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}
