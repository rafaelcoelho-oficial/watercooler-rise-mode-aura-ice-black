$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repoRoot "star.png"
$iconPath = Join-Path $repoRoot "assets\AuraIce.ico"
$faviconPath = Join-Path $repoRoot "favicon.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Imagem de origem não encontrada: $sourcePath"
}

Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)
$frames = @()

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()

        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size = $size; Data = $stream.ToArray() }
        }
        finally {
            $stream.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

$iconStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $imageOffset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Data.Length)
        $writer.Write([UInt32]$imageOffset)
        $imageOffset += $frame.Data.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Data)
    }

    [byte[]]$iconData = $iconStream.ToArray()
    [System.IO.File]::WriteAllBytes($iconPath, $iconData)
    [System.IO.File]::WriteAllBytes($faviconPath, $iconData)
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}

Write-Host "Ícone multi-resolução gerado: $iconPath"
Write-Host "Favicon gerado: $faviconPath"
