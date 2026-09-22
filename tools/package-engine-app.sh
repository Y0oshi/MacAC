#!/usr/bin/env bash
# Wraps a published self-contained engine into MacAC Engine.app so macOS
# Gatekeeper can evaluate it as an app (bare executables are rejected on
# recent macOS). Also bundles the Vulkan loader + MoltenVK.
#   usage: package-engine-app.sh <rid: osx-arm64|osx-x64> [out-dir]
set -euo pipefail
cd "$(dirname "$0")/.."
RID="${1:?rid}"; OUT="${2:-dist}"
VERSION="${VERSION:-$(grep -oE '<Version>[^<]+' Directory.Build.props | sed 's/<Version>//')}"

STAGE="$OUT/publish-$RID"
APP="$OUT/MacAC Engine-$RID.app"
rm -rf "$STAGE" "$APP"

echo "[engine] publishing $RID (self-contained)…"
dotnet publish src/MacAC.Client/MacAC.Client.csproj -c Release -r "$RID" --self-contained true \
  -o "$STAGE" -p:DebugType=none --nologo -v q
dotnet publish src/MacAC.Forge/MacAC.Forge.csproj -c Release -r "$RID" --self-contained true \
  -o "$STAGE" -p:DebugType=none --nologo -v q

echo "[engine] building bundle…"
MACOS="$APP/Contents/MacOS"; PAYLOAD="$APP/Contents/Resources/engine"
mkdir -p "$MACOS" "$PAYLOAD"
# codesign only allows Mach-O in Contents/MacOS, so the .NET payload (managed
# dlls, runtime, natives) lives under Resources/engine where it is sealed as
# data. A tiny launcher stub in MacOS/ execs the real host from there.
ditto --norsrc --noextattr --noqtn "$STAGE" "$PAYLOAD"
cat > "$MACOS/MacAC" <<'STUB'
#!/bin/sh
# MacAC engine launcher stub: runs the self-contained host from the payload.
DIR="$(cd "$(dirname "$0")/../Resources/engine" && pwd)"
exec "$DIR/MacAC.Client" "$@"
STUB
chmod +x "$MACOS/MacAC"
ditto --norsrc --noextattr --noqtn Launcher/Assets/MacAC.icns "$APP/Contents/Resources/MacAC.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>MacAC Engine</string>
  <key>CFBundleDisplayName</key><string>MacAC</string>
  <key>CFBundleIdentifier</key><string>org.macac.engine</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>MacAC</string>
  <key>CFBundleIconFile</key><string>MacAC</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.games</string>
  <key>LSUIElement</key><false/>
</dict></plist>
PLIST

# Vulkan runtime. The engine resolves Frameworks/ and Resources/vulkan/ relative
# to its executable directory, so they live inside Contents/MacOS.
./tools/bundle-vulkan.sh "$PAYLOAD" >/dev/null

cat > "$OUT/engine.entitlements" <<ENT
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
ENT

# Sign from a temp directory: folders synced by iCloud Drive / File Provider
# (e.g. ~/Desktop) tag new directories with FinderInfo/fileprovider attributes
# that codesign rejects as "detritus". A copy outside the synced tree is clean.
SIGNDIR="$(mktemp -d /tmp/macac-sign.XXXXXX)"
SIGNAPP="$SIGNDIR/$(basename "$APP")"
ditto --norsrc --noextattr --noqtn "$APP" "$SIGNAPP"
FINAL_APP="$APP"; APP="$SIGNAPP"

echo "[engine] signing (${CODESIGN_IDENTITY:--} )…"
IDENTITY="${CODESIGN_IDENTITY:--}"
# Sign every Mach-O inside first, then the bundle with entitlements.
# Only Mach-O files carry signatures; managed .dll files are PE and get sealed
# by the bundle signature as resources.
find "$PAYLOAD" -type f | while read -r f; do
  if file "$f" | grep -q "Mach-O"; then
    codesign --force --sign "$IDENTITY" --timestamp=none "$f" 2>/dev/null
  fi
done
codesign --force --sign "$IDENTITY" --timestamp=none --identifier org.macac.engine \
  --entitlements "$OUT/engine.entitlements" "$APP" 2>/dev/null
if codesign --verify --strict "$APP" 2>/dev/null; then echo "[engine] signature OK"; else echo "[engine] signature: $(codesign --verify --strict "$APP" 2>&1 | head -1)"; fi

# Move the signed bundle back into dist (mv keeps the signature intact).
rm -rf "$FINAL_APP"; mv "$APP" "$FINAL_APP"; rm -rf "$SIGNDIR"; APP="$FINAL_APP"
rm -rf "$STAGE"
echo "[engine] built: $APP ($(du -sh "$APP" | cut -f1))"
