using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderSyncr.Services;

public sealed partial class FreeFileSyncLogImporter
{
    public FreeFileSyncLogSummary Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a FreeFileSync log or JSON result file.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The FreeFileSync log file was not found.", path);
        }

        var content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new FreeFileSyncLogSummary(path, "empty", null, null, null, null, null, null, null, null, null, string.Empty);
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith('{'))
        {
            return ImportJson(path, trimmed);
        }

        return ImportText(path, content);
    }

    private static FreeFileSyncLogSummary ImportJson(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new FreeFileSyncLogSummary(
            path,
            ReadString(root, "syncResult") ?? "unknown",
            ReadDateTimeOffset(root, "startTime"),
            ReadInt(root, "totalTimeSec"),
            ReadInt(root, "errors"),
            ReadInt(root, "warnings"),
            ReadLong(root, "totalItems"),
            ReadLong(root, "totalBytes"),
            ReadLong(root, "processedItems"),
            ReadLong(root, "processedBytes"),
            ReadString(root, "logFile"),
            string.Empty);
    }

    private static FreeFileSyncLogSummary ImportText(string path, string content)
    {
        var readableText = StripMarkup(content);
        var errors = ReadLabeledInt(readableText, "errors");
        var warnings = ReadLabeledInt(readableText, "warnings");
        var result = InferResult(readableText, errors, warnings);
        var rawSummary = readableText.Length > 1200 ? readableText[..1200] + "..." : readableText;

        return new FreeFileSyncLogSummary(
            path,
            result,
            null,
            null,
            errors,
            warnings,
            null,
            null,
            null,
            null,
            path,
            rawSummary.Trim());
    }

    private static string StripMarkup(string content)
    {
        var withoutScripts = ScriptOrStyleRegex().Replace(content, " ");
        var withoutTags = HtmlTagRegex().Replace(withoutScripts, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string InferResult(string text, int? errors, int? warnings)
    {
        if (errors > 0)
        {
            return "error";
        }

        if (warnings > 0)
        {
            return "warning";
        }

        if (text.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "cancelled";
        }

        if (text.Contains("with errors", StringComparison.OrdinalIgnoreCase)
            || text.Contains("syncResult\": \"error", StringComparison.OrdinalIgnoreCase))
        {
            return "error";
        }

        if (text.Contains("with warnings", StringComparison.OrdinalIgnoreCase)
            || text.Contains("syncResult\": \"warning", StringComparison.OrdinalIgnoreCase))
        {
            return "warning";
        }

        if (text.Contains("success", StringComparison.OrdinalIgnoreCase)
            || text.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
        {
            return "success";
        }

        return "unknown";
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetDateTimeOffset(out var dateTime)
            ? dateTime
            : null;
    }

    private static int? ReadInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static long? ReadLong(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static int? ReadLabeledInt(string text, string label)
    {
        var singularLabel = label.TrimEnd('s');
        var match = Regex.Match(text, $"\\b{Regex.Escape(singularLabel)}s?\\b\\s*[:=]\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : null;
    }

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
