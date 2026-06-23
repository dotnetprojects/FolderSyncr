using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderSyncr.Services;

public sealed class FolderSyncrConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FolderSyncrConfiguration Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a FolderSyncr configuration file.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The FolderSyncr configuration file was not found.", path);
        }

        var configuration = JsonSerializer.Deserialize<FolderSyncrConfiguration>(
            File.ReadAllText(path),
            SerializerOptions);

        return configuration ?? throw new InvalidDataException("The FolderSyncr configuration file is empty or invalid.");
    }

    public void Save(string path, FolderSyncrConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a FolderSyncr configuration file.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(configuration, SerializerOptions));
    }
}
