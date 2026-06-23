using System.IO;
using System.Xml.Linq;

namespace FolderSyncr.Services;

public sealed class FreeFileSyncGlobalSettingsImporter
{
    public bool TryImport(string path, out FreeFileSyncGlobalSettings settings)
    {
        settings = new FreeFileSyncGlobalSettings(null, null);

        if (string.IsNullOrWhiteSpace(path)
            || !File.Exists(path)
            || !string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch
        {
            return false;
        }

        if (!IsGlobalSettingsDocument(document))
        {
            return false;
        }

        settings = new FreeFileSyncGlobalSettings(
            ReadIntAttribute(document, "FileTimeTolerance", "Seconds"),
            ReadBoolAttribute(document, "VerifyCopiedFiles", "Enabled"));
        return true;
    }

    private static bool IsGlobalSettingsDocument(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return false;
        }

        var xmlType = root.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "XmlType", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return string.Equals(xmlType, "GLOBAL", StringComparison.OrdinalIgnoreCase)
            || root.Name.LocalName.Contains("GlobalSettings", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ReadIntAttribute(XDocument document, string elementName, string attributeName)
    {
        var value = document.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase))
            ?.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, attributeName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return int.TryParse(value, out var result) ? result : null;
    }

    private static bool? ReadBoolAttribute(XDocument document, string elementName, string attributeName)
    {
        var value = document.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase))
            ?.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, attributeName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return bool.TryParse(value, out var result) ? result : null;
    }
}
