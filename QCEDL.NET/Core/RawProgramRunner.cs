using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Qualcomm.EmergencyDownload.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;

namespace Qualcomm.EmergencyDownload.Core;

/// <summary>
/// Shared orchestration for the <c>rawprogram</c> flow: resolves XML file patterns, walks
/// <c>&lt;program&gt;</c> and <c>&lt;patch&gt;</c> elements per LUN, and streams image files
/// through <see cref="EdlManager.WriteSectorsFromStreamAsync"/>. Used by both the CLI
/// command and the GUI RawProgram view.
/// </summary>
public static class RawProgramRunner
{
    /// <summary>A single program entry's progress update.</summary>
    public sealed record RawProgramProgress(
        int ProgramIndex,
        int ProgramCount,
        string Label,
        string Filename,
        long BytesDone,
        long BytesTotal,
        double ElapsedSeconds);

    /// <summary>Per-file resolution of the supplied patterns into rawprogramN.xml / patchN.xml by LUN.</summary>
    public sealed record ResolvedRawProgramFiles(
        IReadOnlyList<FileInfo> AllFiles,
        IReadOnlyDictionary<int, FileInfo> RawProgramByLun,
        IReadOnlyDictionary<int, FileInfo> PatchByLun);

    private static readonly Regex RawProgramRegex = new(@"^rawprogram\w*?(\d+)\.xml$", RegexOptions.IgnoreCase);
    private static readonly Regex PatchRegex = new(@"^patch\w*?(\d+)\.xml$", RegexOptions.IgnoreCase);

    /// <summary>Expand the given glob-style patterns (absolute or CWD-relative) into concrete files.</summary>
    public static IReadOnlyList<FileInfo> ResolveXmlFiles(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var resolved = new List<FileInfo>();
        var cwd = Environment.CurrentDirectory;

        foreach (var pattern in patterns)
        {
            var dirName = Path.GetDirectoryName(pattern);
            var fileNamePattern = Path.GetFileName(pattern);
            var searchDir = string.IsNullOrEmpty(dirName) ? cwd : Path.GetFullPath(Path.Combine(cwd, dirName));

            if (!Directory.Exists(searchDir))
            {
                Logging.Log($"Directory '{searchDir}' for pattern '{pattern}' not found.", LogLevel.Warning);
                continue;
            }

            var found = Directory.EnumerateFiles(searchDir, fileNamePattern, SearchOption.TopDirectoryOnly).ToList();
            if (found.Count == 0)
            {
                var literal = new FileInfo(Path.Combine(searchDir, fileNamePattern));
                if (literal.Exists)
                {
                    resolved.Add(literal);
                }
                else
                {
                    Logging.Log($"No files matching pattern '{pattern}' in '{searchDir}'.", LogLevel.Warning);
                }
            }
            else
            {
                resolved.AddRange(found.Select(f => new FileInfo(f)));
            }
        }

        return [.. resolved.DistinctBy(f => f.FullName)];
    }

    /// <summary>Group already-resolved files into rawprogramN / patchN maps keyed by LUN.</summary>
    public static ResolvedRawProgramFiles GroupByLun(IReadOnlyList<FileInfo> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var rawMap = new Dictionary<int, FileInfo>();
        var patchMap = new Dictionary<int, FileInfo>();

        foreach (var file in files)
        {
            var rawMatch = RawProgramRegex.Match(file.Name);
            if (rawMatch.Success && int.TryParse(rawMatch.Groups[1].Value, out var lun))
            {
                if (!rawMap.TryAdd(lun, file))
                {
                    Logging.Log($"Duplicate rawprogram file for LUN {lun}: {file.Name} and {rawMap[lun].Name}. Using first.", LogLevel.Warning);
                }
                continue;
            }

            var patchMatch = PatchRegex.Match(file.Name);
            if (patchMatch.Success && int.TryParse(patchMatch.Groups[1].Value, out var patchLun))
            {
                if (!patchMap.TryAdd(patchLun, file))
                {
                    Logging.Log($"Duplicate patch file for LUN {patchLun}: {file.Name} and {patchMap[patchLun].Name}. Using first.", LogLevel.Warning);
                }
                continue;
            }

            Logging.Log($"Skipping file with unrecognized name format: {file.Name}.", LogLevel.Warning);
        }

        return new ResolvedRawProgramFiles(files, rawMap, patchMap);
    }

