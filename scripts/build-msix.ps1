param(
    [string]$Configuration = 'Release',
    [string]$IdentityName = 'CodigoLimpio.LumaProfiles',
    [string]$Publisher = 'CN=Código Limpio',
    [string]$PublisherDisplayName = 'Código Limpio',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\store')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\LumaProfiles\LumaProfiles.csproj'
$manifestTemplate = Join-Path $root 'packaging\msix\AppxManifest.xml'
$assetsScript = Join-Path $root 'scripts\generate-store-assets.ps1'
$stage = Join-Path $OutputDirectory 'stage'
$publish = Join-Path $OutputDirectory 'publish'
$package = Join-Path $OutputDirectory 'LumaProfiles.msix'
$upload = Join-Path $OutputDirectory 'LumaProfiles.msixupload'

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
foreach ($path in @($stage, $publish, $package, $upload)) {
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "La versión debe ser semántica de tres componentes: $version" }
$packageVersion = "$version.0"

& powershell -NoProfile -ExecutionPolicy Bypass -File $assetsScript -OutputDirectory (Join-Path $OutputDirectory 'assets')
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=portable --no-restore -o $publish
[System.IO.Directory]::CreateDirectory($stage) | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
$stageAssets = Join-Path $stage 'Assets'
[System.IO.Directory]::CreateDirectory($stageAssets) | Out-Null
Copy-Item -Path (Join-Path $OutputDirectory 'assets\*') -Destination $stageAssets -Force

$manifest = (Get-Content -LiteralPath $manifestTemplate -Raw).
    Replace('__IDENTITY_NAME__', $IdentityName).
    Replace('__PUBLISHER__', $Publisher).
    Replace('__PUBLISHER_DISPLAY_NAME__', $PublisherDisplayName).
    Replace('__VERSION__', $packageVersion)
Set-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Value $manifest -Encoding utf8NoBOM

$makeAppx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter MakeAppx.exe -ErrorAction SilentlyContinue |
    Where-Object FullName -Match '\\x64\\MakeAppx\.exe$' | Select-Object -First 1
if (-not $makeAppx) { throw 'MakeAppx.exe no está instalado. Instala Windows 10/11 SDK.' }
& $makeAppx.FullName pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx terminó con código $LASTEXITCODE" }

$uploadWorkspace = Join-Path $OutputDirectory 'upload-workspace'
if (Test-Path $uploadWorkspace) { Remove-Item -LiteralPath $uploadWorkspace -Recurse -Force }
[System.IO.Directory]::CreateDirectory($uploadWorkspace) | Out-Null
Copy-Item -LiteralPath $package -Destination $uploadWorkspace
Compress-Archive -Path (Join-Path $uploadWorkspace '*') -DestinationPath $upload -CompressionLevel Optimal
Write-Host "MSIX: $package" -ForegroundColor Green
Write-Host "Carga Partner Center: $upload" -ForegroundColor Green
Write-Host "Nota: Microsoft Store firma el paquete durante la publicación." -ForegroundColor Yellow
