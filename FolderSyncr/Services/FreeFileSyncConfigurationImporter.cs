using System.IO;
using System.Xml.Linq;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class FreeFileSyncConfigurationImporter
{
    public FreeFileSyncConfiguration Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a FreeFileSync configuration file.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The FreeFileSync configuration file was not found.", path);
        }

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var warnings = new List<string>();
        var pairs = ExtractFolderPairs(document)
            .DistinctBy(pair => $"{pair.LeftPath}\u001f{pair.RightPath}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pairs.Count == 0)
        {
            warnings.Add("No folder pair was found. FolderSyncr looked for FreeFileSync Left/Right folder pair nodes.");
        }

        if (pairs.Count > 1)
        {
            warnings.Add("This configuration contains multiple folder pairs. FolderSyncr imported the first pair into the current UI.");
        }

        var syncMode = DetectSyncMode(document, warnings);
        var compareMethod = DetectCompareMethod(document, warnings);

        return new FreeFileSyncConfiguration(
            path,
            pairs,
            syncMode,
            compareMethod,
            ExtractFilterText(document, "Include", defaultValue: "*"),
            ExtractFilterText(document, "Exclude", defaultValue: string.Empty),
            warnings);
    }

    private static IEnumerable<FreeFileSyncFolderPair> ExtractFolderPairs(XDocument document)
    {
        foreach (var pair in document.Descendants().Where(element => IsName(element, "Pair")))
        {
            var left = ReadPathNode(pair, "Left");
            var right = ReadPathNode(pair, "Right");
            if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
            {
                yield return new FreeFileSyncFolderPair(ExpandPath(left), ExpandPath(right));
            }
        }

        var parentCandidates = document.Descendants()
            .Where(element => !IsName(element, "Pair")
                && element.Elements().Any(child => IsName(child, "Left"))
                && element.Elements().Any(child => IsName(child, "Right")));

        foreach (var candidate in parentCandidates)
        {
            var left = ReadPathNode(candidate, "Left");
            var right = ReadPathNode(candidate, "Right");
            if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
            {
                yield return new FreeFileSyncFolderPair(ExpandPath(left), ExpandPath(right));
            }
        }
    }

    private static SyncMode? DetectSyncMode(XDocument document, List<string> warnings)
    {
        var tokens = CollectNamedValues(document, "Sync", "Variant", "Direction", "Mode");
        var text = string.Join(' ', tokens);

        if (Contains(text, "TwoWay") || Contains(text, "Two way"))
        {
            return SyncMode.TwoWay;
        }

        if (Contains(text, "Mirror"))
        {
            if (Contains(text, "RightToLeft") || Contains(text, "Right to left"))
            {
                return SyncMode.MirrorRightToLeft;
            }

            return SyncMode.MirrorLeftToRight;
        }

        if (Contains(text, "Update"))
        {
            if (Contains(text, "RightToLeft") || Contains(text, "Right to left"))
            {
                return SyncMode.UpdateRightToLeft;
            }

            return SyncMode.UpdateLeftToRight;
        }

        if (Contains(text, "Custom"))
        {
            warnings.Add("Custom FreeFileSync synchronization rules are not supported yet. The current FolderSyncr mode was kept.");
        }

        return null;
    }

    private static CompareMethod? DetectCompareMethod(XDocument document, List<string> warnings)
    {
        var tokens = CollectNamedValues(document, "Compare", "Comparison", "Variant", "Method");
        var text = string.Join(' ', tokens);

        if (Contains(text, "Content"))
        {
            return CompareMethod.ContentHash;
        }

        if (Contains(text, "TimeAndSize") || Contains(text, "Time and Size") || Contains(text, "Time") && Contains(text, "Size"))
        {
            return CompareMethod.TimeAndSize;
        }

        if (Contains(text, "Size"))
        {
            return CompareMethod.SizeOnly;
        }

        return null;
    }

    private static IReadOnlyList<string> CollectNamedValues(XDocument document, params string[] nameFragments)
    {
        var values = new List<string>();

        foreach (var element in document.Descendants())
        {
            if (nameFragments.Any(fragment => element.Name.LocalName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(element.Value);
            }

            foreach (var attribute in element.Attributes())
            {
                if (nameFragments.Any(fragment => attribute.Name.LocalName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    || nameFragments.Any(fragment => attribute.Value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                {
                    values.Add(attribute.Value);
                }
            }
        }

        return values;
    }

    private static string ExtractFilterText(XDocument document, string filterName, string defaultValue)
    {
        var filters = document.Descendants()
            .Where(element => IsName(element, filterName))
            .SelectMany(ReadFilterItems)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return filters.Count == 0 ? defaultValue : string.Join(Environment.NewLine, filters);
    }

    private static IEnumerable<string> ReadFilterItems(XElement filterElement)
    {
        var leaves = filterElement.Descendants()
            .Where(element => !element.HasElements)
            .Select(element => element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (leaves.Count > 0)
        {
            return leaves.SelectMany(SplitFilterText);
        }

        return SplitFilterText(filterElement.Value);
    }

    private static IEnumerable<string> SplitFilterText(string value)
    {
        return value.Split(['|', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ReadPathNode(XElement parent, string childName)
    {
        var child = parent.Elements().FirstOrDefault(element => IsName(element, childName))
            ?? parent.Descendants().FirstOrDefault(element => IsName(element, childName));

        if (child is null)
        {
            return string.Empty;
        }

        var pathAttribute = child.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Contains("Path", StringComparison.OrdinalIgnoreCase));
        if (pathAttribute is not null)
        {
            return pathAttribute.Value.Trim();
        }

        var pathChild = child.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Contains("Path", StringComparison.OrdinalIgnoreCase));
        return (pathChild?.Value ?? child.Value).Trim();
    }

    private static string ExpandPath(string path)
    {
        return Environment.ExpandEnvironmentVariables(path.Trim());
    }

    private static bool IsName(XElement element, string localName)
    {
        return string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
