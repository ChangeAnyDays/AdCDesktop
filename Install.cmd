@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Launch %*
if errorlevel 1 pause
