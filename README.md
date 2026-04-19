# edl-ng

**A modern, user-friendly tool for interacting with Qualcomm devices in Emergency Download (EDL) mode.**

Built with .NET 9, `edl-ng` ships two front-ends — a `System.CommandLine` CLI (`edl-ng`) and an Avalonia 12 desktop GUI (`qcedl-gui`) — on top of a shared protocol library that speaks Sahara (PBL) and Firehose (APSS) over USB to flash, read, and manage Qualcomm-based devices.

## Features

* **Cross-Platform:** Runs on Windows, Linux, and macOS with a single self-contained executable per front-end.
* **Two Front-Ends:**
  * `edl-ng` — scriptable CLI for every supported operation.
  * `qcedl-gui` — Avalonia desktop app with Connection, Partitions, Sectors, RawProgram, Advanced (provision / upload-loader / reset), Logs, and Settings views. Localized in English, Simplified Chinese, Traditional Chinese, and Japanese, with System / Light / Dark theme and live log streaming.
* **Sahara Protocol Support:**
  * Upload Firehose programmers (`.elf` files).
  * Device information retrieval (Serial Number, HWID, RKH).
* **Firehose Protocol Support:**
  * Automatic Firehose configuration.
  * **GPT Management:** Print GUID Partition Table.
  * **Partition Operations:**
    * Read partition to a file.
    * Write file to a partition.
    * Automatic LUN scanning to find partitions.
  * **Sector Operations:**
    * Read raw sectors to a file.
    * Write file to raw sectors.
  * **Rawprogram Flows:** Execute `rawprogramN.xml` + `patchN.xml`, or dump all partitions of a LUN and generate rawprogram XML.
  * **UFS Provisioning:** Apply a provisioning XML.
  * **Device Control:** Reset or power off the device.
  * Get detailed storage information (sector size, LUN count).
* **Flexible Device Detection:**
  * Specify USB VID/PID; pin a specific libusb device by bus/addr when multiple EDL devices are plugged in.
  * Uses COM ports on Windows or LibUsbDotNet (for all platforms, especially Linux/macOS).
  * GUI auto-prompts a device picker when more than one candidate matches.
* **Direct-Mode Backends (skip USB / Sahara / Firehose):**
  * `--hostdev-as-target <path>` — read/write a SPI NOR node or a raw image file directly.
  * `--radxa-wos-platform` — Windows-only Radxa WoS SPI NOR backend.
* **Configurable:**
  * Specify memory type (UFS, eMMC/SD, NVMe, SPINOR etc.).
  * Set maximum payload size for Firehose.
  * Adjust logging levels.

## Usage

`edl-ng` can be downloaded from:

