param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\LumaProfiles\LumaProfiles.csproj'
$output = Join-Path $repositoryRoot 'dist\LumaProfiles'

dotnet restore $project --configfile (Join-Path $repositoryRoot 'NuGet.Config')
dotnet publish $project -c $Configuration --no-restore -o $output

Write-Host "Versión lista en: $output" -ForegroundColor Green
