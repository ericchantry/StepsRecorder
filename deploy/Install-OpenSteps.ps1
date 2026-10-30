<#
.SYNOPSIS
    Installs or upgrades OpenSteps for the current user. Does not need administrator rights.

.DESCRIPTION
    Installs OpenSteps into the user's profile (default %LOCALAPPDATA%\Programs\OpenSteps),
    adds a Start Menu shortcut and registers an entry in Settings > Apps so the user (or IT)
    can uninstall it. Everything is written under the user's own profile and HKCU, so a
    standard user account is enough.

    The package can come from:
      * -Source: a release .zip (local path or UNC share) or an already-extracted folder.
      * The folder this script is in, when the script is shipped inside the release package.
      * A release .zip sitting next to this script.
      * Otherwise, the latest (or -Version) release on GitHub.

    Safe to run repeatedly: re-running upgrades or repairs the installation. User recordings in
    %LOCALAPPDATA%\OpenSteps are never touched.

.PARAMETER Source
    Path to an OpenSteps-<version>-win-x64.zip or to a folder containing OpenSteps.App.exe.

.PARAMETER Version
    GitHub release tag to download when no local package is found, e.g. v0.2.1. Default: latest.

.PARAMETER Repository
    GitHub repository (owner/name) that publishes the releases.

.PARAMETER ExpectedSha256
    If given, the downloaded or supplied .zip must match this SHA-256 hash.

.PARAMETER InstallDir
    Where to install. Must be a folder the user can write to.

.PARAMETER DesktopShortcut
    Also create a desktop shortcut.

.PARAMETER NoStartMenuShortcut
    Do not create the Start Menu shortcut.

.PARAMETER Launch
    Start OpenSteps when installation finishes.

.PARAMETER Force
    Reinstall even when the same version is already installed. Without it, re-running with the same
    version only refreshes shortcuts and the Settings > Apps entry (cheap enough for a logon script).

.PARAMETER Quiet
    Only write to the log file, not the console (for Intune, SCCM and logon scripts).

.PARAMETER LogPath
    Install log location.

.EXAMPLE
    .\Install-OpenSteps.ps1 -Source \\fileserver\software\OpenSteps\OpenSteps-0.2.1-win-x64.zip -Quiet

.EXAMPLE
    .\Install-OpenSteps.ps1 -Version v0.2.1 -ExpectedSha256 <hash> -Quiet

.NOTES
    Exit codes: 0 = success, 1 = failure (see the log).
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Version = 'latest',
    [string]$Repository = 'ericchantry/StepsRecorder',
    [string]$ExpectedSha256,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\OpenSteps'),
    [switch]$DesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$Launch,
    [switch]$Force,
    [switch]$Quiet,
    [string]$LogPath = (Join-Path $env:LOCALAPPDATA 'OpenSteps\install.log')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$AppName = 'OpenSteps'
$ExeName = 'OpenSteps.App.exe'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenSteps'
$StartMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'OpenSteps.lnk'
$DesktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'OpenSteps.lnk'

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    try {
        $dir = Split-Path -Parent $LogPath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Add-Content -Path $LogPath -Value $line -Encoding UTF8
    } catch { }
    if (-not $Quiet) {
        if ($Level -eq 'ERROR') { Write-Host $Message -ForegroundColor Red } else { Write-Host $Message }
    }
}

function Assert-UserContext {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.IsSystem) {
        throw 'This installer is per-user and is running as SYSTEM. Deploy it in the user context (Intune: Install behavior = User; ConfigMgr: Install for user).'
    }
}

function Get-PackageFromGitHub {
    param([string]$WorkDir)
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $api = if ($Version -eq 'latest') {
        "https://api.github.com/repos/$Repository/releases/latest"
    } else {
        "https://api.github.com/repos/$Repository/releases/tags/$Version"
    }

    Write-Log "Looking up release '$Version' from $Repository"
    $release = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'OpenSteps-Installer'; 'Accept' = 'application/vnd.github+json' } -UseBasicParsing
    $asset = @($release.assets | Where-Object { $_.name -like 'OpenSteps-*-win-x64.zip' }) | Select-Object -First 1
    if (-not $asset) { throw "Release $($release.tag_name) has no OpenSteps-*-win-x64.zip asset." }

    $zip = Join-Path $WorkDir $asset.name
    Write-Log "Downloading $($asset.browser_download_url)"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'OpenSteps-Installer' }
    return $zip
}

function Resolve-PackageFolder {
    param([string]$WorkDir)

    $zip = $null
    if ($Source) {
        if (-not (Test-Path -LiteralPath $Source)) { throw "Source not found: $Source" }
        $item = Get-Item -LiteralPath $Source
        if ($item.PSIsContainer) {
            if (-not (Test-Path (Join-Path $item.FullName $ExeName))) { throw "$ExeName was not found in $($item.FullName)" }
            Write-Log "Using package folder $($item.FullName)"
            return $item.FullName
        }
        $zip = $item.FullName
    }
    elseif ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot $ExeName))) {
        Write-Log "Using the package this script was shipped in: $PSScriptRoot"
        return $PSScriptRoot
    }
    elseif ($PSScriptRoot) {
        $zip = Get-ChildItem -Path $PSScriptRoot -Filter 'OpenSteps-*-win-x64.zip' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $zip) { $zip = Get-PackageFromGitHub -WorkDir $WorkDir }
    else { Write-Log "Using package $zip" }

    if ($ExpectedSha256) {
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if ($actual -ne $ExpectedSha256.Trim().ToUpperInvariant()) {
            throw "SHA-256 mismatch for $zip. Expected $ExpectedSha256, got $actual."
        }
        Write-Log 'SHA-256 verified.'
    }

    $extract = Join-Path $WorkDir 'package'
    Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force
    $exe = Get-ChildItem -Path $extract -Filter $ExeName -Recurse -File | Select-Object -First 1
    if (-not $exe) { throw "$ExeName was not found inside $zip" }
    return $exe.DirectoryName
}

