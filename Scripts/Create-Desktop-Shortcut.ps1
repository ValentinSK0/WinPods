$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$shortcutPath = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "WinPods.lnk"
$runnerPath = Join-Path $scriptRoot "Run-WinPods-Hidden.ps1"
$iconPath = Join-Path $repoRoot "Assets\WinPods.ico"

if (-not (Test-Path $runnerPath)) {
    throw "Runner not found: $runnerPath"
}

if (-not (Test-Path $iconPath)) {
    throw "Icon not found: $iconPath"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$runnerPath`""
$shortcut.WorkingDirectory = $repoRoot
$shortcut.IconLocation = $iconPath
$shortcut.Description = "Run WinPods from source"
$shortcut.Save()

Write-Host "Created $shortcutPath"
