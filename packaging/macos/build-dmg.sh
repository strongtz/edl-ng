#!/usr/bin/env bash
# Wrap a .app bundle in a compressed .dmg.
#
# Usage: build-dmg.sh <app-path> <dmg-path> [volume-name] [size]
#
# <size> is passed to `hdiutil create -size` (e.g. 512m, 1g). When omitted,
# hdiutil picks a size from the source folder with its own heuristics, which
# occasionally under-provisions on very small payloads.

set -euo pipefail

if [ "$#" -lt 2 ]; then
    echo "usage: $0 <app-path> <dmg-path> [volume-name] [size]" >&2
    exit 2
fi

APP=$1
DMG=$2
VOLNAME=${3:-qcedl-gui}
SIZE=${4:-}

if [ ! -d "$APP" ]; then
    echo "error: app not found: $APP" >&2
    exit 1
fi

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
cp -a "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

rm -f "$DMG"
HDIUTIL_ARGS=(
    -volname "$VOLNAME"
    -srcfolder "$STAGE"
    -ov
    -format UDZO
    -fs HFS+
)
if [ -n "$SIZE" ]; then
    HDIUTIL_ARGS+=(-size "$SIZE")
fi
hdiutil create "${HDIUTIL_ARGS[@]}" "$DMG"

echo "Wrote $DMG"
