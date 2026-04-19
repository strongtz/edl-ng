#!/usr/bin/env bash
# Build an edl-ng CLI .deb from a pre-built self-contained publish directory.
#
# Usage: build-cli-deb.sh <publish-dir> <deb-arch> <version> <output.deb> <repo-root>
#
# Arguments:
#   publish-dir  Directory produced by `dotnet publish QCEDL.CLI ... --self-contained true`
#                (must contain the `edl-ng` binary at its root).
#   deb-arch     dpkg architecture: amd64, arm64, armhf, etc.
#   version      Package version (e.g. 1.5.0).
#   output.deb   Path to write the .deb.
#   repo-root    Repo root (used to pick up LICENSE and the udev rule).

set -euo pipefail

if [ "$#" -ne 5 ]; then
    echo "usage: $0 <publish-dir> <deb-arch> <version> <output.deb> <repo-root>" >&2
    exit 2
fi

PUBLISH=$1
ARCH=$2
VERSION=$3
OUT=$4
REPO=$5

if [ ! -x "$PUBLISH/edl-ng" ]; then
    echo "error: $PUBLISH/edl-ng not found or not executable" >&2
    exit 1
fi

PKG=edl-ng
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
ROOT="$STAGE/${PKG}_${VERSION}_${ARCH}"

mkdir -p \
    "$ROOT/DEBIAN" \
    "$ROOT/usr/bin" \
    "$ROOT/usr/lib/udev/rules.d" \
    "$ROOT/usr/share/doc/$PKG"

install -m 755 "$PUBLISH/edl-ng" "$ROOT/usr/bin/edl-ng"
install -m 644 "$REPO/resources/udev/99-edl-ng.debian.rules" \
               "$ROOT/usr/lib/udev/rules.d/99-edl-ng.rules"
install -m 644 "$REPO/LICENSE" "$ROOT/usr/share/doc/$PKG/copyright"

INSTALLED_KB=$(du -sk "$ROOT/usr" | cut -f1)

cat > "$ROOT/DEBIAN/control" <<EOF
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_KB
Maintainer: Xilin Wu <wuxilin123@gmail.com>
Depends: libc6, libusb-1.0-0
Homepage: https://github.com/strongtz/edl-ng
Description: Modern toolchain for Qualcomm Emergency Download (EDL) mode
 edl-ng is a .NET cross-platform tool for flashing, reading, and managing
 Qualcomm-based devices in EDL (Emergency Download) mode using the Sahara
 and Firehose protocols. The binary is self-contained and ships with a
 udev rule granting access to EDL devices for the logged-in seat and the
 plugdev group.
EOF

cat > "$ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload || true
    udevadm trigger --subsystem-match=usb --subsystem-match=tty || true
fi
EOF
chmod 755 "$ROOT/DEBIAN/postinst"

cat > "$ROOT/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if command -v udevadm >/dev/null 2>&1; then
        udevadm control --reload || true
    fi
fi
EOF
chmod 755 "$ROOT/DEBIAN/postrm"

dpkg-deb --build --root-owner-group "$ROOT" "$OUT"
echo "Wrote $OUT"
