#!/usr/bin/env bash
# Builds LogLens.app for one Mac architecture and packs it into a .dmg.
# Usage:  installer/macos/build-app.sh <osx-arm64|osx-x64> [version]
# Runs on macOS (codesign and hdiutil come with every Mac). Output: installer/output/LogLens-mac-<AppleSilicon|Intel>.dmg
set -euo pipefail

RID="${1:?usage: build-app.sh <osx-arm64|osx-x64> [version]}"
VERSION="${2:-0.0.0}"
case "$RID" in
  osx-arm64) ARCH_NAME="AppleSilicon" ;;
  osx-x64)   ARCH_NAME="Intel" ;;
  *) echo "Unknown runtime '$RID' (use osx-arm64 or osx-x64)" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/installer/output/macos/$RID"
APP="$OUT/LogLens.app"
DMG="$ROOT/installer/output/LogLens-mac-$ARCH_NAME.dmg"
rm -rf "$OUT" "$DMG"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "== 1/4 Publishing LogLens for $RID (self-contained, .NET runtime included)"
dotnet publish "$ROOT/LogLens.csproj" -c Release -r "$RID" --self-contained true -o "$APP/Contents/MacOS"

echo "== 2/4 Adding Info.plist (version $VERSION) and the icon"
sed "s/__VERSION__/$VERSION/g" "$ROOT/installer/macos/Info.plist" > "$APP/Contents/Info.plist"
cp "$ROOT/Assets/Icon/LogLens.icns" "$APP/Contents/Resources/LogLens.icns"
chmod +x "$APP/Contents/MacOS/LogLens"

echo "== 3/4 Ad-hoc signing (free, no Apple account; Apple Silicon refuses unsigned apps)"
# Order matters: signing the main program checks that everything next to it is already signed,
# so sign all other files first, then the main program, then the bundle as a whole.
MAIN="$APP/Contents/MacOS/LogLens"
find "$APP/Contents/MacOS" -type f ! -path "$MAIN" -print0 | xargs -0 -n 50 codesign --force --sign -
codesign --force --sign - "$MAIN"
codesign --force --sign - "$APP"
codesign --verify --deep --strict "$APP"

echo "== 4/4 Creating the disk image"
STAGE="$OUT/dmg"
mkdir -p "$STAGE"
ditto "$APP" "$STAGE/LogLens.app"              # ditto keeps the signatures intact
codesign --verify --deep --strict "$STAGE/LogLens.app"
ln -s /Applications "$STAGE/Applications"   # drag LogLens onto this to install
# hdiutil is occasionally "busy" on build machines, so retry a few times
for attempt in 1 2 3; do
  if hdiutil create -volname "LogLens" -srcfolder "$STAGE" -ov -format UDZO "$DMG"; then break; fi
  [ "$attempt" = 3 ] && exit 1
  sleep 5
done

echo "Done: $DMG"
