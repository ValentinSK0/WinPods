@echo off
setlocal

cd /d "%~dp0.."

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK not found. Install .NET SDK, then run again.
    pause
    exit /b 1
)

start "WinPods" /D "%CD%" dotnet run --project "%CD%\WinPods.csproj"
