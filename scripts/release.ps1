<#
.SYNOPSIS
  Builds the release assets (portable .exe and .zip) for the version in the csproj and, with -Publish,
  creates the GitHub release and verifies that every asset was uploaded.
.EXAMPLE
  .\scripts\release.ps1            # only builds into dist\release
  .\scripts\release.ps1 -Publish   # builds, tags the current commit, pushes and creates the release
#>
param(
    [switch]$Publish,
    [string]$Title = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\LumaProfiles\LumaProfiles.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Versión inválida en el csproj: $version" }
$tag = "v$version"
$out = Join-Path $root 'dist\release'
$notes = Join-Path $root "docs\release-$tag.md"
$zipName = "LumaProfiles-$tag-win-x64.zip"
$exeName = "LumaProfiles-$tag-win-x64.exe"

if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $out 'zip'), (Join-Path $out 'exe') | Out-Null

dotnet test (Join-Path $root 'LumaProfiles.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron; no se genera el release.' }

dotnet publish $project -c Release -o (Join-Path $out 'zip')
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación framework-dependent.' }
Compress-Archive -Path (Join-Path $out 'zip\*') -DestinationPath (Join-Path $out $zipName)

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o (Join-Path $out 'exe')
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación autocontenida.' }
Copy-Item (Join-Path $out 'exe\LumaProfiles.exe') (Join-Path $out $exeName)

# Smoke test: the portable executable must stay open for a few seconds.
$smoke = Join-Path $out 'smoke'
New-Item -ItemType Directory -Force $smoke | Out-Null
Copy-Item (Join-Path $out $exeName) $smoke
# Its own instance id, so the check works even while Luma Profiles is open on this computer.
$env:LUMA_PROFILES_INSTANCE_ID = 'release-smoke'
$process = Start-Process (Join-Path $smoke $exeName) -PassThru
Remove-Item Env:\LUMA_PROFILES_INSTANCE_ID
Start-Sleep -Seconds 8
$alive = -not $process.HasExited
if ($alive) { $process.Kill() }
if (-not $alive) { throw "El ejecutable se cerró al arrancar (código $($process.ExitCode))." }

Write-Host "Listo: $out\$exeName y $out\$zipName" -ForegroundColor Green
if (-not $Publish) { return }

if (-not (Test-Path $notes)) { throw "Falta $notes con las notas del release." }
if (git status --porcelain --untracked-files=no) { throw 'Hay cambios sin commit; haz commit antes de publicar.' }
git push origin HEAD
if (-not (git tag --list $tag)) { git tag $tag }
git push origin $tag
if (-not $Title) { $Title = "Luma Profiles $version" }

$assets = @((Join-Path $out $exeName), (Join-Path $out $zipName))
gh release create $tag --title $Title --notes-file $notes
# Assets go up one by one so a failure is visible, and the result is verified afterwards.
foreach ($asset in $assets) { gh release upload $tag $asset --clobber }
$uploaded = gh release view $tag --json assets --jq '.assets[].name'
foreach ($asset in $assets) {
    if ($uploaded -notcontains (Split-Path -Leaf $asset)) { throw "No se subió $(Split-Path -Leaf $asset)." }
}
Write-Host "Release publicado: $(gh release view $tag --json url --jq .url)" -ForegroundColor Green
