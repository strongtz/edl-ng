# Installing udev rules for edl-ng on Linux

The GUI and CLI can open the Qualcomm EDL device as your own user — **no `sudo`
required** — once a matching udev rule is in place *and* your user is in the
`wheel` group. The shipped rule grants `MODE=0660, GROUP=wheel` on every EDL
USB/tty node.

## Packaged install

### Nix (flake)

The Nix derivation drops `99-edl-ng.rules` into `$out/lib/udev/rules.d/`. Pick it up
from system config:

```nix
services.udev.packages = [ pkgs.edl-ng ];
users.users.<you>.extraGroups = [ "wheel" ];
```

## Manual install (deb/rpm/Arch/etc.)

Until there are native packages, copy the file from the source tree and join the
`wheel` group:

```sh
sudo install -m 0644 resources/udev/99-edl-ng.rules /etc/udev/rules.d/99-edl-ng.rules
sudo udevadm control --reload
sudo udevadm trigger
sudo usermod -aG wheel "$USER"   # then log out / in so the new group sticks
```

Unplug and replug the device so the new rule applies to it.

## Verifying it worked

```sh
$ ls -l /dev/bus/usb/$(lsusb | awk '/05c6:9008/ {print $2 "/" substr($4,1,3)}')
crw-rw---- 1 root wheel ... /dev/bus/usb/001/014
$ groups
... wheel ...
```

If the device node shows `root wheel 0660` and `wheel` is in your `groups`
output, the GUI and CLI will open it without `sudo`.

## Why `wheel` instead of `plugdev` / `uaccess`?

`wheel` is the group used across the rest of this project for admin-adjacent
access (it's also the default admin group on Arch, Fedora, and nixpkgs). Using
it here keeps privilege management consistent: one group membership covers both
`sudo` and EDL device access.
