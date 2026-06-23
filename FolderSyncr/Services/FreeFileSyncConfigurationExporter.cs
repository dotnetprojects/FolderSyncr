using System.IO;
using System.Xml.Linq;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class FreeFileSyncConfigurationExporter
{
    public void Save(string path, FolderSyncrConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a FreeFileSync configuration file.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = CreateDocument(configuration);
        document.Save(path);
    }

    public XDocument CreateDocument(FolderSyncrConfiguration configuration)
    {
        var synchronization = CreateSynchronization(configuration.SyncMode);
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("FreeFileSync",
                new XAttribute("XmlType", "GUI"),
                new XElement("FolderPairs",
                    new XElement("Pair",
                        new XElement("Left", configuration.LeftPath),
                        new XElement("Right", configuration.RightPath),
                        new XElement("LocalFilter",
                            CreateFilter("Include", configuration.IncludePatterns, defaultValue: "*"),
                            CreateFilter("Exclude", configuration.ExcludePatterns, defaultValue: string.Empty)))),
                new XElement("Comparison",
                    new XElement("Variant", ToCompareVariant(configuration.CompareMethod))),
                synchronization));
    }

    private static XElement CreateSynchronization(SyncMode mode)
    {
        var (variant, direction) = mode switch
        {
            SyncMode.MirrorRightToLeft => ("Mirror", "RightToLeft"),
            SyncMode.MirrorLeftToRight => ("Mirror", "LeftToRight"),
            SyncMode.UpdateRightToLeft => ("Update", "RightToLeft"),
            SyncMode.UpdateLeftToRight => ("Update", "LeftToRight"),
            _ => ("TwoWay", null)
        };

        var element = new XElement("Synchronization", new XElement("Variant", variant));
        if (direction is not null)
        {
            element.Add(new XElement("Direction", direction));
        }

        return element;
    }

    private static string ToCompareVariant(CompareMethod compareMethod)
    {
        return compareMethod switch
        {
            CompareMethod.ContentHash => "Content",
            CompareMethod.SizeOnly => "Size",
            _ => "TimeAndSize"
        };
    }

    private static XElement CreateFilter(string name, string patterns, string defaultValue)
    {
        var items = SplitPatterns(patterns).DefaultIfEmpty(defaultValue);
        return new XElement(name, items
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => new XElement("Item", item)));
    }

    private static IEnumerable<string> SplitPatterns(string patterns)
    {
        return patterns.Split([';', ',', '|', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
