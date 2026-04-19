using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Helpers;

namespace Qualcomm.EmergencyDownload.Core;

/// <summary>
/// Shared orchestration for the <c>dump-rawprogram</c> flow. Reads the GPT of a LUN,
/// writes rawprogramN.xml + patchN.xml, and optionally dumps every partition to disk.
/// </summary>
public static class DumpRawprogramRunner
{
    public sealed record DumpProgress(
        string PartitionName,
        int PartitionIndex,
        int PartitionCount,
        long BytesDone,
        long BytesTotal,
        double ElapsedSeconds);

    public static async Task<int> RunAsync(
        EdlManager manager,
        DirectoryInfo dumpSaveDir,
        uint lun,
        bool genXmlOnly,
        IReadOnlySet<string> skipPartitions,
        Action<DumpProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(dumpSaveDir);
        ArgumentNullException.ThrowIfNull(skipPartitions);

        var effectiveLun = manager.IsDirectMode ? 0u : lun;
        var targetDescription = manager.GetTargetDescription(effectiveLun);

        var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
        var sectorSize = geometry.SectorSize;
        var totalBlocks = geometry.TotalSectors ?? 0;

        Logging.Log($"Using sector size: {sectorSize} bytes for {targetDescription}.", LogLevel.Debug);
        if (totalBlocks == 0)
        {
            Logging.Log("Total block count unavailable; backup GPT calculations may be incomplete.", LogLevel.Warning);
        }

        dumpSaveDir.Create();

        Logging.Log($"Reading GPT from {targetDescription}...");
        byte[] gptData;
        try
        {
            gptData = await manager.ReadSectorsAsync(effectiveLun, 0, 64);
        }
        catch (Exception ex)
        {
            Logging.Log($"Failed to read GPT area: {ex.Message}", LogLevel.Error);
            return 1;
        }

        if (gptData is null || gptData.Length < sectorSize * 2)
        {
            Logging.Log($"Failed to read sufficient data for GPT from LUN {lun}.", LogLevel.Error);
            return 1;
        }

        Gpt? gpt;
        using (var stream = new MemoryStream(gptData))
        {
            try
            {
                gpt = Gpt.ReadFromStream(stream, (int)sectorSize);
            }
            catch (InvalidDataException)
            {
                Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Error);
                return 1;
            }
        }
        if (gpt is null)
        {
            Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Error);
            return 1;
        }

        var mainGptSectors = gpt.Header.FirstUsableLBA;
        var backupGptSectors = totalBlocks > 0 ? totalBlocks - gpt.Header.LastUsableLBA - 1 : 0;

        var mainGptFileName = $"gpt_main{lun}.bin";
        var mainGptPath = Path.Combine(dumpSaveDir.FullName, mainGptFileName);
        byte[]? mainGptDataOut = null;
        if (mainGptSectors is > 0 and <= uint.MaxValue)
        {
            try
            {
                mainGptDataOut = await manager.ReadSectorsAsync(effectiveLun, 0, (uint)mainGptSectors);
                if (mainGptDataOut.Length >= sectorSize)
                {
                    await File.WriteAllBytesAsync(mainGptPath, mainGptDataOut, ct);
                    Logging.Log($"Saved main GPT to '{mainGptPath}' ({mainGptSectors} sectors)", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Error reading/saving main GPT: {ex.Message}", LogLevel.Error);
                return 1;
            }
        }

        byte[]? backupGptDataOut = null;
        var backupGptFileName = $"gpt_backup{lun}.bin";
        var backupGptPath = Path.Combine(dumpSaveDir.FullName, backupGptFileName);
        if (backupGptSectors > 0 && gpt.Header.LastUsableLBA + 1 < totalBlocks && backupGptSectors <= uint.MaxValue)
        {
            try
            {
                backupGptDataOut = await manager.ReadSectorsAsync(effectiveLun, gpt.Header.LastUsableLBA + 1, (uint)backupGptSectors);
                if (backupGptDataOut.Length >= sectorSize)
                {
                    await File.WriteAllBytesAsync(backupGptPath, backupGptDataOut, ct);
                    Logging.Log($"Saved backup GPT to '{backupGptPath}' ({backupGptSectors} sectors)", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Error reading/saving backup GPT: {ex.Message}", LogLevel.Warning);
            }
        }

        var partitions = gpt.Partitions.Where(p => !string.IsNullOrWhiteSpace(p.GetName().TrimEnd('\0'))).ToList();
        Logging.Log($"Found {partitions.Count} partitions on LUN {lun}.", LogLevel.Info);

        var rawprogramDoc = new XDocument(new XElement("data",
            new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))));
        rawprogramDoc.Root!.Add(new XElement("program",
            new XAttribute("filename", mainGptFileName),
            new XAttribute("label", "PrimaryGPT"),
            new XAttribute("start_sector", "0"),
            new XAttribute("num_partition_sectors", mainGptSectors.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))));
        if (backupGptDataOut != null && backupGptSectors > 0)
        {
            rawprogramDoc.Root.Add(new XElement("program",
                new XAttribute("filename", backupGptFileName),
                new XAttribute("label", "BackupGPT"),
                new XAttribute("start_sector", $"NUM_DISK_SECTORS-{backupGptSectors}."),
                new XAttribute("num_partition_sectors", backupGptSectors.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))));
        }
        foreach (var p in partitions)
        {
            var name = p.GetName().TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(name) || p.FirstLBA > uint.MaxValue || p.LastLBA > uint.MaxValue)
            {
                continue;
            }
            var n = p.LastLBA - p.FirstLBA + 1;
            rawprogramDoc.Root.Add(new XElement("program",
                new XAttribute("filename", CreateSafeFileName(name)),
                new XAttribute("label", name),
                new XAttribute("start_sector", p.FirstLBA.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("num_partition_sectors", n.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))));
        }

        var rawprogramXmlPath = Path.Combine(dumpSaveDir.FullName, $"rawprogram{lun}.xml");
        try
        {
            rawprogramDoc.Save(rawprogramXmlPath);
            Logging.Log($"Generated rawprogram XML: '{rawprogramXmlPath}'");
        }
        catch (Exception ex)
        {
            Logging.Log($"Error saving rawprogram XML: {ex.Message}", LogLevel.Error);
            return 1;
        }

        var patchXmlPath = Path.Combine(dumpSaveDir.FullName, $"patch{lun}.xml");
        try
        {
            GeneratePatchXml(lun, sectorSize, mainGptFileName, backupGptFileName).Save(patchXmlPath);
            Logging.Log($"Generated patch XML: '{patchXmlPath}'");
        }
        catch (Exception ex)
        {
            Logging.Log($"Error saving patch XML: {ex.Message}", LogLevel.Error);
            return 1;
        }

        if (genXmlOnly)
        {
            Logging.Log("--gen-xml-only set; skipping partition dump.");
            return 0;
        }

        var partitionIndex = 0;
        var partitionCount = partitions.Count;
        foreach (var part in partitions)
        {
            ct.ThrowIfCancellationRequested();
            partitionIndex++;
            var name = part.GetName().TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            if (skipPartitions.Contains(name))
            {
                Logging.Log($"Skipping '{name}' as requested.", LogLevel.Info);
                continue;
            }

            var numSectors = part.LastLBA - part.FirstLBA + 1;
            if (numSectors == 0)
            {
                continue;
            }

            var safeName = CreateSafeFileName(name);
            var partPath = Path.Combine(dumpSaveDir.FullName, safeName);

            Logging.Log($"Dumping '{name}' ({numSectors} sectors) to '{partPath}'...", LogLevel.Debug);

            var sw = Stopwatch.StartNew();
            var capturedName = name;
            var capturedIndex = partitionIndex;
            void Report(long cur, long tot)
            {
                progress?.Invoke(new DumpProgress(capturedName, capturedIndex, partitionCount, cur, tot, sw.Elapsed.TotalSeconds));
            }

            try
            {
                await using var fs = File.Open(partPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await manager.ReadSectorsToStreamAsync(effectiveLun, part.FirstLBA, numSectors, fs, Report);
                sw.Stop();
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logging.Log($"Error dumping '{name}': {ex.Message}", LogLevel.Error);
                TryDeleteFile(partPath);
                continue;
            }
        }

        Logging.Log($"Dump completed in '{dumpSaveDir.FullName}'.");
        return 0;
    }

    public static string CreateSafeFileName(string partitionName)
    {
        var safe = partitionName;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }
        if (string.IsNullOrWhiteSpace(safe) || safe.StartsWith('.'))
        {
            safe = "partition_" + safe;
        }
        if (!safe.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            safe += ".bin";
        }
        return safe;
    }

    private static XDocument GeneratePatchXml(uint lun, uint sectorSize, string mainGptFileName, string backupGptFileName)
    {
        var patches = new XElement("patches");
        var doc = new XDocument(patches);
        AddPatch(patches, "1", 48, lun, 8, "NUM_DISK_SECTORS-6.", mainGptFileName, sectorSize, "Update Primary Header with LastUseableLBA.");
        AddPatch(patches, "1", 48, lun, 8, "NUM_DISK_SECTORS-6.", "DISK", sectorSize, "Update Primary Header with LastUseableLBA.");
        AddPatch(patches, "4", 48, lun, 8, "NUM_DISK_SECTORS-6.", backupGptFileName, sectorSize, "Update Backup Header with LastUseableLBA.");
        AddPatch(patches, "NUM_DISK_SECTORS-1.", 48, lun, 8, "NUM_DISK_SECTORS-6.", "DISK", sectorSize, "Update Backup Header with LastUseableLBA.");
        AddPatch(patches, "1", 32, lun, 8, "NUM_DISK_SECTORS-1.", mainGptFileName, sectorSize, "Update Primary Header with BackupGPT Header Location.");
        AddPatch(patches, "1", 32, lun, 8, "NUM_DISK_SECTORS-1.", "DISK", sectorSize, "Update Primary Header with BackupGPT Header Location.");
        AddPatch(patches, "4", 24, lun, 8, "NUM_DISK_SECTORS-1.", backupGptFileName, sectorSize, "Update Backup Header with CurrentLBA.");
        AddPatch(patches, "NUM_DISK_SECTORS-1.", 24, lun, 8, "NUM_DISK_SECTORS-1.", "DISK", sectorSize, "Update Backup Header with CurrentLBA.");
        AddPatch(patches, "4", 72, lun, 8, "NUM_DISK_SECTORS-5.", backupGptFileName, sectorSize, "Update Backup Header with Partition Array Location.");
        AddPatch(patches, "NUM_DISK_SECTORS-1", 72, lun, 8, "NUM_DISK_SECTORS-5.", "DISK", sectorSize, "Update Backup Header with Partition Array Location.");
        AddPatch(patches, "1", 88, lun, 4, "CRC32(2,4096)", mainGptFileName, sectorSize, "Update Primary Header with CRC of Partition Array.");
        AddPatch(patches, "1", 88, lun, 4, "CRC32(2,4096)", "DISK", sectorSize, "Update Primary Header with CRC of Partition Array.");
        AddPatch(patches, "4", 88, lun, 4, "CRC32(0,4096)", backupGptFileName, sectorSize, "Update Backup Header with CRC of Partition Array.");
        AddPatch(patches, "NUM_DISK_SECTORS-1.", 88, lun, 4, "CRC32(NUM_DISK_SECTORS-5.,4096)", "DISK", sectorSize, "Update Backup Header with CRC of Partition Array.");
        AddPatch(patches, "1", 16, lun, 4, "0", mainGptFileName, sectorSize, "Zero Out Header CRC in Primary Header.");
        AddPatch(patches, "1", 16, lun, 4, "CRC32(1,92)", mainGptFileName, sectorSize, "Update Primary Header with CRC of Primary Header.");
        AddPatch(patches, "1", 16, lun, 4, "0", "DISK", sectorSize, "Zero Out Header CRC in Primary Header.");
        AddPatch(patches, "1", 16, lun, 4, "CRC32(1,92)", "DISK", sectorSize, "Update Primary Header with CRC of Primary Header.");
        AddPatch(patches, "4", 16, lun, 4, "0", backupGptFileName, sectorSize, "Zero Out Header CRC in Backup Header.");
        AddPatch(patches, "4", 16, lun, 4, "CRC32(4,92)", backupGptFileName, sectorSize, "Update Backup Header with CRC of Backup Header.");
        AddPatch(patches, "NUM_DISK_SECTORS-1.", 16, lun, 4, "0", "DISK", sectorSize, "Zero Out Header CRC in Backup Header.");
        AddPatch(patches, "NUM_DISK_SECTORS-1.", 16, lun, 4, "CRC32(NUM_DISK_SECTORS-1.,92)", "DISK", sectorSize, "Update Backup Header with CRC of Backup Header.");
        return doc;
    }

    private static void AddPatch(XElement patches, string startSector, uint byteOffset, uint ppn, uint sizeInBytes, string value, string filename, uint sectorSize, string what)
    {
        patches.Add(new XElement("patch",
            new XAttribute("start_sector", startSector),
            new XAttribute("byte_offset", byteOffset.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("physical_partition_number", ppn.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("size_in_bytes", sizeInBytes.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("value", value),
            new XAttribute("filename", filename),
            new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("what", what)));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Could not delete '{path}': {ex.Message}", LogLevel.Warning);
        }
    }
}