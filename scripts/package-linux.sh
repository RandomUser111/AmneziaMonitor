#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?version is required}"
RID="${2:?runtime identifier is required}"
PUBLISH_DIR="${3:?publish directory is required}"
OUTPUT_DIR="${4:?output directory is required}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "$RID" in
  linux-x64)
    DEB_ARCH="amd64"
    RPM_ARCH="x86_64"
    ;;
  linux-arm64)
    DEB_ARCH="arm64"
    RPM_ARCH="aarch64"
    ;;
  *)
    echo "Unsupported Linux RID: $RID" >&2
    exit 2
    ;;
esac

command -v fpm >/dev/null 2>&1 || { echo "fpm is required" >&2; exit 3; }

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/opt/amnezia-monitor" \
         "$STAGE/usr/bin" \
         "$STAGE/usr/share/applications" \
         "$STAGE/usr/share/icons/hicolor/256x256/apps" \
         "$STAGE/usr/share/doc/amnezia-monitor"

cp -a "$PUBLISH_DIR"/. "$STAGE/opt/amnezia-monitor/"
chmod +x "$STAGE/opt/amnezia-monitor/AmneziaMonitor"
ln -s /opt/amnezia-monitor/AmneziaMonitor "$STAGE/usr/bin/amnezia-monitor"
cp "$ROOT/packaging/linux/amnezia-monitor.desktop" "$STAGE/usr/share/applications/amnezia-monitor.desktop"
cp "$ROOT/src/AmneziaDashboard.App/Assets/amnezia-monitor-icon.png" "$STAGE/usr/share/icons/hicolor/256x256/apps/amnezia-monitor.png"
cp "$ROOT/LICENSE" "$STAGE/usr/share/doc/amnezia-monitor/LICENSE"
cp "$ROOT/THIRD_PARTY_NOTICES.md" "$STAGE/usr/share/doc/amnezia-monitor/THIRD_PARTY_NOTICES.md"
mkdir -p "$OUTPUT_DIR"

COMMON=(
  -s dir
  -n amnezia-monitor
  -v "$VERSION"
  --license GPL-3.0
  --url "https://github.com/RandomUser111/AmneziaMonitor"
  --description "Cross-platform desktop monitor and management tool for self-hosted Amnezia VPN servers"
  --vendor "Amnezia Monitor"
  --maintainer "Amnezia Monitor contributors"
  -C "$STAGE"
)

fpm "${COMMON[@]}" \
  -t deb \
  -a "$DEB_ARCH" \
  -d "dotnet-runtime-10.0" \
  -d "libfontconfig1" \
  -d "libx11-6" \
  -d "libice6" \
  -d "libsm6" \
  -d "libxrandr2" \
  -p "$OUTPUT_DIR/amnezia-monitor_${VERSION}_${DEB_ARCH}.deb" \
  .

fpm "${COMMON[@]}" \
  -t rpm \
  -a "$RPM_ARCH" \
  -d "dotnet-runtime-10.0" \
  -d "fontconfig" \
  -d "libX11" \
  -d "libICE" \
  -d "libSM" \
  -d "libXrandr" \
  -p "$OUTPUT_DIR/amnezia-monitor-${VERSION}-1.${RPM_ARCH}.rpm" \
  .
