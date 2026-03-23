<#
.SYNOPSIS
    Auto-builds the SteamServerTool project.

.DESCRIPTION
    Restores NuGet packages, builds the solution, optionally runs tests,
    and optionally publishes a self-contained Windows x64 release.
    Build output and any errors are captured in a timestamped log file under
    the repository's "logs" folder.

.PARAMETER Configuration
    Build configuration: Release (default) or Debug.

.PARAMETER RunTests
    If specified, runs the unit tests after building.

.PARAMETER Publish
    If specified, publishes a self-contained Windows x64 executable.

.PARAMETER PublishDir
    Output directory for the published build.
    Defaults to a "publish" folder in the repository root.

.PARAMETER Yes
    Skip all interactive confirmation prompts (useful in CI pipelines).

.EXAMPLE
    .\Scripts\build.ps1

.EXAMPLE
    .\Scripts\build.ps1 -Configuration Debug -RunTests

.EXAMPLE
    .\Scripts\build.ps1 -Publish -PublishDir "C:\Release\SteamServerTool"

.EXAMPLE
    .\Scripts\build.ps1 -Publish -Yes
#>

param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$RunTests,

    [switch]$Publish,

    [string]$PublishDir = "",

    [switch]$Yes
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

# ── Logging setup ─────────────────────────────────────────────────────────────
$LogDir      = Join-Path $RepoRoot "logs"
$BuildLog    = Join-Path $LogDir ("build_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Write-Log([string]$msg) {
    $entry = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $msg"
    Add-Content -Path $BuildLog -Value $entry
}

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "──────────────────────────────────────────" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "──────────────────────────────────────────" -ForegroundColor Cyan
    Write-Log "STEP: $msg"
}

function Invoke-DotNet {
    param([string[]]$Args)
    # Run dotnet, tee output to both the console and the build log.
    $output = & dotnet @Args 2>&1
    $output | ForEach-Object { Write-Host $_; Add-Content -Path $BuildLog -Value $_ }
    if ($LASTEXITCODE -ne 0) {
        $errMsg = "ERROR: dotnet $($Args -join ' ') failed (exit $LASTEXITCODE). See $BuildLog"
        Write-Host $errMsg -ForegroundColor Red
        Write-Log $errMsg
        exit $LASTEXITCODE
    }
}

function Confirm-Step([string]$prompt) {
    if ($Yes) { return }
    Write-Host ""
    Write-Host $prompt -ForegroundColor Yellow
    Read-Host "Press Enter to continue, or Ctrl+C to abort"
}

# ── Log header ────────────────────────────────────────────────────────────────
Write-Log "Build started  (config=$Configuration, test=$RunTests, publish=$Publish)"

# ── Validate dotnet is available ──────────────────────────────────────────────
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found. Install the .NET 9 SDK from https://dot.net"
    exit 1
}

$sdkVersion = & dotnet --version
Write-Host "Using .NET SDK $sdkVersion"
Write-Log ".NET SDK: $sdkVersion"

# ── Restore ───────────────────────────────────────────────────────────────────
Write-Step "Restoring NuGet packages"
foreach ($proj in @($CoreProject, $TestsProject, $AppProject)) {
    Invoke-DotNet "restore", $proj
}

# ── Build ─────────────────────────────────────────────────────────────────────
Write-Step "Building projects ($Configuration)"
foreach ($proj in @($CoreProject, $TestsProject, $AppProject)) {
    Invoke-DotNet "build", $proj, "--configuration", $Configuration, "--no-restore"
}

# ── Test ──────────────────────────────────────────────────────────────────────
if ($RunTests) {
    Write-Step "Running unit tests"
    Invoke-DotNet "test", $TestsProject, "--configuration", $Configuration, "--no-build",
        "--logger", "console;verbosity=normal"
}

# ── Publish ───────────────────────────────────────────────────────────────────
if ($Publish) {
    Confirm-Step "About to publish a self-contained Windows x64 release to '$PublishDir'."
    Write-Step "Publishing self-contained Windows x64 release to '$PublishDir'"
    Invoke-DotNet "publish", $AppProject,
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--self-contained", "true",
        "--output", $PublishDir,
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true"
    Write-Host ""
    Write-Host "Published to: $PublishDir" -ForegroundColor Green
    Write-Log "Published to: $PublishDir"
}

Write-Host ""
Write-Host "✔ Build complete." -ForegroundColor Green
Write-Log "Build complete."
Write-Host "Build log: $BuildLog"