function Stop-OpenSteps {
    $target = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    $running = @(Get-Process -Name 'OpenSteps.App' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($running.Count -eq 0) { return }

    Write-Log 'OpenSteps is running; asking it to close so it can save its session.'
    foreach ($process in $running) { [void]$process.CloseMainWindow() }
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
}

function Install-Files {
    param([string]$PackageFolder)
    $parent = Split-Path -Parent $InstallDir
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

    $staging = "$InstallDir.new"
    $backup = "$InstallDir.old"
    foreach ($path in @($staging, $backup)) {
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }

    Write-Log "Copying files to $InstallDir"
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item -Path (Join-Path $PackageFolder '*') -Destination $staging -Recurse -Force
    if (-not (Test-Path (Join-Path $staging $ExeName))) { throw "Copy failed: $ExeName missing from $staging" }

    # Swap folders so a failed copy never leaves a half-installed app behind.
    if (Test-Path $InstallDir) { Rename-Item -LiteralPath $InstallDir -NewName (Split-Path -Leaf $backup) }
    try {
        Rename-Item -LiteralPath $staging -NewName (Split-Path -Leaf $InstallDir)
    } catch {
        if (Test-Path $backup) { Rename-Item -LiteralPath $backup -NewName (Split-Path -Leaf $InstallDir) }
        throw
    }
    if (Test-Path $backup) { Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue }
}

function New-Shortcut {
    param([string]$Path, [string]$Target)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = Split-Path -Parent $Target
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Description = 'Record step-by-step guides and repro steps with video and a keystroke timeline'
    $shortcut.Save()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

function Register-Uninstall {
    param([string]$Exe)
    $version = (Get-Item -LiteralPath $Exe).VersionInfo.ProductVersion
    if ($version) { $version = ($version -split '\+')[0] }
    $sizeKb = [int]((Get-ChildItem -LiteralPath $InstallDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB)
    $uninstallScript = Join-Path $InstallDir 'Uninstall-OpenSteps.ps1'
    $command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`" -InstallDir `"$InstallDir`""

    if (-not (Test-Path $UninstallKey)) { New-Item -Path $UninstallKey -Force | Out-Null }
    $values = @{
        DisplayName          = $AppName
        DisplayVersion       = $version
        Publisher            = 'OpenSteps contributors'
        InstallLocation      = $InstallDir
        DisplayIcon          = "$Exe,0"
        UninstallString      = $command
        QuietUninstallString = "$command -Quiet"
        InstallDate          = (Get-Date -Format 'yyyyMMdd')
        URLInfoAbout         = "https://github.com/$Repository"
    }
    foreach ($name in $values.Keys) {
        if ($null -ne $values[$name]) { New-ItemProperty -Path $UninstallKey -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null }
    }
    New-ItemProperty -Path $UninstallKey -Name 'EstimatedSize' -Value $sizeKb -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $UninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $UninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
    return $version
}

$workDir = Join-Path ([IO.Path]::GetTempPath()) ("OpenSteps-install-" + [Guid]::NewGuid().ToString('N'))
try {
    Write-Log "Installing $AppName for $env:USERNAME into $InstallDir"
    Assert-UserContext
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    $packageFolder = Resolve-PackageFolder -WorkDir $workDir
    $samePlace = [IO.Path]::GetFullPath($packageFolder).TrimEnd('\') -ieq [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
    $installedExe = Join-Path $InstallDir $ExeName
    $packageVersion = (Get-Item -LiteralPath (Join-Path $packageFolder $ExeName)).VersionInfo.FileVersion
    $installedVersion = if (Test-Path -LiteralPath $installedExe) { (Get-Item -LiteralPath $installedExe).VersionInfo.FileVersion } else { $null }
    if ($samePlace) {
        # Running the copy of this script inside the install folder: just repair shortcuts and registration.
        Write-Log 'Package folder is the install folder; refreshing shortcuts and registration only.'
    } elseif (-not $Force -and $installedVersion -and $installedVersion -eq $packageVersion) {
        Write-Log "Version $installedVersion is already installed; refreshing shortcuts and registration only. Use -Force to reinstall."
    } else {
        Stop-OpenSteps
        Install-Files -PackageFolder $packageFolder
    }

    $exe = Join-Path $InstallDir $ExeName
    if (-not $NoStartMenuShortcut) { New-Shortcut -Path $StartMenuShortcut -Target $exe }
    if ($DesktopShortcut) { New-Shortcut -Path $DesktopShortcutPath -Target $exe }
    $registeredVersion = Register-Uninstall -Exe $exe

    Write-Log "$AppName $registeredVersion installed successfully."
    if ($Launch) { Start-Process -FilePath $exe -WorkingDirectory $InstallDir }
    exit 0
}
catch {
    Write-Log "Installation failed: $($_.Exception.Message)" 'ERROR'
    exit 1
}
finally {
    if (Test-Path $workDir) { Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue }
}
