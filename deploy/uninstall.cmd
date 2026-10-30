@echo off
rem Removes the per-user OpenSteps installation. Add -RemoveUserData to also delete saved recordings.
rem The commands are in one block so cmd has read them all before this file is deleted.
(
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-OpenSteps.ps1" %*
  exit /b
)
