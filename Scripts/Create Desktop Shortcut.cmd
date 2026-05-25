@echo off
setlocal

cd /d "%~dp0.."

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Create-Desktop-Shortcut.ps1"
if errorlevel 1 (
    pause
    exit /b 1
)

echo Desktop shortcut created: WinPods
pause
