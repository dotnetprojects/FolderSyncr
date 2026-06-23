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
    private readonly SyncDatabaseStore _syncDatabaseStore;

    public SyncEngine(SyncDatabaseStore? syncDatabaseStore = null)
    {
        _syncDatabaseStore = syncDatabaseStore ?? new SyncDatabaseStore();
    }

    public async Task<IReadOnlyList<SyncOperation>> CompareAsync(
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        options = ExpandOptions(options);
        ValidateOptions(options);
        var leftStorage = CreateStorage(options.LeftPath);
        var rightStorage = CreateStorage(options.RightPath);

        progress?.Report("Scanning left folder...");
        var leftFiles = await leftStorage.ScanAsync(options.CompareMethod, options, progress, cancellationToken);

        progress?.Report("Scanning right folder...");
        var rightFiles = await rightStorage.ScanAsync(options.CompareMethod, options, progress, cancellationToken);

        progress?.Report("Building operation preview...");
        var syncDatabase = options.Mode == SyncMode.TwoWay && !leftStorage.IsRemote && !rightStorage.IsRemote
            ? _syncDatabaseStore.LoadPair(options.LeftPath, options.RightPath)
            : null;
        return BuildOperations(
            leftFiles,
            rightFiles,
            options.Mode,
            options.CustomRules,
            syncDatabase,
            options.CompareMethod,
            GetFileTimeTolerance(options),
            options.IgnoreDaylightSavingTimeShift);
    }

    public async Task ExecuteAsync(
        IReadOnlyList<SyncOperation> operations,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        options = ExpandOptions(options);
        ValidateOptions(options);
        var leftStorage = CreateStorage(options.LeftPath);
        var rightStorage = CreateStorage(options.RightPath);

        var locks = AcquireSyncLocks(options, leftStorage, rightStorage);
        try
        {
            var executable = operations.Where(operation => operation.ShouldExecute).ToList();
            if (executable.Count == 0)
            {
                if (CanUpdateSyncDatabase(operations))
                {
                    await SaveSyncDatabaseAsync(options, progress, cancellationToken);
                }

                progress?.Report("No file changes required.");
                return;
            }

            var maxDegreeOfParallelism = Math.Max(1, options.RemoteConnectionCount);
            if (maxDegreeOfParallelism == 1)
            {
                foreach (var operation in executable)
                {
                    await ExecuteOperationWithHandlingAsync(operation, options, leftStorage, rightStorage, progress, cancellationToken);
                }
            }
            else
            {
                await Parallel.ForEachAsync(
                    executable,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = maxDegreeOfParallelism
                    },
                    async (operation, token) =>
                    {
                        await ExecuteOperationWithHandlingAsync(operation, options, leftStorage, rightStorage, progress, token);
                    });
            }

            if (CanUpdateSyncDatabase(operations))
            {
                await SaveSyncDatabaseAsync(options, progress, cancellationToken);
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

    private static async Task ExecuteOperationWithHandlingAsync(
        SyncOperation operation,
        SyncOptions options,
        ISyncStorage leftStorage,
        ISyncStorage rightStorage,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        operation.Status = "Running";
        try
        {
            await ExecuteOperationAsync(operation, options, leftStorage, rightStorage, progress, cancellationToken);
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

    private static async Task ExecuteOperationAsync(
        SyncOperation operation,
        SyncOptions options,
        ISyncStorage leftStorage,
        ISyncStorage rightStorage,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        switch (operation.Kind)
        {
            case OperationKind.CopyLeftToRight:
                await CopyAsync(operation.Left!, leftStorage, rightStorage, options, progress, cancellationToken);
                break;
            case OperationKind.CopyRightToLeft:
                await CopyAsync(operation.Right!, rightStorage, leftStorage, options, progress, cancellationToken);
                break;
            case OperationKind.DeleteLeft:
                await leftStorage.DeleteFileAsync(operation.Left!, options, progress, cancellationToken);
                break;
            case OperationKind.DeleteRight:
                await rightStorage.DeleteFileAsync(operation.Right!, options, progress, cancellationToken);
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

        foreach (var path in EnumerateFilePaths(root, options.SymbolicLinkHandling))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsInternalMetadataFile(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, path);
            if (!filter.IsMatch(relativePath))
            {
                continue;
            }

            var info = new FileInfo(path);
            var isSymbolicLink = info.LinkTarget is not null;
            if (isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.Skip)
            {
                continue;
            }

            string? hash = null;
            if (isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks)
            {
                hash = info.LinkTarget;
            }
            else if (compareMethod == CompareMethod.ContentHash)
            {
                hash = await HashFileAsync(path, cancellationToken);
            }

            files[relativePath] = new FileSnapshot(
                root,
                relativePath,
                path,
                info.Length,
                info.LastWriteTimeUtc,
                hash,
                isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks,
                isSymbolicLink && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks ? info.LinkTarget : null);

            if (files.Count % 250 == 0)
            {
                progress?.Report($"Scanned {files.Count:n0} file(s) in {root}");
            }
        }

        progress?.Report($"Found {files.Count:n0} file(s) in {root}");
        return files;
    }

    private static IEnumerable<string> EnumerateFilePaths(string root, SymbolicLinkHandling symbolicLinkHandling)
    {
        foreach (var file in Directory.EnumerateFiles(root))
        {
            yield return file;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var isSymbolicDirectory = File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
            if (isSymbolicDirectory && symbolicLinkHandling != SymbolicLinkHandling.Follow)
            {
                continue;
            }

            foreach (var file in EnumerateFilePaths(directory, symbolicLinkHandling))
            {
                yield return file;
            }
        }
    }

    private static IReadOnlyList<SyncOperation> BuildOperations(
        IReadOnlyDictionary<string, FileSnapshot> leftFiles,
        IReadOnlyDictionary<string, FileSnapshot> rightFiles,
        SyncMode mode,
        CustomSyncRules customRules,
        SyncDatabase? syncDatabase,
        CompareMethod compareMethod,
        TimeSpan fileTimeTolerance,
        bool ignoreDaylightSavingTimeShift)
    {
        var databaseEntries = syncDatabase?.Entries.ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        var allPaths = leftFiles.Keys
            .Union(rightFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        var operations = new List<SyncOperation>();
        foreach (var relativePath in allPaths)
        {
            leftFiles.TryGetValue(relativePath, out var left);
            rightFiles.TryGetValue(relativePath, out var right);
            SyncDatabaseEntry? databaseEntry = null;
            databaseEntries?.TryGetValue(relativePath, out databaseEntry);

            operations.Add(new SyncOperation
            {
                RelativePath = relativePath,
                Left = left,
                Right = right,
                Kind = DetermineOperation(left, right, mode, customRules, databaseEntry, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift)
            });
        }

        if (mode == SyncMode.TwoWay && databaseEntries is not null)
        {
            MarkDetectedMoves(operations, databaseEntries, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift);
        }

        return operations;
    }

    private static void MarkDetectedMoves(
        IReadOnlyList<SyncOperation> operations,
        IReadOnlyDictionary<string, SyncDatabaseEntry> databaseEntries,
        CompareMethod compareMethod,
        TimeSpan fileTimeTolerance,
        bool ignoreDaylightSavingTimeShift)
    {
        MarkDetectedMovesInDirection(
            operations,
            databaseEntries,
            copyKind: OperationKind.CopyLeftToRight,
            deleteKind: OperationKind.DeleteRight,
            getCopySnapshot: operation => operation.Left,
            getPreviousFingerprint: entry => entry.Left ?? entry.Right,
            compareMethod,
            fileTimeTolerance,
            ignoreDaylightSavingTimeShift);

        MarkDetectedMovesInDirection(
            operations,
            databaseEntries,
            copyKind: OperationKind.CopyRightToLeft,
            deleteKind: OperationKind.DeleteLeft,
            getCopySnapshot: operation => operation.Right,
            getPreviousFingerprint: entry => entry.Right ?? entry.Left,
            compareMethod,
            fileTimeTolerance,
            ignoreDaylightSavingTimeShift);
    }

    private static void MarkDetectedMovesInDirection(
        IReadOnlyList<SyncOperation> operations,
        IReadOnlyDictionary<string, SyncDatabaseEntry> databaseEntries,
        OperationKind copyKind,
        OperationKind deleteKind,
        Func<SyncOperation, FileSnapshot?> getCopySnapshot,
        Func<SyncDatabaseEntry, FileFingerprint?> getPreviousFingerprint,
        CompareMethod compareMethod,
        TimeSpan fileTimeTolerance,
        bool ignoreDaylightSavingTimeShift)
    {
        var deleteCandidates = operations
            .Where(operation => operation.Kind == deleteKind && databaseEntries.ContainsKey(operation.RelativePath))
            .ToList();
        var usedDeletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var copyOperation in operations.Where(operation => operation.Kind == copyKind))
        {
            if (databaseEntries.ContainsKey(copyOperation.RelativePath))
            {
                continue;
            }

            var copySnapshot = getCopySnapshot(copyOperation);
            if (copySnapshot is null)
            {
                continue;
            }

            var deleteOperation = deleteCandidates.FirstOrDefault(candidate =>
                !usedDeletes.Contains(candidate.RelativePath)
                && databaseEntries.TryGetValue(candidate.RelativePath, out var databaseEntry)
                && getPreviousFingerprint(databaseEntry) is { } fingerprint
                && MatchesFingerprint(copySnapshot, fingerprint, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift));

            if (deleteOperation is null)
            {
                continue;
            }

            copyOperation.MovePartnerRelativePath = deleteOperation.RelativePath;
            deleteOperation.MovePartnerRelativePath = copyOperation.RelativePath;
            usedDeletes.Add(deleteOperation.RelativePath);
        }
    }

    private static OperationKind DetermineOperation(FileSnapshot? left, FileSnapshot? right, SyncMode mode, CustomSyncRules customRules, SyncDatabaseEntry? databaseEntry, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        return mode switch
        {
            SyncMode.MirrorLeftToRight => DetermineMirrorOperation(left, right, leftIsSource: true, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.MirrorRightToLeft => DetermineMirrorOperation(left, right, leftIsSource: false, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.UpdateLeftToRight => DetermineUpdateOperation(left, right, leftIsSource: true, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.UpdateRightToLeft => DetermineUpdateOperation(left, right, leftIsSource: false, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            SyncMode.Custom => DetermineCustomOperation(left, right, customRules, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift),
            _ => DetermineTwoWayOperation(left, right, databaseEntry, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift)
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

    private static OperationKind DetermineTwoWayOperation(FileSnapshot? left, FileSnapshot? right, SyncDatabaseEntry? databaseEntry, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        if (databaseEntry is not null
            && DetermineTwoWayOperationFromDatabase(left, right, databaseEntry, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift) is { } databaseOperation)
        {
            return databaseOperation;
        }

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

    private static OperationKind? DetermineTwoWayOperationFromDatabase(FileSnapshot? left, FileSnapshot? right, SyncDatabaseEntry databaseEntry, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        if (databaseEntry.Left is null && databaseEntry.Right is null)
        {
            return null;
        }

        if (left is not null
            && right is not null
            && AreEquivalent(left, right, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift))
        {
            return OperationKind.Equal;
        }

        var leftChanged = HasChangedSinceDatabase(left, databaseEntry.Left, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift);
        var rightChanged = HasChangedSinceDatabase(right, databaseEntry.Right, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift);

        if (!leftChanged && !rightChanged)
        {
            return OperationKind.Equal;
        }

        if (leftChanged && rightChanged)
        {
            return OperationKind.Conflict;
        }

        if (leftChanged)
        {
            return left is null ? OperationKind.DeleteRight : OperationKind.CopyLeftToRight;
        }

        return right is null ? OperationKind.DeleteLeft : OperationKind.CopyRightToLeft;
    }

    private static bool HasChangedSinceDatabase(FileSnapshot? current, FileFingerprint? previous, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        if (current is null || previous is null)
        {
            return current is not null || previous is not null;
        }

        if (current.IsSymbolicLink || previous.IsSymbolicLink)
        {
            return current.IsSymbolicLink != previous.IsSymbolicLink
                || !string.Equals(current.LinkTarget, previous.LinkTarget, StringComparison.Ordinal);
        }

        if (current.Hash is not null && previous.Hash is not null)
        {
            return !string.Equals(current.Hash, previous.Hash, StringComparison.OrdinalIgnoreCase);
        }

        if (compareMethod == CompareMethod.SizeOnly)
        {
            return current.Length != previous.Length;
        }

        if (current.Length != previous.Length)
        {
            return true;
        }

        var timestampDifference = Math.Abs((current.LastWriteTimeUtc - previous.LastWriteTimeUtc).TotalSeconds);
        return timestampDifference > fileTimeTolerance.TotalSeconds
            && (!ignoreDaylightSavingTimeShift
                || Math.Abs(timestampDifference - TimeSpan.FromHours(1).TotalSeconds) > fileTimeTolerance.TotalSeconds);
    }

    private static bool MatchesFingerprint(FileSnapshot current, FileFingerprint previous, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        return !HasChangedSinceDatabase(current, previous, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift);
    }

    private static OperationKind DetermineCustomOperation(FileSnapshot? left, FileSnapshot? right, CustomSyncRules customRules, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        if (left is null && right is null)
        {
            return OperationKind.Equal;
        }

        if (left is null)
        {
            return MapCustomAction(customRules.RightOnly, leftExists: false, rightExists: true);
        }

        if (right is null)
        {
            return MapCustomAction(customRules.LeftOnly, leftExists: true, rightExists: false);
        }

        if (AreEquivalent(left, right, compareMethod, fileTimeTolerance, ignoreDaylightSavingTimeShift))
        {
            return OperationKind.Equal;
        }

        if (IsNewer(left, right, fileTimeTolerance))
        {
            return MapCustomAction(customRules.LeftNewer, leftExists: true, rightExists: true);
        }

        if (IsNewer(right, left, fileTimeTolerance))
        {
            return MapCustomAction(customRules.RightNewer, leftExists: true, rightExists: true);
        }

        return MapCustomAction(customRules.Different, leftExists: true, rightExists: true);
    }

    private static OperationKind MapCustomAction(CustomSyncAction action, bool leftExists, bool rightExists)
    {
        return action switch
        {
            CustomSyncAction.DoNothing => OperationKind.Equal,
            CustomSyncAction.CopyLeftToRight when leftExists => OperationKind.CopyLeftToRight,
            CustomSyncAction.CopyRightToLeft when rightExists => OperationKind.CopyRightToLeft,
            CustomSyncAction.DeleteLeft when leftExists => OperationKind.DeleteLeft,
            CustomSyncAction.DeleteRight when rightExists => OperationKind.DeleteRight,
            _ => OperationKind.Conflict
        };
    }

    private static bool AreEquivalent(FileSnapshot left, FileSnapshot right, CompareMethod compareMethod, TimeSpan fileTimeTolerance, bool ignoreDaylightSavingTimeShift)
    {
        if (left.IsSymbolicLink || right.IsSymbolicLink)
        {
            return left.IsSymbolicLink == right.IsSymbolicLink
                && string.Equals(left.LinkTarget, right.LinkTarget, StringComparison.Ordinal);
        }

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
            CustomRules = options.CustomRules,
            CompareMethod = options.CompareMethod,
            FileTimeToleranceSeconds = options.FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = options.IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = options.VerifyCopiedFiles,
            DeletionHandling = options.DeletionHandling,
            VersioningMode = options.VersioningMode,
            VersioningFolderPath = PathMacroExpander.Expand(options.VersioningFolderPath),
            ErrorHandling = options.ErrorHandling,
            SymbolicLinkHandling = options.SymbolicLinkHandling,
            RemoteConnectionCount = Math.Max(1, options.RemoteConnectionCount),
            SftpCompression = options.SftpCompression,
            IncludePatterns = options.IncludePatterns,
            ExcludePatterns = options.ExcludePatterns,
            DryRun = options.DryRun
        };
    }

    private static IReadOnlyList<FileStream> AcquireSyncLocks(SyncOptions options, ISyncStorage leftStorage, ISyncStorage rightStorage)
    {
        var lockPaths = new[] { (Path: options.LeftPath, Storage: leftStorage), (Path: options.RightPath, Storage: rightStorage) }
        .Where(item => !item.Storage.IsRemote)
        .Select(item => Path.Combine(item.Path, LockFileName))
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
        ISyncStorage sourceStorage,
        ISyncStorage destinationStorage,
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report($"Copy {source.RelativePath}");
        if (source.IsSymbolicLink && source.LinkTarget is not null && options.SymbolicLinkHandling == SymbolicLinkHandling.CopyLinksAsLinks)
        {
            if (!sourceStorage.IsRemote && !destinationStorage.IsRemote)
            {
                var destinationPath = GetFullDestinationPath(destinationStorage, source.RelativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.CreateSymbolicLink(destinationPath, source.LinkTarget);
                return;
            }

            throw new NotSupportedException("Copying symbolic links as links is supported only for local folder pairs.");
        }

        await using (var input = await sourceStorage.OpenReadAsync(source, options, cancellationToken))
        {
            await destinationStorage.WriteFileAsync(source.RelativePath, input, source.LastWriteTimeUtc, options, cancellationToken);
        }

        if (options.VerifyCopiedFiles)
        {
            progress?.Report($"Verify {source.RelativePath}");
            if (!await FilesAreEqualAsync(source, sourceStorage, destinationStorage, options, cancellationToken))
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

    private async Task SaveSyncDatabaseAsync(
        SyncOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (options.DryRun
            || options.Mode != SyncMode.TwoWay
            || RemoteSyncRoot.IsRemotePath(options.LeftPath)
            || RemoteSyncRoot.IsRemotePath(options.RightPath))
        {
            return;
        }

        progress?.Report("Updating sync database...");
        var leftFiles = await new LocalSyncStorage(options.LeftPath).ScanAsync(options.CompareMethod, options, progress, cancellationToken);
        var rightFiles = await new LocalSyncStorage(options.RightPath).ScanAsync(options.CompareMethod, options, progress, cancellationToken);
        _syncDatabaseStore.SavePair(options.LeftPath, options.RightPath, leftFiles, rightFiles);
    }

    private static bool IsInternalMetadataFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, LockFileName, StringComparison.OrdinalIgnoreCase)
            || SyncDatabaseStore.IsDatabaseFile(path);
    }

    private static bool CanUpdateSyncDatabase(IReadOnlyList<SyncOperation> operations)
    {
        return operations.All(operation =>
            operation.Kind == OperationKind.Equal
            || operation.ShouldExecute && string.Equals(operation.Status, "Done", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task<bool> FilesAreEqualAsync(
        FileSnapshot source,
        ISyncStorage sourceStorage,
        ISyncStorage destinationStorage,
        SyncOptions options,
        CancellationToken cancellationToken)
    {
        await using var left = await sourceStorage.OpenReadAsync(source, options, cancellationToken);
        await using var right = await destinationStorage.OpenReadAsync(
            new FileSnapshot(destinationStorage.Root, source.RelativePath, GetFullDestinationPath(destinationStorage, source.RelativePath), source.Length, source.LastWriteTimeUtc, null),
            options,
            cancellationToken);

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
        ValidatePath(options.LeftPath, "left");
        ValidatePath(options.RightPath, "right");
    }

    private static void ValidatePath(string path, string side)
    {
        if (RemoteSyncRoot.TryParse(path, out var remoteRoot))
        {
            var parsedRoot = remoteRoot ?? throw new InvalidOperationException($"The {side} remote path is invalid.");
            if (parsedRoot.Protocol == RemoteSyncProtocol.Ftp)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(parsedRoot.Username) || string.IsNullOrEmpty(parsedRoot.Password))
            {
                throw new InvalidOperationException($"The {side} SFTP path must include a user name and password.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            if (PathMacroExpander.GetVolumeLabelReference(path) is { } volumeLabel)
            {
                throw new DirectoryNotFoundException($"Drive with volume label '{volumeLabel}' is not available.");
            }

            if (PathMacroExpander.GetVolumeGuidReference(path) is { } volumeGuid)
            {
                throw new DirectoryNotFoundException($"Volume GUID path '{volumeGuid}' is not available.");
            }

            if (PathMacroExpander.GetUncShareRoot(path) is { } uncRoot && !Directory.Exists(uncRoot))
            {
                throw new DirectoryNotFoundException($"Network share '{uncRoot}' is not available.");
            }

            throw new DirectoryNotFoundException($"Choose an existing {side} folder.");
        }
    }

    private static ISyncStorage CreateStorage(string root)
    {
        if (RemoteSyncRoot.TryParse(root, out var remoteRoot))
        {
            var parsedRoot = remoteRoot ?? throw new InvalidOperationException("The remote path is invalid.");
            return parsedRoot.Protocol switch
            {
                RemoteSyncProtocol.Sftp => new SftpSyncStorage(parsedRoot),
                RemoteSyncProtocol.Ftp => new FtpSyncStorage(parsedRoot),
                _ => throw new NotSupportedException($"Remote protocol {parsedRoot.Protocol} is not supported.")
            };
        }

        return new LocalSyncStorage(root);
    }

    private static string GetFullDestinationPath(ISyncStorage destinationStorage, string relativePath)
    {
        return RemoteSyncRoot.TryParse(destinationStorage.Root, out var remoteRoot)
            ? remoteRoot!.Combine(relativePath)
            : Path.Combine(destinationStorage.Root, relativePath);
    }
}
