#!/usr/bin/env bash
# Bundles the Vulkan loader + MoltenVK into a published engine directory so the
# engine runs on Macs without Homebrew. Layout matches GraphicalVkFetcher:
#   <engine>/Frameworks/libvulkan.1.dylib
#   <engine>/Frameworks/libMoltenVK.dylib
#   <engine>/Resources/vulkan/icd.d/MoltenVK_icd.json
set -euo pipefail
ENGINE="${1:?usage: bundle-vulkan.sh <published-engine-dir>}"
BREW="$(brew --prefix 2>/dev/null || echo /opt/homebrew)"

FW="$ENGINE/Frameworks"; ICD="$ENGINE/Resources/vulkan/icd.d"
rm -rf "$FW" "$ENGINE/Resources/vulkan"; mkdir -p "$FW" "$ICD"

# Prefer the vendored universal (arm64 + x86_64) libraries; fall back to Homebrew.
VENDOR="$(cd "$(dirname "$0")/.." && pwd)/third_party/vulkan"
if [ -f "$VENDOR/libvulkan.1.dylib" ] && [ -f "$VENDOR/libMoltenVK.dylib" ]; then
  cp "$VENDOR/libvulkan.1.dylib" "$FW/libvulkan.1.dylib"
  cp "$VENDOR/libMoltenVK.dylib" "$FW/libMoltenVK.dylib"
  echo "[vulkan] using vendored universal libraries"
else
  cp -L "$BREW/lib/libvulkan.1.dylib"  "$FW/libvulkan.1.dylib"
  cp -L "$BREW/lib/libMoltenVK.dylib"  "$FW/libMoltenVK.dylib"
  echo "[vulkan] using Homebrew libraries (not universal)"
fi
chmod 644 "$FW"/*.dylib

# Make them relocatable: no absolute Homebrew paths inside.
install_name_tool 2>/dev/null -id "@rpath/libvulkan.1.dylib" "$FW/libvulkan.1.dylib"
install_name_tool 2>/dev/null -id "@rpath/libMoltenVK.dylib" "$FW/libMoltenVK.dylib"
for lib in "$FW"/*.dylib; do
  { otool -L "$lib" | awk 'NR>1{print $1}' | grep "^$BREW/" || true; } | while read -r dep; do
    [ -n "$dep" ] && install_name_tool 2>/dev/null -change "$dep" "@loader_path/$(basename "$dep")" "$lib"
  done
done

# ICD manifest pointing at the bundled driver (relative to the manifest).
cat > "$ICD/MoltenVK_icd.json" <<JSON
{
    "file_format_version" : "1.0.0",
    "ICD": {
        "library_path": "../../../Frameworks/libMoltenVK.dylib",
        "api_version" : "1.4.0",
        "is_portability_driver" : true
    }
}
JSON

# Re-sign after modification (ad-hoc; CI replaces with Developer ID).
codesign --force --sign - "$FW/libvulkan.1.dylib" "$FW/libMoltenVK.dylib" >/dev/null 2>&1 || true

echo "[vulkan] bundled into $ENGINE"
left=$(otool -L "$FW/libMoltenVK.dylib" "$FW/libvulkan.1.dylib" | grep -c "$BREW" || true)
echo "[vulkan] remaining Homebrew refs: $left"
