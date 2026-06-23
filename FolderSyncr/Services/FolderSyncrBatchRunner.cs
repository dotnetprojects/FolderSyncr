using System.IO;
using System.Diagnostics;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class FolderSyncrBatchRunner(
    SyncEngine? syncEngine = null,
    FolderSyncrConfigurationStore? configurationStore = null,
    FreeFileSyncConfigurationImporter? freeFileSyncImporter = null,
    SyncRunHistoryStore? runHistoryStore = null)
{
    private readonly SyncEngine _syncEngine = syncEngine ?? new SyncEngine();
    private readonly FolderSyncrConfigurationStore _configurationStore = configurationStore ?? new FolderSyncrConfigurationStore();
    private readonly FreeFileSyncConfigurationImporter _freeFileSyncImporter = freeFileSyncImporter ?? new FreeFileSyncConfigurationImporter();
    private readonly SyncRunHistoryStore _runHistoryStore = runHistoryStore ?? new SyncRunHistoryStore();

    public async Task<BatchRunReport> RunAsync(BatchRunOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var warnings = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syncOptions = LoadOptions(options, out warnings);

            var operations = await _syncEngine.CompareAsync(syncOptions, progress, cancellationToken);
            var executable = operations.Where(operation => operation.ShouldExecute).ToList();
            if (!options.DryRun)
            {
                await _syncEngine.ExecuteAsync(operations, syncOptions, progress, cancellationToken);
            }

            var operationErrors = executable.Count(operation => string.Equals(operation.Status, "Error", StringComparison.OrdinalIgnoreCase));
            var processedOperations = options.DryRun
                ? executable
                : executable.Where(operation => string.Equals(operation.Status, "Done", StringComparison.OrdinalIgnoreCase)).ToList();
            stopwatch.Stop();
            var result = CreateResult(
                options.DryRun ? "dry-run" : operationErrors > 0 ? "error" : warnings > 0 ? "warning" : "success",
                startTime,
                stopwatch.Elapsed,
                errors: operationErrors,
                warnings,
                operations,
                processedOperations,
                message: options.DryRun
                    ? $"Dry run completed. {executable.Count} change(s) would be applied."
                    : operationErrors > 0
                        ? $"Batch synchronization completed with {operationErrors} item error(s)."
                        : $"Batch synchronization completed. {executable.Count} change(s) applied.");
            result = SaveResult(result, options.JsonOutputPath);

            return new BatchRunReport(operationErrors > 0 ? 2 : warnings > 0 ? 1 : 0, result);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var result = CreateResult("cancelled", startTime, stopwatch.Elapsed, errors: 0, warnings, [], [], "Batch synchronization was cancelled.");
            result = SaveResult(result, options.JsonOutputPath);
            return new BatchRunReport(3, result);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var result = CreateResult("error", startTime, stopwatch.Elapsed, errors: 1, warnings, [], [], exception.Message);
            result = SaveResult(result, options.JsonOutputPath);
            return new BatchRunReport(2, result);
        }
    }

    private SyncOptions LoadOptions(BatchRunOptions options, out int warnings)
    {
        if (string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            throw new ArgumentException("Pass a FolderSyncr or FreeFileSync configuration path.", nameof(options));
        }

        SyncOptions syncOptions;
        warnings = 0;
        if (IsNativeConfigurationPath(options.ConfigurationPath))
        {
            var configuration = _configurationStore.Load(options.ConfigurationPath);
            syncOptions = FromNative(configuration);
        }
        else
        {
            var configuration = _freeFileSyncImporter.Import(options.ConfigurationPath);
            warnings = configuration.Warnings.Count;
            syncOptions = FromFreeFileSync(configuration);
        }

        return CopyWithOverrides(syncOptions, options);
    }

    private static SyncOptions FromNative(FolderSyncrConfiguration configuration)
    {
        return new SyncOptions
        {
            LeftPath = configuration.LeftPath,
            RightPath = configuration.RightPath,
            Mode = configuration.SyncMode,
            CompareMethod = configuration.CompareMethod,
            FileTimeToleranceSeconds = configuration.FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = configuration.IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = configuration.VerifyCopiedFiles,
            DeletionHandling = configuration.DeletionHandling,
            VersioningMode = configuration.VersioningMode,
            VersioningFolderPath = configuration.VersioningFolderPath,
            ErrorHandling = configuration.ErrorHandling,
            SymbolicLinkHandling = configuration.SymbolicLinkHandling,
            IncludePatterns = configuration.IncludePatterns,
            ExcludePatterns = configuration.ExcludePatterns
        };
    }

    private static SyncOptions CopyWithOverrides(SyncOptions syncOptions, BatchRunOptions options)
    {
        return new SyncOptions
        {
            LeftPath = string.IsNullOrWhiteSpace(options.OverrideLeftPath) ? syncOptions.LeftPath : options.OverrideLeftPath,
            RightPath = string.IsNullOrWhiteSpace(options.OverrideRightPath) ? syncOptions.RightPath : options.OverrideRightPath,
            Mode = syncOptions.Mode,
            CompareMethod = syncOptions.CompareMethod,
            FileTimeToleranceSeconds = syncOptions.FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = syncOptions.IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = syncOptions.VerifyCopiedFiles,
            DeletionHandling = syncOptions.DeletionHandling,
            VersioningMode = syncOptions.VersioningMode,
            VersioningFolderPath = syncOptions.VersioningFolderPath,
            IncludePatterns = syncOptions.IncludePatterns,
            ExcludePatterns = syncOptions.ExcludePatterns,
            DryRun = options.DryRun,
            ErrorHandling = options.ErrorHandling ?? syncOptions.ErrorHandling,
            SymbolicLinkHandling = options.SymbolicLinkHandling ?? syncOptions.SymbolicLinkHandling
        };
    }

    private static SyncOptions FromFreeFileSync(FreeFileSyncConfiguration configuration)
    {
        var pair = configuration.FolderPairs.FirstOrDefault()
            ?? throw new InvalidDataException("The FreeFileSync configuration does not contain a folder pair.");

        return new SyncOptions
        {
            LeftPath = pair.LeftPath,
            RightPath = pair.RightPath,
            Mode = configuration.SyncMode ?? SyncMode.TwoWay,
            CompareMethod = configuration.CompareMethod ?? CompareMethod.TimeAndSize,
            FileTimeToleranceSeconds = 2,
            IncludePatterns = configuration.IncludePatterns,
            ExcludePatterns = configuration.ExcludePatterns
        };
    }

    private SyncRunResult SaveResult(SyncRunResult result, string? jsonOutputPath)
    {
        var historyPath = _runHistoryStore.Save(result);
        var savedResult = result with { LogFile = historyPath };
        if (!string.IsNullOrWhiteSpace(jsonOutputPath))
        {
            SyncRunJsonWriter.Write(jsonOutputPath, savedResult);
        }

        return savedResult;
    }

    private static SyncRunResult CreateResult(
        string syncResult,
        DateTimeOffset startTime,
        TimeSpan elapsed,
        int errors,
        int warnings,
        IReadOnlyList<SyncOperation> operations,
        IReadOnlyList<SyncOperation> processedOperations,
        string message)
    {
        return new SyncRunResult(
            syncResult,
            startTime,
            Math.Max(0, (int)Math.Round(elapsed.TotalSeconds)),
            errors,
            warnings,
            operations.Count,
            operations.Sum(GetOperationBytes),
            processedOperations.Count,
            processedOperations.Sum(GetOperationBytes),
            null,
            message);
    }

    private static long GetOperationBytes(SyncOperation operation)
    {
        return operation.Kind switch
        {
            OperationKind.CopyLeftToRight => operation.Left?.Length ?? 0,
            OperationKind.CopyRightToLeft => operation.Right?.Length ?? 0,
            OperationKind.DeleteLeft => operation.Left?.Length ?? 0,
            OperationKind.DeleteRight => operation.Right?.Length ?? 0,
            _ => operation.Left?.Length ?? operation.Right?.Length ?? 0
        };
    }

    private static bool IsNativeConfigurationPath(string path)
    {
        return path.EndsWith(".foldersyncr.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }
}
