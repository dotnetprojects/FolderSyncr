using System.Diagnostics;
using System.IO;
using FolderSyncr.Models;

namespace FolderSyncr.Services;

public sealed class RealTimeSyncRunner(FolderSyncrBatchRunner? batchRunner = null)
{
    private static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromSeconds(10);
    private readonly FolderSyncrBatchRunner _batchRunner = batchRunner ?? new FolderSyncrBatchRunner();

    public async Task<BatchRunReport> RunAsync(
        BatchRunOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var report = await RunOnceAfterNextChangeAsync(options, progress, cancellationToken);
                if (report.ExitCode == 2)
                {
                    return report;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return CreateCancelledReport();
        }
    }

    public async Task<BatchRunReport> RunOnceAfterNextChangeAsync(
        BatchRunOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var temporaryEnvironment = new TemporaryEnvironmentScope(options.TemporaryVariables);
        var syncOptions = _batchRunner.LoadSyncOptions(options, out _);
        var watchedPaths = GetWatchedPaths(syncOptions);
        if (watchedPaths.Count == 0)
        {
            throw new DirectoryNotFoundException("No existing folder pair paths are available for realtime monitoring.");
        }

        using var changeTracker = new ChangeTracker();
        using var watchers = new WatcherSet(watchedPaths, changeTracker.OnChanged);
        progress?.Report($"Watching {watchedPaths.Count} folder(s).");

        var change = await changeTracker.WaitForIdleChangeAsync(
            options.WatchIdleDelay ?? DefaultIdleDelay,
            cancellationToken);

        progress?.Report($"Detected {change.Action}: {change.Path}");
        var variables = MergeVariables(options.TemporaryVariables, change);
        var runOptions = options with
        {
            DryRun = false,
            Watch = false,
            WatchIdleDelay = null,
            TemporaryVariables = variables
        };

        return await _batchRunner.RunAsync(runOptions, progress, cancellationToken);
    }

    private static IReadOnlyList<string> GetWatchedPaths(IReadOnlyList<SyncOptions> options)
    {
        return options
            .SelectMany(option => new[] { option.LeftPath, option.RightPath })
            .Select(PathMacroExpander.Expand)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> MergeVariables(
        IReadOnlyDictionary<string, string>? baseVariables,
        DetectedChange change)
    {
        var variables = new Dictionary<string, string>(baseVariables ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["change_path"] = change.Path,
            ["change_action"] = change.Action,
            ["changed_file"] = change.Path
        };
        return variables;
    }

    private static BatchRunReport CreateCancelledReport()
    {
        return new BatchRunReport(
            3,
            new SyncRunResult(
                "cancelled",
                DateTimeOffset.Now,
                0,
                Errors: 0,
                Warnings: 0,
                TotalItems: 0,
                TotalBytes: 0,
                ProcessedItems: 0,
                ProcessedBytes: 0,
                LogFile: null,
                Message: "Realtime monitoring was cancelled."));
    }

    private sealed record DetectedChange(string Path, string Action, long Ticks);

    private sealed class ChangeTracker : IDisposable
    {
        private readonly Lock _lock = new();
        private readonly TaskCompletionSource _firstChange = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DetectedChange? _lastChange;

        public void OnChanged(string path, WatcherChangeTypes changeType)
        {
            var action = changeType switch
            {
                WatcherChangeTypes.Created => "create",
                WatcherChangeTypes.Deleted => "delete",
                WatcherChangeTypes.Renamed => "update",
                _ => "update"
            };

            lock (_lock)
            {
                _lastChange = new DetectedChange(path, action, Stopwatch.GetTimestamp());
            }

            _firstChange.TrySetResult();
        }

        public async Task<DetectedChange> WaitForIdleChangeAsync(TimeSpan idleDelay, CancellationToken cancellationToken)
        {
            await _firstChange.Task.WaitAsync(cancellationToken);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(idleDelay, cancellationToken);

                DetectedChange? change;
                lock (_lock)
                {
                    change = _lastChange;
                }

                if (change is null)
                {
                    continue;
                }

                var elapsed = Stopwatch.GetElapsedTime(change.Ticks);
                if (elapsed >= idleDelay)
                {
                    return change;
                }
            }
        }

        public void Dispose()
        {
            _firstChange.TrySetCanceled();
        }
    }

    private sealed class WatcherSet : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers;

        public WatcherSet(IEnumerable<string> paths, Action<string, WatcherChangeTypes> onChanged)
        {
            _watchers = paths.Select(path => CreateWatcher(path, onChanged)).ToList();
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
        }

        private static FileSystemWatcher CreateWatcher(string path, Action<string, WatcherChangeTypes> onChanged)
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };

            watcher.Created += (_, args) => onChanged(args.FullPath, args.ChangeType);
            watcher.Changed += (_, args) => onChanged(args.FullPath, args.ChangeType);
            watcher.Deleted += (_, args) => onChanged(args.FullPath, args.ChangeType);
            watcher.Renamed += (_, args) => onChanged(args.FullPath, args.ChangeType);
            return watcher;
        }
    }
}
