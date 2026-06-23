using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Forms;
using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SyncEngine _syncEngine = new();
    private CancellationTokenSource? _cancellation;
    private string _leftPath = string.Empty;
    private string _rightPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.TwoWay;
    private CompareMethod _selectedCompareMethod = CompareMethod.TimeAndSize;
    private string _includePatterns = "*";
    private string _excludePatterns = "**/bin/**;**/obj/**;**/.git/**";
    private string _status = "Choose two folders, compare, then sync.";
    private bool _isBusy;

    public MainViewModel()
    {
        BrowseLeftCommand = new RelayCommand(() => BrowseAsync(isLeft: true), () => !IsBusy);
        BrowseRightCommand = new RelayCommand(() => BrowseAsync(isLeft: false), () => !IsBusy);
        CompareCommand = new RelayCommand(CompareAsync, CanRunFolderAction);
        SyncCommand = new RelayCommand(SyncAsync, () => CanRunFolderAction() && Operations.Any(operation => operation.WillChangeFileSystem));
        CancelCommand = new RelayCommand(CancelAsync, () => IsBusy);
    }

    public ObservableCollection<SyncOperation> Operations { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];
    public ObservableCollection<ConfigurationItem> Configurations { get; } =
    [
        new() { Name = "[Last session]", LastSync = "Today" },
        new() { Name = "Backup Projects", LastSync = "Today" },
        new() { Name = "Backup Documents", LastSync = "Yesterday" },
        new() { Name = "Mirror Archive", LastSync = "Never", IsHealthy = false },
    ];

    public ObservableCollection<OverviewItem> OverviewRows { get; } = [];

    public IReadOnlyList<SyncMode> SyncModes { get; } = Enum.GetValues<SyncMode>();
    public IReadOnlyList<CompareMethod> CompareMethods { get; } = Enum.GetValues<CompareMethod>();

    public RelayCommand BrowseLeftCommand { get; }
    public RelayCommand BrowseRightCommand { get; }
    public RelayCommand CompareCommand { get; }
    public RelayCommand SyncCommand { get; }
    public RelayCommand CancelCommand { get; }

    public string LeftPath
    {
        get => _leftPath;
        set
        {
            if (SetProperty(ref _leftPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string RightPath
    {
        get => _rightPath;
        set
        {
            if (SetProperty(ref _rightPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public SyncMode SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public CompareMethod SelectedCompareMethod
    {
        get => _selectedCompareMethod;
        set => SetProperty(ref _selectedCompareMethod, value);
    }

    public string IncludePatterns
    {
        get => _includePatterns;
        set => SetProperty(ref _includePatterns, value);
    }

    public string ExcludePatterns
    {
        get => _excludePatterns;
        set => SetProperty(ref _excludePatterns, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int ChangeCount => Operations.Count(operation => operation.WillChangeFileSystem);
    public int ConflictCount => Operations.Count(operation => operation.Kind == OperationKind.Conflict);
    public int TotalCount => Operations.Count;
    public int LeftFileCount => Operations.Count(operation => operation.Left is not null);
    public int RightFileCount => Operations.Count(operation => operation.Right is not null);

    private async Task BrowseAsync(bool isLeft)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = isLeft ? "Choose the left folder" : "Choose the right folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            if (isLeft)
            {
                LeftPath = dialog.SelectedPath;
            }
            else
            {
                RightPath = dialog.SelectedPath;
            }
        }

        await Task.CompletedTask;
    }

    private async Task CompareAsync()
    {
        await RunBusyAsync(async token =>
        {
            Operations.Clear();
            AddLog("Compare started.");

            var operations = await _syncEngine.CompareAsync(CreateOptions(dryRun: true), CreateProgress(), token);
            foreach (var operation in operations)
            {
                Operations.Add(operation);
            }

            OnOperationSummaryChanged();
            Status = $"Compare finished: {ChangeCount} change(s), {ConflictCount} conflict(s), {TotalCount} item(s).";
            AddLog(Status);
        });
    }

    private async Task SyncAsync()
    {
        await RunBusyAsync(async token =>
        {
            AddLog("Sync started.");
            await _syncEngine.ExecuteAsync(Operations.ToList(), CreateOptions(dryRun: false), CreateProgress(), token);

            var refreshed = await _syncEngine.CompareAsync(CreateOptions(dryRun: true), CreateProgress(), token);
            Operations.Clear();
            foreach (var operation in refreshed)
            {
                Operations.Add(operation);
            }

            OnOperationSummaryChanged();
            Status = $"Sync finished: {ChangeCount} change(s) remaining, {ConflictCount} conflict(s).";
            AddLog(Status);
        });
    }

    private Task CancelAsync()
    {
        _cancellation?.Cancel();
        Status = "Cancelling...";
        AddLog("Cancellation requested.");
        return Task.CompletedTask;
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            await action(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Operation cancelled.";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
            RaiseCommandStates();
        }
    }

    private SyncOptions CreateOptions(bool dryRun)
    {
        return new SyncOptions
        {
            LeftPath = LeftPath,
            RightPath = RightPath,
            Mode = SelectedMode,
            CompareMethod = SelectedCompareMethod,
            IncludePatterns = IncludePatterns,
            ExcludePatterns = ExcludePatterns,
            DryRun = dryRun
        };
    }

    private IProgress<string> CreateProgress()
    {
        return new Progress<string>(message =>
        {
            Status = message;
            AddLog(message);
        });
    }

    private bool CanRunFolderAction()
    {
        return !IsBusy
            && Directory.Exists(LeftPath)
            && Directory.Exists(RightPath)
            && !string.Equals(
                Path.GetFullPath(LeftPath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(RightPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private void AddLog(string message)
    {
        LogEntries.Insert(0, $"[{DateTime.Now:T}] {message}");
        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    private void OnOperationSummaryChanged()
    {
        OnPropertyChanged(nameof(ChangeCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(LeftFileCount));
        OnPropertyChanged(nameof(RightFileCount));
        RefreshOverview();
        SyncCommand.RaiseCanExecuteChanged();
    }

    private void RefreshOverview()
    {
        OverviewRows.Clear();
        var totalItems = Math.Max(Operations.Count, 1);
        var groups = Operations
            .GroupBy(operation => GetTopFolder(operation.RelativePath), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8);

        foreach (var group in groups)
        {
            var size = group.Sum(operation => operation.Left?.Length ?? operation.Right?.Length ?? 0);
            OverviewRows.Add(new OverviewItem
            {
                Folder = group.Key,
                Items = group.Count(),
                Size = FormatBytes(size),
                Percentage = group.Count() * 100d / totalItems
            });
        }
    }

    private static string GetTopFolder(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.IndexOf('/');
        return slash > 0 ? normalized[..slash] : "Files";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private void RaiseCommandStates()
    {
        BrowseLeftCommand.RaiseCanExecuteChanged();
        BrowseRightCommand.RaiseCanExecuteChanged();
        CompareCommand.RaiseCanExecuteChanged();
        SyncCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
