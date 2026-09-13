#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.4.0}"
MODE="${2:-compact}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/AmneziaDashboard.App/AmneziaDashboard.App.csproj"
DIST="$ROOT/dist"
PUBLISH_ROOT="$ROOT/artifacts/publish"

if [[ "$MODE" == "standalone" ]]; then
  SELF_CONTAINED=true
else
  MODE=compact
  SELF_CONTAINED=false
fi

rm -rf "$DIST" "$PUBLISH_ROOT"
mkdir -p "$DIST" "$PUBLISH_ROOT"

echo "Release mode: $MODE"
if [[ "$MODE" == "compact" ]]; then
  echo "Compact packages require .NET 10 Runtime on the target machine."
fi

dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release --no-restore -p:Version="$VERSION"

for rid in win-x64 win-arm64 linux-x64 linux-arm64; do
  echo "Restoring runtime assets for $rid..."
  dotnet restore "$PROJECT" -r "$rid"

  echo "Publishing $rid ($MODE)..."
  out="$PUBLISH_ROOT/$MODE/$rid"

  dotnet publish "$PROJECT" \
    -c Release \
    -r "$rid" \
    --self-contained "$SELF_CONTAINED" \
    --no-restore \
    -p:Version="$VERSION" \
    -p:UseAppHost=true \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$out"

  if [[ ! -d "$out" ]] || [[ -z "$(find "$out" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    echo "Publish directory is missing or empty for $rid: $out" >&2
    exit 1
  fi

  cp "$ROOT/LICENSE" "$out/LICENSE"
  cp "$ROOT/THIRD_PARTY_NOTICES.md" "$out/THIRD_PARTY_NOTICES.md"

  if [[ "$MODE" == "standalone" ]]; then
    cat > "$out/README-RUNTIME.txt" <<INFO
Amnezia Monitor $VERSION
Platform: $rid

Standalone build. The .NET runtime is included.
This package is larger because it contains the runtime and native libraries.
INFO
    suffix="-$rid-standalone"
  else
    cat > "$out/README-RUNTIME.txt" <<INFO
Amnezia Monitor $VERSION
Platform: $rid

Compact build. Requires .NET 10 Runtime:
https://dotnet.microsoft.com/download/dotnet/10.0
INFO
    suffix="-$rid"
  fi

  if [[ "$rid" == win-* ]]; then
    (cd "$out" && zip -qr "$DIST/AmneziaMonitor-v$VERSION$suffix.zip" .)
  else
    (cd "$out" && tar -czf "$DIST/AmneziaMonitor-v$VERSION$suffix.tar.gz" .)
  fi
done

echo "Release archives created in $DIST"
ls -lh "$DIST"
