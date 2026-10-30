@echo off
rem Installs OpenSteps for the current user (no administrator rights needed).
rem Any arguments are passed through, e.g.  install.cmd -Quiet -DesktopShortcut
(
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-OpenSteps.ps1" %*
  exit /b
)
