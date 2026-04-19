#!/usr/bin/env bash
# Build a qcedl-gui .deb from a pre-built self-contained publish directory.
#
# Usage: build-gui-deb.sh <publish-dir> <deb-arch> <version> <output.deb> <repo-root>
#
# The GUI publish tree contains hundreds of assemblies, native libs, and the
# AOT/non-AOT helper binary. We install the whole tree to /usr/lib/qcedl-gui/
# and expose it via a shell launcher at /usr/bin/qcedl-gui, plus a .desktop
# entry and the Debian udev rule.

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

if [ ! -x "$PUBLISH/qcedl-gui" ]; then
    echo "error: $PUBLISH/qcedl-gui not found or not executable" >&2
    exit 1
fi

PKG=qcedl-gui
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
ROOT="$STAGE/${PKG}_${VERSION}_${ARCH}"

mkdir -p \
    "$ROOT/DEBIAN" \
    "$ROOT/usr/bin" \
    "$ROOT/usr/lib/$PKG" \
    "$ROOT/usr/lib/udev/rules.d" \
    "$ROOT/usr/share/applications" \
    "$ROOT/usr/share/doc/$PKG"

cp -a "$PUBLISH/." "$ROOT/usr/lib/$PKG/"
chmod 755 "$ROOT/usr/lib/$PKG/qcedl-gui"

cat > "$ROOT/usr/bin/qcedl-gui" <<'EOF'
#!/bin/sh
exec /usr/lib/qcedl-gui/qcedl-gui "$@"
EOF
chmod 755 "$ROOT/usr/bin/qcedl-gui"

install -m 644 "$REPO/packaging/arch/gui/qcedl-gui.desktop" \
               "$ROOT/usr/share/applications/qcedl-gui.desktop"
install -m 644 "$REPO/resources/udev/99-edl-ng.debian.rules" \
               "$ROOT/usr/lib/udev/rules.d/99-edl-ng.rules"
install -m 644 "$REPO/LICENSE" "$ROOT/usr/share/doc/$PKG/copyright"

for SZ in 16 32 48 64 128 256 512; do
    SRC="$REPO/resources/icons/edl-ng-${SZ}.png"
    [ -f "$SRC" ] || continue
    install -D -m 644 "$SRC" "$ROOT/usr/share/icons/hicolor/${SZ}x${SZ}/apps/qcedl-gui.png"
done

INSTALLED_KB=$(du -sk "$ROOT/usr" | cut -f1)

cat > "$ROOT/DEBIAN/control" <<EOF
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_KB
Maintainer: Xilin Wu <wuxilin123@gmail.com>
Depends: libc6, libusb-1.0-0, libfontconfig1, libice6, libsm6,
 libx11-6, libxcursor1, libxi6, libxrandr2
Homepage: https://github.com/strongtz/edl-ng
Description: GUI front-end for edl-ng (Qualcomm EDL toolchain)
 qcedl-gui is an Avalonia-based GUI for edl-ng. It drives the shared
 protocol library to flash, read, and manage Qualcomm-based devices in
 EDL (Emergency Download) mode. Self-contained, no system .NET required.
EOF

cat > "$ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload || true
    udevadm trigger --subsystem-match=usb --subsystem-match=tty || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
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
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
    fi
fi
EOF
chmod 755 "$ROOT/DEBIAN/postrm"

dpkg-deb --build --root-owner-group "$ROOT" "$OUT"
echo "Wrote $OUT"
