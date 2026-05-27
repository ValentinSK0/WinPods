param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $repoRoot "WinPods.csproj"
$publishRoot = Join-Path $repoRoot "publish"
$publishDir = Join-Path $publishRoot $Runtime

if (-not (Test-Path $projectPath)) {
    throw "Project not found: $projectPath"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install .NET SDK 10, then run this script again."
}

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Push-Location $repoRoot
try {
    & dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $publishDir `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$exePath = Join-Path $publishDir "WinPods.exe"
if (-not (Test-Path $exePath)) {
    throw "Published executable not found: $exePath"
}

Write-Host "Published WinPods: $exePath"