- [Releases](https://github.com/strongtz/edl-ng/releases)
- [Arch Linux CN repo](https://www.archlinuxcn.org/archlinux-cn-repo-and-mirror/) [Maintainer = @Cryolitia]

### GUI (`qcedl-gui`)

Launch the GUI and use the left-side nav to step through Connection → Partitions / Sectors / RawProgram / Advanced. Destructive actions (write, erase, provision, rawprogram flash) require typed confirmation. Settings (language, theme, log level, last-used loader / VID / PID / memory / backend) persist across launches.

#### Platform notes

- **Linux** — the GUI opens the device without `sudo` once the shipped udev rule is active. Nix consumers get it via `services.udev.packages`; manual installs: see [docs/linux-udev.md](docs/linux-udev.md).
- **macOS** — defaults to the LibUsb backend, which opens the EDL interface through IOKit without elevation. If you override to the serial backend (`/dev/tty.usbserial-*`), you'll need to run the app as root.
- **Windows** — no elevation required; devices are opened through the regular COM / WinUSB APIs.

### CLI (`edl-ng`)

The general command structure is:
`edl-ng [global-options] <command> [command-options-and-arguments]`

Run `edl-ng --help` for a full list of commands and options, or refer to the specific command help using `edl-ng <command> --help`.

### Supported Commands

* `upload-loader`: Connects in Sahara mode and uploads the specified Firehose loader. (e.g., qsahara_device_programmer.xml, xbl_s_devprg_ns.melf or prog_firehose_*.elf)
* `printgpt`: Reads and prints the GPT from the device.
* `read-part <partition_name> <filename>`: Reads a partition to a file.
* `read-sector <start_sector> <num_sectors> <filename>`: Reads sectors to a file.
* `read-lun <filename>`: Reads the entire LUN (all sectors) to a file.
* `dump-rawprogram <dump_save_dir>`: Reads all partitions to individual files from a certain LUN and generates rawprogram XML file.
* `write-part <partition_name> <filename>`: Writes data from a file to a partition.
* `write-sector <start_sector> <filename>`: Writes data from a file to sectors.
* `erase-part <partition_name>`: Erases a partition by name from the device.
* `erase-sector <start_sector> <sectors>`: Erases a specified number of sectors from a given LUN and start LBA.
* `provision <xmlfile>`: Performs UFS provisioning using an XML file.
* `rawprogram <xmlfile_patterns>`: Processes rawprogramN.xml and patchN.xml files for flashing.
* `reset`: Resets or powers off the device.
  * `--mode <reset|off|edl>`: Reset mode (default: `reset`).
  * `--delay <seconds>`: Delay before executing power command.

### Examples

* **Flash a flat build to device using rawprogram XML files**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf --memory UFS rawprogram rawprogram*.xml patch*.xml
    ```

* **Print GPT from LUN 0 (UFS memory):**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf --memory UFS printgpt --lun 0
    ```

* **Read the 'modem' partition from any LUN to `modem.bin`:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf read-part modem modem.bin
    ```

* **Write `modem.bin` to the 'modem' partition found in any LUN:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf write-part modem modem.bin
    ```

* **Read the entire LUN 0 to `lun0.bin`:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf --memory UFS read-lun lun0.bin --lun 0
    ```

* **Dump all partitions from LUN 0 to directory and generate rawprogram XML:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf --memory UFS dump-rawprogram ./partitions --lun 0
    ```

    This will create partition files (e.g., `system.bin`, `vendor.bin`), GPT files (`gpt_main0.bin`, `gpt_backup0.bin`), and `rawprogram0.xml`.

* **Write `lun0.bin` to the entire LUN 0:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf --memory UFS write-sector 0 lun0.bin --lun 0
    ```

* **Reboot the device:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf reset
    ```

### Verified Target Platforms

* Snapdragon 835 (MSM8998)
* Dragonwing QCS6490
* Dragonwing QCS8550
* Snapdragon X Elite (SC8380)

SoCs older than MSM8998 are not tested and may not yet be supported.

Devices with vendor customized DevPrg may not be supported as well.

## Prerequisites

* **.NET 9 SDK** (no need to install .NET runtime if using pre-built binaries).
* **Qualcomm USB Drivers:**
  * **Windows:** Both Qualcomm® USB Driver (QUD) and WinUSB driver (Zadig) are supported.
  * **Linux/macOS:** `libusb` is used. You may also need to configure udev rules on Linux to allow user access to the device.
* **Firehose Programmer:** An appropriate `.elf` programmer file for your specific device (e.g., `prog_firehose_*.elf` or `xbl_s_devprg_ns.melf`).

## Building

1. Clone the repository.
2. Ensure you have the .NET 9 SDK installed.
3. From the solution root, build everything:

    ```bash
    dotnet build
    ```

4. Output binaries:
    * CLI: `QCEDL.CLI/bin/<Configuration>/net9.0/<Platform>/edl-ng`
    * GUI: `QCEDL.GUI/bin/<Configuration>/net9.0/<rid>/qcedl-gui`

   Run the GUI directly from source with `dotnet run --project QCEDL.GUI/QCEDL.GUI.csproj`. For a self-contained CLI build, use `dotnet publish QCEDL.CLI/QCEDL.CLI.csproj -c Release -r <rid> --self-contained true` (rids: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`).

## License

This project is licensed under the MIT license.

## Acknowledgments

This project is inspired by [gus33000/QCEDL.NET](https://github.com/gus33000/QCEDL.NET) and [bkerler/edl](https://github.com/bkerler/edl).
