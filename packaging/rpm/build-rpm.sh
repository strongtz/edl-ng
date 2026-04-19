#!/usr/bin/env bash
# Build an .rpm from a prebuilt self-contained publish directory.
#
# Usage: build-rpm.sh <spec-name> <publish-dir> <rpm-arch> <version> <output-dir> <repo-root>
#
#   spec-name    edl-ng | qcedl-gui (matches a file under packaging/rpm/<spec>.spec)
#   publish-dir  `dotnet publish ... --self-contained true` output
#   rpm-arch     x86_64 | aarch64
#   version      1.5.0 etc.
#   output-dir   Directory the .rpm is copied into.
#   repo-root    Repo root (to pick up LICENSE and udev rule).

set -euo pipefail

if [ "$#" -ne 6 ]; then
    echo "usage: $0 <spec-name> <publish-dir> <rpm-arch> <version> <output-dir> <repo-root>" >&2
    exit 2
fi

SPEC=$1
PUBLISH=$2
ARCH=$3
VERSION=$4
OUTDIR=$5
REPO=$6

SPEC_FILE="$REPO/packaging/rpm/${SPEC}.spec"
if [ ! -f "$SPEC_FILE" ]; then
    echo "error: spec file not found: $SPEC_FILE" >&2
    exit 1
fi
if [ ! -d "$PUBLISH" ]; then
    echo "error: publish dir not found: $PUBLISH" >&2
    exit 1
fi

TOP=$(mktemp -d)
trap 'rm -rf "$TOP"' EXIT
mkdir -p "$TOP"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}

cp -a "$PUBLISH" "$TOP/SOURCES/publish"
cp "$REPO/LICENSE" "$TOP/SOURCES/LICENSE"
# rpm rule file: reuse the main (wheel-based) rule — rpm targets Fedora/RHEL/SUSE.
cp "$REPO/resources/udev/99-edl-ng.rules" "$TOP/SOURCES/99-edl-ng.rules"
if [ "$SPEC" = "qcedl-gui" ]; then
    cp "$REPO/packaging/arch/gui/qcedl-gui.desktop" "$TOP/SOURCES/qcedl-gui.desktop"
    mkdir -p "$TOP/SOURCES/icons"
    for SZ in 16 32 48 64 128 256 512; do
        SRC="$REPO/resources/icons/edl-ng-${SZ}.png"
        [ -f "$SRC" ] || continue
        cp "$SRC" "$TOP/SOURCES/icons/"
    done
fi
cp "$SPEC_FILE" "$TOP/SPECS/"

rpmbuild \
    --define "_topdir $TOP" \
    --define "version $VERSION" \
    --define "_binary_payload w9.xzdio" \
    --target "$ARCH" \
    -bb "$TOP/SPECS/$(basename "$SPEC_FILE")"

mkdir -p "$OUTDIR"
find "$TOP/RPMS" -name '*.rpm' -exec cp -v {} "$OUTDIR/" \;
