#!/usr/bin/env bash
# build.sh – Build the SteamServerTool solution.
#
# NOTE: WPF applications require a Windows runtime. Use this script inside
#       WSL2, or in a CI environment that has the .NET 9 SDK with
#       Windows-targeting support enabled (EnableWindowsTargeting=true).
#
# Usage:
#   ./Scripts/build.sh [--debug] [--test] [--publish] [--publish-dir <dir>] [--yes]
#
# Options:
#   --debug           Use Debug configuration (default: Release)
#   --test            Run unit tests after building
#   --publish         Publish a self-contained Windows x64 binary
#   --publish-dir     Output directory for the published build
#                     (default: <repo-root>/publish)
#   --yes             Skip all confirmation prompts (useful in CI pipelines)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CORE_PROJECT="$REPO_ROOT/SteamServerTool.Core/SteamServerTool.Core.csproj"
TESTS_PROJECT="$REPO_ROOT/SteamServerTool.Tests/SteamServerTool.Tests.csproj"
APP_PROJECT="$REPO_ROOT/SteamServerTool/SteamServerTool.csproj"

CONFIGURATION="Release"
RUN_TESTS=false
PUBLISH=false
PUBLISH_DIR="$REPO_ROOT/publish"
AUTO_YES=false

# Log file for this build run (written next to the script)
LOG_DIR="$REPO_ROOT/logs"
BUILD_LOG="$LOG_DIR/build_$(date +%Y%m%d_%H%M%S).log"

# ── Parse arguments ───────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --debug)        CONFIGURATION="Debug"   ;;
        --test)         RUN_TESTS=true           ;;
        --publish)      PUBLISH=true             ;;
        --publish-dir)  PUBLISH_DIR="$2"; shift  ;;
        --yes)          AUTO_YES=true            ;;
        *)  echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

# ── Helpers ───────────────────────────────────────────────────────────────────
step() {
    local msg="  $1"
    echo ""
    echo "────────────────────────────────────────────"
    echo "$msg"
    echo "────────────────────────────────────────────"
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] STEP: $1" >> "$BUILD_LOG"
}

log_and_run() {
    # Run a command, tee-ing both stdout and stderr into the log file.
    # Exits with the command's original exit code on failure.
    "$@" 2>&1 | tee -a "$BUILD_LOG"
    # tee masks the exit code – capture it separately.
    local exit_code="${PIPESTATUS[0]}"
    if [[ "$exit_code" -ne 0 ]]; then
        echo "" | tee -a "$BUILD_LOG"
        echo "ERROR: Command failed (exit $exit_code): $*" | tee -a "$BUILD_LOG"
        echo "Build log written to: $BUILD_LOG"
        exit "$exit_code"
    fi
}

confirm() {
    if [[ "$AUTO_YES" == "true" ]]; then return; fi
    echo ""
    read -r -p "$1 Press Enter to continue, or Ctrl+C to abort: "
}

# ── Prepare log directory ─────────────────────────────────────────────────────
mkdir -p "$LOG_DIR"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] Build started  (config=$CONFIGURATION, test=$RUN_TESTS, publish=$PUBLISH)" \
    > "$BUILD_LOG"

# ── Validate dotnet ───────────────────────────────────────────────────────────
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet CLI not found. Install the .NET 9 SDK from https://dot.net" >&2
    exit 1
fi
SDK_VERSION="$(dotnet --version)"
echo "Using .NET SDK $SDK_VERSION"
echo "[$(date '+%Y-%m-%d %H:%M:%S')] .NET SDK: $SDK_VERSION" >> "$BUILD_LOG"

# ── Restore ───────────────────────────────────────────────────────────────────
step "Restoring NuGet packages"
for proj in "$CORE_PROJECT" "$TESTS_PROJECT" "$APP_PROJECT"; do
    log_and_run dotnet restore "$proj"
done

# ── Build ─────────────────────────────────────────────────────────────────────
step "Building projects ($CONFIGURATION)"
for proj in "$CORE_PROJECT" "$TESTS_PROJECT" "$APP_PROJECT"; do
    log_and_run dotnet build "$proj" --configuration "$CONFIGURATION" --no-restore
done

# ── Test ──────────────────────────────────────────────────────────────────────
if [[ "$RUN_TESTS" == "true" ]]; then
    step "Running unit tests"
    log_and_run dotnet test "$TESTS_PROJECT" --configuration "$CONFIGURATION" --no-build \
        --logger "console;verbosity=normal"
fi

# ── Publish ───────────────────────────────────────────────────────────────────
if [[ "$PUBLISH" == "true" ]]; then
    confirm "About to publish a self-contained Windows x64 release to '$PUBLISH_DIR'."
    step "Publishing self-contained Windows x64 release to '$PUBLISH_DIR'"
    log_and_run dotnet publish "$APP_PROJECT" \
        --configuration "$CONFIGURATION" \
        --runtime win-x64 \
        --self-contained true \
        --output "$PUBLISH_DIR" \
        -p:PublishSingleFile=true \
        -p:PublishReadyToRun=true
    echo ""
    echo "Published to: $PUBLISH_DIR"
fi

echo ""
echo "✔ Build complete."
echo "[$(date '+%Y-%m-%d %H:%M:%S')] Build complete." >> "$BUILD_LOG"
echo "Build log: $BUILD_LOG"

