using System.IO;
using System.Text.Json;

namespace FolderSyncr.Services;

public sealed class SyncRunHistoryStore(string? historyDirectory = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public string Save(SyncRunResult result)
    {
        var directory = GetTargetHistoryDirectory();
        Directory.CreateDirectory(directory);

        var fileName = $"{result.StartTime:yyyyMMdd-HHmmss-fff}-{result.SyncResult}.json";
        var path = Path.Combine(directory, SanitizeFileName(fileName));
        var resultWithPath = result with { LogFile = path };
        File.WriteAllText(path, JsonSerializer.Serialize(resultWithPath, SerializerOptions));
        return path;
    }

    public IReadOnlyList<string> ListNewestFirst()
    {
        var directory = GetTargetHistoryDirectory();
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToList()
            : [];
    }

    private static string GetHistoryDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FolderSyncr",
            "History");
    }

    private string GetTargetHistoryDirectory()
    {
        return string.IsNullOrWhiteSpace(historyDirectory) ? GetHistoryDirectory() : historyDirectory;
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '-');
        }

        return fileName;
    }
}
