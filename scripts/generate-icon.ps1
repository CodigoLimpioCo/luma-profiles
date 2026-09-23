param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\LumaProfiles\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-LumaBitmap([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(1.0, $size * 0.055)
    $tileSize = $size - ($inset * 2)
    $tile = New-RoundedRectanglePath $inset $inset $tileSize $tileSize ($size * 0.22)
    $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new([float]$inset, [float]$inset),
        [System.Drawing.PointF]::new([float]($size - $inset), [float]($size - $inset)),
        [System.Drawing.Color]::FromArgb(255, 85, 214, 245),
        [System.Drawing.Color]::FromArgb(255, 112, 87, 248)
    )
    $graphics.FillPath($gradient, $tile)

    $shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 10, 20, 29))
    $graphics.FillEllipse($shadow, [float]($size * 0.36), [float]($size * 0.34), [float]($size * 0.50), [float]($size * 0.50))

    $highlight = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(245, 224, 250, 255))
    $graphics.FillEllipse($highlight, [float]($size * 0.15), [float]($size * 0.15), [float]($size * 0.22), [float]($size * 0.22))

    $highlight.Dispose()
    $shadow.Dispose()
    $gradient.Dispose()
    $tile.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngPayloads = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = New-LumaBitmap $size
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngPayloads.Add($stream.ToArray())
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $resolvedOutput 'LumaProfilesIcon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $stream.Dispose()
    $bitmap.Dispose()
}

$iconPath = Join-Path $resolvedOutput 'LumaProfiles.ico'
$file = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)

for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $payload = $pngPayloads[$index]
    $encodedSize = if ($size -eq 256) { 0 } else { $size }
    $writer.Write([byte]$encodedSize)
    $writer.Write([byte]$encodedSize)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$payload.Length)
    $writer.Write([uint32]$offset)
    $offset += $payload.Length
}

foreach ($payload in $pngPayloads) {
    $writer.Write($payload)
}

$writer.Dispose()
$file.Dispose()
Write-Host "Icono generado: $iconPath"
