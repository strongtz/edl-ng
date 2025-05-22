using System.Xml.Linq;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using QCEDL.NET.Logging;
using System.Text;
using System.Globalization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;

public static class QualcommFirehoseXml
{
    [Obsolete]
    private static string GetEnumXmlName<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var type = typeof(TEnum);
        var memberName = Enum.GetName(type, value);
        if (memberName == null)
        {
            return value.ToString(); // Fallback
        }
        
        // var memberInfo = type.GetMember(memberName).FirstOrDefault();
        // if (memberInfo != null)
        // {
        //     var xmlEnumAttribute = memberInfo.GetCustomAttribute<XmlEnumAttribute>();
        //     if (xmlEnumAttribute != null && !string.IsNullOrEmpty(xmlEnumAttribute.Name))
        //     {
        //         return xmlEnumAttribute.Name;
        //     }
        // }
        return value.ToString(); // Fallback if no attribute or name is empty
    }

    public static string BuildCommandPacket(Data[] dataPayloads)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" ?>");

        foreach (var data in dataPayloads)
        {
            var dataElement = new XElement("data");

            if (data.Configure != null)
            {
                var cfg = data.Configure;
                var configureElement = new XElement("configure");
                if (cfg.ShouldSerializeMemoryName()) configureElement.Add(new XAttribute("MemoryName", cfg.MemoryName.ToStringFast()));
                if (cfg.ShouldSerializeVerbose()) configureElement.Add(new XAttribute("Verbose", cfg.Verbose)); // "0" or "1"
                if (cfg.ShouldSerializeMaxPayloadSizeToTargetInBytes()) configureElement.Add(new XAttribute("MaxPayloadSizeToTargetInBytes", cfg.MaxPayloadSizeToTargetInBytes.ToString(CultureInfo.InvariantCulture)));
                if (cfg.ShouldSerializeAlwaysValidate()) configureElement.Add(new XAttribute("AlwaysValidate", cfg.AlwaysValidate)); // "0" or "1"
                if (cfg.ShouldSerializeMaxDigestTableSizeInBytes()) configureElement.Add(new XAttribute("MaxDigestTableSizeInBytes", cfg.MaxDigestTableSizeInBytes.ToString(CultureInfo.InvariantCulture)));
                if (cfg.ShouldSerializeZlpAwareHost()) configureElement.Add(new XAttribute("ZlpAwareHost", cfg.ZlpAwareHost)); // "0" or "1"
                if (cfg.ShouldSerializeSkipWrite()) configureElement.Add(new XAttribute("SkipWrite", cfg.SkipWrite)); // "0" or "1"
                if (cfg.ShouldSerializeSkipStorageInit()) configureElement.Add(new XAttribute("SkipStorageInit", cfg.SkipStorageInit));
                dataElement.Add(configureElement);
            }
            else if (data.Read != null)
            {
                var read = data.Read;
                var readElement = new XElement("read");
                if (read.ShouldSerializeStorageType()) readElement.Add(new XAttribute("storage_type", read.StorageType.ToStringFast()));
                if (read.ShouldSerializePhysicalPartitionNumber()) readElement.Add(new XAttribute("physical_partition_number", read.PhysicalPartitionNumber.ToString(CultureInfo.InvariantCulture)));
                if (read.ShouldSerializeSlot()) readElement.Add(new XAttribute("slot", read.Slot.ToString(CultureInfo.InvariantCulture)));
                if (read.ShouldSerializeSectorSizeInBytes()) readElement.Add(new XAttribute("SECTOR_SIZE_IN_BYTES", read.SectorSizeInBytes.ToString(CultureInfo.InvariantCulture)));
                if (read.ShouldSerializeStartSector()) readElement.Add(new XAttribute("start_sector", read.StartSector)); // Is a string
                if (read.ShouldSerializeNumPartitionSectors()) readElement.Add(new XAttribute("num_partition_sectors", read.NumPartitionSectors)); // Is a string
                // IOOptionsIODataDevDataMixin specific attributes for Read
                if (read.ShouldSerializeLastSector()) readElement.Add(new XAttribute("last_sector", read.LastSector.ToString(CultureInfo.InvariantCulture)));
                if (read.ShouldSerializeSkipBadBlock()) readElement.Add(new XAttribute("skip_bad_block", read.SkipBadBlock.ToString(CultureInfo.InvariantCulture)));
                if (read.ShouldSerializeGetSpare()) readElement.Add(new XAttribute("get_spare", read.GetSpare.ToString(CultureInfo.InvariantCulture)));
                if (read.ShouldSerializeECCDisabled()) readElement.Add(new XAttribute("ecc_disabled", read.ECCDisabled.ToString(CultureInfo.InvariantCulture)));
                dataElement.Add(readElement);
            }
            else if (data.Program != null)
            {
                var prog = data.Program;
                var programElement = new XElement("program");
                if (prog.ShouldSerializeStorageType()) programElement.Add(new XAttribute("storage_type", prog.StorageType.ToStringFast()));
                if (prog.ShouldSerializePhysicalPartitionNumber()) programElement.Add(new XAttribute("physical_partition_number", prog.PhysicalPartitionNumber.ToString(CultureInfo.InvariantCulture)));
                if (prog.ShouldSerializeSlot()) programElement.Add(new XAttribute("slot", prog.Slot.ToString(CultureInfo.InvariantCulture)));
                if (prog.ShouldSerializeSectorSizeInBytes()) programElement.Add(new XAttribute("SECTOR_SIZE_IN_BYTES", prog.SectorSizeInBytes.ToString(CultureInfo.InvariantCulture)));
                if (prog.ShouldSerializeStartSector()) programElement.Add(new XAttribute("start_sector", prog.StartSector)); // Is a string
                if (prog.ShouldSerializeNumPartitionSectors()) programElement.Add(new XAttribute("num_partition_sectors", prog.NumPartitionSectors)); // Is a string
                if (prog.ShouldSerializeFileName()) programElement.Add(new XAttribute("filename", prog.FileName));
                dataElement.Add(programElement);
            }
            else if (data.Erase != null)
            {
                var eraseCmd = data.Erase;
                var eraseElement = new XElement("erase");
                if (eraseCmd.ShouldSerializeStorageType()) eraseElement.Add(new XAttribute("storage_type", eraseCmd.StorageType.ToStringFast()));
                if (eraseCmd.ShouldSerializePhysicalPartitionNumber()) eraseElement.Add(new XAttribute("physical_partition_number", eraseCmd.PhysicalPartitionNumber.ToString(CultureInfo.InvariantCulture)));
                if (eraseCmd.ShouldSerializeSlot()) eraseElement.Add(new XAttribute("slot", eraseCmd.Slot.ToString(CultureInfo.InvariantCulture)));
                if (eraseCmd.ShouldSerializeSectorSizeInBytes()) eraseElement.Add(new XAttribute("SECTOR_SIZE_IN_BYTES", eraseCmd.SectorSizeInBytes.ToString(CultureInfo.InvariantCulture)));
                if (eraseCmd.ShouldSerializeStartSector()) eraseElement.Add(new XAttribute("start_sector", eraseCmd.StartSector)); // Is a string
                if (eraseCmd.ShouldSerializeNumPartitionSectors()) eraseElement.Add(new XAttribute("num_partition_sectors", eraseCmd.NumPartitionSectors)); // Is a string
                dataElement.Add(eraseElement);
            }
            else if (data.Power != null)
            {
                var pwr = data.Power;
                var powerElement = new XElement("power");
                if (pwr.ShouldSerializeValue()) powerElement.Add(new XAttribute("value", pwr.Value.ToStringFast()));
                if (pwr.ShouldSerializeDelayInSeconds()) powerElement.Add(new XAttribute("DelayInSeconds", pwr.DelayInSeconds.ToString(CultureInfo.InvariantCulture)));
                dataElement.Add(powerElement);
            }
            else if (data.GetStorageInfo != null)
            {
                var gsi = data.GetStorageInfo;
                var getStorageInfoElement = new XElement("getstorageinfo");
                if (gsi.ShouldSerializeStorageType()) getStorageInfoElement.Add(new XAttribute("storage_type", gsi.StorageType.ToStringFast()));
                if (gsi.ShouldSerializePhysicalPartitionNumber()) getStorageInfoElement.Add(new XAttribute("physical_partition_number", gsi.PhysicalPartitionNumber.ToString(CultureInfo.InvariantCulture)));
                if (gsi.ShouldSerializeSlot()) getStorageInfoElement.Add(new XAttribute("slot", gsi.Slot.ToString(CultureInfo.InvariantCulture))); // From DevData
                if (gsi.ShouldSerializePrintJson()) getStorageInfoElement.Add(new XAttribute("print_json", gsi.PrintJson.ToString(CultureInfo.InvariantCulture)));
                dataElement.Add(getStorageInfoElement);
            }
            else if (data.Nop != null)
            {
                dataElement.Add(new XElement("nop"));
            }
            // Note: 'Log' and 'Response' elements are typically part of incoming messages, not outgoing commands.
            // If there's a scenario to send them, add them here.

            sb.Append(dataElement.ToString(SaveOptions.DisableFormatting));
        }
        return sb.ToString();
    }

    public static Data[] GetDataPayloads(string commandPacket) // Changed method name for clarity
    {
        var dataList = new List<Data>();

        // Remove XML declaration and control characters, then wrap in a root element to parse fragments.
        var cleanedXml = commandPacket
            .Replace("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>", "")
            .Replace("<?xml version=\"1.0\"?>", ""); // Handle both cases

        // Replace control characters that might break XElement.Parse
        // The original code had: newCommandPacket = newCommandPacket.Replace((char)0x14, ' ');
        // We need a more general way or ensure the input is clean. For now, let's assume 0x14 was the main one.
        cleanedXml = cleanedXml.Replace(((char)0x14).ToString(), " ");


        if (string.IsNullOrWhiteSpace(cleanedXml))
        {
            LibraryLogger.Warning("GetDataPayloads: Input XML string is empty or whitespace after cleaning.");
            return [];
        }

        var wrappedXml = $"<rootWrapper>{cleanedXml}</rootWrapper>";
        XElement rootElement;
        try
        {
            rootElement = XElement.Parse(wrappedXml);
        }
        catch (System.Xml.XmlException ex)
        {
            LibraryLogger.Error($"GetDataPayloads: Failed to parse XML: {ex.Message}. XML content (cleaned): '{cleanedXml}'");
            throw; // Re-throw or return empty array based on desired error handling
        }


        foreach (var dataElement in rootElement.Elements("data"))
        {
            var data = new Data();

            var logElement = dataElement.Element("log");
            if (logElement != null)
            {
                data.Log = new Log { Value = (string?)logElement.Attribute("value") };
            }

            var responseElement = dataElement.Element("response");
            if (responseElement != null)
            {
                data.Response = new Response
                {
                    Value = (string?)responseElement.Attribute("value"),
                    // Assuming "true" or "1" means true for RawMode. Firehose seems to use "true".
                    RawMode = "true".Equals((string?)responseElement.Attribute("rawmode"), StringComparison.OrdinalIgnoreCase) ||
                              "1".Equals((string?)responseElement.Attribute("rawmode"), StringComparison.OrdinalIgnoreCase)
                };
            }

            var configureElement = dataElement.Element("configure");
            if (configureElement != null)
            {
                var configure = new Configure();
                var memoryNameStr = (string?)configureElement.Attribute("MemoryName");
                if (memoryNameStr != null && Enum.TryParse<StorageType>(memoryNameStr, true, out var memNameVal)) configure.MemoryName = memNameVal;
                configure.Verbose = (string?)configureElement.Attribute("Verbose") ?? "0"; // Default from class
                if (ulong.TryParse((string?)configureElement.Attribute("MaxPayloadSizeToTargetInBytes"), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxPayload)) configure.MaxPayloadSizeToTargetInBytes = maxPayload;
                configure.AlwaysValidate = (string?)configureElement.Attribute("AlwaysValidate") ?? "0";
                if (ulong.TryParse((string?)configureElement.Attribute("MaxDigestTableSizeInBytes"), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxDigest)) configure.MaxDigestTableSizeInBytes = maxDigest;
                configure.ZlpAwareHost = (string?)configureElement.Attribute("ZlpAwareHost") ?? "1";
                configure.SkipWrite = (string?)configureElement.Attribute("SkipWrite") ?? "0";
                data.Configure = configure;
            }

            var readElement = dataElement.Element("read");
            if (readElement != null)
            {
                var read = new Read();
                var storageTypeStr = (string?)readElement.Attribute("storage_type");
                if (storageTypeStr != null && Enum.TryParse<StorageType>(storageTypeStr, true, out var stVal)) read.StorageType = stVal;
                if (uint.TryParse((string?)readElement.Attribute("physical_partition_number"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ppnVal)) read.PhysicalPartitionNumber = ppnVal;
                if (uint.TryParse((string?)readElement.Attribute("slot"), NumberStyles.Any, CultureInfo.InvariantCulture, out var slotVal)) read.Slot = slotVal;
                if (uint.TryParse((string?)readElement.Attribute("SECTOR_SIZE_IN_BYTES"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ssVal)) read.SectorSizeInBytes = ssVal;
                read.StartSector = (string?)readElement.Attribute("start_sector") ?? "";
                read.NumPartitionSectors = (string?)readElement.Attribute("num_partition_sectors") ?? "";
                if (ulong.TryParse((string?)readElement.Attribute("last_sector"), NumberStyles.Any, CultureInfo.InvariantCulture, out var lsVal)) read.LastSector = lsVal;
                if (byte.TryParse((string?)readElement.Attribute("skip_bad_block"), NumberStyles.Any, CultureInfo.InvariantCulture, out var sbbVal)) read.SkipBadBlock = sbbVal;
                if (byte.TryParse((string?)readElement.Attribute("get_spare"), NumberStyles.Any, CultureInfo.InvariantCulture, out var gsVal)) read.GetSpare = gsVal;
                if (byte.TryParse((string?)readElement.Attribute("ecc_disabled"), NumberStyles.Any, CultureInfo.InvariantCulture, out var edVal)) read.ECCDisabled = edVal;
                data.Read = read;
            }

            var programElement = dataElement.Element("program");
            if (programElement != null)
            {
                var program = new Program();
                var storageTypeStr = (string?)programElement.Attribute("storage_type");
                if (storageTypeStr != null && Enum.TryParse<StorageType>(storageTypeStr, true, out var stVal)) program.StorageType = stVal;
                if (uint.TryParse((string?)programElement.Attribute("physical_partition_number"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ppnVal)) program.PhysicalPartitionNumber = ppnVal;
                if (uint.TryParse((string?)programElement.Attribute("slot"), NumberStyles.Any, CultureInfo.InvariantCulture, out var slotVal)) program.Slot = slotVal;
                if (uint.TryParse((string?)programElement.Attribute("SECTOR_SIZE_IN_BYTES"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ssVal)) program.SectorSizeInBytes = ssVal;
                program.StartSector = (string?)programElement.Attribute("start_sector") ?? "";
                program.NumPartitionSectors = (string?)programElement.Attribute("num_partition_sectors") ?? "";
                program.FileName = (string?)programElement.Attribute("filename") ?? "";
                data.Program = program;
            }

            var eraseElement = dataElement.Element("erase");
            if (eraseElement != null)
            {
                var eraseCmd = new EraseCommand();
                var storageTypeStr = (string?)eraseElement.Attribute("storage_type");
                if (storageTypeStr != null && Enum.TryParse<StorageType>(storageTypeStr, true, out var stVal)) eraseCmd.StorageType = stVal;
                if (uint.TryParse((string?)eraseElement.Attribute("physical_partition_number"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ppnVal)) eraseCmd.PhysicalPartitionNumber = ppnVal;
                if (uint.TryParse((string?)eraseElement.Attribute("slot"), NumberStyles.Any, CultureInfo.InvariantCulture, out var slotVal)) eraseCmd.Slot = slotVal;
                if (uint.TryParse((string?)eraseElement.Attribute("SECTOR_SIZE_IN_BYTES"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ssVal)) eraseCmd.SectorSizeInBytes = ssVal;
                eraseCmd.StartSector = (string?)eraseElement.Attribute("start_sector") ?? "";
                eraseCmd.NumPartitionSectors = (string?)eraseElement.Attribute("num_partition_sectors") ?? "";
                data.Erase = eraseCmd;
            }

            var powerElement = dataElement.Element("power");
            if (powerElement != null)
            {
                var power = new Power();
                var powerValueStr = (string?)powerElement.Attribute("value");
                if (powerValueStr != null && Enum.TryParse<PowerValue>(powerValueStr, true, out var pvVal)) power.Value = pvVal;
                if (ulong.TryParse((string?)powerElement.Attribute("DelayInSeconds"), NumberStyles.Any, CultureInfo.InvariantCulture, out var disVal)) power.DelayInSeconds = disVal;
                data.Power = power;
            }

            var getStorageInfoElement = dataElement.Element("getstorageinfo");
            if (getStorageInfoElement != null)
            {
                var gsi = new GetStorageInfo();
                var storageTypeStr = (string?)getStorageInfoElement.Attribute("storage_type");
                if (storageTypeStr != null && Enum.TryParse<StorageType>(storageTypeStr, true, out var stVal)) gsi.StorageType = stVal;
                if (uint.TryParse((string?)getStorageInfoElement.Attribute("physical_partition_number"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ppnVal)) gsi.PhysicalPartitionNumber = ppnVal;
                if (uint.TryParse((string?)getStorageInfoElement.Attribute("slot"), NumberStyles.Any, CultureInfo.InvariantCulture, out var slotVal)) gsi.Slot = slotVal;
                if (ulong.TryParse((string?)getStorageInfoElement.Attribute("print_json"), NumberStyles.Any, CultureInfo.InvariantCulture, out var pjVal)) gsi.PrintJson = pjVal;
                data.GetStorageInfo = gsi;
            }

            var nopElement = dataElement.Element("nop");
            if (nopElement != null)
            {
                data.Nop = new Nop();
            }

            dataList.Add(data);
        }
        return dataList.ToArray();
    }
}