using System.Text.Json.Serialization;

namespace FolderSyncr.Services;

public sealed record SyncRunResult(
    [property: JsonPropertyName("syncResult")] string SyncResult,
    [property: JsonPropertyName("startTime")] DateTimeOffset StartTime,
    [property: JsonPropertyName("totalTimeSec")] int TotalTimeSec,
    [property: JsonPropertyName("errors")] int Errors,
    [property: JsonPropertyName("warnings")] int Warnings,
    [property: JsonPropertyName("totalItems")] long TotalItems,
    [property: JsonPropertyName("totalBytes")] long TotalBytes,
    [property: JsonPropertyName("processedItems")] long ProcessedItems,
    [property: JsonPropertyName("processedBytes")] long ProcessedBytes,
    [property: JsonPropertyName("logFile")] string? LogFile,
    [property: JsonPropertyName("message")] string? Message);
