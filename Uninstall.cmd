@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-AdCat.ps1" -InstallDir "%~dp0." %*
if errorlevel 1 pause
