using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QCEDL.GUI.Views;
using Qualcomm.EmergencyDownload.Core;
using ReactiveUI;

namespace QCEDL.GUI.Services;

// Shared request DTOs used by view-model interactions. Each view-model owns its
// own Interaction<TIn, TOut> instances; the records here are just the payload
// shapes so we don't duplicate parameter lists across VMs.

public sealed record OpenFileRequest(
    string Title,
    IReadOnlyList<FilePickerFileType> Filters,
    bool AllowMultiple);

public sealed record SaveFileRequest(
    string Title,
    string? SuggestedName,
    string? DefaultExtension);

public sealed record OpenFolderRequest(string Title);

public sealed record ConfirmRequest(
    string Title,
    string Message,
    bool Danger,
    string? RequiredConfirmation = null);

public sealed record DeviceChooserRequest(IReadOnlyList<DeviceCandidate> Candidates);

public static class FilePickerTypes
{
    public static readonly FilePickerFileType AnyFile =
        new("All files") { Patterns = ["*"] };

    public static readonly FilePickerFileType Xml =
        new("XML") { Patterns = ["*.xml"] };

    public static readonly FilePickerFileType FirehoseLoader =
        new("Firehose programmer") { Patterns = ["*.elf", "*.melf", "*.mbn", "*.xml"] };
}

// Bridges view-model Interactions to Avalonia's StorageProvider / ConfirmDialog.
// Views call these from OnAttachedToVisualTree and dispose the returned
// IDisposables on detach so handlers don't outlive the visual tree they resolve
// TopLevel / owner-window against.
public static class DialogBridges
{
    public static IDisposable RegisterPickFile(
        Control view,
        Interaction<OpenFileRequest, IReadOnlyList<string>> interaction) =>
        interaction.RegisterHandler(async ctx =>
        {
            var top = TopLevel.GetTopLevel(view);
            if (top is null)
            {
                ctx.SetOutput([]);
                return;
            }
            var req = ctx.Input;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = req.Title,
                AllowMultiple = req.AllowMultiple,
                FileTypeFilter = req.Filters,
            });
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Cast<string>()
                .ToList();
            ctx.SetOutput(paths);
        });

    public static IDisposable RegisterSaveFile(
        Control view,
        Interaction<SaveFileRequest, string?> interaction) =>
        interaction.RegisterHandler(async ctx =>
        {
            var top = TopLevel.GetTopLevel(view);
            if (top is null)
            {
                ctx.SetOutput(null);
                return;
            }
            var req = ctx.Input;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = req.Title,
                SuggestedFileName = req.SuggestedName,
                DefaultExtension = req.DefaultExtension,
            });
            ctx.SetOutput(file?.TryGetLocalPath());
        });

    public static IDisposable RegisterPickFolder(
        Control view,
        Interaction<OpenFolderRequest, string?> interaction) =>
        interaction.RegisterHandler(async ctx =>
        {
            var top = TopLevel.GetTopLevel(view);
            if (top is null)
            {
                ctx.SetOutput(null);
                return;
            }
            var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = ctx.Input.Title,
                AllowMultiple = false,
            });
            ctx.SetOutput(dirs.Count > 0 ? dirs[0].TryGetLocalPath() : null);
        });

    public static IDisposable RegisterConfirm(
        Control view,
        Interaction<ConfirmRequest, bool> interaction) =>
        interaction.RegisterHandler(async ctx =>
        {
            if (TopLevel.GetTopLevel(view) is not Window owner)
            {
                ctx.SetOutput(false);
                return;
            }
            var req = ctx.Input;
            var ok = await ConfirmDialog.ShowAsync(
                owner, req.Title, req.Message, req.Danger, req.RequiredConfirmation);
            ctx.SetOutput(ok);
        });

    public static IDisposable RegisterPickDevice(
        Control view,
        Interaction<DeviceChooserRequest, DeviceCandidate?> interaction) =>
        interaction.RegisterHandler(async ctx =>
        {
            if (TopLevel.GetTopLevel(view) is not Window owner)
            {
                ctx.SetOutput(null);
                return;
            }
            var chosen = await DeviceChooserDialog.ShowAsync(owner, ctx.Input.Candidates);
            ctx.SetOutput(chosen);
        });
}