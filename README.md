# edl-ng

**A modern, user-friendly tool for interacting with Qualcomm devices in Emergency Download (EDL) mode.**

Built with .NET, `edl-ng` provides tools for both Sahara and Firehose protocols, enabling device flashing, partition management, and low-level device interaction.

## Features

* **Cross-Platform:** Designed to run on Windows, Linux, and macOS with a single executable.
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
  * **Device Control:** Reset or power off the device.
  * Get detailed storage information (sector size, LUN count).
* **Flexible Device Detection:**
  * Specify USB VID/PID.
  * Uses COM ports on Windows or LibUsbDotNet (for all platforms, especially Linux/macOS).
* **Configurable:**
  * Specify memory type (UFS, eMMC/SD, NVMe, SPINOR etc.).
  * Set maximum payload size for Firehose.
  * Adjust logging levels.

## Usage

`edl-ng` can be downloaded from:

- [Releases](https://github.com/strongtz/edl-ng/releases)
- [Arch Linux CN repo](https://www.archlinuxcn.org/archlinux-cn-repo-and-mirror/) [Maintainer = @Cryolitia]

The general command structure is:
`edl-ng [global-options] <command> [command-options-and-arguments]`

Run `edl-ng --help` for a full list of commands and options, or refer to the specific command help using `edl-ng <command> --help`.

### Supported Commands

* `upload-loader`: Connects in Sahara mode and uploads the specified Firehose loader.
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
3. Navigate to the solution directory (`/`) and run:

    ```bash
    dotnet build
    ```

4. The executable `edl-ng` will be located in `QCEDL.CLI/bin/<Configuration>/net9.0/<Platform>/`. For example: `QCEDL.CLI/bin/Debug/net9.0/win-x64/edl-ng`.

## License

This project is licensed under the MIT license.

## Acknowledgments

This project is inspired by [gus33000/QCEDL.NET](https://github.com/gus33000/QCEDL.NET) and [bkerler/edl](https://github.com/bkerler/edl).
