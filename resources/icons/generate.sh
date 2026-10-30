#!/usr/bin/env bash
# Regenerate the edl-ng icon set from resources/icons/edl-ng.svg.
#
# Produces, relative to the repo root:
#   resources/icons/edl-ng-<N>.png   N in {16,32,48,64,128,256,512,1024}
#   resources/icons/edl-ng.png       alias for 512
#   resources/icons/edl-ng.ico       multi-size Windows icon
#   QCEDL.GUI/Assets/edl-ng.ico      same .ico, Avalonia window icon
#   QCEDL.GUI/macOS/AppIcon.icns     macOS bundle icon
#
# Requires: rsvg-convert (librsvg) for PNG rasterization,
#           ImageMagick ('magick') for .ico assembly,
#           iconutil (ships with macOS) for .icns.

set -euo pipefail

SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
REPO=$( cd "$SCRIPT_DIR/../.." && pwd )

SVG="$SCRIPT_DIR/edl-ng.svg"
ICON_DIR="$REPO/resources/icons"
GUI_ASSETS="$REPO/QCEDL.GUI/Assets"
MAC_DIR="$REPO/QCEDL.GUI/macOS"
mkdir -p "$ICON_DIR" "$GUI_ASSETS" "$MAC_DIR"

if [ ! -f "$SVG" ]; then
    echo "error: source SVG not found: $SVG" >&2
    exit 1
fi
for tool in rsvg-convert magick; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "error: '$tool' not on PATH" >&2
        exit 1
    fi
done

echo "==> Rasterizing PNG sizes from $(basename "$SVG")"
for SZ in 16 32 48 64 128 256 512 1024; do
    rsvg-convert -w "$SZ" -h "$SZ" "$SVG" -o "$ICON_DIR/edl-ng-${SZ}.png"
done
cp "$ICON_DIR/edl-ng-512.png" "$ICON_DIR/edl-ng.png"

echo "==> Building multi-size .ico"
magick \
    "$ICON_DIR/edl-ng-16.png" \
    "$ICON_DIR/edl-ng-32.png" \
    "$ICON_DIR/edl-ng-48.png" \
    "$ICON_DIR/edl-ng-64.png" \
    "$ICON_DIR/edl-ng-128.png" \
    "$ICON_DIR/edl-ng-256.png" \
    -colors 256 "$ICON_DIR/edl-ng.ico"
cp "$ICON_DIR/edl-ng.ico" "$GUI_ASSETS/edl-ng.ico"

echo "==> Building .icns"
if command -v iconutil >/dev/null 2>&1; then
    ICONSET=$(mktemp -d)/AppIcon.iconset
    mkdir -p "$ICONSET"
    cp "$ICON_DIR/edl-ng-16.png"   "$ICONSET/icon_16x16.png"
    cp "$ICON_DIR/edl-ng-32.png"   "$ICONSET/icon_16x16@2x.png"
    cp "$ICON_DIR/edl-ng-32.png"   "$ICONSET/icon_32x32.png"
    cp "$ICON_DIR/edl-ng-64.png"   "$ICONSET/icon_32x32@2x.png"
    cp "$ICON_DIR/edl-ng-128.png"  "$ICONSET/icon_128x128.png"
    cp "$ICON_DIR/edl-ng-256.png"  "$ICONSET/icon_128x128@2x.png"
    cp "$ICON_DIR/edl-ng-256.png"  "$ICONSET/icon_256x256.png"
    cp "$ICON_DIR/edl-ng-512.png"  "$ICONSET/icon_256x256@2x.png"
    cp "$ICON_DIR/edl-ng-512.png"  "$ICONSET/icon_512x512.png"
    cp "$ICON_DIR/edl-ng-1024.png" "$ICONSET/icon_512x512@2x.png"
    iconutil -c icns "$ICONSET" -o "$MAC_DIR/AppIcon.icns"
    rm -rf "$(dirname "$ICONSET")"
else
    echo "warning: iconutil not available; skipping .icns (run on macOS to regenerate)" >&2
fi

echo "Done."
echo "  SVG:    $SVG"
echo "  PNGs:   $ICON_DIR/edl-ng-*.png"
echo "  ICO:    $ICON_DIR/edl-ng.ico  (also copied to $GUI_ASSETS/)"
echo "  ICNS:   $MAC_DIR/AppIcon.icns"
