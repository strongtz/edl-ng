using System.Xml;
using System.Xml.Linq;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal static class SaharaConfigParser
{
    public static Dictionary<uint, string> ParseAndValidateConfig(string xmlPath)
    {
        var mappings = new Dictionary<uint, string>();

        if (!File.Exists(xmlPath))
        {
            throw new FileNotFoundException("Sahara configuration XML not found.", xmlPath);
        }

        var settings = new XmlReaderSettings { IgnoreWhitespace = true };
        using var reader = XmlReader.Create(xmlPath, settings);

        var doc = XDocument.Load(reader);
        var baseDir = Path.GetDirectoryName(xmlPath) ?? "";

        if (doc.Root?.Name.LocalName != "sahara_config")
        {
            throw new XmlException("Invalid Sahara config: Root element must be <sahara_config>.");
        }

        var chipset = doc.Root.Element("chipset")?.Value;
        if (!string.IsNullOrEmpty(chipset))
        {
            LibraryLogger.Debug($"Parsing Sahara config for chipset: {chipset}");
        }

        var imagesContainer = doc.Root.Element("images") ?? throw new XmlException("Invalid Sahara config: Missing <images> container.");

        var imageElements = imagesContainer.Elements("image").ToList();
        if (imageElements.Count == 0)
        {
            throw new XmlException("Invalid Sahara config: No <image> elements found.");
        }

        foreach (var img in imageElements)
        {
            var idStr = img.Attribute("image_id")?.Value;
            var relPath = img.Attribute("image_path")?.Value;

            if (string.IsNullOrEmpty(idStr) || string.IsNullOrEmpty(relPath))
            {
                LibraryLogger.Warning("Skipping invalid <image> node: Missing image_id or image_path.");
                continue;
            }

            if (!uint.TryParse(idStr, out var id))
            {
                throw new XmlException($"Invalid Sahara config: <image> has a non-numeric image_id '{idStr}'.");
            }
            if (mappings.ContainsKey(id))
            {
                throw new XmlException($"Invalid Sahara config: Duplicate <image> entry for image_id '{id}'.");
            }
            var fullPath = Path.IsPathRooted(relPath) ? relPath : Path.Combine(baseDir, relPath);
            mappings[id] = File.Exists(fullPath)
                ? fullPath
                : throw new FileNotFoundException($"Sahara image file defined in XML not found: {relPath}", fullPath);
        }
        return mappings.Count == 0 ? throw new XmlException("Invalid Sahara config: No valid <image> mappings were produced.") : mappings;
    }
}
