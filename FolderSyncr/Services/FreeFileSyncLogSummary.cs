namespace FolderSyncr.Services;

public sealed record FreeFileSyncLogSummary(
    string SourcePath,
    string SyncResult,
    DateTimeOffset? StartTime,
    int? TotalTimeSeconds,
    int? Errors,
    int? Warnings,
    long? TotalItems,
    long? TotalBytes,
    long? ProcessedItems,
    long? ProcessedBytes,
    string? LogFile,
    string RawSummary)
{
    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            $"Result: {SyncResult}",
            $"Source: {SourcePath}"
        };

        if (StartTime is { } startTime)
        {
            lines.Add($"Start time: {startTime:yyyy-MM-dd HH:mm:ss zzz}");
        }

        if (TotalTimeSeconds is { } totalTime)
        {
            lines.Add($"Duration: {totalTime} sec");
        }

        if (Errors is { } errors)
        {
            lines.Add($"Errors: {errors}");
        }

        if (Warnings is { } warnings)
        {
            lines.Add($"Warnings: {warnings}");
        }

        if (ProcessedItems is not null || TotalItems is not null)
        {
            lines.Add($"Items: {ProcessedItems?.ToString("n0") ?? "?"} / {TotalItems?.ToString("n0") ?? "?"}");
        }

        if (ProcessedBytes is not null || TotalBytes is not null)
        {
            lines.Add($"Bytes: {FormatBytes(ProcessedBytes)} / {FormatBytes(TotalBytes)}");
        }

        if (!string.IsNullOrWhiteSpace(LogFile))
        {
            lines.Add($"Log file: {LogFile}");
        }

        if (!string.IsNullOrWhiteSpace(RawSummary))
        {
            lines.Add(string.Empty);
            lines.Add(RawSummary);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "?";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
