using System.IO;
using System.Security.Cryptography;
using FolderSyncr.Models;
using RecycleOption = Microsoft.VisualBasic.FileIO.RecycleOption;
using UIOption = Microsoft.VisualBasic.FileIO.UIOption;
using VisualBasicFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace FolderSyncr.Services;

public sealed class SyncEngine
{
    private const string LockFileName = ".foldersyncr.lock";

    public async Task<IReadOnlyList<SyncOperation>> CompareAsync(
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        options = ExpandOptions(options);
        ValidateOptions(options);

        progress?.Report("Scanning left folder...");
        var leftFiles = await ScanAsync(options.LeftPath, options.CompareMethod, options, progress, cancellationToken);

        progress?.Report("Scanning right folder...");
        var rightFiles = await ScanAsync(options.RightPath, options.CompareMethod, options, progress, cancellationToken);

        progress?.Report("Building operation preview...");
        return BuildOperations(leftFiles, rightFiles, options.Mode, options.CompareMethod, GetFileTimeTolerance(options), options.IgnoreDaylightSavingTimeShift);
    }

    public async Task ExecuteAsync(
        IReadOnlyList<SyncOperation> operations,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        options = ExpandOptions(options);
        ValidateOptions(options);

        var locks = AcquireSyncLocks(options);
        try
        {
            var executable = operations.Where(operation => operation.ShouldExecute).ToList();
            if (executable.Count == 0)
            {
                progress?.Report("No file changes required.");
                return;
            }

            foreach (var operation in executable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                operation.Status = "Running";
                try
                {
                    await ExecuteOperationAsync(operation, options, progress, cancellationToken);
                    operation.Status = "Done";
                }
                catch (Exception exception) when (exception is not OperationCanceledException && options.ErrorHandling == SyncErrorHandling.IgnoreErrors)
                {
                    operation.Status = "Error";
                    progress?.Report($"Ignored error for {operation.RelativePath}: {exception.Message}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    operation.Status = "Error";
                    progress?.Report($"Error for {operation.RelativePath}: {exception.Message}");
                    throw;
                }
            }

            progress?.Report($"Sync completed. {executable.Count} change(s) applied.");
        }
        finally
        {
            foreach (var syncLock in locks)
            {
                syncLock.Dispose();
            }
        }
    }

    private static async Task ExecuteOperationAsync(
        SyncOperation operation,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        switch (operation.Kind)
        {
            case OperationKind.CopyLeftToRight:
                await CopyAsync(operation.Left!, options.RightPath, options.VerifyCopiedFiles, progress, cancellationToken);
                break;
            case OperationKind.CopyRightToLeft:
                await CopyAsync(operation.Right!, options.LeftPath, options.VerifyCopiedFiles, progress, cancellationToken);
                break;
            case OperationKind.DeleteLeft:
                DeleteFile(operation.Left!, options, progress);
                break;
            case OperationKind.DeleteRight:
                DeleteFile(operation.Right!, options, progress);
                break;
        }
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
        SyncMode mode,
        CompareMethod compareMethod,
        TimeSpan fileTimeTolerance,
        bool ignoreDaylightSavingTimeShift)
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
                Kind = DetermineOperation(left, right, mode, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift)
            });
        }

        return operations;
    }

    private static OperationKind DetermineOperation(FileSnapshot? left, FileSnapshot? right, SyncMode mode, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        return mode switch
        {
            SyncMode.MirrorLeftToRight => DetermineMirrorOperation(left, right, leftIsSource: true, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.MirrorRightToLeft => DetermineMirrorOperation(left, right, leftIsSource: false, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.UpdateLeftToRight => DetermineUpdateOperation(left, right, leftIsSource: true, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.UpdateRightToLeft => DetermineUpdateOperation(left, right, leftIsSource: false, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            _ => DetermineTwoWayOperation(left, right, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift)
        };
    }

    private static OperationKind DetermineMirrorOperation(FileSnapshot? left, FileSnapshot? right, bool leftIsSource, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
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

        if (target is null || !AreEquivalent(source, target, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift))
        {
            return leftIsSource ? OperationKind.CopyLeftToRight : OperationKind.CopyRightToLeft;
        }

        return OperationKind.Equal;
    }

    private static OperationKind DetermineUpdateOperation(FileSnapshot? left, FileSnapshot? right, bool leftIsSource, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        var source = leftIsSource ? left : right;
        var target = leftIsSource ? right : left;

        if (source is null)
        {
            return OperationKind.Equal;
        }

        if (target is null)
        {
            return leftIsSource ? OperationKind.CopyLeftToRight : OperationKind.CopyRightToLeft;
        }

        if (AreEquivalent(source, target, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift))
        {
            return OperationKind.Equal;
        }

        if (IsNewer(source, target, fileTimeTolerance))
        {
            return leftIsSource ? OperationKind.CopyLeftToRight : OperationKind.CopyRightToLeft;
        }

        return OperationKind.Equal;
    }

    private static OperationKind DetermineTwoWayOperation(FileSnapshot? left, FileSnapshot? right, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
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

        if (AreEquivalent(left, right, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift))
        {
            return OperationKind.Equal;
        }

        if (IsNewer(left, right, fileTimeTolerance))
        {
            return OperationKind.CopyLeftToRight;
        }

        if (IsNewer(right, left, fileTimeTolerance))
        {
            return OperationKind.CopyRightToLeft;
        }

        return OperationKind.Conflict;
    }

    private static bool AreEquivalent(FileSnapshot left, FileSnapshot right, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        if (compareMethod == CompareMethod.SizeOnly)
        {
            return left.Length == right.Length;
        }

        if (left.Hash is not null || right.Hash is not null)
        {
            return string.Equals(left.Hash, right.Hash, StringComparison.OrdinalIgnoreCase);
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        var timestampDifference = Math.Abs((left.LastWriteTimeUtc - right.LastWriteTimeUtc).TotalSeconds);
        return timestampDifference <= fileTimeTolerance.TotalSeconds
            || ignoreDaylightSavingTimeShift
            && Math.Abs(timestampDifference - TimeSpan.FromHours(1).TotalSeconds) <= fileTimeTolerance.TotalSeconds;
    }

    private static bool IsNewer(FileSnapshot source, FileSnapshot target, TimeSpan fileTimeTolerance)
    {
        return source.LastWriteTimeUtc - target.LastWriteTimeUtc > fileTimeTolerance;
    }

    private static TimeSpan GetFileTimeTolerance(SyncOptions options)
    {
        return TimeSpan.FromSeconds(Math.Max(0, options.FileTimeToleranceSeconds));
    }

    private static SyncOptions ExpandOptions(SyncOptions options)
    {
        return new SyncOptions
        {
            LeftPath = PathMacroExpander.Expand(options.LeftPath),
            RightPath = PathMacroExpander.Expand(options.RightPath),
            Mode = options.Mode,
            CompareMethod = options.CompareMethod,
            FileTimeToleranceSeconds = options.FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = options.IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = options.VerifyCopiedFiles,
            DeletionHandling = options.DeletionHandling,
            VersioningMode = options.VersioningMode,
            VersioningFolderPath = PathMacroExpander.Expand(options.VersioningFolderPath),
            ErrorHandling = options.ErrorHandling,
            IncludePatterns = options.IncludePatterns,
            ExcludePatterns = options.ExcludePatterns,
            DryRun = options.DryRun
        };
    }

    private static IReadOnlyList<FileStream> AcquireSyncLocks(SyncOptions options)
    {
        var lockPaths = new[]
        {
            Path.Combine(options.LeftPath, LockFileName),
            Path.Combine(options.RightPath, LockFileName)
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

        var locks = new List<FileStream>();
        try
        {
            foreach (var lockPath in lockPaths)
            {
                locks.Add(new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose));
            }
        }
        catch
        {
            foreach (var syncLock in locks)
            {
                syncLock.Dispose();
            }

            throw;
        }

        return locks;
    }

    private static async Task CopyAsync(
        FileSnapshot source,
        string destinationRoot,
        bool verifyCopiedFiles,
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
        await using (var input = File.Open(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        File.SetLastWriteTimeUtc(destinationPath, source.LastWriteTimeUtc);

        if (verifyCopiedFiles)
        {
            progress?.Report($"Verify {source.RelativePath}");
            if (!await FilesAreEqualAsync(source.FullPath, destinationPath, cancellationToken))
            {
                throw new IOException($"Verification failed after copying {source.RelativePath}.");
            }
        }
    }

    private static void DeleteFile(FileSnapshot file, SyncOptions options, IProgress<string>? progress)
    {
        progress?.Report($"Delete {file.RelativePath}");
        switch (options.DeletionHandling)
        {
            case DeletionHandling.RecycleBin:
                VisualBasicFileSystem.DeleteFile(file.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                break;
            case DeletionHandling.VersioningFolder:
                MoveToVersioningFolder(file, options);
                break;
            default:
                File.Delete(file.FullPath);
                break;
        }

        RemoveEmptyParents(Path.GetDirectoryName(file.FullPath));
    }

    private static void MoveToVersioningFolder(FileSnapshot file, SyncOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.VersioningFolderPath))
        {
            throw new InvalidOperationException("Choose a versioning folder before using versioning deletion handling.");
        }

        var versionRoot = PathMacroExpander.Expand(options.VersioningFolderPath);
        var destinationPath = GetVersioningDestinationPath(file, versionRoot, options.VersioningMode);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            if (options.VersioningMode == VersioningMode.Replace)
            {
                File.Delete(destinationPath);
            }
            else
            {
                destinationPath = GetUniquePath(destinationPath);
            }
        }

        File.Move(file.FullPath, destinationPath);
    }

    private static string GetVersioningDestinationPath(FileSnapshot file, string versionRoot, VersioningMode versioningMode)
    {
        return versioningMode switch
        {
            VersioningMode.Replace => Path.Combine(versionRoot, file.RelativePath),
            VersioningMode.FileTime => Path.Combine(
                versionRoot,
                AppendTimestampToFileName(file.RelativePath, file.LastWriteTimeUtc)),
            _ => Path.Combine(
                versionRoot,
                DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture),
                file.RelativePath)
        };
    }

    private static string AppendTimestampToFileName(string relativePath, DateTime lastWriteTimeUtc)
    {
        var directory = Path.GetDirectoryName(relativePath);
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var extension = Path.GetExtension(relativePath);
        var timestamp = lastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var stampedName = $"{fileName} {timestamp}{extension}";
        return string.IsNullOrWhiteSpace(directory) ? stampedName : Path.Combine(directory, stampedName);
    }

    private static string GetUniquePath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
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

    private static async Task<bool> FilesAreEqualAsync(string leftPath, string rightPath, CancellationToken cancellationToken)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        await using var left = File.Open(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var right = File.Open(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var leftBuffer = new byte[81920];
        var rightBuffer = new byte[81920];
        while (true)
        {
            var leftRead = await left.ReadAsync(leftBuffer, cancellationToken);
            var rightRead = await right.ReadAsync(rightBuffer, cancellationToken);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }
    }

    private static void ValidateOptions(SyncOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LeftPath) || !Directory.Exists(options.LeftPath))
        {
            if (PathMacroExpander.GetVolumeLabelReference(options.LeftPath) is { } volumeLabel)
            {
                throw new DirectoryNotFoundException($"Drive with volume label '{volumeLabel}' is not available.");
            }

            if (PathMacroExpander.GetVolumeGuidReference(options.LeftPath) is { } volumeGuid)
            {
                throw new DirectoryNotFoundException($"Volume GUID path '{volumeGuid}' is not available.");
            }

            if (PathMacroExpander.GetUncShareRoot(options.LeftPath) is { } uncRoot && !Directory.Exists(uncRoot))
            {
                throw new DirectoryNotFoundException($"Network share '{uncRoot}' is not available.");
            }

            throw new DirectoryNotFoundException("Choose an existing left folder.");
        }

        if (string.IsNullOrWhiteSpace(options.RightPath) || !Directory.Exists(options.RightPath))
        {
            if (PathMacroExpander.GetVolumeLabelReference(options.RightPath) is { } volumeLabel)
            {
                throw new DirectoryNotFoundException($"Drive with volume label '{volumeLabel}' is not available.");
            }

            if (PathMacroExpander.GetVolumeGuidReference(options.RightPath) is { } volumeGuid)
            {
                throw new DirectoryNotFoundException($"Volume GUID path '{volumeGuid}' is not available.");
            }

            if (PathMacroExpander.GetUncShareRoot(options.RightPath) is { } uncRoot && !Directory.Exists(uncRoot))
            {
                throw new DirectoryNotFoundException($"Network share '{uncRoot}' is not available.");
            }

            throw new DirectoryNotFoundException("Choose an existing right folder.");
        }
    }
}
