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

.PARAMETER EvenIfRecording
    Uninstall even when OpenSteps says it is recording or has unsaved work open. Without it the uninstall is
    postponed (exit code 1618) so nobody loses a recording.

.PARAMETER Quiet
    Only write to the log file, not the console.

.NOTES
    Exit codes: 0 = success, 1 = failure (see the log), 1618 = postponed because OpenSteps is recording or
    has unsaved work open.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\OpenSteps'),
    [switch]$RemoveUserData,
    [switch]$EvenIfRecording,
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

function Send-OpenStepsCommand {
    # Talks to the running app over its per-user named pipe (OpenSteps 0.5 and later).
    # Returns the app's one-line reply, or $null when nothing answers (not running, or an older version).
    param([string]$Command)
    try {
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $session = (Get-Process -Id $PID).SessionId
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', "OpenSteps-$session-$sid", [System.IO.Pipes.PipeDirection]::InOut)
        try {
            $pipe.Connect(2000)
            $writer = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
            $writer.AutoFlush = $true
            $reader = New-Object System.IO.StreamReader($pipe)
            $writer.WriteLine($Command)
            return $reader.ReadLine()
        } finally {
            $pipe.Dispose()
        }
    } catch {
        return $null
    }
}

function Stop-OpenSteps {
    # Returns $true when OpenSteps is closed (or was not running), $false when it is busy and was left alone.
    $target = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    $running = @(Get-Process -Name 'OpenSteps.App' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($running.Count -eq 0) { return $true }

    # Ask the app itself to save and close. It refuses while recording or while a dialog has unsaved work.
    $reply = Send-OpenStepsCommand 'quit'
    if ($reply -eq 'recording' -or $reply -eq 'busy') {
        if (-not $EvenIfRecording) {
            $what = if ($reply -eq 'recording') { 'a recording is in progress' } else { 'it has unsaved work open' }
            Write-Log "OpenSteps is running and $what; leaving it alone. Run again later, or use -EvenIfRecording to close it anyway." 'WARN'
            return $false
        }
        Write-Log "OpenSteps reports '$reply' but -EvenIfRecording was given; closing it." 'WARN'
    } elseif ($reply -eq 'ok') {
        Write-Log 'OpenSteps saved its work and is closing.'
    } else {
        # An older version without the control pipe: fall back to asking its window to close.
        Write-Log 'OpenSteps is running; asking it to close so it can save its session.'
        foreach ($process in $running) { [void]$process.CloseMainWindow() }
    }

    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and @($running | Where-Object { -not $_.HasExited }).Count -gt 0) {
        Start-Sleep -Milliseconds 500
        foreach ($process in $running) { $process.Refresh() }
    }
    foreach ($process in $running) {
        if (-not $process.HasExited) {
            Write-Log "Stopping OpenSteps process $($process.Id)." 'WARN'
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 500
    return $true
}

try {
    # The script may be running from inside the folder it is about to delete.
    Set-Location -LiteralPath ([IO.Path]::GetTempPath())
    $InstallDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
    Write-Log "Uninstalling OpenSteps from $InstallDir"

    if (-not (Stop-OpenSteps)) {
        Write-Log 'Uninstall postponed so no work is lost. Exit code 1618 asks Intune / Configuration Manager to retry later.'
        exit 1618
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
