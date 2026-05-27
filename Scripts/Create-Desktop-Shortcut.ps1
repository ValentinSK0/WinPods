$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$shortcutPath = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "WinPods.lnk"
$runnerPath = Join-Path $scriptRoot "Run-WinPods-Hidden.ps1"
$iconPath = Join-Path $repoRoot "Assets\WinPods.ico"
$iconCacheDir = Join-Path $env:LOCALAPPDATA "WinPods\Icons"

if (-not (Test-Path $runnerPath)) {
    throw "Runner not found: $runnerPath"
}

if (-not (Test-Path $iconPath)) {
    throw "Icon not found: $iconPath"
}

$iconHash = (Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash.Substring(0, 12).ToLowerInvariant()
$shortcutIconPath = Join-Path $iconCacheDir "WinPods-$iconHash.ico"
New-Item -ItemType Directory -Force -Path $iconCacheDir | Out-Null
Copy-Item -LiteralPath $iconPath -Destination $shortcutIconPath -Force

$shell = New-Object -ComObject WScript.Shell

if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$runnerPath`""
$shortcut.WorkingDirectory = $repoRoot
$shortcut.IconLocation = "$shortcutIconPath,0"
$shortcut.Description = "Run WinPods from source"
$shortcut.Save()

& "$env:WINDIR\System32\ie4uinit.exe" -show 2>$null

Write-Host "Created $shortcutPath"
Write-Host "Shortcut icon: $shortcutIconPath"
