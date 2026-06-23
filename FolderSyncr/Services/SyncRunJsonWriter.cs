using System.IO;
using System.Text.Json;

namespace FolderSyncr.Services;

public static class SyncRunJsonWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string Serialize(SyncRunResult result)
    {
        return JsonSerializer.Serialize(result, SerializerOptions);
    }

    public static void Write(string path, SyncRunResult result)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(result));
    }
}
