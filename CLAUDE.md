# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`edl-ng` is a .NET 9 cross-platform toolchain for Qualcomm Emergency Download (EDL) mode. It ships two front-ends — a `System.CommandLine` CLI and an Avalonia 12 GUI — on top of a shared protocol library that speaks Sahara (PBL) and Firehose (APSS) over USB to flash, read, and manage Qualcomm-based devices.

## Build / Format / Publish

- Build: `dotnet build` (solution file is `edl-ng.slnx`). Outputs:
  - CLI: `QCEDL.CLI/bin/<Config>/net9.0/<Platform>/edl-ng`
  - GUI: `QCEDL.GUI/bin/<Config>/net9.0/<rid>/qcedl-gui`
- Format check (matches CI): `dotnet format --verify-no-changes --verbosity diagnostic`. CI fails on any formatting drift — run `dotnet format` before committing.
- Publish self-contained CLI (matches CI): `dotnet publish QCEDL.CLI/QCEDL.CLI.csproj -c Release -r <rid> --self-contained true` where `<rid>` is `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`.
- Run the GUI locally: `dotnet run --project QCEDL.GUI/QCEDL.GUI.csproj`.
- There is no test project; don't run `dotnet test`.
- `Directory.Build.props` applies to all projects: `TreatWarningsAsErrors=true`, nullable enabled, `AllowUnsafeBlocks=true`, `AnalysisMode=Recommended`, doc generation on. Warnings break the build — fix them, don't suppress. The GUI project locally demotes a small set of IDE style rules (block-vs-expression body etc.) to warnings via `NoWarn` because Avalonia code-behind idiomatically mixes both — see `QCEDL.GUI/QCEDL.GUI.csproj`.

## Solution layout

Four C# projects (referenced in `edl-ng.slnx`) plus a Nix flake:

- `QCEDL.CLI/` — CLI entry point only. `Program.cs` wires `System.CommandLine` global options and registers one command per file in `Commands/` (`printgpt`, `read-part`/`write-part`, `read-sector`/`write-sector`/`read-lun`, `erase-part`/`erase-sector`, `rawprogram`, `dump-rawprogram`, `provision`, `upload-loader`, `reset`). Note that `read-lun` lives in `ReadSectorCommand.cs` (`CreateReadLunCommand`), not its own file. Besides commands, this project only holds the `System.CommandLine` glue: `Core/GlobalOptionsBinder.cs` (binds parse results onto `EdlOptions`) and `Helpers/CommandExecutor.cs`. Targets `AnyCPU`/`x64`/`ARM64`.
- `QCEDL.NET/` — protocol + transport + shared orchestration library. `Core/` hosts the **public** orchestration surface (`EdlManager`, `EdlOptions`, `DeviceMode`, `StorageGeometry`, `BlockDeviceManagerBase`, `HostDeviceManager`, `RadxaWoSDeviceManager`) **plus the higher-level `*Runner` flows** (`RawProgramRunner`, `DumpRawprogramRunner`, `ProvisionRunner`). `Helpers/` holds the shared `Logging` sink, `ProgressReporter`, `AlignmentHelper`, and `ImageSizeParser`. No UI or `System.CommandLine` dependencies. Exposes internals to `edl-ng` and `qcedl-gui` via `InternalsVisibleTo`. `AnyCPU` only.
- `QCEDL.GUI/` — Avalonia 12 + ReactiveUI desktop app. References `QCEDL.NET` directly and drives `EdlManager` — it does **not** shell out to the `edl-ng` executable and does **not** depend on `QCEDL.CLI`. `AnyCPU` only, self-contained publish to match CLI's `NETSDK1151` constraint.
- `QCEDL.Analyzer/` — Roslyn source generator (`EnumExtensionsGenerator.cs`) consumed at build time.

## Architecture (the parts that need reading multiple files to grasp)

**Connection lifecycle is owned by `QCEDL.NET/Core/EdlManager.cs`** (namespace `Qualcomm.EmergencyDownload.Core`). Every CLI command and every GUI operation goes through it:

