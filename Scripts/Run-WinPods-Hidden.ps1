$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $repoRoot "WinPods.csproj"
$targetFramework = "net10.0-windows10.0.19041.0"
$exePath = Join-Path $repoRoot "bin\Debug\$targetFramework\WinPods.exe"
$logDir = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "WinPods"
$logPath = Join-Path $logDir "launcher.log"

function Show-WinPodsError([string]$message) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        $message,
        "WinPods",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
}

try {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Show-WinPodsError ".NET SDK not found. Install .NET SDK, then run WinPods again."
        exit 1
    }

    Push-Location $repoRoot
    try {
        & dotnet build $projectPath *> $logPath
        if ($LASTEXITCODE -ne 0) {
            Show-WinPodsError "WinPods build failed. See $logPath"
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path $exePath)) {
        Show-WinPodsError "WinPods.exe not found after build: $exePath"
        exit 1
    }

    Start-Process -FilePath $exePath -WorkingDirectory $repoRoot
}
catch {
    try {
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
        $_ | Out-String | Set-Content -Path $logPath
    }
    catch {
    }

    Show-WinPodsError "WinPods launcher failed. See $logPath"
    exit 1
}
