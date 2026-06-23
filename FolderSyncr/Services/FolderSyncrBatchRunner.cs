using System.IO;
using System.Diagnostics;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class FolderSyncrBatchRunner(
    SyncEngine? syncEngine = null,
    FolderSyncrConfigurationStore? configurationStore = null,
    FreeFileSyncConfigurationImporter? freeFileSyncImporter = null,
    FreeFileSyncGlobalSettingsImporter? globalSettingsImporter = null,
    SyncRunHistoryStore? runHistoryStore = null)
{
    private readonly SyncEngine _syncEngine = syncEngine ?? new SyncEngine();
    private readonly FolderSyncrConfigurationStore _configurationStore = configurationStore ?? new FolderSyncrConfigurationStore();
    private readonly FreeFileSyncConfigurationImporter _freeFileSyncImporter = freeFileSyncImporter ?? new FreeFileSyncConfigurationImporter();
    private readonly FreeFileSyncGlobalSettingsImporter _globalSettingsImporter = globalSettingsImporter ?? new FreeFileSyncGlobalSettingsImporter();
    private readonly SyncRunHistoryStore _runHistoryStore = runHistoryStore ?? new SyncRunHistoryStore();

    public async Task<BatchRunReport> RunAsync(BatchRunOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var warnings = 0;

        try
        {
            using var temporaryEnvironment = new TemporaryEnvironmentScope(options.TemporaryVariables);
            cancellationToken.ThrowIfCancellationRequested();
            var syncOptionsList = LoadSyncOptions(options, out warnings);

            var allOperations = new List<SyncOperation>();
            var executable = new List<SyncOperation>();
            foreach (var syncOptions in syncOptionsList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Processing {syncOptions.LeftPath} <-> {syncOptions.RightPath}");
                var operations = await _syncEngine.CompareAsync(syncOptions, progress, cancellationToken);
                var pairExecutable = operations.Where(operation => operation.ShouldExecute).ToList();
                allOperations.AddRange(operations);
                executable.AddRange(pairExecutable);
                if (!options.DryRun)
                {
                    await _syncEngine.ExecuteAsync(operations, syncOptions, progress, cancellationToken);
                }
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
                allOperations,
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

    public IReadOnlyList<SyncOptions> LoadSyncOptions(BatchRunOptions options, out int warnings)
    {
        var configurationPaths = options.ConfigurationPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (configurationPaths.Length == 0)
        {
            throw new ArgumentException("Pass a FolderSyncr or FreeFileSync configuration path.", nameof(options));
        }

        var syncOptionsList = new List<SyncOptions>();
        FreeFileSyncGlobalSettings? globalSettings = null;
        warnings = 0;
        foreach (var configurationPath in configurationPaths)
        {
            if (_globalSettingsImporter.TryImport(configurationPath, out var importedGlobalSettings))
            {
                globalSettings = MergeGlobalSettings(globalSettings, importedGlobalSettings);
                continue;
            }

            if (IsNativeConfigurationPath(configurationPath))
            {
                var configuration = _configurationStore.Load(configurationPath);
                syncOptionsList.AddRange(FromNative(configuration));
            }
            else
            {
                var configuration = _freeFileSyncImporter.Import(configurationPath);
                warnings += configuration.Warnings.Count;
                syncOptionsList.AddRange(FromFreeFileSync(configuration));
            }
        }

        if (syncOptionsList.Count == 0)
        {
            throw new InvalidDataException("No FolderSyncr or FreeFileSync synchronization configuration was provided.");
        }

        if (globalSettings is not null)
        {
            syncOptionsList = syncOptionsList
                .Select(syncOptions => ApplyGlobalSettings(syncOptions, globalSettings))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(options.OverrideLeftPath) || !string.IsNullOrWhiteSpace(options.OverrideRightPath))
        {
            return [CopyWithOverrides(syncOptionsList[0], options)];
        }

        return syncOptionsList.Select(syncOptions => CopyWithOverrides(syncOptions, options)).ToArray();
    }

    private static FreeFileSyncGlobalSettings MergeGlobalSettings(
        FreeFileSyncGlobalSettings? current,
        FreeFileSyncGlobalSettings next)
    {
        return new FreeFileSyncGlobalSettings(
            next.FileTimeToleranceSeconds ?? current?.FileTimeToleranceSeconds,
            next.VerifyCopiedFiles ?? current?.VerifyCopiedFiles);
    }

    private static SyncOptions ApplyGlobalSettings(SyncOptions syncOptions, FreeFileSyncGlobalSettings settings)
    {
        return new SyncOptions
        {
            LeftPath = syncOptions.LeftPath,
            RightPath = syncOptions.RightPath,
            Mode = syncOptions.Mode,
            CustomRules = syncOptions.CustomRules,
            CompareMethod = syncOptions.CompareMethod,
            FileTimeToleranceSeconds = settings.FileTimeToleranceSeconds ?? syncOptions.FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = syncOptions.IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = settings.VerifyCopiedFiles ?? syncOptions.VerifyCopiedFiles,
            DeletionHandling = syncOptions.DeletionHandling,
            VersioningMode = syncOptions.VersioningMode,
            VersioningFolderPath = syncOptions.VersioningFolderPath,
            IncludePatterns = syncOptions.IncludePatterns,
            ExcludePatterns = syncOptions.ExcludePatterns,
            DryRun = syncOptions.DryRun,
            ErrorHandling = syncOptions.ErrorHandling,
            SymbolicLinkHandling = syncOptions.SymbolicLinkHandling
        };
    }

    private static IReadOnlyList<SyncOptions> FromNative(FolderSyncrConfiguration configuration)
    {
        var pairs = configuration.FolderPairs is { Count: > 0 }
            ? configuration.FolderPairs
            : [new FolderPairConfiguration(configuration.LeftPath, configuration.RightPath)];

        return pairs.Select(pair => CreateSyncOptions(configuration, pair)).ToArray();
    }

    private static SyncOptions CopyWithOverrides(SyncOptions syncOptions, BatchRunOptions options)
    {
        return new SyncOptions
        {
            LeftPath = string.IsNullOrWhiteSpace(options.OverrideLeftPath) ? syncOptions.LeftPath : options.OverrideLeftPath,
            RightPath = string.IsNullOrWhiteSpace(options.OverrideRightPath) ? syncOptions.RightPath : options.OverrideRightPath,
            Mode = syncOptions.Mode,
            CustomRules = syncOptions.CustomRules,
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

    private static IReadOnlyList<SyncOptions> FromFreeFileSync(FreeFileSyncConfiguration configuration)
    {
        if (configuration.FolderPairs.Count == 0)
        {
            throw new InvalidDataException("The FreeFileSync configuration does not contain a folder pair.");
        }

        return configuration.FolderPairs.Select(pair => new SyncOptions
        {
            LeftPath = pair.LeftPath,
            RightPath = pair.RightPath,
            Mode = configuration.SyncMode ?? SyncMode.TwoWay,
            CustomRules = CustomSyncRules.Default,
            CompareMethod = configuration.CompareMethod ?? CompareMethod.TimeAndSize,
            FileTimeToleranceSeconds = 2,
            IncludePatterns = string.IsNullOrWhiteSpace(pair.IncludePatterns) ? configuration.IncludePatterns : pair.IncludePatterns,
            ExcludePatterns = string.IsNullOrWhiteSpace(pair.ExcludePatterns) ? configuration.ExcludePatterns : pair.ExcludePatterns
        }).ToArray();
    }

    private static SyncOptions CreateSyncOptions(FolderSyncrConfiguration configuration, FolderPairConfiguration pair)
    {
        return new SyncOptions
        {
            LeftPath = pair.LeftPath,
            RightPath = pair.RightPath,
            Mode = configuration.SyncMode,
            CustomRules = configuration.CustomRules ?? CustomSyncRules.Default,
            CompareMethod = configuration.CompareMethod,
            FileTimeToleranceSeconds = configuration.FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = configuration.IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = configuration.VerifyCopiedFiles,
            DeletionHandling = configuration.DeletionHandling,
            VersioningMode = configuration.VersioningMode,
            VersioningFolderPath = configuration.VersioningFolderPath,
            ErrorHandling = configuration.ErrorHandling,
            SymbolicLinkHandling = configuration.SymbolicLinkHandling,
            IncludePatterns = string.IsNullOrWhiteSpace(pair.IncludePatterns) ? configuration.IncludePatterns : pair.IncludePatterns,
            ExcludePatterns = string.IsNullOrWhiteSpace(pair.ExcludePatterns) ? configuration.ExcludePatterns : pair.ExcludePatterns
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
