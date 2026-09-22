#!/usr/bin/env bash
# Builds every release artifact into dist/release/:
#   MacAC-<version>.dmg              universal launcher
#   MacAC-Engine-osx-arm64.zip       self-contained engine (Apple Silicon)
#   MacAC-Engine-osx-x64.zip         self-contained engine (Intel)
#   engine-manifest.json             what the launcher reads to install the engine
#   SHA256SUMS.txt
# Set CODESIGN_IDENTITY to a Developer ID to sign for distribution; default is ad-hoc.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${VERSION:-$(grep -oE '<Version>[^<]+' Directory.Build.props | sed 's/<Version>//')}"
OUT="dist/release"; rm -rf "$OUT"; mkdir -p "$OUT"
echo "[release] MacAC $VERSION"

# Work outside the repo: synced folders (iCloud Drive / File Provider) tag
# directories with attributes codesign rejects.
WORK="$(mktemp -d /tmp/macac-release.XXXXXX)"; trap 'rm -rf "$WORK"' EXIT

# --- engines ---
for rid in osx-arm64 osx-x64; do
  VERSION="$VERSION" ./tools/package-engine-app.sh "$rid" dist
  ditto --norsrc --noextattr --noqtn "dist/MacAC Engine-$rid.app" "$WORK/MacAC Engine-$rid.app"
  ( cd "$WORK" && ditto -c -k --keepParent "MacAC Engine-$rid.app" "MacAC-Engine-$rid.zip" )
  mv "$WORK/MacAC-Engine-$rid.zip" "$OUT/"
  echo "[release] MacAC-Engine-$rid.zip"
done

# --- launcher + dmg ---
( cd Launcher && VERSION="$VERSION" ./scripts/build-app.sh build >/dev/null )
DMG="$OUT/MacAC-$VERSION.dmg"; STAGE="$WORK/dmg"; mkdir -p "$STAGE"
ditto --norsrc --noextattr --noqtn Launcher/build/MacAC.app "$STAGE/MacAC.app"
ln -s /Applications "$STAGE/Applications"
IDENTITY="${CODESIGN_IDENTITY:--}"
codesign --force --deep --sign "$IDENTITY" ${CODESIGN_IDENTITY:+--options runtime --timestamp} "$STAGE/MacAC.app" 2>/dev/null
hdiutil create -volname "MacAC" -srcfolder "$STAGE" -ov -format UDZO -quiet "$DMG"
if [ -n "${CODESIGN_IDENTITY:-}" ]; then codesign --sign "$CODESIGN_IDENTITY" --timestamp "$DMG"; fi
echo "[release] $(basename "$DMG")"

# --- manifest + checksums ---
sha() { shasum -a 256 "$1" | cut -d' ' -f1; }
size() { stat -f%z "$1"; }
cat > "$OUT/engine-manifest.json" <<JSON
{
  "version": "$VERSION",
  "builds": [
    { "rid": "osx-arm64", "asset": "MacAC-Engine-osx-arm64.zip",
      "assetSize": $(size "$OUT/MacAC-Engine-osx-arm64.zip"), "assetSha256": "$(sha "$OUT/MacAC-Engine-osx-arm64.zip")" },
    { "rid": "osx-x64", "asset": "MacAC-Engine-osx-x64.zip",
      "assetSize": $(size "$OUT/MacAC-Engine-osx-x64.zip"), "assetSha256": "$(sha "$OUT/MacAC-Engine-osx-x64.zip")" }
  ]
}
JSON
( cd "$OUT" && shasum -a 256 *.zip *.dmg > SHA256SUMS.txt )
echo "[release] done:"; ls -la "$OUT" | awk 'NR>1{printf "  %-34s %6.1f MB\n",$9,$5/1048576}'
