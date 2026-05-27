param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$InnoCompilerPath
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $repoRoot "WinPods.csproj"
$publishScript = Join-Path $scriptRoot "Publish-Release.ps1"
$installerScript = Join-Path $repoRoot "Installer\WinPods.iss"
$publishDir = Join-Path (Join-Path $repoRoot "publish") $Runtime
$distDir = Join-Path $repoRoot "dist"

function Get-ProjectVersion {
    param([string]$Path)

    [xml]$project = Get-Content -LiteralPath $Path
    $versionNode = $project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Version not found in $Path"
    }

    return [string]$versionNode
}

function Find-InnoCompiler {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path $ExplicitPath) {
            return (Resolve-Path $ExplicitPath).Path
        }

        throw "Inno Setup compiler not found: $ExplicitPath"
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles} "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup compiler ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php and run this script again."
}

if (-not (Test-Path $publishScript)) {
    throw "Publish script not found: $publishScript"
}

if (-not (Test-Path $installerScript)) {
    throw "Installer script not found: $installerScript"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -Path $projectPath
}

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $publishScript -Runtime $Runtime -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Publish script failed with exit code $LASTEXITCODE"
}

$iscc = Find-InnoCompiler -ExplicitPath $InnoCompilerPath
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$outputBaseName = "WinPodsSetup-$Version"

& $iscc $installerScript `
    "/DAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$distDir" `
    "/DOutputBaseName=$outputBaseName"

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE"
}

$installerPath = Join-Path $distDir "$outputBaseName.exe"
if (-not (Test-Path $installerPath)) {
    throw "Installer not found after build: $installerPath"
}

Write-Host "Built installer: $installerPath"
