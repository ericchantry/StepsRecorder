<#
.SYNOPSIS
    Removes the per-user OpenSteps installation. Does not need administrator rights.

.DESCRIPTION
    Closes OpenSteps, removes its shortcuts, its Settings > Apps entry and the install folder.
    Saved recordings in %LOCALAPPDATA%\OpenSteps are kept unless -RemoveUserData is given.

.PARAMETER InstallDir
    The folder OpenSteps was installed to.

.PARAMETER RemoveUserData
    Also delete saved recordings and settings (%LOCALAPPDATA%\OpenSteps). This cannot be undone.

.PARAMETER Quiet
    Only write to the log file, not the console.

.NOTES
    Exit codes: 0 = success, 1 = failure (see the log).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\OpenSteps'),
    [switch]$RemoveUserData,
    [switch]$Quiet,
    [string]$LogPath = (Join-Path $env:TEMP 'OpenSteps-uninstall.log')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$ExeName = 'OpenSteps.App.exe'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenSteps'
$Shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'OpenSteps.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'OpenSteps.lnk')
)
$UserData = Join-Path $env:LOCALAPPDATA 'OpenSteps'

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    try { Add-Content -Path $LogPath -Value $line -Encoding UTF8 } catch { }
    if (-not $Quiet) {
        if ($Level -eq 'ERROR') { Write-Host $Message -ForegroundColor Red } else { Write-Host $Message }
    }
}

try {
    # The script may be running from inside the folder it is about to delete.
    Set-Location -LiteralPath ([IO.Path]::GetTempPath())
    $InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
    Write-Log "Uninstalling OpenSteps from $InstallDir"

    $prefix = $InstallDir + '\'
    $running = @(Get-Process -Name 'OpenSteps.App' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    foreach ($process in $running) { [void]$process.CloseMainWindow() }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and @($running | Where-Object { $_.Refresh(); -not $_.HasExited }).Count -gt 0) {
        Start-Sleep -Milliseconds 500
    }
    foreach ($process in $running) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }

    foreach ($shortcut in $Shortcuts) {
        if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    }

    if (Test-Path $UninstallKey) { Remove-Item -Path $UninstallKey -Recurse -Force }

    if (Test-Path -LiteralPath $InstallDir) {
        # Only delete a folder that really is an OpenSteps install.
        if (-not (Test-Path -LiteralPath (Join-Path $InstallDir $ExeName))) {
            throw "$InstallDir does not look like an OpenSteps installation ($ExeName not found); not deleting it."
        }
        Start-Sleep -Milliseconds 500
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
    }

    if ($RemoveUserData -and (Test-Path -LiteralPath $UserData)) {
        Write-Log "Removing saved recordings and settings in $UserData"
        Remove-Item -LiteralPath $UserData -Recurse -Force
    }

    Write-Log 'OpenSteps was uninstalled.'
    exit 0
}
catch {
    Write-Log "Uninstall failed: $($_.Exception.Message)" 'ERROR'
    exit 1
}
