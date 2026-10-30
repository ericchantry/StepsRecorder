@echo off
rem Removes the per-user OpenSteps installation. Add -RemoveUserData to also delete saved recordings.
rem "(goto) 2>nul" leaves the batch file before cmd tries to read it again, because the
rem uninstaller deletes the folder this file lives in. "exit /b" keeps PowerShell's exit code.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-OpenSteps.ps1" %* & (goto) 2>nul & exit /b