    /// <summary>
    /// Execute rawprogramN.xml + patchN.xml against <paramref name="manager"/>. Callers control how
    /// progress surfaces via <paramref name="progress"/>.
    /// </summary>
    public static async Task<int> RunAsync(
        EdlManager manager,
        ResolvedRawProgramFiles files,
        Action<RawProgramProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(files);

        if (files.RawProgramByLun.Count == 0)
        {
            Logging.Log("No rawprogramN.xml files found to process.", LogLevel.Error);
            return 1;
        }

        if (manager.IsDirectMode)
        {
            var modeLabel = manager.IsHostDeviceMode ? "host device" : "Radxa WoS platform";
            Logging.Log($"Operating in direct-access mode: {modeLabel}", LogLevel.Info);
            if (files.RawProgramByLun.Count > 1)
            {
                Logging.Log($"Multiple LUN files found but direct mode only supports one physical target. Continuing on the same device.", LogLevel.Warning);
            }
        }
        else
        {
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();
        }

        foreach (var lunKey in files.RawProgramByLun.Keys.OrderBy(k => k))
        {
            ct.ThrowIfCancellationRequested();
            var rawFile = files.RawProgramByLun[lunKey];
            Logging.Log($"--- Processing LUN {lunKey} using {rawFile.Name} ---");

            XDocument rawDoc;
            try
            {
                rawDoc = XDocument.Load(rawFile.FullName);
            }
            catch (Exception ex)
            {
                Logging.Log($"Error parsing '{rawFile.FullName}': {ex.Message}", LogLevel.Error);
                return 1;
            }

            if (rawDoc.Root is null || rawDoc.Root.Name != "data")
            {
                Logging.Log($"Invalid XML structure in '{rawFile.FullName}': root must be <data>.", LogLevel.Error);
                return 1;
            }

            var programs = rawDoc.Root.Elements("program").ToList();
            var rc = await ProcessProgramsAsync(manager, programs, rawFile, progress, ct);
            if (rc != 0)
            {
                return rc;
            }

            if (files.PatchByLun.TryGetValue(lunKey, out var patchFile))
            {
                Logging.Log($"--- Patching LUN {lunKey} using {patchFile.Name} ---");
                var pc = await ProcessPatchFileAsync(manager, patchFile, lunKey, ct);
                if (pc != 0)
                {
                    return pc;
                }
            }
            else
            {
                Logging.Log($"Note: patch{lunKey}.xml not found. Skipping patching for LUN {lunKey}.");
            }
        }

        Logging.Log("rawprogram run finished successfully.");
        return 0;
    }

