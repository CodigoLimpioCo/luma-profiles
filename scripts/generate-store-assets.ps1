param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'store-assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'src\LumaProfiles\Assets\LumaProfilesIcon.png'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

function Save-IconCanvas([int]$size, [string]$name) {
    $canvas = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $source = [System.Drawing.Image]::FromFile($sourcePath)
    $margin = [int][Math]::Round($size * 0.04)
    $target = [System.Drawing.Rectangle]::new($margin, $margin, $size - ($margin * 2), $size - ($margin * 2))
    $graphics.DrawImage($source, $target)
    $source.Dispose()
    $canvas.Save((Join-Path $resolvedOutput $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $canvas.Dispose()
}

Save-IconCanvas 44 'Square44x44Logo.png'
Save-IconCanvas 50 'StoreLogo.png'
Save-IconCanvas 71 'Square71x71Logo.png'
Save-IconCanvas 150 'Square150x150Logo.png'
Save-IconCanvas 310 'Square310x310Logo.png'
Save-IconCanvas 300 'StoreListingLogo.png'

$wide = [System.Drawing.Bitmap]::new(620, 300, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$wideGraphics = [System.Drawing.Graphics]::FromImage($wide)
$wideGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$wideGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(0, 0), [System.Drawing.Point]::new(620, 300),
    [System.Drawing.Color]::FromArgb(255, 10, 17, 24), [System.Drawing.Color]::FromArgb(255, 31, 48, 62))
$wideGraphics.FillRectangle($background, 0, 0, 620, 300)
$icon = [System.Drawing.Image]::FromFile($sourcePath)
$wideGraphics.DrawImage($icon, [System.Drawing.Rectangle]::new(44, 45, 210, 210))
$font = [System.Drawing.Font]::new('Segoe UI', 38, [System.Drawing.FontStyle]::Bold)
$smallFont = [System.Drawing.Font]::new('Segoe UI', 17, [System.Drawing.FontStyle]::Regular)
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$cyan = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 85, 214, 245))
$wideGraphics.DrawString('Luma Profiles', $font, $white, 285, 91)
$wideGraphics.DrawString('Monitor Studio · Código Limpio', $smallFont, $cyan, 289, 151)
$wide.Save((Join-Path $resolvedOutput 'Wide310x150Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$cyan.Dispose(); $white.Dispose(); $smallFont.Dispose(); $font.Dispose(); $icon.Dispose(); $background.Dispose(); $wideGraphics.Dispose(); $wide.Dispose()

Write-Host "Recursos de Microsoft Store generados en: $resolvedOutput" -ForegroundColor Green
