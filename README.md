# edl-ng

**A modern, cross-platform command-line interface (CLI) for interacting with Qualcomm devices in Emergency Download (EDL) mode.**

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
    * Write file to a partition (with automatic padding).
    * Automatic LUN scanning to find partitions.
  * **Sector Operations:**
    * Read raw sectors to a file.
    * Write file to raw sectors (with automatic padding).
  * **Device Control:** Reset or power off the device.
  * Get detailed storage information (sector size, LUN count).
* **Flexible Device Detection:**
  * Specify USB VID/PID.
  * Uses COM ports on Windows or LibUsbDotNet (for all platforms, especially Linux/macOS).
* **Configurable:**
  * Specify memory type (UFS, eMMC/SDCC, NVMe, etc.).
  * Set maximum payload size for Firehose.
  * Adjust logging levels.

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
    dotnet build -c Release
    ```

4. The executable `edl-ng` will be located in `QCEDL.CLI/bin/<Platform>/<Configuration>/net9.0/`. For example: `QCEDL.CLI/bin/x64/Release/net9.0/edl-ng`.

## Usage

The general command structure is:
`edl-ng [global-options] <command> [command-options-and-arguments]`

Run `edl-ng --help` for a full list of commands and options.

### Supported Commands

* `upload-loader`: Connects in Sahara mode and uploads the specified Firehose loader.
* `printgpt`: Reads and prints the GPT from the device.
* `read-part <partition_name> <filename>`: Reads a partition to a file.
* `write-part <partition_name> <filename>`: Writes data from a file to a partition.
* `read-sector <start_sector> <num_sectors> <filename>`: Reads sectors to a file.
* `write-sector <start_sector> <filename>`: Writes data from a file to sectors.
* `reset`: Resets or powers off the device.
  * `--mode <reset|off|edl>`: Reset mode (default: `reset`).
  * `--delay <seconds>`: Delay before executing power command.

### Examples

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

* **Reboot the device:**

    ```bash
    edl-ng --loader prog_firehose_ddr.elf reset
    ```

### Verified Target Platforms

* Snapdragon 835 (MSM8998)
* Dragonwing QCS6490
* Dragonwing QCS8550

SoCs older than msm8998 may not yet be supported.

## License

This project is licensed under the MIT license.

## Acknowledgments

This project is inspired by [gus33000/QCEDL.NET](https://github.com/gus33000/QCEDL.NET) and [bkerler/edl](https://github.com/bkerler/edl).
