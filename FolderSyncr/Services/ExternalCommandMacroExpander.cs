using System.IO;
using System.Text.RegularExpressions;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public static partial class ExternalCommandMacroExpander
{
    public static string Expand(
        string commandLine,
        IReadOnlyList<SyncOperation> operations,
        string? leftRoot = null,
        string? rightRoot = null)
    {
        if (string.IsNullOrWhiteSpace(commandLine) || operations.Count == 0)
        {
            return commandLine;
        }

        return ItemMacroRegex().Replace(commandLine, match =>
        {
            var macroName = match.Groups["name"].Value;
            var suffix = match.Groups["suffix"].Value.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            var useOppositeSide = string.Equals(suffix, "2", StringComparison.OrdinalIgnoreCase);
            var useAllSelected = string.Equals(suffix, "s", StringComparison.OrdinalIgnoreCase);

            var values = useAllSelected
                ? operations.Select(operation => GetMacroValue(macroName, operation, useOppositeSide: false, leftRoot, rightRoot))
                : [GetMacroValue(macroName, operations[0], useOppositeSide, leftRoot, rightRoot)];

            return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(QuoteArgument));
        });
    }

    private static string GetMacroValue(
        string macroName,
        SyncOperation operation,
        bool useOppositeSide,
        string? leftRoot,
        string? rightRoot)
    {
        var snapshot = GetSnapshot(operation, useOppositeSide);
        var fullPath = snapshot?.FullPath ?? GetPathFromRoot(operation, useOppositeSide, leftRoot, rightRoot);
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        return macroName.ToLowerInvariant() switch
        {
            "item_path" or "local_path" => fullPath,
            "item_name" => Path.GetFileName(fullPath),
            "parent_path" => Path.GetDirectoryName(fullPath) ?? string.Empty,
            _ => string.Empty
        };
    }

    private static FileSnapshot? GetSnapshot(SyncOperation operation, bool useOppositeSide)
    {
        var primary = operation.Left ?? operation.Right;
        var opposite = ReferenceEquals(primary, operation.Left) ? operation.Right : operation.Left;
        return useOppositeSide ? opposite : primary;
    }

    private static string? GetPathFromRoot(SyncOperation operation, bool useOppositeSide, string? leftRoot, string? rightRoot)
    {
        var primaryIsLeft = operation.Left is not null || operation.Right is null;
        var root = useOppositeSide
            ? primaryIsLeft ? rightRoot : leftRoot
            : primaryIsLeft ? leftRoot : rightRoot;

        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, operation.RelativePath);
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    [GeneratedRegex("%(?<name>item_path|local_path|item_name|parent_path)(?:\\s*(?<suffix>2|s)|(?<suffix>s))?%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemMacroRegex();
}
