using System.IO;
using System.Text.Json;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class SyncDatabaseStore
{
    public const string FileName = "sync.ffs_db";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public SyncDatabase? Load(string root)
    {
        var path = GetDatabasePath(root);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SyncDatabase>(File.ReadAllText(path), SerializerOptions);
    }

    public SyncDatabase? LoadPair(string leftRoot, string rightRoot)
    {
        return TryLoad(leftRoot) ?? TryLoad(rightRoot);
    }

    public void SavePair(
        string leftRoot,
        string rightRoot,
        IReadOnlyDictionary<string, FileSnapshot> leftFiles,
        IReadOnlyDictionary<string, FileSnapshot> rightFiles)
    {
        var database = Create(leftRoot, rightRoot, leftFiles, rightFiles);
        Save(leftRoot, database);
        Save(rightRoot, database);
    }

    public static bool IsDatabaseFile(string path)
    {
        return string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase);
    }

    private SyncDatabase? TryLoad(string root)
    {
        try
        {
            return Load(root);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SyncDatabase Create(
        string leftRoot,
        string rightRoot,
        IReadOnlyDictionary<string, FileSnapshot> leftFiles,
        IReadOnlyDictionary<string, FileSnapshot> rightFiles)
    {
        var entries = leftFiles.Keys
            .Union(rightFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(relativePath =>
            {
                leftFiles.TryGetValue(relativePath, out var left);
                rightFiles.TryGetValue(relativePath, out var right);
                return new SyncDatabaseEntry(
                    relativePath,
                    left is null ? null : ToFingerprint(left),
                    right is null ? null : ToFingerprint(right));
            })
            .ToArray();

        return new SyncDatabase(1, DateTime.UtcNow, leftRoot, rightRoot, entries);
    }

    private static FileFingerprint ToFingerprint(FileSnapshot snapshot)
    {
        return new FileFingerprint(
            snapshot.Length,
            snapshot.LastWriteTimeUtc,
            snapshot.Hash,
            snapshot.IsSymbolicLink,
            snapshot.LinkTarget);
    }

    private static void Save(string root, SyncDatabase database)
    {
        Directory.CreateDirectory(root);
        var path = GetDatabasePath(root);
        File.WriteAllText(path, JsonSerializer.Serialize(database, SerializerOptions));
        TryHideFile(path);
    }

    private static string GetDatabasePath(string root)
    {
        return Path.Combine(root, FileName);
    }

    private static void TryHideFile(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed record SyncDatabase(
    int Version,
    DateTime SavedAtUtc,
    string LeftRoot,
    string RightRoot,
    IReadOnlyList<SyncDatabaseEntry> Entries);

public sealed record SyncDatabaseEntry(
    string RelativePath,
    FileFingerprint? Left,
    FileFingerprint? Right);

public sealed record FileFingerprint(
    long Length,
    DateTime LastWriteTimeUtc,
    string? Hash,
    bool IsSymbolicLink,
    string? LinkTarget);
