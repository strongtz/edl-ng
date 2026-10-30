# QCEDL.GUI — Implementation Tracker

Living audit + phased plan for the Avalonia 12 + ReactiveUI GUI front-end.
Status legend: **Todo** / **In Progress** / **Done** / **Blocked** / **Deferred**.

---

## 1. CLI Feature Inventory (source of truth for scope)

Derived from `QCEDL.CLI/Program.cs` (globals) and `QCEDL.CLI/Commands/*`.

### Global options (bound by `GlobalOptionsBinder`)

| Option | CLI flag | Notes |
|---|---|---|
| Loader path | `--loader` / `-l` | Firehose programmer (`.elf` / `.melf` / `qsahara_device_programmer.xml`). |
| USB VID | `--vid` | Hex, defaults to `0x05C6`. |
| USB PID | `--pid` | Hex, defaults to `0x9008` or `0x900E`. |
| Memory type | `--memory` | `StorageType` enum (UFS, eMMC/Sdcc, SPINOR, NVMe, NAND). |
| Log level | `--loglevel` | Trace/Debug/Info/Warning/Error. |
| Max payload | `--maxpayload` | Firehose `configure` max payload bytes. |
| Slot | `--slot` / `-s` | 0 or 1 (eMMC / sdcard, etc.). |
| Host device target | `--hostdev-as-target` | Bypasses USB; reads/writes SPI NOR / file directly. |
| Image size | `--img-size` | Used with `--hostdev-as-target` file path (e.g. `32M`, `1G`). |
| Radxa WoS | `--radxa-wos-platform` | Windows-only SPI NOR backend. |

### Commands

| Command | Summary |
|---|---|
| `upload-loader` | Sahara handshake + programmer upload only. |
| `reset` | Firehose reset / power-off (`--mode`, `--delay`). |
| `printgpt` | Parse + print GPT; per-LUN or scan all (`--lun`). |
| `read-part` | Read a named partition to a file. |
| `read-sector` | Read N sectors from `--lun` + start LBA to a file. |
| `read-lun` | Read the whole LUN to a file. |
| `write-part` | Write a file to a named partition. |
| `write-sector` | Write a file to sectors at start LBA. |
| `erase-part` | Erase a named partition. |
| `erase-sector` | Erase N sectors at start LBA. |
| `dump-rawprogram` | Dump all partitions of a LUN + generate `rawprogram` XML (`--gen-xml-only`, `--skip`). |
| `rawprogram` | Execute `rawprogramN.xml` + `patchN.xml`. |
| `provision` | UFS provisioning via XML. |

### Orchestration layer

`EdlManager` owns: device discovery (Windows / Linux LibUsb / Linux TTY fallback), mode
probing (Sahara / SaharaMemoryDebug / Firehose), Sahara handshake + programmer upload,
Firehose configure, and dispatch to one of three `IStorageBackend` implementations:
`FirehoseStorageBackend`, `HostStorageBackend`, `RadxaWoSBackend`.

---

## 2. CLI → GUI Mapping

Single-window shell with left-side nav. Screens:

| View | Covers |
|---|---|
| **Overview** | App intro, detected device snapshot, connection status, quick actions. |
| **Connection** | Target mode (USB / Host-dev / Radxa WoS), VID/PID, memory type, loader file picker, slot, max payload, image size, connect / upload-loader / reset. |
| **Partitions** | `printgpt` listing, `read-part`, `write-part`, `erase-part`. Confirmation UX for writes/erases. |
| **Sectors** | `read-sector` / `read-lun`, `write-sector`, `erase-sector`. |
| **RawProgram** | `rawprogram` execute, `dump-rawprogram` export, `--gen-xml-only`, `--skip`. |
| **Advanced** | `provision`, `upload-loader`, `reset` mode+delay. |
| **Logs** | Live log stream from shared `Logging` sink, copy/save. |
| **Settings/About** | Log level, theme, version. |

Shared UX: progress rows with cancel (where the underlying op supports it), file pickers,
destructive-action confirmation dialog with target summary (LUN/partition/LBA range).

---

## 3. Refactor Notes

