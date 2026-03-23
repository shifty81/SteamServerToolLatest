<#
.SYNOPSIS
    Auto-builds the SteamServerTool project.

.DESCRIPTION
    Restores NuGet packages, builds the solution, optionally runs tests,
    and optionally publishes a self-contained Windows x64 release.

.PARAMETER Configuration
    Build configuration: Release (default) or Debug.

.PARAMETER RunTests
    If specified, runs the unit tests after building.

.PARAMETER Publish
    If specified, publishes a self-contained Windows x64 executable.

.PARAMETER PublishDir
    Output directory for the published build.
    Defaults to a "publish" folder in the repository root.

.EXAMPLE
    .\Scripts\build.ps1

.EXAMPLE
    .\Scripts\build.ps1 -Configuration Debug -RunTests

.EXAMPLE
    .\Scripts\build.ps1 -Publish -PublishDir "C:\Release\SteamServerTool"
#>

param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$RunTests,

    [switch]$Publish,

    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot      = Split-Path -Parent $PSScriptRoot
$CoreProject   = Join-Path $RepoRoot "SteamServerTool.Core"   "SteamServerTool.Core.csproj"
$TestsProject  = Join-Path $RepoRoot "SteamServerTool.Tests"  "SteamServerTool.Tests.csproj"
$AppProject    = Join-Path $RepoRoot "SteamServerTool"         "SteamServerTool.csproj"

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $RepoRoot "publish"
}

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "──────────────────────────────────────────" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "──────────────────────────────────────────" -ForegroundColor Cyan
}

# ── Validate dotnet is available ──────────────────────────────────────────────
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found. Install the .NET 9 SDK from https://dot.net"
    exit 1
}

$sdkVersion = & dotnet --version
Write-Host "Using .NET SDK $sdkVersion"

# ── Restore ───────────────────────────────────────────────────────────────────
Write-Step "Restoring NuGet packages"
foreach ($proj in @($CoreProject, $TestsProject, $AppProject)) {
    & dotnet restore $proj
    if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed: $proj"; exit $LASTEXITCODE }
}

# ── Build ─────────────────────────────────────────────────────────────────────
Write-Step "Building projects ($Configuration)"
foreach ($proj in @($CoreProject, $TestsProject, $AppProject)) {
    & dotnet build $proj --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed: $proj"; exit $LASTEXITCODE }
}

# ── Test ──────────────────────────────────────────────────────────────────────
if ($RunTests) {
    Write-Step "Running unit tests"
    & dotnet test $TestsProject --configuration $Configuration --no-build `
        --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { Write-Error "Tests failed."; exit $LASTEXITCODE }
}

# ── Publish ───────────────────────────────────────────────────────────────────
if ($Publish) {
    Write-Step "Publishing self-contained Windows x64 release to '$PublishDir'"
    & dotnet publish $AppProject `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $PublishDir `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit $LASTEXITCODE }
    Write-Host ""
    Write-Host "Published to: $PublishDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "✔ Build complete." -ForegroundColor Green
