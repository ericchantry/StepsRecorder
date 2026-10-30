@echo off
rem Removes the per-user OpenSteps installation. Add -RemoveUserData to also delete saved recordings.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-OpenSteps.ps1" %*
exit /b %ERRORLEVEL%
