# Deploying OpenSteps (IT guide)

OpenSteps installs **per user, without administrator rights**. Everything goes into the user's
profile and `HKCU`, and the package is self-contained, so no .NET runtime has to be installed.

| | |
|---|---|
| Install folder | `%LOCALAPPDATA%\Programs\OpenSteps` |
| Start Menu shortcut | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\OpenSteps.lnk` |
| Settings > Apps entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenSteps` |
| Recordings and settings | `%LOCALAPPDATA%\OpenSteps` (kept on upgrade and uninstall) |
| Install log | `%LOCALAPPDATA%\OpenSteps\install.log` |
| Requirements | Windows 10 1809 or later, x64. Video needs Media Foundation (install the Media Feature Pack on Windows "N" editions). |

## The package

`OpenSteps-<version>-win-x64.zip` contains the app plus:

- `install.cmd` / `Install-OpenSteps.ps1` – install or upgrade for the current user
- `uninstall.cmd` / `Uninstall-OpenSteps.ps1` – remove it

Build it with `.\build\Publish-OpenSteps.ps1` (writes the zip and a `.sha256` file to `artifacts\`),
or download it from the GitHub release. Pushing a `v*` tag runs `.github/workflows/release.yml`,
which builds, tests and publishes the release.

## Installing

Interactive (a user does it themselves): unzip, double-click `install.cmd`.

Silent, from the unzipped package:

```bat
install.cmd -Quiet
```

Silent, from a file share (the script and zip can live anywhere the user can read):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\fileserver\software\OpenSteps\Install-OpenSteps.ps1 `
    -Source \\fileserver\software\OpenSteps\OpenSteps-0.2.1-win-x64.zip -Quiet
```

Silent, straight from GitHub with a pinned version and hash:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenSteps.ps1 -Version v0.2.1 -ExpectedSha256 <hash> -Quiet
```

| Parameter | Purpose |
|---|---|
| `-Source` | Release `.zip` (local or UNC) or an extracted package folder. If omitted: the package the script ships in, then a zip next to the script, then GitHub. |
| `-Version` | GitHub release tag when downloading. Default `latest`. |
| `-Repository` | GitHub `owner/name`. Default `ericchantry/StepsRecorder`. |
| `-ExpectedSha256` | Refuse to install a zip with a different hash. |
| `-InstallDir` | Install somewhere else in the user's profile. |
| `-DesktopShortcut` | Also add a desktop shortcut. |
| `-NoStartMenuShortcut` | Skip the Start Menu shortcut. |
| `-Force` | Reinstall even if the same version is installed. |
| `-Launch` | Start OpenSteps when done. |
| `-Quiet` | Log only, no console output. |

Exit codes: `0` success, `1` failure (details in the log). The script is idempotent: running it
again with the same version only refreshes the shortcut and Apps entry, so it is cheap enough for
a logon script. Upgrades close a running OpenSteps (it saves its session on close), swap the
install folder atomically and keep user recordings.

### Microsoft Intune (Win32 app)

1. Wrap the **extracted** package folder with `IntuneWinAppUtil.exe` (setup file: `install.cmd`).
2. Install command: `install.cmd -Quiet`
3. Uninstall command: `uninstall.cmd -Quiet`
4. Install behavior: **User** (the script refuses to run as SYSTEM).
5. Detection rule: File – path `%LOCALAPPDATA%\Programs\OpenSteps`, file `OpenSteps.App.exe`, exists
   (or Registry – `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenSteps`, value `DisplayVersion`).
6. Return codes: `0` success, `1` failed.

### Configuration Manager

Create an Application with a Script Installer deployment type using the same install/uninstall
commands and detection rule, with **Installation behavior: Install for user**.

### Group Policy logon script

User Configuration > Policies > Windows Settings > Scripts > Logon:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\fileserver\software\OpenSteps\Install-OpenSteps.ps1 -Source \\fileserver\software\OpenSteps\OpenSteps-0.2.1-win-x64.zip -Quiet
```

Replace the zip on the share to roll out a new version.

## Uninstalling

`uninstall.cmd -Quiet`, or Settings > Apps > OpenSteps. Add `-RemoveUserData` to also delete
saved recordings and settings in `%LOCALAPPDATA%\OpenSteps`.

## Policies

Set these `DWORD` values under `HKLM\SOFTWARE\Policies\OpenSteps` (machine-wide) or
`HKCU\SOFTWARE\Policies\OpenSteps` (per user) with Group Policy Preferences, an Intune
remediation/platform script or your management tool. Machine values win. Standard users cannot
write to either key, so users cannot override them.

| Value | `1` means |
|---|---|
| `DisableTypedTextCapture` | Never record the characters users type. Key names, shortcuts and key counts are still recorded. The "Typed text" option is greyed out. |
| `DisableVideoRecording` | Never record session video. Screenshots and the input timeline still work. |

```reg
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\OpenSteps]
"DisableTypedTextCapture"=dword:00000001
"DisableVideoRecording"=dword:00000000
```

## What gets recorded

Only while the user has pressed Record (a tray icon is shown the whole time, and recording can be
paused):

- Screenshots for each step, with click highlights.
- A video of all monitors (if enabled), with the cursor and a ring on each click.
- A timeline of clicks, scrolls, drags, keys and shortcuts with timestamps and the window they went to.
- Typed characters (if enabled). Fields that UI Automation reports as password fields, or whose
  name looks like a secret (PIN, CVV, card number, security code, …), are always masked and never stored.
- System details for bug reports: Windows version, displays and scaling, language, keyboard layout,
  time zone. Never user names, computer names or network details.

Everything stays on the computer in `%LOCALAPPDATA%\OpenSteps` until the user exports it. There is no
telemetry and no network access other than the optional GitHub download in the installer.

Consider telling users that typed text is recorded while they record, and use the policy above
if your organisation does not want it.

## Things to plan for

- **Security software.** OpenSteps uses a global keyboard and mouse hook and records the screen,
  which is exactly what keyloggers do. Unsigned builds are likely to be flagged by EDR/antivirus.
  Code-sign `OpenSteps.App.exe` (and the scripts) with your organisation's certificate, or allowlist
  the install path / file hash.
- **AppLocker / WDAC.** Many organisations block executables under `%LOCALAPPDATA%`. Add a publisher
  rule (after signing) or a path rule for `%LOCALAPPDATA%\Programs\OpenSteps\*`.
- **PowerShell execution policy.** `-ExecutionPolicy Bypass` does not override an execution policy
  enforced by Group Policy. If you enforce `AllSigned`, sign `Install-OpenSteps.ps1` and
  `Uninstall-OpenSteps.ps1`.
- **Elevated apps.** Without admin rights, Windows does not let OpenSteps see clicks or keys in
  windows running as administrator, or capture the UAC/secure desktop. Those moments appear in the
  video as the last visible frame and are missing from the timeline.
- **Video size.** At the default 10 frames per second, expect roughly 10–16 MB per minute depending
  on screen size and activity. Very wide multi-monitor desktops are scaled down to fit 3840×2160.
