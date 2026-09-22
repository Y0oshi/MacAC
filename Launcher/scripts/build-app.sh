#!/usr/bin/env bash
# Builds MacAC.app as a universal (Apple Silicon + Intel) macOS app bundle.
# Requires only the Xcode Command Line Tools (no Xcode).
set -euo pipefail
cd "$(dirname "$0")/.."
OUT="${1:-build}"
NAME="MacAC"
VERSION="${VERSION:-1.0.0}"

echo "[macac] building arm64…"
swift build -c release --triple arm64-apple-macosx   >/dev/null
echo "[macac] building x86_64…"
swift build -c release --triple x86_64-apple-macosx  >/dev/null
mkdir -p .build/universal
BIN=".build/universal/MacACLauncher"
lipo -create \
  ".build/arm64-apple-macosx/release/MacACLauncher" \
  ".build/x86_64-apple-macosx/release/MacACLauncher" \
  -output "$BIN"

APP="$OUT/$NAME.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/$NAME"
cp Assets/MacAC.icns "$APP/Contents/Resources/MacAC.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundleDisplayName</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>org.macac.launcher</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>$NAME</string>
  <key>CFBundleIconFile</key><string>MacAC</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.games</string>
  <key>NSHumanReadableCopyright</key><string>MacAC</string>
</dict></plist>
PLIST

# Ad-hoc signature so the bundle launches locally; replace with Developer ID for distribution.
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
# Rebuilding in place leaves Finder/Dock showing a stale (or missing) icon;
# bump the bundle and re-register it so they pick up the current one.
touch "$APP"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP" >/dev/null 2>&1 || true
echo "[macac] built $APP"
lipo -info "$APP/Contents/MacOS/$NAME"
