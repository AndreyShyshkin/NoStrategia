#!/usr/bin/env bash
set -e

# ==============================================================================
# NoStrategia Telemetry Agent - Automated Build Script
# Builds both Production (FEX/Proton/Windows compatible) and Dev packages
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_PROJ="$SCRIPT_DIR/src/TelemetryAgent.App/TelemetryAgent.App.csproj"
TEST_PROJ="$SCRIPT_DIR/tests/TelemetryAgent.Tests/TelemetryAgent.Tests.csproj"

echo "=== 1. Running Unit Tests ==="
dotnet test "$TEST_PROJ" -c Release

echo "=== 2. Building Production Release (FEX / Wine / Windows Compatible) ==="
PROD_OUT="$SCRIPT_DIR/publish/prod/TelemetryAgent"
rm -rf "$PROD_OUT"
dotnet publish "$APP_PROJ" \
  -r win-x64 \
  -c Release \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:ProdMode=true \
  -o "$PROD_OUT"

# Strip unnecessary heavy debug symbols for distribution zip
rm -f "$PROD_OUT"/*.pdb

echo "=== 3. Packaging Production Zip ==="
mkdir -p "$SCRIPT_DIR/release_packages"
(
  cd "$SCRIPT_DIR/publish/prod"
  rm -f "$SCRIPT_DIR/release_packages/TelemetryAgent-v1.0.0-win-x64.zip"
  zip -r "$SCRIPT_DIR/release_packages/TelemetryAgent-v1.0.0-win-x64.zip" TelemetryAgent > /dev/null
)
echo "Production package created: $SCRIPT_DIR/release_packages/TelemetryAgent-v1.0.0-win-x64.zip"

echo "=== 4. Building Dev Build (with Mock Simulator) ==="
DEV_OUT="$SCRIPT_DIR/publish/dev/TelemetryAgent-Dev"
rm -rf "$DEV_OUT"
dotnet publish "$APP_PROJ" \
  -r win-x64 \
  -c Release \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:ProdMode=false \
  -o "$DEV_OUT"
rm -f "$DEV_OUT"/*.pdb

echo "Dev package created at: $DEV_OUT"

# Check if deploy flag was passed
if [[ "$1" == "--deploy" ]]; then
  echo "=== 5. Deploying to Wine / Proton Compatibility Containers ==="
  STEAM_PREFIX="$HOME/Library/Application Support/Steam/steamapps/compatdata/805550/pfx/drive_c"
  GAMEMAC_PREFIX="$HOME/Library/Application Support/com.gamemac.www/wine-engine/containers/virtual_containers/1/drive_c"

  if [ -d "$STEAM_PREFIX" ]; then
    rm -rf "$STEAM_PREFIX/TelemetryAgent"
    cp -r "$PROD_OUT" "$STEAM_PREFIX/TelemetryAgent"
    cat << 'EOF' > "$STEAM_PREFIX/TelemetryAgent.bat"
@echo off
start "" "%~dp0TelemetryAgent\TelemetryAgent.exe" %*
EOF
    echo "  [OK] Deployed to Steam Prefix 805550 (C:\TelemetryAgent\TelemetryAgent.exe)"
  fi

  if [ -d "$GAMEMAC_PREFIX" ]; then
    rm -rf "$GAMEMAC_PREFIX/TelemetryAgent"
    cp -r "$PROD_OUT" "$GAMEMAC_PREFIX/TelemetryAgent"
    cat << 'EOF' > "$GAMEMAC_PREFIX/TelemetryAgent.bat"
@echo off
start "" "%~dp0TelemetryAgent\TelemetryAgent.exe" %*
EOF
    echo "  [OK] Deployed to GameMac Container 1 (C:\TelemetryAgent\TelemetryAgent.exe)"
  fi
fi

echo "=== Build Complete! ==="