1. `EdlManager` discovers the device (Windows COM port via `ComPortGuid`, or cross-platform via `LibUsbDotNet` matched by VID/PID — defaults `0x05C6` / `0x9008|0x900E`). Serial-backend auto-discovery has per-OS paths: `FindDeviceLinuxSerial` (sysfs `/sys/class/tty` + idVendor/idProduct match), `FindDeviceWindowsSerial` (`ComPortGuid` enumeration), `FindDeviceMacOSSerial` (`/dev/tty.usbserial-*` enumeration — macOS has no sysfs equivalent, so VID/PID is not verified; users with multiple adapters should pass `--serial-device`).
2. Opens `QualcommSerial` (`QCEDL.NET/Transport/`).
3. Runs the Sahara handshake via `QualcommSahara` (`QCEDL.NET/Layers/PBL/Sahara/`). The loader file passed via `--loader` can be a raw `.elf`/`.melf` programmer **or** a `qsahara_device_programmer.xml` — `SaharaConfigParser.cs` selects the right ELF for the detected chip (HWID / PK hash in `ChipInfo/`).
4. Switches to Firehose (`QualcommFirehose` in `QCEDL.NET/Layers/APSS/Firehose/`), configures memory type (UFS/eMMC/SPINOR/NVMe/…), reads storage geometry, then hands an `IStorageBackend` to the caller.

**`EdlOptions` is the POCO that drives `EdlManager`.** Originally `EdlManager` consumed a `GlobalOptionsBinder` (coupled to `System.CommandLine`); we extracted `EdlOptions` so the GUI can construct a manager directly. The CLI binder is now `BinderBase<EdlOptions>` and projects `System.CommandLine` parse results onto the same POCO.

**Direct-mode bypass:** `--hostdev-as-target <path>` (SPI NOR / file) and `--radxa-wos-platform` skip the USB/Sahara/Firehose stack entirely and use `HostDeviceManager` / `RadxaWoSDeviceManager` as the `IStorageBackend`. Callers should never assume USB is live — check `EdlManager.IsDirectMode` / `IsFirehoseMode` before calling protocol-specific helpers.

