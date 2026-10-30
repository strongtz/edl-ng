%global _build_id_links none
%global debug_package %{nil}
%define __strip /bin/true
%define __objdump /bin/true

%undefine _missing_build_ids_terminate_build

Name:           qcedl-gui
Version:        %{version}
Release:        1%{?dist}
Summary:        Avalonia-based GUI front-end for edl-ng

License:        MIT
URL:            https://github.com/strongtz/edl-ng

AutoReqProv:    no
Requires:       libusbx
Requires:       fontconfig
Requires:       libICE
Requires:       libSM
Requires:       libX11
Requires:       libXcursor
Requires:       libXi
Requires:       libXrandr

%description
qcedl-gui is an Avalonia-based GUI for edl-ng. It drives the shared
protocol library to flash, read, and manage Qualcomm-based devices in
EDL (Emergency Download) mode. Self-contained, no system .NET required.

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}/usr/lib/qcedl-gui
cp -a %{_sourcedir}/publish/. %{buildroot}/usr/lib/qcedl-gui/
chmod 755 %{buildroot}/usr/lib/qcedl-gui/qcedl-gui

mkdir -p %{buildroot}/usr/bin
cat > %{buildroot}/usr/bin/qcedl-gui <<'EOF'
#!/bin/sh
exec /usr/lib/qcedl-gui/qcedl-gui "$@"
EOF
chmod 755 %{buildroot}/usr/bin/qcedl-gui

install -Dm 644 %{_sourcedir}/qcedl-gui.desktop %{buildroot}/usr/share/applications/qcedl-gui.desktop
install -Dm 644 %{_sourcedir}/99-edl-ng.rules %{buildroot}/usr/lib/udev/rules.d/99-edl-ng.rules
install -Dm 644 %{_sourcedir}/LICENSE %{buildroot}/usr/share/licenses/qcedl-gui/LICENSE

for sz in 16 32 48 64 128 256 512; do
    src=%{_sourcedir}/icons/edl-ng-${sz}.png
    [ -f "$src" ] || continue
    install -Dm 644 "$src" %{buildroot}/usr/share/icons/hicolor/${sz}x${sz}/apps/qcedl-gui.png
done

%post
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

%postun
if [ "$1" = 0 ]; then
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

%files
/usr/bin/qcedl-gui
/usr/lib/qcedl-gui
/usr/lib/udev/rules.d/99-edl-ng.rules
/usr/share/applications/qcedl-gui.desktop
/usr/share/icons/hicolor/*/apps/qcedl-gui.png
%license /usr/share/licenses/qcedl-gui/LICENSE
