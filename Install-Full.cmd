@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Mode Full -IncludeNexus -IncludeTaskbar %*
if errorlevel 1 pause