- `EdlManager` currently takes `GlobalOptionsBinder` (couples to `System.CommandLine`).
  → Extract a plain POCO `EdlOptions` used by both CLI and GUI; make `EdlManager` public.
- `Logging` is `internal static` + writes to `Console`.
  → Introduce a `ILogSink` abstraction; CLI keeps console sink, GUI adds an observable sink
  feeding the Logs view.
- Types the GUI must touch directly (`DeviceMode`, `StorageGeometry`, `IStorageBackend`,
  `EdlManager`, `EdlOptions`) need to become `public`.
- GUI references the `QCEDL.CLI` assembly as a library (Exe projects can still be
  referenced — we only consume public types). This avoids duplicating orchestration logic.

---

## 4. Phased Rollout

**Phase 0 — Foundations** *(this deliverable)*
- [Done] CLI audit document (this file).
- [Done] Refactor: `EdlOptions` POCO + public surface on `EdlManager`, `Logging`, `StorageGeometry`, `DeviceMode`.
- [Done] `QCEDL.GUI` project: Avalonia 12 + ReactiveUI.Avalonia, registered in `edl-ng.slnx`.
- [Done] Custom theme/token system derived from `DESIGN.md` (no external theme packs).
- [Done] Single-window shell + left-nav + placeholders for all sections.
- [Done] Observable log sink wired into Logs view.

**Phase 1 — Connect & Inspect**
- [Done] Connection view: options form, connect action, target/loader pickers, mode+storage readout.
- [Done] Overview view: live connection summary card.
- [Done] Partitions view: `printgpt` scan + table rendering.
- [Done] `read-part` with file save picker + progress.

**Phase 2 — Read / Write / Erase**
- [Done] `read-part`, `read-sector`, `read-lun` end-to-end with progress. (Cancellation is Todo — underlying Firehose layer has no `CancellationToken` surface.)
- [Done] `write-part`, `write-sector` with file pickers + confirmation.
- [Done] `erase-part`, `erase-sector` with typed-string confirmation for destructive ranges.

**Phase 3 — RawProgram & Advanced**
- [Done] `rawprogram` execute (multi-file selection, progress aggregation).
- [Done] `dump-rawprogram` export (output dir picker, `--gen-xml-only`, `--skip`).
- [Done] `provision` XML runner.
- [Done] `upload-loader` standalone action.
- [Done] `reset` with mode + delay.

**Phase 4 — Polish**
- [Done] Keyboard shortcuts, focus visuals audit, contrast audit.
- [Done] Settings persistence (log level, last loader, last VID/PID, memory type, backend).
- [Done] About pane + version.
- [Done] Multi-device selection UX — `EdlManager.EnumerateDevices(EdlOptions)`
  surfaces an enumerator; GUI Connection view lists candidates and auto-prompts
  when >1 is found. CLI still picks the first match.
- [Deferred] Streaming probe / emergency-flash 9006 handling — blocked on protocol-layer work.

---

## 5. Implementation Status (per capability)

