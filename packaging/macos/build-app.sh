#!/usr/bin/env bash
# Assemble a macOS .app bundle from a dotnet publish output directory.
#
# Usage: build-app.sh <publish-dir> <output-app> <info-plist> <version> [icon-icns]
#
# Layout produced:
#   <output-app>/
#     Contents/
#       Info.plist        (CFBundleShortVersionString / CFBundleVersion patched to <version>)
#       MacOS/            <- every file from <publish-dir>
#       Resources/        <- AppIcon.icns (if provided)
#       PkgInfo
#
# Signing and notarization are intentionally out of scope — do those in the
# caller so failures surface in the CI step that owns the secrets.

set -euo pipefail

if [ "$#" -lt 4 ]; then
    echo "usage: $0 <publish-dir> <output-app> <info-plist> <version> [icon-icns]" >&2
    exit 2
fi

PUBLISH_DIR=$1
APP=$2
PLIST=$3
VERSION=$4
ICON=${5:-}

if [ ! -d "$PUBLISH_DIR" ]; then
    echo "error: publish dir not found: $PUBLISH_DIR" >&2
    exit 1
fi
if [ ! -f "$PLIST" ]; then
    echo "error: Info.plist not found: $PLIST" >&2
    exit 1
fi

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -a "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"

# Clean debug leftovers — CI usually does this upstream but be defensive.
find "$APP/Contents/MacOS" -name '*.pdb' -delete
find "$APP/Contents/MacOS" -name '*.xml' -delete
find "$APP/Contents/MacOS" -name '*.dSYM' -type d -exec rm -rf {} + 2>/dev/null || true

# The bundle executable must live in Contents/MacOS and have the exact name
# listed as CFBundleExecutable. `dotnet publish` already produces `qcedl-gui`.
if [ ! -f "$APP/Contents/MacOS/qcedl-gui" ]; then
    echo "error: qcedl-gui binary missing from publish dir" >&2
    exit 1
fi
chmod +x "$APP/Contents/MacOS/qcedl-gui"

cp "$PLIST" "$APP/Contents/Info.plist"

# Patch version strings — Info.plist in the repo ships a 0.0.0 placeholder so
# the file itself has no stale version to keep in sync.
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION"             "$APP/Contents/Info.plist"

if [ -n "$ICON" ] && [ -f "$ICON" ]; then
    cp "$ICON" "$APP/Contents/Resources/AppIcon.icns"
    # Inject CFBundleIconFile if not already present.
    /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$APP/Contents/Info.plist" 2>/dev/null \
        || /usr/libexec/PlistBuddy -c "Set :CFBundleIconFile AppIcon" "$APP/Contents/Info.plist"
fi

printf 'APPL????' > "$APP/Contents/PkgInfo"

echo "Assembled $APP"
