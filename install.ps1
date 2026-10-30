$ErrorActionPreference = "Stop"
$appName = "OpenSteps"
$appExe = "OpenSteps.App.exe"
$localAppData = [Environment]::GetFolderPath("LocalApplicationData")
$targetDir = Join-Path $localAppData $appName
$startMenu = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $startMenu "$appName.lnk"

# Output directory for a typical .NET build
$sourceDir = "src\OpenSteps.App\bin\Release\net8.0-windows"

if (-Not (Test-Path $sourceDir)) {
    # If the default release directory isn't found, use the current directory (assuming extracted ZIP)
    $sourceDir = $PSScriptRoot
}

Write-Host "Installing OpenSteps to $targetDir..."
if (-Not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

Copy-Item -Path "$sourceDir\*" -Destination $targetDir -Recurse -Force

Write-Host "Creating shortcut in Start Menu..."
$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $targetDir $appExe
$shortcut.WorkingDirectory = $targetDir
$shortcut.Save()

Write-Host "Installation complete! OpenSteps is now available in your Start Menu."
