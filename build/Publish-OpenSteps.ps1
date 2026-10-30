<#
.SYNOPSIS
    Builds the self-contained OpenSteps release package: artifacts\OpenSteps-<version>-win-x64.zip

.DESCRIPTION
    Publishes OpenSteps.App as a self-contained win-x64 app (no .NET install needed on endpoints),
    adds the per-user install/uninstall scripts, zips the result and writes its SHA-256 next to it.
    Hand the zip to IT, put it on a file share, or attach it to a GitHub release.

.EXAMPLE
    .\build\Publish-OpenSteps.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\OpenSteps.App\OpenSteps.App.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish\OpenSteps-$Version-$Runtime"
$zipPath = Join-Path $artifacts "OpenSteps-$Version-$Runtime.zip"

if (Test-Path $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Write-Host "Publishing OpenSteps $Version ($Configuration, $Runtime)..."
& dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $publishDir `
    -p:Version=$Version -p:DebugType=none -p:GenerateDocumentationFile=false --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item -Path (Join-Path $root 'deploy\*') -Destination $publishDir -Include '*.ps1', '*.cmd' -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publishDir -Force

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding ASCII

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Package: $zipPath ($sizeMb MB)"
Write-Host "SHA-256: $hash"
