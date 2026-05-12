$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "WinPods.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        ".NET SDK not found. Install .NET SDK, then run WinPods again.",
        "WinPods",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}

Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $projectPath) `
    -WorkingDirectory $repoRoot `
    -WindowStyle Hidden