| Capability | Phase | Status | Notes |
|---|---|---|---|
| Custom theme tokens + styles | 0 | Done | `Themes/Tokens.axaml`, `Themes/Styles.axaml`. Parchment bg, terracotta CTA, ring shadows, serif headings. |
| Single-window shell | 0 | Done | `MainWindow.axaml` + `ShellViewModel` with section enum. |
| Log sink abstraction | 0 | Done | `ILogSink` in `QCEDL.NET.Logging` via existing `LibraryLogger`; GUI `ObservableLogSink`. |
| `EdlOptions` POCO | 0 | Done | `QCEDL.CLI/Core/EdlOptions.cs`; `GlobalOptionsBinder` projects onto it. |
| Device discovery / mode probe | 1 | Done (reused) | `EdlManager.DetectCurrentModeAsync` called from `EdlService`. |
| Connection view | 1 | Done | Loader/VID/PID/memory/slot/host-dev/radxa options; Connect + Probe actions. |
| Overview | 1 | Done | Connection summary + last-action row. |
| `printgpt` | 1 | Done | LUN scanner + partition table. |
| `read-part` | 2 | Done | Save-file picker, partition auto-fill from table row, progress bar. |
| `write-part` | 2 | Done | Open-file picker + confirmation dialog with target summary. |
| `erase-part` | 2 | Done | Typed-partition-name confirmation for destructive path. |
| `read-sector` / `read-lun` | 2 | Done | Sectors view: LUN + start/count, save picker, progress. `read-lun` uses `StorageGeometry.TotalSectors`. |
| `write-sector` | 2 | Done | Confirmation requires typed `WRITE`. |
| `erase-sector` | 2 | Done | Confirmation requires typed `ERASE`. |
| `rawprogram` | 3 | Done | Multi-file picker, per-program progress, typed `FLASH` confirmation. Shared runner `RawProgramRunner`. |
| `dump-rawprogram` | 3 | Done | Output folder picker, `--gen-xml-only`, skip list. Shared runner `DumpRawprogramRunner`. |
| `provision` | 3 | Done | XML picker + typed `PROVISION` confirmation. Shared runner `ProvisionRunner`. |
| `upload-loader` | 3 | Done | Standalone button on Advanced view; requires Sahara mode. |
| `reset` | 3 | Done | Mode + delay on Advanced view; confirmation required. |
| Destructive-action confirmation dialog | 2 | Done | `Views/ConfirmDialog` + `ConfirmDialogViewModel`. Supports plain confirm and typed-string double-confirm. |
| Cancellable progress | 2 | Todo | Underlying Firehose/Sahara paths don't accept a `CancellationToken`; revisit once the protocol layer exposes one. |
| Settings persistence | 4 | Done | `GuiSettings.Current` shared model persists culture, log level, last loader path, VID/PID, memory type, and transport backend. |
| Keyboard shortcuts | 4 | Done | `Window.KeyBindings` + NativeMenu gestures wire Ctrl/Cmd+1..7 for nav, Ctrl+, for Settings, F1 for docs. |
| Focus visuals | 4 | Done | Buttons and ListBoxItems highlight with the Focus Blue ring on keyboard focus; TextBox/ComboBox already had it. |
| Multi-device selection | 4 | Done | `EdlManager.EnumerateDevices` + `DeviceCandidate`; Connection view Scan + auto-prompting `DeviceChooserDialog` when >1 candidate matches. `EdlOptions.UsbDeviceId` pins a specific libusb device by `bus_/addr_`. |

---

## 6. Known Constraints / Open Items

- CLI's `Logging` still owns the console sink; GUI uses the same shared sink so logs
  appear both in the Logs view and console when the GUI is run from a terminal.
- `EdlManager` dispatches several blocking methods on `Task.Run`; progress for some paths
  (Sahara upload) is not granular — surface the log stream as the primary signal.
- Cross-platform USB detection requires LibUsb to be installed on Linux/macOS; GUI should
  surface a clear error rather than silently failing.
- Multi-device selection is not yet modelled in the CLI; the GUI currently mirrors
  "first-match wins" and logs a warning.
- GUI does not shell out to `edl-ng` — it links against `QCEDL.CLI` as a library and
  reuses the public orchestration surface directly.

---

## 7. Update Log

- **2026-04-19** — Initial audit + Phase 0 + Phase 1 (Overview, Connection, Partitions
  `printgpt`) implemented. Remaining work tracked in Section 5.
- **2026-04-19** — Phase 4 polish: extended `GuiSettings` to persist log level + last-used
  Connection options, added a Log-level picker to Settings, wired cross-platform nav
  shortcuts via `Window.KeyBindings`, and strengthened Focus-Blue focus rings on buttons /
  nav items. Multi-device selection and 9006 emergency-flash handling remain deferred on
  the protocol layer.
- **2026-04-19** — Multi-device selection: added `DeviceCandidate` +
  `EdlManager.EnumerateDevices(EdlOptions)` (static, non-destructive); extended
  `EdlOptions` with `UsbDeviceId` (`usb:vid_XXXX,pid_YYYY,bus_N,addr_M`) and
  taught `QualcommSerial` to open a specific libusb device when bus/addr is
  pinned. GUI Connection view now has a Devices card (Scan + inline list +
  Clear selection) and Connect/Probe auto-open a new `DeviceChooserDialog`
  when >1 candidate matches. Also fixed `EdlService.CloneOptions` silently
  dropping `Backend`/`SerialDevicePath`.
