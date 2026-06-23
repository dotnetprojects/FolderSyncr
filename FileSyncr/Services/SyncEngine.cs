using System.IO;
using System.Security.Cryptography;
using FileSyncr.Models;

namespace FileSyncr.Services;

public sealed class SyncEngine
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<SyncOperation>> CompareAsync(
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        progress?.Report("Scanning left folder...");
        var leftFiles = await ScanAsync(options.LeftPath, options.CompareMethod, options, progress, cancellationToken);

        progress?.Report("Scanning right folder...");
        var rightFiles = await ScanAsync(options.RightPath, options.CompareMethod, options, progress, cancellationToken);

        progress?.Report("Building operation preview...");
        return BuildOperations(leftFiles, rightFiles, options.Mode);
    }

    public async Task ExecuteAsync(
        IReadOnlyList<SyncOperation> operations,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        var executable = operations.Where(operation => operation.WillChangeFileSystem).ToList();
        if (executable.Count == 0)
        {
            progress?.Report("No file changes required.");
            return;
        }

        foreach (var operation in executable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            operation.Status = "Running";
            switch (operation.Kind)
            {
                case OperationKind.CopyLeftToRight:
                    await CopyAsync(operation.Left!, options.RightPath, progress, cancellationToken);
                    break;
                case OperationKind.CopyRightToLeft:
                    await CopyAsync(operation.Right!, options.LeftPath, progress, cancellationToken);
                    break;
                case OperationKind.DeleteLeft:
                    DeleteFile(operation.Left!.FullPath, progress);
                    break;
                case OperationKind.DeleteRight:
                    DeleteFile(operation.Right!.FullPath, progress);
                    break;
            }

            operation.Status = "Done";
        }

        progress?.Report($"Sync completed. {executable.Count} change(s) applied.");
    }

    private static async Task<Dictionary<string, FileSnapshot>> ScanAsync(
        string root,
        CompareMethod compareMethod,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var filter = new FileFilter(options.IncludePatterns, options.ExcludePatterns);
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(root, path);
            if (!filter.IsMatch(relativePath))
            {
                continue;
            }

            var info = new FileInfo(path);
            string? hash = null;
            if (compareMethod == CompareMethod.ContentHash)
            {
                hash = await HashFileAsync(path, cancellationToken);
            }

            files[relativePath] = new FileSnapshot(
                root,
                relativePath,
                path,
                info.Length,
                info.LastWriteTimeUtc,
                hash);

            if (files.Count % 250 == 0)
            {
                progress?.Report($"Scanned {files.Count:n0} file(s) in {root}");
            }
        }

        progress?.Report($"Found {files.Count:n0} file(s) in {root}");
        return files;
    }

    private static IReadOnlyList<SyncOperation> BuildOperations(
        IReadOnlyDictionary<string, FileSnapshot> leftFiles,
        IReadOnlyDictionary<string, FileSnapshot> rightFiles,
        SyncMode mode)
    {
        var allPaths = leftFiles.Keys
            .Union(rightFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var operations = new List<SyncOperation>();
        foreach (var relativePath in allPaths)
        {
            leftFiles.TryGetValue(relativePath, out var left);
            rightFiles.TryGetValue(relativePath, out var right);

            operations.Add(new SyncOperation
            {
                RelativePath = relativePath,
                Left = left,
                Right = right,
                Kind = DetermineOperation(left, right, mode)
            });
        }

        return operations;
    }

    private static OperationKind DetermineOperation(FileSnapshot? left, FileSnapshot? right, SyncMode mode)
    {
        return mode switch
        {
            SyncMode.MirrorLeftToRight => DetermineMirrorOperation(left, right, leftIsSource: true),
            SyncMode.MirrorRightToLeft => DetermineMirrorOperation(left, right, leftIsSource: false),
            SyncMode.UpdateLeftToRight => DetermineUpdateOperation(left, right, leftIsSource: true),
            SyncMode.UpdateRightToLeft => DetermineUpdateOperation(left, right, leftIsSource: false),
            _ => DetermineTwoWayOperation(left, right)
        };
    }

    private static OperationKind DetermineMirrorOperation(FileSnapshot? left, FileSnapshot? right, bool leftIsSource)
    {
        var source = leftIsSource ? left : right;
        var target = leftIsSource ? right : left;

        if (source is null && target is null)
        {
            return OperationKind.Equal;
        }

        if (source is null)
        {
            return leftIsSource ? OperationKind.DeleteRight : OperationKind.DeleteLeft;
        }

        if (target is null || !AreEquivalent(source, target))
        {
            return leftIsSource ? OperationKind.CopyLeftToRight : OperationKind.CopyRightToLeft;
        }

        return OperationKind.Equal;
    }

    private static OperationKind DetermineUpdateOperation(FileSnapshot? left, FileSnapshot? right, bool leftIsSource)
    {
        var source = leftIsSource ? left : right;
        var target = leftIsSource ? right : left;

        if (source is null)
        {
            return OperationKind.Equal;
        }

        if (target is null || IsNewer(source, target))
        {
            return leftIsSource ? OperationKind.CopyLeftToRight : OperationKind.CopyRightToLeft;
        }

        return OperationKind.Equal;
    }

    private static OperationKind DetermineTwoWayOperation(FileSnapshot? left, FileSnapshot? right)
    {
        if (left is null && right is null)
        {
            return OperationKind.Equal;
        }

        if (left is null)
        {
            return OperationKind.CopyRightToLeft;
        }

        if (right is null)
        {
            return OperationKind.CopyLeftToRight;
        }

        if (AreEquivalent(left, right))
        {
            return OperationKind.Equal;
        }

        if (IsNewer(left, right))
        {
            return OperationKind.CopyLeftToRight;
        }

        if (IsNewer(right, left))
        {
            return OperationKind.CopyRightToLeft;
        }

        return OperationKind.Conflict;
    }

    private static bool AreEquivalent(FileSnapshot left, FileSnapshot right)
    {
        if (left.Hash is not null || right.Hash is not null)
        {
            return string.Equals(left.Hash, right.Hash, StringComparison.OrdinalIgnoreCase);
        }

        return left.Length == right.Length
            && Math.Abs((left.LastWriteTimeUtc - right.LastWriteTimeUtc).TotalSeconds) <= TimestampTolerance.TotalSeconds;
    }

    private static bool IsNewer(FileSnapshot source, FileSnapshot target)
    {
        return source.LastWriteTimeUtc - target.LastWriteTimeUtc > TimestampTolerance;
    }

    private static async Task CopyAsync(
        FileSnapshot source,
        string destinationRoot,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var destinationPath = Path.Combine(destinationRoot, source.RelativePath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        progress?.Report($"Copy {source.RelativePath}");
        await using var input = File.Open(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        File.SetLastWriteTimeUtc(destinationPath, source.LastWriteTimeUtc);
    }

    private static void DeleteFile(string path, IProgress<string>? progress)
    {
        progress?.Report($"Delete {path}");
        File.Delete(path);
        RemoveEmptyParents(Path.GetDirectoryName(path));
    }

    private static void RemoveEmptyParents(string? directory)
    {
        while (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void ValidateOptions(SyncOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LeftPath) || !Directory.Exists(options.LeftPath))
        {
            throw new DirectoryNotFoundException("Choose an existing left folder.");
        }

        if (string.IsNullOrWhiteSpace(options.RightPath) || !Directory.Exists(options.RightPath))
        {
            throw new DirectoryNotFoundException("Choose an existing right folder.");
        }
    }
}