**Non-destructive device enumeration.** `EdlManager.EnumerateDevices(EdlOptions)` is a *static* discovery entry point — it does **not** open any device, so it is safe to call while a session is active. It returns `DeviceCandidate` records (`QCEDL.NET/Core/DeviceCandidate.cs`) whose `Id` is transport-specific: tty/COM path for serial backends (assignable to `EdlOptions.SerialDevicePath`) or `usb:vid_XXXX,pid_YYYY,bus_N,addr_M` for LibUsb (assignable to `EdlOptions.UsbDeviceId`). Setting `UsbDeviceId` pins a specific bus/address when multiple EDL devices are plugged in; when null, the backend falls back to first-match-by-VID/PID. `QualcommSerial.FindLibUsbDevice` honours the bus/addr filter by enumerating `LibUsbContext.List()` (since `UsbDeviceFinder` can't filter on bus/addr). The GUI surfaces this via `EdlService.EnumerateDevicesAsync` (runs off-thread, does not touch the session lock) and a `DeviceChooserDialog` (see dialog plumbing below). `ConnectionViewModel.ConnectAsync` / `ProbeAsync` call the enumerator up-front and auto-open the chooser when more than one candidate matches, so users are never silently routed to the first-match device. `EdlService.CloneOptions` must mirror every field on `EdlOptions` used for transport selection (`Backend`, `SerialDevicePath`, `UsbDeviceId`) — dropping any of these silently reroutes sessions.

**Transport backend auto-selection.** `EdlOptions.Backend = Auto` resolves to **LibUsb on Windows *and* macOS** and Serial on Linux. macOS picks LibUsb by default because the IOKit-backed `/dev/tty.usbserial-*` path requires root; libusb opens the USB interface directly via IOKit without elevation.

**USB iSerial capture.** Never read an EDL 9008 device's iSerial via a transient `UsbDevice.TryOpen()/Close()` pre-open — on macOS IOKit that open/close cycle perturbs the device state and the subsequent Sahara loader transfer desyncs (symptom: Sahara reads `0x6D783F3C` = `"<?xm"` mid-upload). `QualcommSerial` exposes `UsbSerialNumber` populated from the already-open handle; `EdlManager.CapturePinnedUsbSerial()` and the probe path read from there. The re-enum (Sahara→Firehose) path may still use `TryReadUsbSerial` to disambiguate candidates because that happens post-Sahara.

**Adding a new CLI command:** create `QCEDL.CLI/Commands/FooCommand.cs` exposing `public static Command Create(GlobalOptionsBinder)`, register it in `Program.cs`. The command's `ExecuteAsync` takes `EdlOptions` (from `Qualcomm.EmergencyDownload.Core`, not the binder) — construct `new EdlManager(options)` to obtain a session.

**Multi-step flows live in `QCEDL.NET/Core/*Runner`, not `Commands/`.** `RawProgramRunner`, `DumpRawprogramRunner`, and `ProvisionRunner` are static classes that take an already-constructed `EdlManager` (plus a `progress` callback and a `CancellationToken`) and drive the full operation. The CLI command files are thin shells that parse args, call `CommandExecutor.RunAsync`, and delegate to the runner; `RawProgramViewModel` and `AdvancedViewModel` in the GUI call the same runners. If a CLI command grows beyond arg parsing + a single `EdlManager` call, extract the logic into a `FooRunner` in `QCEDL.NET/Core/` first so the GUI can reuse it.

**Adding a GUI screen:** create a `ViewModel` under `QCEDL.GUI/ViewModels/`, a matching `UserControl` under `Views/`, add a nav entry in `ShellViewModel`, and wire a `DataTemplate` in `MainWindow.axaml`. Share sessions via the single `EdlService` in `QCEDL.GUI/Services/` — it guards calls with a semaphore so the UI can't issue overlapping operations. Long-running ops (read/write/erase) should marshal `progressCallback(long done, long total)` onto the UI thread via `Dispatcher.UIThread.Post` — see `PartitionsViewModel` / `SectorsViewModel` / `RawProgramViewModel` for the pattern. The Overview, Connection, Partitions, Sectors, RawProgram, Advanced (Provision + Reset + Upload-Loader), Logs, and Settings views are all live — no more `PlaceholderViewModel` entries in the nav.

**GUI view-model shape.** Every VM is `public sealed partial class FooViewModel : ViewModelBase` and leans on **`ReactiveUI.SourceGenerators`** (NuGet, v2.6.1, private asset on `QCEDL.GUI.csproj`):

- State fields use `[Reactive] private T _foo;` to generate the `RaiseAndSetIfChanged` property. Only fall back to hand-written `get/set` when the setter has extra side-effects (raising a dependent `CanInteract`, cascading property changes, etc.).
- Commands use `[ReactiveCommand(CanExecute = nameof(_canRun))]` on private methods; the generator strips the `Async` suffix, so `ConnectAsync` → `ConnectCommand`. `_canRun` is an `IObservable<bool>` field (e.g. `this.WhenAnyValue(x => x.IsBusy).Select(b => !b)`). Methods with one parameter become `ReactiveCommand<T, Unit>` — that's how `RawProgramViewModel.RemoveXml(string)` gets wired to a per-row button's `CommandParameter`.
- Constructors end with `LogCommandErrors()` (defined on `ViewModelBase`) — reflection-based, subscribes every `IHandleObservableErrors` property's `ThrownExceptions` to `Logging.Log` as `VmName.CommandName failed: …`. Don't hand-roll per-command exception subscribers unless you need a localized message; two sites (`ConnectionViewModel`'s `Conn_*FailedFormat`, `PartitionsViewModel`'s `Parts_LogScanFailedFormat`) still do.
- XAML uses `Command="{Binding FooCommand}"` — **no `Click=` handlers on non-dialog views**. The only surviving code-behind handlers are dialog-local (`AboutDialog`, `LicenseViewerDialog`, `ConfirmDialog`'s own buttons) and the per-row `CommandParameter` lookup on `RawProgramView` (uses `#Root.((vm:RawProgramViewModel)DataContext).RemoveXmlCommand`).

**Dialog / picker plumbing goes through `ReactiveUI.Interaction`**, not code-behind. VMs declare `public Interaction<TRequest, TResponse> Foo { get; } = new();` and call `await Foo.Handle(...)`. Views register handlers in `OnAttachedToVisualTree` via `QCEDL.GUI/Services/DialogInteractions.cs::DialogBridges.{RegisterPickFile,RegisterSaveFile,RegisterPickFolder,RegisterConfirm,RegisterPickDevice}`, stash the `IDisposable` in a `CompositeDisposable`, and clear it in `OnDetachedFromVisualTree`. Request DTOs (`OpenFileRequest`, `SaveFileRequest`, `OpenFolderRequest`, `ConfirmRequest`, `DeviceChooserRequest`) and shared `FilePickerTypes` live in the same file. Never resolve `TopLevel.GetTopLevel(this)` / `StorageProvider` from a VM — that's the view's job via the bridges. The `DeviceChooserDialog` (`Views/DeviceChooserDialog.axaml` + `DeviceChooserDialogViewModel`) is the canonical device-picker surface — `ConnectionViewModel` uses it through `PickDevice` when `ScanDevicesCommand` surfaces multiple `DeviceCandidate`s.

**Destructive-action confirmations:** `Views/ConfirmDialog` + `ConfirmDialogViewModel` is the single confirm-before-acting surface. VMs don't call `ConfirmDialog.ShowAsync` directly; they `await Confirm.Handle(new ConfirmRequest(...))` and let `DialogBridges.RegisterConfirm` resolve the owner window. Passing `RequiredConfirmation` on the request gates the confirm button behind a typed-string match — used for `erase-part` (type the partition name), `write-sector` (type `WRITE`), `erase-sector` (type `ERASE`), `provision` (type `PROVISION`), `rawprogram` exec (type `FLASH`). Don't invent a second confirm control; add new variants by passing different `ConfirmRequest` fields.

**Firehose commands vs. XML layer:** `QualcommFirehoseCommands.cs` is the strongly-typed command surface; `Layers/APSS/Firehose/Xml/` contains the on-wire XML element types (serialized via `System.Xml`) and `JSON/StorageInfo/` parses the storage-info blob. `StorageType` enum lives under `Xml/Elements` — this is what `--memory` binds to.

**Partition table:** `QCEDL.NET/PartitionTable/GPT*.cs` is a standalone GPT parser used by `printgpt`, `read-part`, `write-part`, `dump-rawprogram` (CLI) and the Partitions view (GUI). It works off an `IStorageBackend`, so it's reused across Firehose and direct-mode paths.

**Logging is a single sink shared by CLI and GUI.** `QCEDL.NET/Helpers/Logging.cs` (namespace `Qualcomm.EmergencyDownload.Helpers`) owns both a console writer (toggleable via `Logging.ConsoleSinkEnabled`) and a `LogEmitted` event. The GUI subscribes an `ObservableLogSink` (`QCEDL.GUI/Services/`) to stream entries into the Logs view on the UI thread, and disables the console sink on startup. `QCEDL.NET/Logging/LibraryLogger` forwards protocol-layer logs into the same pipeline via `LibraryLogger.LogAction`.

## GUI design system

The GUI's look is a Claude-inspired warm theme derived directly from `DESIGN.md`. It ships no third-party theme pack.

- `QCEDL.GUI/Themes/Tokens.axaml` — raw palette, semantic brushes, typography families, radii (4→32px scale), spacing scale, and `BoxShadows` for the whisper shadow layer.
- `QCEDL.GUI/Themes/Controls.axaml` — **ControlThemes written from scratch** (no `FluentTheme`) for `Button`, `ToggleButton`, `RepeatButton`, `TextBox`, `CheckBox`, `ComboBox` + `ComboBoxItem`, `ListBox` + `ListBoxItem`, `ItemsControl`, `ContentControl`, `Thumb`, `ScrollBar`, `ScrollViewer`, `ProgressBar`, `Separator`, `ToolTip`. Templates follow Avalonia's `PART_*` contract.
- `QCEDL.GUI/Themes/Styles.axaml` — class-based variants layered on the ControlThemes: typography classes (`display`, `section`, `h1`–`h3`, `feature`, `bodySerif`, `bodyLarge`, `body`, `caption`, `label`, `overline`, `mono`, colour modifiers), button variants (`primary` = Terracotta CTA, `dark`, `ghost`, `danger`, `small`, `link`), card variants (`elevated` with whisper shadow, `hero`, `flat`, `dark`), plus `chip` and `nav`.
- `App.axaml` loads them in order tokens → controls → styles; resource resolution depends on that order.

When adding a new control, write its `ControlTheme` in `Controls.axaml` (not views) and only use `DynamicResource` brushes/radii — never inline hex literals. If you need a new visual variant of an existing control, add a class-selector rule to `Styles.axaml`.

**Dark mode.** `Tokens.axaml` splits semantic brushes into Avalonia `ResourceDictionary.ThemeDictionaries` keyed `Light` / `Dark`; accent brushes (`BrushTerracotta`, `BrushCoral`, `BrushError`, `BrushFocus`, `BrushTextInverse`, `BrushTextOnDark`) plus fonts/radii/spacing live outside the theme dictionaries because they're theme-invariant. New brushes must be declared in **both** `Light` and `Dark` dictionaries or they'll resolve to `null` under one variant. `QCEDL.GUI/Services/ThemeService.cs` is the singleton that owns the app-wide `RequestedThemeVariant` (System/Light/Dark → `ThemeVariant.Default`/`Light`/`Dark`); setting it triggers every `DynamicResource` to re-resolve automatically, so no imperative resource-swap code is needed. `App.OnFrameworkInitializationCompleted` calls `ThemeService.Instance.Apply(GuiSettings.ResolveStartupTheme(…))` before building any view so first paint matches the persisted choice. `SettingsViewModel.SelectedTheme` is the picker binding — it calls `ThemeService.Instance.Apply(value)` and persists via `GuiSettings.Current.Theme`.

## GUI localization, fonts, settings

The GUI is localized and runtime-switchable between English, Simplified Chinese, Traditional Chinese, and Japanese.

- `QCEDL.GUI/Resource/Strings.resx` (+ `.zh-Hans.resx`, `.zh-Hant.resx`, `.ja.resx`) are the string tables. They're auto-included as `EmbeddedResource` by the SDK — don't list them in the csproj. The base resource lives at `QCEDL.GUI.Resource.Strings`.
- `QCEDL.GUI/Services/Localizer.cs` is a singleton (`Localizer.Instance`) that wraps the `ResourceManager` and exposes an indexer `this[key]`. Setting `Culture` raises `PropertyChanged("Item[]")` so every live binding re-resolves, and fires a `CultureChanged` event for imperative consumers (e.g. the font swapper).
- `QCEDL.GUI/Markup/LocalizeExtension.cs` is the XAML side: `Text="{l:Localize Nav_Overview}"` compiles to a `OneWay` `Binding` against `Localizer.Instance` with path `[Nav_Overview]`. Prefer this over ad-hoc `{Binding [Key], Source=…}`.
- `QCEDL.GUI/Services/FontTheme.cs` picks the primary `FontSerif` / `FontSans` / `FontMono` family chain based on the active culture and assigns them to `Application.Current.Resources`. This is needed because Avalonia's per-glyph fallback inside a single `FontFamily` does not pick a CJK *serif* face — the primary family has to be culture-specific. `App.axaml.cs` calls `FontTheme.Apply` on startup and re-runs it on every `CultureChanged`.
- `QCEDL.GUI/Services/GuiSettings.cs` persists preferences as JSON under `%APPDATA%/edl-ng/gui-settings.json` (or the platform equivalent). The model carries `culture`, `logLevel`, `theme`, `loaderPath`, `vidHex`, `pidHex`, `memoryType`, and `backend`. Callers read/write through the shared `GuiSettings.Current` instance and call `GuiSettings.Save()` (no-arg overload) so partial updates never clobber unrelated fields. `ResolveStartupCulture` falls back persisted → OS UI culture (exact match, then two-letter language) → `en`; `ResolveStartupLogLevel` falls back persisted → `Info`; `ResolveStartupTheme` falls back persisted → `AppTheme.System`. Load happens in `App.OnFrameworkInitializationCompleted` **before** any view is built so initial bindings see the right language, log verbosity, and theme.
- `SettingsViewModel` + `SettingsView` expose language, theme, and log-level pickers; changing any writes the value into `GuiSettings.Current` and saves. `ConnectionViewModel` seeds loader/VID/PID/memory/backend from the same model on construction and persists them on every `ApplyOptions` (invoked by Connect / Probe).

When adding a new user-facing string: add it to `Strings.resx` with an English value, then to each translated `.resx`, and reference it in XAML via `{l:Localize KeyName}` or in code via `Localizer.Instance["KeyName"]`. Don't hard-code user-visible literals in views.

## GUI menus, About, and bundled licenses

- `MainWindow.axaml` declares a `NativeMenu` (View / Device / Help) that renders as the macOS app menu and as an in-window menu bar on other platforms. `App.axaml` carries a parallel top-level NativeMenu so the About item is reachable before any window exists. Bind nav menu items via `ShellViewModel.NavigateCommand` (takes a `Nav_*` key) and jump-to-Advanced items via `GoAdvancedCommand`. The same window also declares `<Window.KeyBindings>` mirroring the menu gestures (Ctrl+1…7 for nav, Ctrl+, for Settings, F1 for docs) so shortcuts work on Windows/Linux where NativeMenu accelerators aren't wired — on macOS the Cmd+N NativeMenu entries handle it and the Ctrl+N bindings are harmless duplicates.
- `QCEDL.GUI/Services/AppCommands.cs` exposes static `ICommand`s (`OpenDocs`, `ShowAbout`) used by NativeMenu entries that can't bind through the shell DataContext. Use these for any new menu item that needs to run without a view-model path (e.g. `Command="{x:Static svc:AppCommands.ShowAbout}"`).
- `AboutDialog` + `LicenseViewerDialog` (`Views/AboutDialog.axaml`, `Views/LicenseViewerDialog.axaml`) show project info and per-dependency license viewers. The dependency list lives in `AboutDialogViewModel`; each entry's `LicenseKey` must match a file under `QCEDL.GUI/Assets/Licenses/<key>.txt`.
- License texts are shipped as `AvaloniaResource` and loaded via `LicenseTexts.Load(key)` (`avares://qcedl-gui/Assets/Licenses/<key>.txt`). The project's own `LICENSE` is linked into that folder as `edl-ng.txt` from the csproj. To add a dependency, drop its license file under `Assets/Licenses/`, add a `DependencyInfo` row in `AboutDialogViewModel`, and leave the csproj alone — `Include="Assets\**"` picks it up.
- `ConfirmDialog.ShowAsync` has optional `linkText`/`linkUrl`/`showCancel` parameters for info-style dialogs with a hyperlink and no Cancel button. Keep destructive-action uses on the default signature.

## GUI implementation tracker

`gui-todos.md` at the repo root is a living tracker — CLI feature inventory, GUI screen mapping, per-capability status, and phased rollout. Update the Status column when finishing work on a capability; don't let it drift behind the code.

## Conventions worth knowing

- Target is `net9.0` everywhere; `ImplicitUsings` is on — don't add redundant `using System;` etc.
- Solution uses the newer `.slnx` format (edit with Rider/VS 2022 17.10+; `dotnet` handles it fine).
- Namespaces follow `Qualcomm.EmergencyDownload.*` in `QCEDL.NET`, `QCEDL.CLI.*` in the CLI, and `QCEDL.GUI.*` in the GUI — match existing neighbors.
- `IDE0058` / `IDE0130` / `CS1591` are downgraded to warnings intentionally; everything else is an error.
- GUI project additionally tolerates `IDE0021`, `IDE0022`, `IDE0044`, `IDE0090`, `IDE0290` locally.
- Avalonia views must use compiled bindings (`x:DataType` on every `DataTemplate` / root). `AvaloniaUseCompiledBindingsByDefault=true` is on in `QCEDL.GUI.csproj`.

## Nix

`flake.nix` exposes `packages.<system>.edl-ng` (derivation in `pkgs/edl-ng/`) and a devShell. Use `nix build` for a reproducible CLI build; the CI workflow `nix-build.yml` gates this on Linux only (macOS runners were removed — see commit `acb89a0`). The GUI is not yet packaged through Nix.
