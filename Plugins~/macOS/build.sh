#!/bin/sh
# Builds DesktopDockMac.bundle (universal: Apple silicon + Intel) from DesktopDockMac.m.
# Run on a Mac with the Xcode command line tools (xcode-select --install):
#
#   sh DesktopDock/Plugins~/macOS/build.sh            # writes DesktopDock/Plugins/macOS/DesktopDockMac.bundle
#   sh DesktopDock/Plugins~/macOS/build.sh <out-dir>  # writes <out-dir>/DesktopDockMac.bundle
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT_DIR="${1:-$HERE/../../Plugins/macOS}"
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
BUNDLE="$OUT_DIR/DesktopDockMac.bundle"

rm -rf "$BUNDLE"
mkdir -p "$BUNDLE/Contents/MacOS"
cp "$HERE/Info.plist" "$BUNDLE/Contents/Info.plist"

xcrun clang -bundle -fobjc-arc -O2 -Wall -Wextra \
  -arch arm64 -arch x86_64 -mmacosx-version-min=10.13 \
  -framework Cocoa -framework QuartzCore -framework ServiceManagement \
  -o "$BUNDLE/Contents/MacOS/DesktopDockMac" \
  "$HERE/DesktopDockMac.m"

# Ad-hoc signature so it loads on Apple silicon. Your own build/notarization step re-signs the whole app.
codesign --force --sign - "$BUNDLE"

echo "Built $BUNDLE"
lipo -info "$BUNDLE/Contents/MacOS/DesktopDockMac"
