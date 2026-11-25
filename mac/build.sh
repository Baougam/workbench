#!/bin/bash
# Builds Workbench.app for macOS (Apple Silicon + Intel) and zips it to download/Workbench-mac.zip.
# Runs on a Mac with Xcode command line tools (GitHub Actions uses this).
set -euo pipefail
cd "$(dirname "$0")"
VERSION=$(tr -d ' \r\n' < ../VERSION)
rm -rf build && mkdir -p build

for ARCH in arm64 x86_64; do
  swiftc -O -parse-as-library -module-name Workbench -target "$ARCH-apple-macos13.0" \
    Sources/*.swift -o "build/Workbench-$ARCH" \
    -framework AppKit -framework SwiftUI -framework ServiceManagement -framework CoreServices
done
lipo -create -output build/Workbench build/Workbench-arm64 build/Workbench-x86_64

APP=build/Workbench.app
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp build/Workbench "$APP/Contents/MacOS/Workbench"
sed "s/__VERSION__/$VERSION/g" Info.plist > "$APP/Contents/Info.plist"

# icon
swift Tools/makeicon.swift build/icon.png
mkdir -p build/AppIcon.iconset
for S in 16 32 128 256 512; do
  sips -z $S $S build/icon.png --out "build/AppIcon.iconset/icon_${S}x${S}.png" >/dev/null
  sips -z $((S*2)) $((S*2)) build/icon.png --out "build/AppIcon.iconset/icon_${S}x${S}@2x.png" >/dev/null
done
iconutil -c icns build/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"

# browser extension
mkdir -p "$APP/Contents/Resources/browser"
cp ../browser/manifest.json ../browser/background.js "$APP/Contents/Resources/browser/"
for S in 16 32 48 128; do sips -z $S $S build/icon.png --out "$APP/Contents/Resources/browser/icon$S.png" >/dev/null; done

# ad-hoc signature (needed to run on Apple Silicon; no Apple developer account involved)
codesign --force --deep --sign - "$APP"
codesign --verify --verbose "$APP"

mkdir -p ../download
rm -f ../download/Workbench-mac.zip
(cd build && ditto -c -k --keepParent Workbench.app ../../download/Workbench-mac.zip)
echo "Built Workbench $VERSION → download/Workbench-mac.zip"
