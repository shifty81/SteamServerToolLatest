#!/usr/bin/env bash
# build.sh – Build the SteamServerTool solution.
#
# NOTE: WPF applications require a Windows runtime. Use this script inside
#       WSL2, or in a CI environment that has the .NET 9 SDK with
#       Windows-targeting support enabled (EnableWindowsTargeting=true).
#
# Usage:
#   ./Scripts/build.sh [--debug] [--test] [--publish] [--publish-dir <dir>]
#
# Options:
#   --debug           Use Debug configuration (default: Release)
#   --test            Run unit tests after building
#   --publish         Publish a self-contained Windows x64 binary
#   --publish-dir     Output directory for the published build
#                     (default: <repo-root>/publish)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CORE_PROJECT="$REPO_ROOT/SteamServerTool.Core/SteamServerTool.Core.csproj"
TESTS_PROJECT="$REPO_ROOT/SteamServerTool.Tests/SteamServerTool.Tests.csproj"
APP_PROJECT="$REPO_ROOT/SteamServerTool/SteamServerTool.csproj"

CONFIGURATION="Release"
RUN_TESTS=false
PUBLISH=false
PUBLISH_DIR="$REPO_ROOT/publish"

# ── Parse arguments ───────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --debug)        CONFIGURATION="Debug"   ;;
        --test)         RUN_TESTS=true           ;;
        --publish)      PUBLISH=true             ;;
        --publish-dir)  PUBLISH_DIR="$2"; shift  ;;
        *)  echo "Unknown option: $1" >&2; exit 1 ;;
    esac
    shift
done

step() {
    echo ""
    echo "────────────────────────────────────────────"
    echo "  $1"
    echo "────────────────────────────────────────────"
}

# ── Validate dotnet ───────────────────────────────────────────────────────────
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet CLI not found. Install the .NET 9 SDK from https://dot.net" >&2
    exit 1
fi
echo "Using .NET SDK $(dotnet --version)"

# ── Restore ───────────────────────────────────────────────────────────────────
step "Restoring NuGet packages"
for proj in "$CORE_PROJECT" "$TESTS_PROJECT" "$APP_PROJECT"; do
    dotnet restore "$proj"
done

# ── Build ─────────────────────────────────────────────────────────────────────
step "Building projects ($CONFIGURATION)"
for proj in "$CORE_PROJECT" "$TESTS_PROJECT" "$APP_PROJECT"; do
    dotnet build "$proj" --configuration "$CONFIGURATION" --no-restore
done

# ── Test ──────────────────────────────────────────────────────────────────────
if [[ "$RUN_TESTS" == "true" ]]; then
    step "Running unit tests"
    dotnet test "$TESTS_PROJECT" --configuration "$CONFIGURATION" --no-build \
        --logger "console;verbosity=normal"
fi

# ── Publish ───────────────────────────────────────────────────────────────────
if [[ "$PUBLISH" == "true" ]]; then
    step "Publishing self-contained Windows x64 release to '$PUBLISH_DIR'"
    dotnet publish "$APP_PROJECT" \
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