    private static async Task<int> ProcessProgramsAsync(
        EdlManager manager,
        List<XElement> programs,
        FileInfo rawFile,
        Action<RawProgramProgress>? progress,
        CancellationToken ct)
    {
        var programIndex = 0;
        foreach (var element in programs)
        {
            ct.ThrowIfCancellationRequested();
            programIndex++;
            var filename = element.Attribute("filename")?.Value;
            var label = element.Attribute("label")?.Value ?? "N/A";

            if (string.IsNullOrEmpty(filename))
            {
                continue;
            }

            var startSectorStr = element.Attribute("start_sector")?.Value;
            var sectorSizeStr = element.Attribute("SECTOR_SIZE_IN_BYTES")?.Value;
            var ppnStr = element.Attribute("physical_partition_number")?.Value;

            if (string.IsNullOrEmpty(startSectorStr) || string.IsNullOrEmpty(sectorSizeStr) || string.IsNullOrEmpty(ppnStr))
            {
                Logging.Log($"<program> (Label: {label}) in {rawFile.Name} missing required attributes.", LogLevel.Error);
                continue;
            }

            if (!uint.TryParse(sectorSizeStr, out var xmlSectorSize) || xmlSectorSize == 0)
            {
                Logging.Log($"Invalid SECTOR_SIZE_IN_BYTES '{sectorSizeStr}' for <program> '{label}'.", LogLevel.Error);
                continue;
            }

            if (!uint.TryParse(ppnStr, out var targetLun))
            {
                Logging.Log($"Invalid physical_partition_number '{ppnStr}' for <program> '{label}'.", LogLevel.Error);
                continue;
            }

            var effectiveLun = manager.IsDirectMode ? 0u : targetLun;
            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var sectorSize = geometry.SectorSize;
            if (sectorSize != xmlSectorSize)
            {
                Logging.Log($"XML sector size ({xmlSectorSize}) differs from device ({sectorSize}). Using device.", LogLevel.Warning);
            }

            var totalDisk = geometry.TotalSectors ?? 0;
            if (!TryParseSectorExpression(startSectorStr, totalDisk, out var resolvedStart))
            {
                Logging.Log($"Could not parse start_sector '{startSectorStr}' for <program> '{label}'.", LogLevel.Error);
                return 1;
            }

            Stream dataStream;
            long dataLength;
            if (string.Equals(filename, "ZERO", StringComparison.OrdinalIgnoreCase))
            {
                var nSectorsStr = element.Attribute("num_partition_sectors")?.Value;
                if (string.IsNullOrEmpty(nSectorsStr) || !ulong.TryParse(nSectorsStr, out var nSectors))
                {
                    Logging.Log("ZERO file requires num_partition_sectors.", LogLevel.Error);
                    return 1;
                }
                dataLength = (long)(nSectors * sectorSize);
                dataStream = new ZeroFillStream(dataLength);
            }
            else
            {
                var imageFile = new FileInfo(Path.Combine(rawFile.DirectoryName ?? string.Empty, filename));
                if (!imageFile.Exists)
                {
                    Logging.Log($"Image '{imageFile.FullName}' (Label: {label}) not found.", LogLevel.Error);
                    continue;
                }
                if (imageFile.Length == 0)
                {
                    Logging.Log($"Image '{imageFile.FullName}' (Label: {label}) is empty. Skipping.", LogLevel.Warning);
                    continue;
                }
                dataLength = imageFile.Length;
                dataStream = imageFile.OpenRead();
            }

            await using var stream = dataStream;
            var programCount = programs.Count;
            var sw = Stopwatch.StartNew();
            var capturedIndex = programIndex;
            var capturedLabel = label;
            var capturedFilename = filename;

            void Report(long cur, long tot)
            {
                progress?.Invoke(new RawProgramProgress(
                    capturedIndex, programCount, capturedLabel, capturedFilename,
                    cur, tot, sw.Elapsed.TotalSeconds));
            }

            try
            {
                await manager.WriteSectorsFromStreamAsync(
                    effectiveLun,
                    resolvedStart,
                    stream,
                    dataLength,
                    padToSector: true,
                    filename,
                    Report);
                sw.Stop();
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logging.Log($"Error writing '{filename}' (Label: {label}): {ex.Message}", LogLevel.Error);
                return 1;
            }

            Logging.Log($"Programmed '{filename}' ({label}) in {sw.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }

        return 0;
    }

    private static async Task<int> ProcessPatchFileAsync(
        EdlManager manager,
        FileInfo patchFile,
        int lunKey,
        CancellationToken ct)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(patchFile.FullName);
        }
        catch (Exception ex)
        {
            Logging.Log($"Error parsing '{patchFile.FullName}': {ex.Message}", LogLevel.Error);
            return 1;
        }

        if (doc.Root is null || doc.Root.Name != "patches")
        {
            Logging.Log($"Invalid XML structure in '{patchFile.FullName}': root must be <patches>.", LogLevel.Error);
            return 1;
        }

        var patches = doc.Root.Elements("patch").ToList();
        var index = 0;
        foreach (var patchEl in patches)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            try
            {
                if (manager.IsDirectMode)
                {
                    var startSector = patchEl.Attribute("start_sector")?.Value;
                    var byteOffsetStr = patchEl.Attribute("byte_offset")?.Value;
                    var sizeStr = patchEl.Attribute("size_in_bytes")?.Value;
                    var value = patchEl.Attribute("value")?.Value;
                    var filename = patchEl.Attribute("filename")?.Value ?? "DISK";

                    if (string.IsNullOrEmpty(startSector) || string.IsNullOrEmpty(byteOffsetStr) ||
                        string.IsNullOrEmpty(sizeStr) || string.IsNullOrEmpty(value))
                    {
                        Logging.Log($"Patch {index} missing attributes.", LogLevel.Error);
                        return 1;
                    }

                    if (!uint.TryParse(byteOffsetStr, out var byteOffset) ||
                        !uint.TryParse(sizeStr, out var sizeInBytes))
                    {
                        Logging.Log($"Patch {index} invalid byte_offset/size.", LogLevel.Error);
                        return 1;
                    }

                    await manager.ApplyPatchAsync(startSector, byteOffset, sizeInBytes, value, filename)
                        ;
                }
                else
                {
                    var payload = $"<?xml version=\"1.0\" ?><data>{patchEl.ToString(SaveOptions.DisableFormatting)}</data>";
                    var ok = await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(payload), ct)
                        ;
                    if (!ok)
                    {
                        Logging.Log($"Patch {index} NAK/failed.", LogLevel.Error);
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Error applying patch {index}: {ex.Message}", LogLevel.Error);
                return 1;
            }
        }

        Logging.Log($"Patching for LUN {lunKey} using {patchFile.Name} completed.", LogLevel.Debug);
        return 0;
    }

