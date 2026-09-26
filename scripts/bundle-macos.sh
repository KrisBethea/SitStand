#!/usr/bin/env bash
# Build a menu-bar-only SitStand.app for macOS.
#
#   ./scripts/bundle-macos.sh            # Apple Silicon (osx-arm64)
#   ./scripts/bundle-macos.sh osx-x64    # Intel
#
# Produces artifacts/SitStand.app, ad-hoc signed so it launches on the machine that built it.
# For other machines you need a Developer ID certificate and notarization — see README.
set -euo pipefail

RID="${1:-osx-arm64}"
CONFIG="${CONFIG:-Release}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISH="$ROOT/artifacts/publish-$RID"
APP="$ROOT/artifacts/SitStand.app"

echo "→ publishing $RID ($CONFIG)"
dotnet publish "$ROOT/src/SitStand.App" \
  -c "$CONFIG" -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=none \
  -o "$PUBLISH"

echo "→ assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
cp "$ROOT/src/SitStand.App/macos/Info.plist" "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/SitStand"

if [ -f "$ROOT/src/SitStand.App/macos/AppIcon.icns" ]; then
  cp "$ROOT/src/SitStand.App/macos/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
  /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$APP/Contents/Info.plist" 2>/dev/null || true
fi

echo "→ ad-hoc signing"
codesign --force --deep --sign - "$APP"

echo "✓ $APP"
echo "  open \"$APP\"        # run it"
echo "  If Gatekeeper complains after copying elsewhere: xattr -dr com.apple.quarantine \"$APP\""
