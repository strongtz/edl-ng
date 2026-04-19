%global _build_id_links none
%global debug_package %{nil}
%define __strip /bin/true
%define __objdump /bin/true

%undefine _missing_build_ids_terminate_build

Name:           edl-ng
Version:        %{version}
Release:        1%{?dist}
Summary:        Modern toolchain for Qualcomm Emergency Download (EDL) mode

License:        MIT
URL:            https://github.com/strongtz/edl-ng

# Prebuilt self-contained publish tree is staged into %{_sourcedir}/publish
# by packaging/rpm/build-rpm.sh before rpmbuild runs.
AutoReqProv:    no
Requires:       libusbx

%description
edl-ng is a .NET cross-platform tool for flashing, reading, and managing
Qualcomm-based devices in EDL (Emergency Download) mode using the Sahara
and Firehose protocols. The binary is self-contained and ships with a
udev rule granting access to EDL devices for the "wheel" group.

%install
rm -rf %{buildroot}
install -Dm 755 %{_sourcedir}/publish/edl-ng %{buildroot}/usr/bin/edl-ng
install -Dm 644 %{_sourcedir}/99-edl-ng.rules %{buildroot}/usr/lib/udev/rules.d/99-edl-ng.rules
install -Dm 644 %{_sourcedir}/LICENSE %{buildroot}/usr/share/licenses/edl-ng/LICENSE

%post
if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload || true
    udevadm trigger --subsystem-match=usb --subsystem-match=tty || true
fi

%postun
if [ "$1" = 0 ]; then
    if command -v udevadm >/dev/null 2>&1; then
        udevadm control --reload || true
    fi
fi

%files
/usr/bin/edl-ng
/usr/lib/udev/rules.d/99-edl-ng.rules
%license /usr/share/licenses/edl-ng/LICENSE