    /// <summary>
    /// Parse sector expressions like <c>"123"</c>, <c>"NUM_DISK_SECTORS"</c>,
    /// <c>"NUM_DISK_SECTORS-5."</c>. Public so the GUI can pre-validate inputs.
    /// </summary>
    public static bool TryParseSectorExpression(string expression, ulong totalDiskSectorsForLun, out ulong resultSector)
    {
        resultSector = 0;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmed = expression.Trim();

        if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
        {
            return true;
        }
        if (trimmed.EndsWith('.') && ulong.TryParse(trimmed.AsSpan(0, trimmed.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
        {
            return true;
        }

        if (!trimmed.Contains("NUM_DISK_SECTORS"))
        {
            return false;
        }

        if (totalDiskSectorsForLun == 0)
        {
            Logging.Log("Cannot resolve NUM_DISK_SECTORS: total disk sectors unknown.", LogLevel.Error);
            return false;
        }

        var match = Regex.Match(trimmed, @"^\s*NUM_DISK_SECTORS\s*(?:([+-])\s*(\d+))?\s*\.?\s*$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            Logging.Log($"Unsupported NUM_DISK_SECTORS expression: '{trimmed}'.", LogLevel.Error);
            return false;
        }

        resultSector = totalDiskSectorsForLun;
        if (match.Groups[1].Success && match.Groups[2].Success)
        {
            var op = match.Groups[1].Value;
            if (!ulong.TryParse(match.Groups[2].Value, out var operand))
            {
                return false;
            }
            if (op == "-")
            {
                if (resultSector < operand)
                {
                    Logging.Log($"NUM_DISK_SECTORS ({resultSector}) - {operand} is negative.", LogLevel.Error);
                    return false;
                }
                resultSector -= operand;
            }
            else
            {
                resultSector += operand;
            }
        }
        return true;
    }

    private sealed class ZeroFillStream : Stream
    {
        private readonly long _length;
        private long _position;

        public ZeroFillStream(long length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length)
            {
                return 0;
            }
            var toRead = (int)Math.Min(count, _length - _position);
            Array.Clear(buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}