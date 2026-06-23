using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SyncEngine _syncEngine = new();
    private readonly FreeFileSyncConfigurationImporter _configurationImporter = new();
    private readonly FreeFileSyncConfigurationExporter _configurationExporter = new();
    private readonly FolderSyncrConfigurationStore _configurationStore = new();
    private readonly FreeFileSyncLogImporter _logImporter = new();
    private readonly SyncRunHistoryStore _runHistoryStore = new();
    private readonly SampleDataGenerator _sampleDataGenerator = new();
    private CancellationTokenSource? _cancellation;
    private string? _currentConfigurationPath;
    private List<FolderPairConfiguration> _folderPairs = [];
    private string _leftPath = string.Empty;
    private string _rightPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.TwoWay;
    private CompareMethod _selectedCompareMethod = CompareMethod.TimeAndSize;
    private int _fileTimeToleranceSeconds = 2;
    private bool _ignoreDaylightSavingTimeShift;
    private bool _verifyCopiedFiles;
    private DeletionHandling _selectedDeletionHandling = DeletionHandling.Permanent;
    private VersioningMode _selectedVersioningMode = VersioningMode.TimeStampFolder;
    private string _versioningFolderPath = string.Empty;
    private SyncErrorHandling _selectedErrorHandling = SyncErrorHandling.ShowErrors;
    private SymbolicLinkHandling _selectedSymbolicLinkHandling = SymbolicLinkHandling.Skip;
    private CustomSyncRules _customRules = CustomSyncRules.Default;
    private bool _useSynchronizationDatabase = true;
    private int _remoteConnectionCount = 1;
    private bool _sftpCompression;
    private bool _useVolumeShadowCopy;
    private string _includePatterns = "*";
    private string _excludePatterns = "**/bin/**;**/obj/**;**/.git/**";
    private string _status = "Choose two folders, compare, then sync.";
    private bool _isBusy;
    private bool _isDarkMode;
    private bool _isConfigurationVisible = true;
    private bool _isOverviewVisible = true;
    private OperationViewFilter _operationViewFilter = OperationViewFilter.All;
    private string? _overviewFolderFilter;
    private OverviewItem? _selectedOverviewItem;

    public MainViewModel()
    {
        OperationsView = CollectionViewSource.GetDefaultView(Operations);
        OperationsView.Filter = FilterOperation;

        BrowseLeftCommand = new RelayCommand(() => BrowseAsync(isLeft: true), () => !IsBusy);
        BrowseRightCommand = new RelayCommand(() => BrowseAsync(isLeft: false), () => !IsBusy);
        CompareCommand = new RelayCommand(CompareAsync, CanRunFolderAction);
        SyncCommand = new RelayCommand(SyncAsync, () => CanRunFolderAction() && Operations.Any(operation => operation.ShouldExecute));
        CancelCommand = new RelayCommand(CancelAsync, () => IsBusy);
        ToggleThemeCommand = new RelayCommand(ToggleThemeAsync);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsAsync(SettingsDialogTab.Compare));
        OpenFilterCommand = new RelayCommand(() => OpenSettingsAsync(SettingsDialogTab.Filter));
        OpenSynchronizationSettingsCommand = new RelayCommand(() => OpenSettingsAsync(SettingsDialogTab.Synchronization));
        OpenFolderPairsCommand = new RelayCommand(OpenFolderPairsAsync);
        OpenExternalCommandsCommand = new RelayCommand(OpenExternalCommandsAsync);
        SwapSidesCommand = new RelayCommand(SwapSidesAsync);
        CloudPathCommand = new RelayCommand(() => SetStatusAsync("Cloud path integration is not implemented yet. Use Browse or paste a local/cloud-synced folder path."));
        NewConfigurationCommand = new RelayCommand(NewConfigurationAsync);
        OpenConfigurationCommand = new RelayCommand(OpenConfigurationAsync);
        SaveConfigurationCommand = new RelayCommand(SaveConfigurationAsync);
        SaveAsConfigurationCommand = new RelayCommand(SaveAsConfigurationAsync);
        ExportFreeFileSyncConfigurationCommand = new RelayCommand(ExportFreeFileSyncConfigurationAsync);
        ReloadConfigurationCommand = new RelayCommand(ReloadConfigurationAsync);
        OpenFreeFileSyncLogCommand = new RelayCommand(OpenFreeFileSyncLogAsync);
        CreateSampleDataCommand = new RelayCommand(CreateSampleDataAsync);
        OpenDocumentationCommand = new RelayCommand(OpenDocumentationAsync);
        AboutCommand = new RelayCommand(AboutAsync);
        ExitCommand = new RelayCommand(ExitAsync);
        ShowConfigurationCommand = new RelayCommand(() => SetConfigurationVisibleAsync(true));
        CloseConfigurationCommand = new RelayCommand(() => SetConfigurationVisibleAsync(false));
        ShowOverviewCommand = new RelayCommand(() => SetOverviewVisibleAsync(true));
        CloseOverviewCommand = new RelayCommand(() => SetOverviewVisibleAsync(false));
        ShowAllOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.All));
        ShowChangeOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.Changes));
        ShowEqualOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.Equal));
        ShowCopyLeftToRightOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.CopyLeftToRight));
        ShowCopyRightToLeftOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.CopyRightToLeft));
        ShowDeleteLeftOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.DeleteLeft));
        ShowDeleteRightOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.DeleteRight));
        ShowConflictOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.Conflicts));
    }

    public ObservableCollection<SyncOperation> Operations { get; } = [];
    public ICollectionView OperationsView { get; }
    public ObservableCollection<string> LogEntries { get; } = [];
    public ObservableCollection<ConfigurationItem> Configurations { get; } =
    [
        new() { Name = "[Last session]", LastSync = "Today" },
        new() { Name = "Backup Projects", LastSync = "Today" },
        new() { Name = "Backup Documents", LastSync = "Yesterday" },
        new() { Name = "Mirror Archive", LastSync = "Never", IsHealthy = false },
    ];

    public ObservableCollection<OverviewItem> OverviewRows { get; } = [];
    public ObservableCollection<ExternalCommandDefinition> ExternalCommands { get; } = CreateDefaultExternalCommands();

    public OverviewItem? SelectedOverviewItem
    {
        get => _selectedOverviewItem;
        set
        {
            if (SetProperty(ref _selectedOverviewItem, value))
            {
                SetOverviewFolderFilter(value);
            }
        }
    }

    public IReadOnlyList<SyncMode> SyncModes { get; } = Enum.GetValues<SyncMode>();
    public IReadOnlyList<CompareMethod> CompareMethods { get; } = Enum.GetValues<CompareMethod>();
    public IReadOnlyList<DeletionHandling> DeletionHandlingModes { get; } = Enum.GetValues<DeletionHandling>();
    public IReadOnlyList<VersioningMode> VersioningModes { get; } = Enum.GetValues<VersioningMode>();
    public IReadOnlyList<SyncErrorHandling> ErrorHandlingModes { get; } = Enum.GetValues<SyncErrorHandling>();
    public IReadOnlyList<SymbolicLinkHandling> SymbolicLinkHandlingModes { get; } = Enum.GetValues<SymbolicLinkHandling>();
    public IReadOnlyList<CustomSyncAction> CustomSyncActions { get; } = Enum.GetValues<CustomSyncAction>();

    public RelayCommand BrowseLeftCommand { get; }
    public RelayCommand BrowseRightCommand { get; }
    public RelayCommand CompareCommand { get; }
    public RelayCommand SyncCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenFilterCommand { get; }
    public RelayCommand OpenSynchronizationSettingsCommand { get; }
    public RelayCommand OpenFolderPairsCommand { get; }
    public RelayCommand OpenExternalCommandsCommand { get; }
    public RelayCommand SwapSidesCommand { get; }
    public RelayCommand CloudPathCommand { get; }
    public RelayCommand NewConfigurationCommand { get; }
    public RelayCommand OpenConfigurationCommand { get; }
    public RelayCommand SaveConfigurationCommand { get; }
    public RelayCommand SaveAsConfigurationCommand { get; }
    public RelayCommand ExportFreeFileSyncConfigurationCommand { get; }
    public RelayCommand ReloadConfigurationCommand { get; }
    public RelayCommand OpenFreeFileSyncLogCommand { get; }
    public RelayCommand CreateSampleDataCommand { get; }
    public RelayCommand OpenDocumentationCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand ShowConfigurationCommand { get; }
    public RelayCommand CloseConfigurationCommand { get; }
    public RelayCommand ShowOverviewCommand { get; }
    public RelayCommand CloseOverviewCommand { get; }
    public RelayCommand ShowAllOperationsCommand { get; }
    public RelayCommand ShowChangeOperationsCommand { get; }
    public RelayCommand ShowEqualOperationsCommand { get; }
    public RelayCommand ShowCopyLeftToRightOperationsCommand { get; }
    public RelayCommand ShowCopyRightToLeftOperationsCommand { get; }
    public RelayCommand ShowDeleteLeftOperationsCommand { get; }
    public RelayCommand ShowDeleteRightOperationsCommand { get; }
    public RelayCommand ShowConflictOperationsCommand { get; }

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

    public int FileTimeToleranceSeconds
    {
        get => _fileTimeToleranceSeconds;
        set => SetProperty(ref _fileTimeToleranceSeconds, Math.Max(0, value));
    }

    public bool VerifyCopiedFiles
    {
        get => _verifyCopiedFiles;
        set => SetProperty(ref _verifyCopiedFiles, value);
    }

    public bool IgnoreDaylightSavingTimeShift
    {
        get => _ignoreDaylightSavingTimeShift;
        set => SetProperty(ref _ignoreDaylightSavingTimeShift, value);
    }

    public DeletionHandling SelectedDeletionHandling
    {
        get => _selectedDeletionHandling;
        set => SetProperty(ref _selectedDeletionHandling, value);
    }

    public string VersioningFolderPath
    {
        get => _versioningFolderPath;
        set => SetProperty(ref _versioningFolderPath, value);
    }

    public VersioningMode SelectedVersioningMode
    {
        get => _selectedVersioningMode;
        set => SetProperty(ref _selectedVersioningMode, value);
    }

    public SyncErrorHandling SelectedErrorHandling
    {
        get => _selectedErrorHandling;
        set => SetProperty(ref _selectedErrorHandling, value);
    }

    public SymbolicLinkHandling SelectedSymbolicLinkHandling
    {
        get => _selectedSymbolicLinkHandling;
        set => SetProperty(ref _selectedSymbolicLinkHandling, value);
    }

    public CustomSyncRules CustomRules
    {
        get => _customRules;
        set => SetProperty(ref _customRules, value);
    }

    public bool UseSynchronizationDatabase
    {
        get => _useSynchronizationDatabase;
        set => SetProperty(ref _useSynchronizationDatabase, value);
    }

    public int RemoteConnectionCount
    {
        get => _remoteConnectionCount;
        set => SetProperty(ref _remoteConnectionCount, Math.Max(1, value));
    }

    public bool SftpCompression
    {
        get => _sftpCompression;
        set => SetProperty(ref _sftpCompression, value);
    }

    public bool UseVolumeShadowCopy
    {
        get => _useVolumeShadowCopy;
        set => SetProperty(ref _useVolumeShadowCopy, value);
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

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                OnPropertyChanged(nameof(ThemeButtonText));
            }
        }
    }

    public string ThemeButtonText => IsDarkMode ? "Light mode" : "Dark mode";

    public bool IsConfigurationVisible
    {
        get => _isConfigurationVisible;
        private set
        {
            if (SetProperty(ref _isConfigurationVisible, value))
            {
                OnPropertyChanged(nameof(IsLeftPaneSplitterVisible));
            }
        }
    }

    public bool IsOverviewVisible
    {
        get => _isOverviewVisible;
        private set
        {
            if (SetProperty(ref _isOverviewVisible, value))
            {
                OnPropertyChanged(nameof(IsLeftPaneSplitterVisible));
            }
        }
    }

    public bool IsLeftPaneSplitterVisible => IsConfigurationVisible && IsOverviewVisible;

    public int ChangeCount => Operations.Count(operation => operation.ShouldExecute);
    public int EqualCount => CountOperations(OperationKind.Equal);
    public int CopyLeftToRightCount => CountOperations(OperationKind.CopyLeftToRight);
    public int CopyRightToLeftCount => CountOperations(OperationKind.CopyRightToLeft);
    public int DeleteLeftCount => CountOperations(OperationKind.DeleteLeft);
    public int DeleteRightCount => CountOperations(OperationKind.DeleteRight);
    public int ConflictCount => Operations.Count(operation => operation.EffectiveKind == OperationKind.Conflict);
    public int TotalCount => Operations.Count;
    public int LeftFileCount => Operations.Count(operation => operation.Left is not null);
    public int RightFileCount => Operations.Count(operation => operation.Right is not null);

    public Task ApplyStartupOptionsAsync(CommandLineStartupOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            if (!File.Exists(options.ConfigurationPath))
            {
                return SetStatusAsync($"Startup configuration was not found: {options.ConfigurationPath}");
            }

            OpenConfigurationFile(options.ConfigurationPath);
        }

        if (!string.IsNullOrWhiteSpace(options.OverrideLeftPath) || !string.IsNullOrWhiteSpace(options.OverrideRightPath))
        {
            if (!string.IsNullOrWhiteSpace(options.OverrideLeftPath))
            {
                LeftPath = options.OverrideLeftPath;
            }

            if (!string.IsNullOrWhiteSpace(options.OverrideRightPath))
            {
                RightPath = options.OverrideRightPath;
            }

            ClearOperations();
            OnOperationSummaryChanged();
            return SetStatusAsync("Startup folder pair override applied.");
        }

        return Task.CompletedTask;
    }

    private async Task BrowseAsync(bool isLeft)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = isLeft ? "Choose the left folder" : "Choose the right folder",
            Multiselect = false
        };

        var currentPath = isLeft ? LeftPath : RightPath;
        if (Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog() == true)
        {
            if (isLeft)
            {
                LeftPath = dialog.FolderName;
            }
            else
            {
                RightPath = dialog.FolderName;
            }
        }

        await Task.CompletedTask;
    }

    private async Task CompareAsync()
    {
        await RunBusyAsync(async token =>
        {
            ClearOperations();
            AddLog("Compare started.");

            var operations = await _syncEngine.CompareAsync(CreateOptions(dryRun: true), CreateProgress(), token);
            AddOperations(operations);

            OnOperationSummaryChanged();
            Status = $"Compare finished: {ChangeCount} change(s), {ConflictCount} conflict(s), {TotalCount} item(s).";
            AddLog(Status);
        });
    }

    private async Task SyncAsync()
    {
        await RunBusyAsync(async token =>
        {
            var startTime = DateTimeOffset.Now;
            var started = Stopwatch.StartNew();
            var executable = Operations.Where(operation => operation.ShouldExecute).ToList();
            var totalBytes = Operations.Sum(GetOperationBytes);
            var plannedBytes = executable.Sum(GetOperationBytes);
            var syncResult = "success";
            var errors = 0;
            string? message = null;

            AddLog("Sync started.");
            try
            {
                await _syncEngine.ExecuteAsync(Operations.ToList(), CreateOptions(dryRun: false), CreateProgress(), token);
            }
            catch (OperationCanceledException)
            {
                syncResult = "cancelled";
                message = "Operation cancelled.";
                throw;
            }
            catch (Exception ex)
            {
                syncResult = "error";
                errors = 1;
                message = ex.Message;
                throw;
            }
            finally
            {
                started.Stop();
                var operationErrors = executable.Count(operation => string.Equals(operation.Status, "Error", StringComparison.OrdinalIgnoreCase));
                if (operationErrors > 0 && syncResult == "success")
                {
                    syncResult = "error";
                    errors = operationErrors;
                    message = $"{operationErrors} item(s) failed.";
                }

                var processedItems = executable.Count(operation => string.Equals(operation.Status, "Done", StringComparison.OrdinalIgnoreCase));
                var processedBytes = executable
                    .Where(operation => string.Equals(operation.Status, "Done", StringComparison.OrdinalIgnoreCase))
                    .Sum(GetOperationBytes);
                var historyPath = _runHistoryStore.Save(new SyncRunResult(
                    syncResult,
                    startTime,
                    Math.Max(0, (int)Math.Round(started.Elapsed.TotalSeconds)),
                    errors,
                    0,
                    Operations.Count,
                    totalBytes,
                    processedItems,
                    syncResult == "success" ? plannedBytes : processedBytes,
                    null,
                    message));
                AddLog($"Run history saved: {historyPath}");
            }

            var refreshed = await _syncEngine.CompareAsync(CreateOptions(dryRun: true), CreateProgress(), token);
            ClearOperations();
            AddOperations(refreshed);

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

    private Task ToggleThemeAsync()
    {
        IsDarkMode = !IsDarkMode;
        ThemeManager.Apply(IsDarkMode);
        return Task.CompletedTask;
    }

    private Task OpenSettingsAsync(SettingsDialogTab initialTab)
    {
        var selectedMode = SelectedMode;
        var selectedCompareMethod = SelectedCompareMethod;
        var selectedDeletionHandling = SelectedDeletionHandling;

        var toleranceBox = new TextBox
        {
            Text = FileTimeToleranceSeconds.ToString(),
            Width = 90,
            Margin = new Thickness(0, 4, 12, 0)
        };

        var verifyBox = new CheckBox
        {
            Content = "Verify copied files by binary compare",
            IsChecked = VerifyCopiedFiles,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var dstBox = new CheckBox
        {
            Content = "Ignore one-hour daylight saving time shifts",
            IsChecked = IgnoreDaylightSavingTimeShift,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var versioningBox = new TextBox
        {
            Text = VersioningFolderPath,
            Margin = new Thickness(0, 4, 0, 10),
            MinWidth = 360
        };

        var versioningModeBox = new ComboBox
        {
            ItemsSource = VersioningModes,
            SelectedItem = SelectedVersioningMode,
            Margin = new Thickness(0, 4, 0, 10),
            MinWidth = 220
        };

        var errorHandlingBox = new ComboBox
        {
            ItemsSource = ErrorHandlingModes,
            SelectedItem = SelectedErrorHandling,
            Margin = new Thickness(0, 4, 0, 10),
            MinWidth = 220
        };

        var symbolicLinkBox = new ComboBox
        {
            ItemsSource = SymbolicLinkHandlingModes,
            SelectedItem = SelectedSymbolicLinkHandling,
            Margin = new Thickness(0, 4, 0, 10),
            MinWidth = 220
        };

        var remoteConnectionCountBox = new TextBox
        {
            Text = Math.Max(2, RemoteConnectionCount).ToString(),
            Width = 90,
            Margin = new Thickness(0, 4, 12, 0)
        };
        var parallelFileCopyBox = new CheckBox
        {
            Content = "Enable parallel file copy",
            IsChecked = RemoteConnectionCount > 1,
            Margin = new Thickness(0, 0, 0, 10)
        };
        remoteConnectionCountBox.IsEnabled = parallelFileCopyBox.IsChecked == true;
        parallelFileCopyBox.Checked += (_, _) => remoteConnectionCountBox.IsEnabled = true;
        parallelFileCopyBox.Unchecked += (_, _) => remoteConnectionCountBox.IsEnabled = false;

        var sftpCompressionBox = new CheckBox
        {
            Content = "Use SFTP compression",
            IsChecked = SftpCompression,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var volumeShadowCopyBox = new CheckBox
        {
            Content = "Use Volume Shadow Copy for locked local files",
            IsChecked = UseVolumeShadowCopy,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var syncDatabaseBox = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "Use synchronization database to detect changes, deletions, moves, and conflicts",
                TextWrapping = TextWrapping.Wrap
            },
            IsChecked = UseSynchronizationDatabase,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var includeBox = new TextBox
        {
            Text = IncludePatterns,
            AcceptsReturn = true,
            MinHeight = 120,
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var excludeBox = new TextBox
        {
            Text = ExcludePatterns,
            AcceptsReturn = true,
            MinHeight = 190,
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var leftOnlyRule = CustomRules.LeftOnly;
        var rightOnlyRule = CustomRules.RightOnly;
        var leftNewerRule = CustomRules.LeftNewer;
        var rightNewerRule = CustomRules.RightNewer;
        var differentRule = CustomRules.Different;

        var comparePanel = CreateSettingsScrollPanel(
            CreateSettingsGrid(
                CreateSettingsChoiceSection(
                    "Variant",
                    "Choose how files are compared before synchronization.",
                    CreateChoiceList(
                        [
                            (CompareMethod.TimeAndSize, "\uE823", "File time and size", "Fast default comparison using modified time and file size."),
                            (CompareMethod.ContentHash, "\uE8A5", "File content", "Read and hash file contents for byte-level comparison."),
                            (CompareMethod.SizeOnly, "\uE8B7", "File size", "Compare only file length when timestamps are unreliable.")
                        ],
                        () => selectedCompareMethod,
                        value => selectedCompareMethod = value)),
                CreateSettingsSection(
                    "Tolerances and links",
                    CreateVerticalStack(
                        CreateLabeledRow("File time tolerance in seconds", toleranceBox),
                        dstBox,
                        CreateLabeledRow("Symbolic links", symbolicLinkBox),
                        verifyBox)),
                CreateSettingsSection(
                    "Performance",
                    CreateVerticalStack(
                        parallelFileCopyBox,
                        CreateLabeledRow("Parallel file copy count", remoteConnectionCountBox),
                        sftpCompressionBox,
                        volumeShadowCopyBox))));

        var filterPanel = CreateSettingsScrollPanel(
            CreateSettingsGrid(
                CreateSettingsSection(
                    "Include",
                    CreateVerticalStack(
                        new TextBlock { Text = "One pattern per line or separated by |." },
                        includeBox)),
                CreateSettingsSection(
                    "Exclude",
                    CreateVerticalStack(
                        new TextBlock { Text = "Exclude files or folders relative to the folder pair." },
                        excludeBox)),
                CreateSettingsSection(
                    "Filter hints",
                    CreateVerticalStack(
                        new TextBlock { Text = "* and ? wildcards are supported." },
                        new TextBlock { Text = "Use a trailing slash for folder-only filters." },
                        new TextBlock { Text = "Use : for file-only filters." }))));

        var syncPanel = CreateSynchronizationSettingsPanel(
            () => selectedMode,
            value => selectedMode = value,
            syncDatabaseBox,
            () => leftOnlyRule,
            value => leftOnlyRule = value,
            () => rightOnlyRule,
            value => rightOnlyRule = value,
            () => leftNewerRule,
            value => leftNewerRule = value,
            () => rightNewerRule,
            value => rightNewerRule = value,
            () => differentRule,
            value => differentRule = value,
            () => selectedDeletionHandling,
            value => selectedDeletionHandling = value,
            versioningModeBox,
            versioningBox,
            errorHandlingBox);

        var tabs = new TabControl
        {
            MinWidth = 790,
            MinHeight = 460,
            Margin = new Thickness(14)
        };
        tabs.Items.Add(CreateSettingsTab("\uE713", "Compare (F6)", comparePanel));
        tabs.Items.Add(CreateSettingsTab("\uE71C", "Filter (F7)", filterPanel));
        tabs.Items.Add(CreateSettingsTab("\uE72C", "Synchronization (F8)", syncPanel));
        tabs.SelectedIndex = initialTab switch
        {
            SettingsDialogTab.Filter => 1,
            SettingsDialogTab.Synchronization => 2,
            _ => 0
        };

        if (ShowDialog("Synchronization settings", tabs, resizable: true, width: 1040, height: 640))
        {
            SelectedMode = selectedMode;
            SelectedCompareMethod = selectedCompareMethod;
            if (!int.TryParse(toleranceBox.Text, out var toleranceSeconds) || toleranceSeconds < 0)
            {
                toleranceSeconds = 2;
            }

            FileTimeToleranceSeconds = toleranceSeconds;
            IgnoreDaylightSavingTimeShift = dstBox.IsChecked == true;
            VerifyCopiedFiles = verifyBox.IsChecked == true;
            SelectedDeletionHandling = selectedDeletionHandling;
            SelectedVersioningMode = (VersioningMode)versioningModeBox.SelectedItem;
            SelectedErrorHandling = (SyncErrorHandling)errorHandlingBox.SelectedItem;
            SelectedSymbolicLinkHandling = (SymbolicLinkHandling)symbolicLinkBox.SelectedItem;
            VersioningFolderPath = versioningBox.Text.Trim();
            if (!int.TryParse(remoteConnectionCountBox.Text, out var remoteConnections) || remoteConnections < 2)
            {
                remoteConnections = 2;
            }

            RemoteConnectionCount = parallelFileCopyBox.IsChecked == true ? remoteConnections : 1;
            SftpCompression = sftpCompressionBox.IsChecked == true;
            UseVolumeShadowCopy = volumeShadowCopyBox.IsChecked == true;
            UseSynchronizationDatabase = syncDatabaseBox.IsChecked == true;
            IncludePatterns = string.IsNullOrWhiteSpace(includeBox.Text) ? "*" : includeBox.Text.Trim();
            ExcludePatterns = excludeBox.Text.Trim();
            CustomRules = new CustomSyncRules(
                leftOnlyRule,
                rightOnlyRule,
                leftNewerRule,
                rightNewerRule,
                differentRule);
            SetStatusAsync($"Settings updated: {SelectedMode}, {SelectedCompareMethod}, {SelectedDeletionHandling}, {SelectedErrorHandling}.").GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    private static TabItem CreateSettingsTab(string icon, string title, UIElement content)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 4, 0)
        };
        header.Children.Add(new TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        return new TabItem
        {
            Header = header,
            Content = content
        };
    }

    private static ScrollViewer CreateSettingsScrollPanel(UIElement content)
    {
        return new ScrollViewer
        {
            Content = content,
            MaxHeight = 610,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static Grid CreateSettingsGrid(params UIElement[] sections)
    {
        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < sections.Length; index++)
        {
            var row = index / 2;
            if (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var section = sections[index];
            Grid.SetRow(section, row);
            Grid.SetColumn(section, index % 2);
            grid.Children.Add(section);
        }

        return grid;
    }

    private static Border CreateSettingsChoiceSection(string title, string description, UIElement choices)
    {
        return CreateSettingsSection(
            title,
            CreateVerticalStack(
                new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                },
                choices));
    }

    private static Border CreateSettingsSection(string title, UIElement body)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        content.Children.Add(body);

        var border = new Border
        {
            Child = content,
            Margin = new Thickness(8),
            Padding = new Thickness(14),
            MinHeight = 160,
            BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
        return border;
    }

    private static Border CreateCompactSettingsSection(string title, UIElement body)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(body);

        var border = new Border
        {
            Child = content,
            Margin = new Thickness(0),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
        return border;
    }

    private static StackPanel CreateVerticalStack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private static StackPanel CreateLabeledRow(string label, UIElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        panel.Children.Add(control);
        return panel;
    }

    private static Grid CreateSynchronizationSettingsPanel(
        Func<SyncMode> getMode,
        Action<SyncMode> setMode,
        CheckBox syncDatabaseBox,
        Func<CustomSyncAction> getLeftOnly,
        Action<CustomSyncAction> setLeftOnly,
        Func<CustomSyncAction> getRightOnly,
        Action<CustomSyncAction> setRightOnly,
        Func<CustomSyncAction> getLeftNewer,
        Action<CustomSyncAction> setLeftNewer,
        Func<CustomSyncAction> getRightNewer,
        Action<CustomSyncAction> setRightNewer,
        Func<CustomSyncAction> getDifferent,
        Action<CustomSyncAction> setDifferent,
        Func<DeletionHandling> getDeletionHandling,
        Action<DeletionHandling> setDeletionHandling,
        ComboBox versioningModeBox,
        TextBox versioningBox,
        ComboBox errorHandlingBox)
    {
        var root = new Grid { Margin = new Thickness(8, 10, 8, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ruleHost = new ContentControl();
        var variantChoiceHost = new ContentControl();

        void RefreshRules()
        {
            ruleHost.Content = CreateFreeFileSyncRuleMatrix(
                syncDatabaseBox.IsChecked == true,
                getLeftOnly,
                setLeftOnly,
                getRightOnly,
                setRightOnly,
                getLeftNewer,
                setLeftNewer,
                getRightNewer,
                setRightNewer,
                getDifferent,
                setDifferent,
                MarkCustom);
        }

        void RefreshVariants()
        {
            variantChoiceHost.Content = CreateCompactChoiceList(
                [
                    (SyncMode.TwoWay, "<=>", "Two way", string.Empty),
                    (SyncMode.MirrorLeftToRight, "=>", "Mirror left to right", string.Empty),
                    (SyncMode.MirrorRightToLeft, "<=", "Mirror right to left", string.Empty),
                    (SyncMode.UpdateLeftToRight, "^>", "Update left to right", string.Empty),
                    (SyncMode.UpdateRightToLeft, "<v", "Update right to left", string.Empty),
                    (SyncMode.Custom, "<>", "Custom", string.Empty)
                ],
                getMode,
                ApplyVariant);
        }

        void ApplyVariant(SyncMode mode)
        {
            setMode(mode);
            ApplySynchronizationPreset(
                mode,
                setLeftOnly,
                setRightOnly,
                setLeftNewer,
                setRightNewer,
                setDifferent);
            RefreshRules();
        }

        void MarkCustom()
        {
            if (getMode() == SyncMode.Custom)
            {
                return;
            }

            setMode(SyncMode.Custom);
            RefreshVariants();
        }

        RefreshVariants();

        var variantPanel = CreateVerticalStack(
            new TextBlock
            {
                Text = "Variant",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            },
            variantChoiceHost);

        syncDatabaseBox.Checked += (_, _) => RefreshRules();
        syncDatabaseBox.Unchecked += (_, _) => RefreshRules();
        RefreshRules();

        var databasePanel = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        databasePanel.Children.Add(new TextBlock
        {
            Text = "\uE1DB",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 28,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        databasePanel.Children.Add(syncDatabaseBox);
        databasePanel.Children.Add(new TextBlock
        {
            Text = "When enabled, FolderSyncr can detect deletions, moved files, and two-sided conflicts. Without it, synchronization uses the four visible difference actions.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var moveDetectionPanel = new Border
        {
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "<>",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Width = 44,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Detect moved files",
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        moveDetectionPanel.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
        moveDetectionPanel.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
        databasePanel.Children.Add(moveDetectionPanel);

        var rightTop = new Grid();
        rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rightTop.Children.Add(databasePanel);
        Grid.SetColumn(ruleHost, 1);
        rightTop.Children.Add(ruleHost);

        Grid.SetRow(variantPanel, 0);
        Grid.SetColumn(variantPanel, 0);
        root.Children.Add(variantPanel);
        Grid.SetRow(rightTop, 0);
        Grid.SetColumn(rightTop, 2);
        root.Children.Add(rightTop);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 14, 0, 14)
        };
        separator.SetResourceReference(Border.BackgroundProperty, "BorderBrushSoft");
        Grid.SetRow(separator, 1);
        Grid.SetColumnSpan(separator, 3);
        root.Children.Add(separator);

        var deletionPanel = CreateCompactSettingsSection(
            "Delete and overwrite",
            CreateCompactChoiceList(
                [
                    (DeletionHandling.RecycleBin, "\uE74D", "Recycle bin", string.Empty),
                    (DeletionHandling.Permanent, "\uE74D", "Permanent", string.Empty),
                    (DeletionHandling.VersioningFolder, "\uE8A5", "Versioning", string.Empty)
                ],
                getDeletionHandling,
                setDeletionHandling));
        var advancedPanel = CreateCompactSettingsSection(
            "Advanced",
            CreateTwoColumnForm(
                ("Versioning mode", versioningModeBox),
                ("Versioning folder", versioningBox),
                ("Error handling", errorHandlingBox)));

        Grid.SetRow(deletionPanel, 2);
        Grid.SetColumn(deletionPanel, 0);
        root.Children.Add(deletionPanel);
        Grid.SetRow(advancedPanel, 2);
        Grid.SetColumn(advancedPanel, 2);
        root.Children.Add(advancedPanel);

        return root;
    }

    private static void ApplySynchronizationPreset(
        SyncMode mode,
        Action<CustomSyncAction> setLeftOnly,
        Action<CustomSyncAction> setRightOnly,
        Action<CustomSyncAction> setLeftNewer,
        Action<CustomSyncAction> setRightNewer,
        Action<CustomSyncAction> setDifferent)
    {
        switch (mode)
        {
            case SyncMode.TwoWay:
                setLeftOnly(CustomSyncAction.CopyLeftToRight);
                setRightOnly(CustomSyncAction.CopyRightToLeft);
                setLeftNewer(CustomSyncAction.CopyLeftToRight);
                setRightNewer(CustomSyncAction.CopyRightToLeft);
                setDifferent(CustomSyncAction.DoNothing);
                break;
            case SyncMode.MirrorLeftToRight:
                setLeftOnly(CustomSyncAction.CopyLeftToRight);
                setRightOnly(CustomSyncAction.DeleteRight);
                setLeftNewer(CustomSyncAction.CopyLeftToRight);
                setRightNewer(CustomSyncAction.CopyLeftToRight);
                setDifferent(CustomSyncAction.CopyLeftToRight);
                break;
            case SyncMode.MirrorRightToLeft:
                setLeftOnly(CustomSyncAction.DeleteLeft);
                setRightOnly(CustomSyncAction.CopyRightToLeft);
                setLeftNewer(CustomSyncAction.CopyRightToLeft);
                setRightNewer(CustomSyncAction.CopyRightToLeft);
                setDifferent(CustomSyncAction.CopyRightToLeft);
                break;
            case SyncMode.UpdateLeftToRight:
                setLeftOnly(CustomSyncAction.CopyLeftToRight);
                setRightOnly(CustomSyncAction.DoNothing);
                setLeftNewer(CustomSyncAction.CopyLeftToRight);
                setRightNewer(CustomSyncAction.DoNothing);
                setDifferent(CustomSyncAction.DoNothing);
                break;
            case SyncMode.UpdateRightToLeft:
                setLeftOnly(CustomSyncAction.DoNothing);
                setRightOnly(CustomSyncAction.CopyRightToLeft);
                setLeftNewer(CustomSyncAction.DoNothing);
                setRightNewer(CustomSyncAction.CopyRightToLeft);
                setDifferent(CustomSyncAction.DoNothing);
                break;
        }
    }

    private static StackPanel CreateCompactChoiceList<T>(
        IReadOnlyList<(T Value, string Icon, string Title, string Description)> choices,
        Func<T> getSelected,
        Action<T> setSelected)
        where T : notnull
    {
        var panel = new StackPanel();
        var groupName = $"CompactChoice{Guid.NewGuid():N}";
        var controls = new List<(RadioButton Button, Border Border, TextBlock Icon, TextBlock Title, TextBlock Description)>();
        foreach (var choice in choices)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new TextBlock
            {
                Text = choice.Icon,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var title = new TextBlock
            {
                Text = choice.Title,
                FontWeight = FontWeights.SemiBold
            };
            var description = new TextBlock
            {
                Text = choice.Description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Visibility = string.IsNullOrWhiteSpace(choice.Description) ? Visibility.Collapsed : Visibility.Visible
            };
            var text = new StackPanel();
            text.Children.Add(title);
            text.Children.Add(description);
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(icon);
            row.Children.Add(text);

            var border = new Border
            {
                Child = row,
                Padding = new Thickness(8, 5, 8, 5),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            var button = new RadioButton
            {
                GroupName = groupName,
                Content = border,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = EqualityComparer<T>.Default.Equals(choice.Value, getSelected())
            };
            controls.Add((button, border, icon, title, description));
            button.Checked += (_, _) =>
            {
                setSelected(choice.Value);
                UpdateChoiceStyles(controls);
            };
            button.Unchecked += (_, _) => UpdateChoiceStyles(controls);
            panel.Children.Add(button);
        }

        UpdateChoiceStyles(controls);
        return panel;
    }

    private static Grid CreateTwoColumnForm(params (string Label, FrameworkElement Control)[] rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < rows.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Text = rows[index].Label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 12, 12),
                VerticalAlignment = VerticalAlignment.Top
            };
            var control = rows[index].Control;
            control.Margin = new Thickness(0, 2, 0, 10);

            Grid.SetRow(label, index);
            Grid.SetColumn(label, 0);
            Grid.SetRow(control, index);
            Grid.SetColumn(control, 1);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }

        return grid;
    }

    private static Grid CreateCustomRuleMatrix(params (string Label, string Description, Func<CustomSyncAction> GetSelected, Action<CustomSyncAction> SetSelected, IReadOnlyList<CustomSyncAction> Actions)[] rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < rows.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 12, 12)
            };
            labelPanel.Children.Add(new TextBlock
            {
                Text = rows[index].Label,
                FontWeight = FontWeights.SemiBold
            });
            labelPanel.Children.Add(new TextBlock
            {
                Text = rows[index].Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            });

            var actionsPanel = CreateRuleActionButtons(rows[index].Actions, rows[index].GetSelected, rows[index].SetSelected);
            actionsPanel.Margin = new Thickness(0, 0, 0, 12);

            Grid.SetRow(labelPanel, index);
            Grid.SetColumn(labelPanel, 0);
            Grid.SetRow(actionsPanel, index);
            Grid.SetColumn(actionsPanel, 1);
            grid.Children.Add(labelPanel);
            grid.Children.Add(actionsPanel);
        }

        return grid;
    }

    private static Grid CreateFreeFileSyncRuleMatrix(
        bool useDatabase,
        Func<CustomSyncAction> getLeftOnly,
        Action<CustomSyncAction> setLeftOnly,
        Func<CustomSyncAction> getRightOnly,
        Action<CustomSyncAction> setRightOnly,
        Func<CustomSyncAction> getLeftNewer,
        Action<CustomSyncAction> setLeftNewer,
        Func<CustomSyncAction> getRightNewer,
        Action<CustomSyncAction> setRightNewer,
        Func<CustomSyncAction> getDifferent,
        Action<CustomSyncAction> setDifferent,
        Action onManualEdit)
    {
        return useDatabase
            ? CreateDatabaseRuleMatrix(getLeftOnly, setLeftOnly, getRightOnly, setRightOnly, getLeftNewer, setLeftNewer, getRightNewer, setRightNewer, onManualEdit)
            : CreateNoDatabaseRuleMatrix(getLeftOnly, setLeftOnly, getLeftNewer, setLeftNewer, getRightNewer, setRightNewer, getRightOnly, setRightOnly, onManualEdit);
    }

    private static Grid CreateDatabaseRuleMatrix(
        Func<CustomSyncAction> getLeftOnly,
        Action<CustomSyncAction> setLeftOnly,
        Func<CustomSyncAction> getRightOnly,
        Action<CustomSyncAction> setRightOnly,
        Func<CustomSyncAction> getLeftNewer,
        Action<CustomSyncAction> setLeftNewer,
        Func<CustomSyncAction> getRightNewer,
        Action<CustomSyncAction> setRightNewer,
        Action onManualEdit)
    {
        var grid = CreateRuleMatrixShell();
        AddRuleHeader(grid, 1, "Left");
        AddRuleHeader(grid, 2, "Right");
        AddRuleRowLabel(grid, 1, "Create");
        AddRuleRowLabel(grid, 2, "Update");
        AddRuleRowLabel(grid, 3, "Delete");
        AddRuleButtonGroup(grid, [(1, 1, CustomSyncAction.CopyLeftToRight), (3, 1, CustomSyncAction.DeleteLeft)], getLeftOnly, setLeftOnly, onManualEdit);
        AddRuleButtonGroup(grid, [(1, 2, CustomSyncAction.CopyRightToLeft), (3, 2, CustomSyncAction.DeleteRight)], getRightOnly, setRightOnly, onManualEdit);
        AddRuleButtonGroup(grid, [(2, 1, CustomSyncAction.CopyLeftToRight)], getLeftNewer, setLeftNewer, onManualEdit);
        AddRuleButtonGroup(grid, [(2, 2, CustomSyncAction.CopyRightToLeft)], getRightNewer, setRightNewer, onManualEdit);
        return grid;
    }

    private static Grid CreateNoDatabaseRuleMatrix(
        Func<CustomSyncAction> getLeftOnly,
        Action<CustomSyncAction> setLeftOnly,
        Func<CustomSyncAction> getLeftNewer,
        Action<CustomSyncAction> setLeftNewer,
        Func<CustomSyncAction> getRightNewer,
        Action<CustomSyncAction> setRightNewer,
        Func<CustomSyncAction> getRightOnly,
        Action<CustomSyncAction> setRightOnly,
        Action onManualEdit)
    {
        var grid = CreateRuleMatrixShell();
        AddRuleHeader(grid, 1, "Left only");
        AddRuleHeader(grid, 2, "Newer left");
        AddRuleHeader(grid, 3, "Newer right");
        AddRuleHeader(grid, 4, "Right only");
        AddRuleRowLabel(grid, 1, "Action");
        AddRuleCycleButton(grid, 1, 1, getLeftOnly, setLeftOnly, [CustomSyncAction.CopyLeftToRight, CustomSyncAction.DoNothing, CustomSyncAction.DeleteLeft], onManualEdit);
        AddRuleCycleButton(grid, 1, 2, getLeftNewer, setLeftNewer, [CustomSyncAction.CopyLeftToRight, CustomSyncAction.DoNothing, CustomSyncAction.CopyRightToLeft], onManualEdit);
        AddRuleCycleButton(grid, 1, 3, getRightNewer, setRightNewer, [CustomSyncAction.CopyRightToLeft, CustomSyncAction.DoNothing, CustomSyncAction.CopyLeftToRight], onManualEdit);
        AddRuleCycleButton(grid, 1, 4, getRightOnly, setRightOnly, [CustomSyncAction.CopyRightToLeft, CustomSyncAction.DoNothing, CustomSyncAction.DeleteRight], onManualEdit);
        return grid;
    }

    private static Grid CreateRuleMatrixShell()
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var column = 0; column < 4; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var row = 0; row < 3; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    private static void AddRuleHeader(Grid grid, int column, string text)
    {
        var header = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 6, 5),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(header, 0);
        Grid.SetColumn(header, column);
        grid.Children.Add(header);
    }

    private static void AddRuleRowLabel(Grid grid, int row, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }

    private static void AddRuleButtonGroup(
        Grid grid,
        IReadOnlyList<(int Row, int Column, CustomSyncAction Action)> placements,
        Func<CustomSyncAction> getSelected,
        Action<CustomSyncAction> setSelected,
        Action onManualEdit)
    {
        var groupName = $"RuleMatrix{Guid.NewGuid():N}";
        var controls = new List<(RadioButton Button, Border Border, TextBlock Glyph, TextBlock Label)>();
        foreach (var placement in placements)
        {
            var button = CreateRuleActionButton(placement.Action, getSelected, setSelected, groupName, controls, onManualEdit);
            button.Margin = new Thickness(0, 0, 5, 7);
            Grid.SetRow(button, placement.Row);
            Grid.SetColumn(button, placement.Column);
            grid.Children.Add(button);
        }

        UpdateRuleActionStyles(controls);
    }

    private static void AddRuleCycleButton(
        Grid grid,
        int row,
        int column,
        Func<CustomSyncAction> getSelected,
        Action<CustomSyncAction> setSelected,
        IReadOnlyList<CustomSyncAction> actions,
        Action onManualEdit)
    {
        var glyphText = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var border = new Border
        {
            Child = new Grid { Children = { glyphText } },
            Width = 58,
            Height = 50,
            Padding = new Thickness(6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        var button = new Button
        {
            Content = border,
            Margin = new Thickness(0, 0, 5, 7)
        };

        void Update()
        {
            var selected = getSelected();
            var (glyph, label, brushKey) = GetCustomRuleActionPresentation(selected);
            glyphText.Text = glyph;
            glyphText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            border.SetResourceReference(Border.BackgroundProperty, "SelectionBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "SelectionBrush");
            button.ToolTip = $"{label}. Click to cycle this rule.";
        }

        button.Click += (_, _) =>
        {
            var selected = getSelected();
            var index = -1;
            for (var actionIndex = 0; actionIndex < actions.Count; actionIndex++)
            {
                if (actions[actionIndex] == selected)
                {
                    index = actionIndex;
                    break;
                }
            }

            var next = actions[(index + 1) % actions.Count];
            setSelected(next);
            onManualEdit();
            Update();
        };

        Update();
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private static StackPanel CreateRuleActionButtons(
        IReadOnlyList<CustomSyncAction> actions,
        Func<CustomSyncAction> getSelected,
        Action<CustomSyncAction> setSelected)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var groupName = $"RuleAction{Guid.NewGuid():N}";
        var controls = new List<(RadioButton Button, Border Border, TextBlock Glyph, TextBlock Label)>();

        foreach (var action in actions)
        {
            var button = CreateRuleActionButton(action, getSelected, setSelected, groupName, controls, () => { });
            button.Margin = new Thickness(0, 0, 6, 0);
            panel.Children.Add(button);
        }

        UpdateRuleActionStyles(controls);
        return panel;
    }

    private static RadioButton CreateRuleActionButton(
        CustomSyncAction action,
        Func<CustomSyncAction> getSelected,
        Action<CustomSyncAction> setSelected,
        string groupName,
        List<(RadioButton Button, Border Border, TextBlock Glyph, TextBlock Label)> controls,
        Action onManualEdit)
    {
        var (glyph, label, brushKey) = GetCustomRuleActionPresentation(action);
        var glyphText = new TextBlock
        {
            Text = glyph,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        glyphText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var content = new Grid();
        content.Children.Add(glyphText);

        var border = new Border
        {
            Child = content,
            Width = 58,
            Height = 50,
            Padding = new Thickness(6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };

        var button = new RadioButton
        {
            GroupName = groupName,
            Content = border,
            ToolTip = label,
            IsChecked = getSelected() == action
        };
        controls.Add((button, border, glyphText, labelText));
        button.Checked += (_, _) =>
        {
            setSelected(action);
            onManualEdit();
            UpdateRuleActionStyles(controls);
        };
        button.Unchecked += (_, _) => UpdateRuleActionStyles(controls);
        return button;
    }

    private static (string Glyph, string Label, string BrushKey) GetCustomRuleActionPresentation(CustomSyncAction action)
    {
        return action switch
        {
            CustomSyncAction.CopyLeftToRight => ("=>", "Copy right", "DarkGreenBrush"),
            CustomSyncAction.CopyRightToLeft => ("<=", "Copy left", "DarkGreenBrush"),
            CustomSyncAction.DeleteLeft => ("X<", "Delete left", "DeleteBrush"),
            CustomSyncAction.DeleteRight => (">X", "Delete right", "DeleteBrush"),
            _ => ("--", "No action", "TextBrush")
        };
    }

    private static void UpdateRuleActionStyles(IEnumerable<(RadioButton Button, Border Border, TextBlock Glyph, TextBlock Label)> controls)
    {
        foreach (var control in controls)
        {
            if (control.Button.IsChecked == true)
            {
                control.Border.SetResourceReference(Border.BackgroundProperty, "SelectionBrush");
                control.Border.SetResourceReference(Border.BorderBrushProperty, "SelectionBrush");
                control.Label.SetResourceReference(TextBlock.ForegroundProperty, "SelectionTextBrush");
            }
            else
            {
                control.Border.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
                control.Border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
                control.Label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }
        }
    }

    private static StackPanel CreateChoiceList<T>(
        IReadOnlyList<(T Value, string Icon, string Title, string Description)> choices,
        Func<T> getSelected,
        Action<T> setSelected)
        where T : notnull
    {
        var panel = new StackPanel();
        var groupName = $"SettingsChoice{Guid.NewGuid():N}";
        var controls = new List<(RadioButton Button, Border Border, TextBlock Icon, TextBlock Title, TextBlock Description)>();

        foreach (var choice in choices)
        {
            var icon = new TextBlock
            {
                Text = choice.Icon,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Width = 34,
                VerticalAlignment = VerticalAlignment.Center
            };
            var title = new TextBlock
            {
                Text = choice.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            };
            var description = new TextBlock
            {
                Text = choice.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            var text = new StackPanel();
            text.Children.Add(title);
            text.Children.Add(description);

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            layout.Children.Add(icon);
            layout.Children.Add(text);

            var border = new Border
            {
                Child = layout,
                Padding = new Thickness(10),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            var button = new RadioButton
            {
                GroupName = groupName,
                Content = border,
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = EqualityComparer<T>.Default.Equals(choice.Value, getSelected())
            };

            controls.Add((button, border, icon, title, description));
            button.Checked += (_, _) =>
            {
                setSelected(choice.Value);
                UpdateChoiceStyles(controls);
            };
            button.Unchecked += (_, _) => UpdateChoiceStyles(controls);
            panel.Children.Add(button);
        }

        UpdateChoiceStyles(controls);
        return panel;
    }

    private static void UpdateChoiceStyles(IEnumerable<(RadioButton Button, Border Border, TextBlock Icon, TextBlock Title, TextBlock Description)> controls)
    {
        foreach (var control in controls)
        {
            if (control.Button.IsChecked == true)
            {
                control.Border.SetResourceReference(Border.BackgroundProperty, "SelectionBrush");
                control.Border.SetResourceReference(Border.BorderBrushProperty, "SelectionBrush");
                control.Icon.SetResourceReference(TextBlock.ForegroundProperty, "SelectionTextBrush");
                control.Title.SetResourceReference(TextBlock.ForegroundProperty, "SelectionTextBrush");
                control.Description.SetResourceReference(TextBlock.ForegroundProperty, "SelectionTextBrush");
            }
            else
            {
                control.Border.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
                control.Border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
                control.Icon.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                control.Title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                control.Description.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            }
        }
    }

    private Task OpenFilterAsync()
    {
        var includeBox = new TextBox
        {
            Text = IncludePatterns,
            AcceptsReturn = true,
            MinHeight = 70,
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };

        var excludeBox = new TextBox
        {
            Text = ExcludePatterns,
            AcceptsReturn = true,
            MinHeight = 90,
            Margin = new Thickness(0, 4, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };

        var content = new StackPanel { Margin = new Thickness(18), MinWidth = 420 };
        content.Children.Add(new TextBlock { Text = "Include patterns", FontWeight = FontWeights.SemiBold });
        content.Children.Add(includeBox);
        content.Children.Add(new TextBlock { Text = "Exclude patterns", FontWeight = FontWeights.SemiBold });
        content.Children.Add(excludeBox);

        if (ShowDialog("File filters", content))
        {
            IncludePatterns = string.IsNullOrWhiteSpace(includeBox.Text) ? "*" : includeBox.Text.Trim();
            ExcludePatterns = excludeBox.Text.Trim();
            SetStatusAsync("Filter settings updated. Run Compare to refresh the preview.").GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    private Task OpenFolderPairsAsync()
    {
        var rows = new ObservableCollection<FolderPairEditorRow>(
            GetSavedFolderPairs().Select(pair => new FolderPairEditorRow
            {
                LeftPath = pair.LeftPath,
                RightPath = pair.RightPath,
                IncludePatterns = string.IsNullOrWhiteSpace(pair.IncludePatterns) ? "*" : pair.IncludePatterns,
                ExcludePatterns = pair.ExcludePatterns ?? string.Empty
            }));
        if (rows.Count == 0)
        {
            rows.Add(new FolderPairEditorRow());
        }

        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            MinWidth = 560,
            MinHeight = 260,
            Margin = new Thickness(14),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Left folder",
            Binding = new Binding(nameof(FolderPairEditorRow.LeftPath)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Right folder",
            Binding = new Binding(nameof(FolderPairEditorRow.RightPath)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Include",
            Binding = new Binding(nameof(FolderPairEditorRow.IncludePatterns)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(115)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Exclude",
            Binding = new Binding(nameof(FolderPairEditorRow.ExcludePatterns)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(135)
        });

        if (!ShowDialog("Folder pairs", grid, resizable: true, width: 720, height: 420))
        {
            return Task.CompletedTask;
        }

        grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var pairs = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.LeftPath) || !string.IsNullOrWhiteSpace(row.RightPath))
            .Select(row => new FolderPairConfiguration(
                row.LeftPath.Trim(),
                row.RightPath.Trim(),
                string.IsNullOrWhiteSpace(row.IncludePatterns) ? "*" : row.IncludePatterns.Trim(),
                row.ExcludePatterns.Trim()))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.LeftPath) && !string.IsNullOrWhiteSpace(pair.RightPath))
            .ToList();

        if (pairs.Count == 0)
        {
            return SetStatusAsync("No complete folder pair was entered.");
        }

        _folderPairs = pairs;
        var firstPair = pairs[0];
        LeftPath = firstPair.LeftPath;
        RightPath = firstPair.RightPath;
        IncludePatterns = string.IsNullOrWhiteSpace(firstPair.IncludePatterns) ? "*" : firstPair.IncludePatterns;
        ExcludePatterns = firstPair.ExcludePatterns ?? string.Empty;
        ClearOperations();
        OnOperationSummaryChanged();
        return SetStatusAsync($"Updated {pairs.Count} folder pair(s). The main view shows the first pair.");
    }

    private Task OpenExternalCommandsAsync()
    {
        var commandsBox = new TextBox
        {
            Text = string.Join(Environment.NewLine, ExternalCommands.Select(command => $"{command.Name}={command.CommandLine}")),
            AcceptsReturn = true,
            MinHeight = 180,
            MinWidth = 560,
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var content = new StackPanel { Margin = new Thickness(18), MinWidth = 580 };
        content.Children.Add(new TextBlock { Text = "Commands", FontWeight = FontWeights.SemiBold });
        content.Children.Add(commandsBox);

        if (ShowDialog("External commands", content))
        {
            ExternalCommands.Clear();
            foreach (var command in ParseExternalCommands(commandsBox.Text))
            {
                ExternalCommands.Add(command);
            }

            SetStatusAsync($"External commands updated: {ExternalCommands.Count}.").GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    private Task SwapSidesAsync()
    {
        (LeftPath, RightPath) = (RightPath, LeftPath);
        SetStatusAsync("Left and right folders swapped.").GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    private Task OpenConfigurationAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open configuration",
            Filter = "FolderSyncr configurations (*.foldersyncr.json)|*.foldersyncr.json|FreeFileSync configurations (*.ffs_gui;*.ffs_batch;*.ffs_real)|*.ffs_gui;*.ffs_batch;*.ffs_real|XML files (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        return OpenConfigurationFileAsync(dialog.FileName);
    }

    private Task OpenConfigurationFileAsync(string path)
    {
        OpenConfigurationFile(path);
        return Task.CompletedTask;
    }

    private void OpenConfigurationFile(string path)
    {
        if (IsNativeConfigurationPath(path))
        {
            var nativeConfiguration = _configurationStore.Load(path);
            ApplyNativeConfiguration(path, nativeConfiguration);
            SetStatusAsync($"Opened {Path.GetFileName(path)}.").GetAwaiter().GetResult();
            return;
        }

        var configuration = _configurationImporter.Import(path);
        var firstPair = configuration.FolderPairs.FirstOrDefault();
        if (firstPair is null)
        {
            SetStatusAsync("No folder pair could be imported from the selected FreeFileSync configuration.").GetAwaiter().GetResult();
            return;
        }

        LeftPath = firstPair.LeftPath;
        RightPath = firstPair.RightPath;
        _folderPairs = configuration.FolderPairs
            .Select(pair => new FolderPairConfiguration(
                pair.LeftPath,
                pair.RightPath,
                pair.IncludePatterns,
                pair.ExcludePatterns))
            .ToList();

        if (configuration.SyncMode is { } syncMode)
        {
            SelectedMode = syncMode;
        }

        if (configuration.CompareMethod is { } compareMethod)
        {
            SelectedCompareMethod = compareMethod;
        }

        IncludePatterns = string.IsNullOrWhiteSpace(firstPair.IncludePatterns)
            ? configuration.IncludePatterns
            : firstPair.IncludePatterns;
        ExcludePatterns = string.IsNullOrWhiteSpace(firstPair.ExcludePatterns)
            ? configuration.ExcludePatterns
            : firstPair.ExcludePatterns;
        ClearOperations();
        OnOperationSummaryChanged();
        _currentConfigurationPath = null;

        Configurations.Insert(0, new ConfigurationItem
        {
            Name = Path.GetFileNameWithoutExtension(configuration.SourcePath),
            LastSync = "Imported"
        });

        foreach (var warning in configuration.Warnings)
        {
            AddLog($"Import warning: {warning}");
        }

        SetStatusAsync($"Imported {Path.GetFileName(configuration.SourcePath)}. Run Compare to preview changes.").GetAwaiter().GetResult();
        if (_folderPairs.Count > 1)
        {
            AddLog($"Imported {_folderPairs.Count} folder pairs. Use the folder-pair editor to review or change them.");
        }
    }

    private Task NewConfigurationAsync()
    {
        _currentConfigurationPath = null;
        LeftPath = string.Empty;
        RightPath = string.Empty;
        SelectedMode = SyncMode.TwoWay;
        SelectedCompareMethod = CompareMethod.TimeAndSize;
        FileTimeToleranceSeconds = 2;
        IgnoreDaylightSavingTimeShift = false;
        VerifyCopiedFiles = false;
        SelectedDeletionHandling = DeletionHandling.Permanent;
        SelectedVersioningMode = VersioningMode.TimeStampFolder;
        VersioningFolderPath = string.Empty;
        SelectedErrorHandling = SyncErrorHandling.ShowErrors;
        SelectedSymbolicLinkHandling = SymbolicLinkHandling.Skip;
        CustomRules = CustomSyncRules.Default;
        UseSynchronizationDatabase = true;
        RemoteConnectionCount = 1;
        SftpCompression = false;
        UseVolumeShadowCopy = false;
        IncludePatterns = "*";
        ExcludePatterns = "**/bin/**;**/obj/**;**/.git/**";
        _folderPairs = [];
        ReplaceExternalCommands(CreateDefaultExternalCommands());
        ClearOperations();
        OnOperationSummaryChanged();
        return SetStatusAsync("New configuration created.");
    }

    private Task SaveConfigurationAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentConfigurationPath))
        {
            return SaveAsConfigurationAsync();
        }

        return SaveNativeConfigurationAsync(_currentConfigurationPath);
    }

    private Task SaveAsConfigurationAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save FolderSyncr configuration",
            Filter = "FolderSyncr configurations (*.foldersyncr.json)|*.foldersyncr.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".foldersyncr.json",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_currentConfigurationPath)
                ? "Backup.foldersyncr.json"
                : Path.GetFileName(_currentConfigurationPath)
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        return SaveNativeConfigurationAsync(dialog.FileName);
    }

    private Task ReloadConfigurationAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentConfigurationPath))
        {
            return SetStatusAsync("No FolderSyncr configuration is open. Use File -> Open configuration first.");
        }

        return LoadNativeConfigurationAsync(_currentConfigurationPath);
    }

    private Task ExportFreeFileSyncConfigurationAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export FreeFileSync configuration",
            Filter = "FreeFileSync GUI configurations (*.ffs_gui)|*.ffs_gui|XML files (*.xml)|*.xml|All files (*.*)|*.*",
            DefaultExt = ".ffs_gui",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_currentConfigurationPath)
                ? "Backup.ffs_gui"
                : $"{Path.GetFileNameWithoutExtension(_currentConfigurationPath)}.ffs_gui"
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        _configurationExporter.Save(dialog.FileName, CreateNativeConfiguration(dialog.FileName));
        return SetStatusAsync($"Exported FreeFileSync configuration {Path.GetFileName(dialog.FileName)}.");
    }

    private Task LoadNativeConfigurationAsync(string path)
    {
        var configuration = _configurationStore.Load(path);
        ApplyNativeConfiguration(path, configuration);
        return SetStatusAsync($"Opened {Path.GetFileName(path)}.");
    }

    private Task SaveNativeConfigurationAsync(string path)
    {
        _configurationStore.Save(path, CreateNativeConfiguration(path));
        _currentConfigurationPath = path;
        Configurations.Insert(0, new ConfigurationItem
        {
            Name = Path.GetFileNameWithoutExtension(path),
            LastSync = "Saved"
        });

        return SetStatusAsync($"Saved {Path.GetFileName(path)}.");
    }

    private FolderSyncrConfiguration CreateNativeConfiguration(string path)
    {
        return new FolderSyncrConfiguration(
            Version: 15,
            Name: Path.GetFileNameWithoutExtension(path),
            LeftPath,
            RightPath,
            SelectedMode,
            SelectedCompareMethod,
            FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles,
            SelectedDeletionHandling,
            SelectedVersioningMode,
            VersioningFolderPath,
            SelectedErrorHandling,
            SelectedSymbolicLinkHandling,
            IncludePatterns,
            ExcludePatterns,
            IsDarkMode,
            ExternalCommands.ToArray(),
            GetSavedFolderPairs(),
            CustomRules,
            RemoteConnectionCount,
            SftpCompression,
            UseVolumeShadowCopy,
            UseSynchronizationDatabase);
    }

    private void ApplyNativeConfiguration(string path, FolderSyncrConfiguration configuration)
    {
        _currentConfigurationPath = path;
        _folderPairs = configuration.FolderPairs?.ToList() ?? [];
        var visiblePair = _folderPairs.FirstOrDefault();
        if (visiblePair is not null)
        {
            LeftPath = visiblePair.LeftPath;
            RightPath = visiblePair.RightPath;
        }
        else
        {
            LeftPath = configuration.LeftPath;
            RightPath = configuration.RightPath;
        }

        SelectedMode = configuration.SyncMode;
        SelectedCompareMethod = configuration.CompareMethod;
        FileTimeToleranceSeconds = configuration.Version < 2 ? 2 : configuration.FileTimeToleranceSeconds;
        IgnoreDaylightSavingTimeShift = configuration.Version >= 5 && configuration.IgnoreDaylightSavingTimeShift;
        VerifyCopiedFiles = configuration.Version >= 3 && configuration.VerifyCopiedFiles;
        SelectedDeletionHandling = configuration.Version >= 4 ? configuration.DeletionHandling : DeletionHandling.Permanent;
        SelectedVersioningMode = configuration.Version >= 6 ? configuration.VersioningMode : VersioningMode.TimeStampFolder;
        VersioningFolderPath = configuration.Version >= 4 ? configuration.VersioningFolderPath : string.Empty;
        SelectedErrorHandling = configuration.Version >= 7 ? configuration.ErrorHandling : SyncErrorHandling.ShowErrors;
        SelectedSymbolicLinkHandling = configuration.Version >= 8 ? configuration.SymbolicLinkHandling : SymbolicLinkHandling.Skip;
        CustomRules = configuration.Version >= 12 && configuration.CustomRules is not null
            ? configuration.CustomRules
            : CustomSyncRules.Default;
        UseSynchronizationDatabase = configuration.Version >= 15 ? configuration.UseSynchronizationDatabase : true;
        RemoteConnectionCount = configuration.Version >= 13 ? Math.Max(1, configuration.RemoteConnectionCount) : 1;
        SftpCompression = configuration.Version >= 13 && configuration.SftpCompression;
        UseVolumeShadowCopy = configuration.Version >= 14 && configuration.UseVolumeShadowCopy;
        IncludePatterns = string.IsNullOrWhiteSpace(visiblePair?.IncludePatterns)
            ? string.IsNullOrWhiteSpace(configuration.IncludePatterns) ? "*" : configuration.IncludePatterns
            : visiblePair.IncludePatterns;
        ExcludePatterns = string.IsNullOrWhiteSpace(visiblePair?.ExcludePatterns)
            ? configuration.ExcludePatterns
            : visiblePair.ExcludePatterns;
        ReplaceExternalCommands(configuration.Version >= 9 && configuration.ExternalCommands is not null
            ? configuration.ExternalCommands
            : CreateDefaultExternalCommands());
        ClearOperations();
        OnOperationSummaryChanged();

        if (IsDarkMode != configuration.IsDarkMode)
        {
            IsDarkMode = configuration.IsDarkMode;
            ThemeManager.Apply(IsDarkMode);
        }

        Configurations.Insert(0, new ConfigurationItem
        {
            Name = string.IsNullOrWhiteSpace(configuration.Name) ? Path.GetFileNameWithoutExtension(path) : configuration.Name,
            LastSync = "Opened"
        });

        if (_folderPairs.Count > 1)
        {
            AddLog($"Loaded {_folderPairs.Count} preserved folder pairs. Use the folder-pair editor to review or change them.");
        }
    }

    private static bool IsNativeConfigurationPath(string path)
    {
        return path.EndsWith(".foldersyncr.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
    }

    private Task OpenFreeFileSyncLogAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open FreeFileSync log",
            Filter = "FreeFileSync logs and JSON (*.json;*.html;*.htm;*.log;*.txt)|*.json;*.html;*.htm;*.log;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        var summary = _logImporter.Import(dialog.FileName);
        AddLog($"Imported FreeFileSync log: {summary.SyncResult}, errors {summary.Errors?.ToString() ?? "?"}, warnings {summary.Warnings?.ToString() ?? "?"}.");

        var summaryText = new TextBox
        {
            Text = summary.ToDisplayText(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinWidth = 520,
            MinHeight = 260,
            Margin = new Thickness(18)
        };

        ShowDialog("FreeFileSync log summary", summaryText);
        return SetStatusAsync($"Imported FreeFileSync log: {Path.GetFileName(summary.SourcePath)}.");
    }

    private Task CreateSampleDataAsync()
    {
        var sample = _sampleDataGenerator.Create();
        _currentConfigurationPath = null;
        LeftPath = sample.LeftPath;
        RightPath = sample.RightPath;
        _folderPairs = [];
        SelectedMode = SyncMode.TwoWay;
        SelectedCompareMethod = CompareMethod.TimeAndSize;
        CustomRules = CustomSyncRules.Default;
        UseSynchronizationDatabase = true;
        RemoteConnectionCount = 1;
        SftpCompression = false;
        UseVolumeShadowCopy = false;
        IncludePatterns = "*";
        ExcludePatterns = "**/bin/**;**/obj/**;**/.git/**";
        ClearOperations();
        OnOperationSummaryChanged();

        Configurations.Insert(0, new ConfigurationItem
        {
            Name = "Sample data",
            LastSync = "Created"
        });

        return SetStatusAsync($"Sample data created in {sample.RootPath}. Run Compare to see the planned actions.");
    }

    private bool ShowDialog(string title, UIElement body, bool resizable = false, double? width = null, double? height = null)
    {
        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 86,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 86,
            IsCancel = true
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(18, 0, 18, 16)
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);

        var window = new Window
        {
            Title = title,
            Content = root,
            SizeToContent = width is null || height is null ? SizeToContent.WidthAndHeight : SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize,
            Owner = Application.Current.MainWindow,
            MinWidth = 360
        };
        if (width is not null)
        {
            window.Width = width.Value;
            window.MinWidth = Math.Min(width.Value, 560);
        }

        if (height is not null)
        {
            window.Height = height.Value;
            window.MinHeight = Math.Min(height.Value, 360);
        }

        ApplyDialogTheme(window);

        okButton.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        return window.ShowDialog() == true;
    }

    private void ApplyDialogTheme(Window window)
    {
        ThemeManager.ApplyTo(window.Resources, IsDarkMode);

        var panelBrush = GetBrush(window.Resources, "PanelBrush");
        var inputBrush = GetBrush(window.Resources, "InputBrush");
        var buttonBrush = GetBrush(window.Resources, "ButtonBrush");
        var borderBrush = GetBrush(window.Resources, "BorderBrushSoft");
        var textBrush = GetBrush(window.Resources, "TextBrush");

        window.Background = panelBrush;
        window.SetValue(TextElement.ForegroundProperty, textBrush);
        window.Resources[SystemColors.ControlBrushKey] = panelBrush;
        window.Resources[SystemColors.WindowBrushKey] = panelBrush;
        window.Resources[SystemColors.ControlTextBrushKey] = textBrush;

        window.Resources[typeof(TextBlock)] = CreateStyle<TextBlock>(
            (TextBlock.ForegroundProperty, textBrush),
            (TextBlock.FontSizeProperty, 13d));

        window.Resources[typeof(TextBox)] = CreateStyle<TextBox>(
            (TextBox.ForegroundProperty, textBrush),
            (TextBox.CaretBrushProperty, textBrush),
            (TextBox.BackgroundProperty, inputBrush),
            (TextBox.BorderBrushProperty, borderBrush),
            (TextBox.BorderThicknessProperty, new Thickness(1)),
            (TextBox.PaddingProperty, new Thickness(8, 5, 8, 5)));

        window.Resources[typeof(ComboBox)] = CreateDialogComboBoxStyle();

        var comboBoxItemStyle = CreateStyle<ComboBoxItem>(
            (ComboBoxItem.ForegroundProperty, textBrush),
            (ComboBoxItem.BackgroundProperty, inputBrush),
            (ComboBoxItem.PaddingProperty, new Thickness(8, 6, 8, 6)),
            (ComboBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        comboBoxItemStyle.Triggers.Add(new Trigger
        {
            Property = ComboBoxItem.IsHighlightedProperty,
            Value = true,
            Setters = { new Setter(Control.BackgroundProperty, GetBrush(window.Resources, "MenuHoverBrush")) }
        });
        comboBoxItemStyle.Triggers.Add(new Trigger
        {
            Property = ComboBoxItem.IsSelectedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, GetBrush(window.Resources, "SelectionBrush")),
                new Setter(Control.ForegroundProperty, GetBrush(window.Resources, "SelectionTextBrush"))
            }
        });
        window.Resources[typeof(ComboBoxItem)] = comboBoxItemStyle;

        window.Resources[typeof(CheckBox)] = CreateStyle<CheckBox>(
            (CheckBox.ForegroundProperty, textBrush),
            (CheckBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            (CheckBox.MinHeightProperty, 28d));

        window.Resources[typeof(RadioButton)] = CreateStyle<RadioButton>(
            (RadioButton.ForegroundProperty, textBrush),
            (RadioButton.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            (RadioButton.MinHeightProperty, 34d));

        window.Resources[typeof(TabControl)] = CreateStyle<TabControl>(
            (Control.BackgroundProperty, panelBrush),
            (Control.BorderBrushProperty, borderBrush));

        window.Resources[typeof(TabItem)] = CreateDialogTabItemStyle();

        window.Resources[typeof(Button)] = CreateStyle<Button>(
            (Button.ForegroundProperty, textBrush),
            (Button.BackgroundProperty, buttonBrush),
            (Button.BorderBrushProperty, borderBrush),
            (Button.BorderThicknessProperty, new Thickness(1)),
            (Button.PaddingProperty, new Thickness(12, 5, 12, 5)),
            (Button.MinHeightProperty, 32d));
    }

    private static Style CreateStyle<TControl>(params (DependencyProperty Property, object Value)[] setters)
        where TControl : FrameworkElement
    {
        var style = new Style(typeof(TControl));
        foreach (var setter in setters)
        {
            style.Setters.Add(new Setter(setter.Property, setter.Value));
        }

        return style;
    }

    private static Style CreateDialogComboBoxStyle()
    {
        const string styleXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
       TargetType="{x:Type ComboBox}">
    <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
    <Setter Property="Background" Value="{DynamicResource InputBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrushSoft}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="MinHeight" Value="34" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type ComboBox}">
                <Grid>
                    <ToggleButton x:Name="ToggleButton"
                                  Focusable="False"
                                  ClickMode="Press"
                                  IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                        <ToggleButton.Template>
                            <ControlTemplate TargetType="{x:Type ToggleButton}">
                                <Border x:Name="Chrome"
                                        Background="{DynamicResource InputBrush}"
                                        BorderBrush="{DynamicResource BorderBrushSoft}"
                                        BorderThickness="1">
                                    <Grid>
                                        <ContentPresenter Margin="8,0,30,0"
                                                          HorizontalAlignment="Stretch"
                                                          VerticalAlignment="Center"
                                                          RecognizesAccessKey="True"
                                                          TextElement.Foreground="{DynamicResource TextBrush}" />
                                        <Path HorizontalAlignment="Right"
                                              VerticalAlignment="Center"
                                              Margin="0,0,10,0"
                                              Fill="{DynamicResource TextBrush}"
                                              Data="M 0 0 L 4 4 L 8 0 Z" />
                                    </Grid>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource HeaderBlueBrush}" />
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </ToggleButton.Template>
                        <ContentPresenter Content="{TemplateBinding SelectionBoxItem}"
                                          ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                          ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}" />
                    </ToggleButton>
                    <Popup x:Name="PART_Popup"
                           IsOpen="{TemplateBinding IsDropDownOpen}"
                           Placement="Bottom"
                           AllowsTransparency="True"
                           Focusable="False"
                           PopupAnimation="Fade">
                        <Border Background="{DynamicResource InputBrush}"
                                BorderBrush="{DynamicResource BorderBrushSoft}"
                                BorderThickness="1"
                                MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                                MaxHeight="260">
                            <ScrollViewer CanContentScroll="True">
                                <ItemsPresenter />
                            </ScrollViewer>
                        </Border>
                    </Popup>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
""";

        return (Style)XamlReader.Parse(styleXaml);
    }

    private static Style CreateDialogTabItemStyle()
    {
        const string styleXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
       TargetType="{x:Type TabItem}">
    <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
    <Setter Property="Background" Value="{DynamicResource ButtonBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrushSoft}" />
    <Setter Property="Padding" Value="13,8" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type TabItem}">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="1"
                        Padding="{TemplateBinding Padding}">
                    <ContentPresenter ContentSource="Header"
                                      RecognizesAccessKey="True"
                                      TextElement.Foreground="{TemplateBinding Foreground}" />
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource MenuHoverBrush}" />
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                        <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                        <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.6" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
""";
        return (Style)XamlReader.Parse(styleXaml);
    }

    private static Brush GetBrush(ResourceDictionary resources, string key)
    {
        return (Brush)resources[key];
    }

    private Task SetStatusAsync(string message)
    {
        Status = message;
        AddLog(message);
        return Task.CompletedTask;
    }

    private Task SetConfigurationVisibleAsync(bool visible)
    {
        IsConfigurationVisible = visible;
        return SetStatusAsync(visible ? "Configuration pane shown." : "Configuration pane closed.");
    }

    private Task SetOverviewVisibleAsync(bool visible)
    {
        IsOverviewVisible = visible;
        return SetStatusAsync(visible ? "Overview pane shown." : "Overview pane closed.");
    }

    private Task SetOperationViewFilterAsync(OperationViewFilter filter)
    {
        _operationViewFilter = filter;
        if (filter == OperationViewFilter.All && _overviewFolderFilter is not null)
        {
            _overviewFolderFilter = null;
            SetSelectedOverviewItemSilently(null);
        }

        OperationsView.Refresh();

        var label = filter switch
        {
            OperationViewFilter.Changes => "changes",
            OperationViewFilter.Equal => "equal items",
            OperationViewFilter.CopyLeftToRight => "left-to-right copies",
            OperationViewFilter.CopyRightToLeft => "right-to-left copies",
            OperationViewFilter.DeleteLeft => "left deletes",
            OperationViewFilter.DeleteRight => "right deletes",
            OperationViewFilter.Conflicts => "conflicts",
            _ => "all items"
        };

        return SetStatusAsync($"Showing {label}.");
    }

    private void SetOverviewFolderFilter(OverviewItem? item)
    {
        _overviewFolderFilter = item?.Folder;
        if (item is not null)
        {
            _operationViewFilter = OperationViewFilter.All;
        }

        OperationsView.Refresh();
        if (item is not null)
        {
            SetStatusAsync($"Showing overview folder {item.Folder}.").GetAwaiter().GetResult();
        }
    }

    public Task OpenOperationSideAsync(SyncOperation operation, bool openLeftSide)
    {
        var snapshot = openLeftSide ? operation.Left : operation.Right;
        var side = openLeftSide ? "left" : "right";
        if (snapshot is null)
        {
            return SetStatusAsync($"No {side} file exists for {operation.RelativePath}.");
        }

        if (!File.Exists(snapshot.FullPath) && !Directory.Exists(snapshot.FullPath))
        {
            return SetStatusAsync($"The {side} path no longer exists: {snapshot.FullPath}");
        }

        Process.Start(new ProcessStartInfo(snapshot.FullPath) { UseShellExecute = true });
        return SetStatusAsync($"Opened {side} item: {operation.RelativePath}");
    }

    public Task OpenOperationDefaultAsync(SyncOperation operation)
    {
        return operation.Left is not null
            ? OpenOperationSideAsync(operation, openLeftSide: true)
            : OpenOperationSideAsync(operation, openLeftSide: false);
    }

    public Task CopyOperationRelativePathAsync(SyncOperation operation)
    {
        Clipboard.SetText(operation.RelativePath);
        return SetStatusAsync($"Copied relative path: {operation.RelativePath}");
    }

    public Task ExcludeOperationAsync(SyncOperation operation)
    {
        var pattern = GetFilterPattern(operation);
        var existingPatterns = SplitFilterPatterns(ExcludePatterns);

        if (!existingPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            ExcludePatterns = string.IsNullOrWhiteSpace(ExcludePatterns)
                ? pattern
                : $"{ExcludePatterns}{Environment.NewLine}{pattern}";
        }

        return SetStatusAsync($"Excluded {operation.RelativePath}. Run Compare to refresh the preview.");
    }

    public Task IncludeOperationAsync(SyncOperation operation)
    {
        var pattern = GetFilterPattern(operation);
        var includePatterns = SplitFilterPatterns(IncludePatterns);
        if (!includePatterns.Contains("*", StringComparer.OrdinalIgnoreCase)
            && !includePatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            IncludePatterns = string.IsNullOrWhiteSpace(IncludePatterns)
                ? pattern
                : $"{IncludePatterns}{Environment.NewLine}{pattern}";
        }

        var excludePatterns = SplitFilterPatterns(ExcludePatterns);
        var remainingExcludes = excludePatterns
            .Where(existing => !string.Equals(existing, pattern, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (remainingExcludes.Length != excludePatterns.Length)
        {
            ExcludePatterns = string.Join(Environment.NewLine, remainingExcludes);
        }

        return SetStatusAsync($"Included {operation.RelativePath}. Run Compare to refresh the preview.");
    }

    public Task RunExternalCommandAsync(ExternalCommandDefinition command, IReadOnlyList<SyncOperation> operations)
    {
        if (string.IsNullOrWhiteSpace(command.CommandLine))
        {
            return SetStatusAsync($"External command '{command.Name}' has no command line.");
        }

        if (operations.Count == 0)
        {
            return SetStatusAsync("No comparison item is selected for the external command.");
        }

        try
        {
            var expandedCommand = ExternalCommandMacroExpander.Expand(command.CommandLine, operations, LeftPath, RightPath);
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/D");
            startInfo.ArgumentList.Add("/C");
            startInfo.ArgumentList.Add(expandedCommand);
            Process.Start(startInfo);
            return SetStatusAsync($"Started external command: {command.Name}.");
        }
        catch (Exception exception)
        {
            return SetStatusAsync($"External command failed: {exception.Message}");
        }
    }

    private static string GetFilterPattern(SyncOperation operation)
    {
        return operation.RelativePath.Replace('\\', '/');
    }

    private static string[] SplitFilterPatterns(string patterns)
    {
        return patterns.Split([';', ',', '|', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static ExternalCommandDefinition[] ParseExternalCommands(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
                return separatorIndex < 0
                    ? null
                    : new ExternalCommandDefinition(line[..separatorIndex].Trim(), line[(separatorIndex + 1)..].Trim());
            })
            .Where(command => command is not null
                && !string.IsNullOrWhiteSpace(command.Name)
                && !string.IsNullOrWhiteSpace(command.CommandLine))
            .Cast<ExternalCommandDefinition>()
            .ToArray();
    }

    private static ObservableCollection<ExternalCommandDefinition> CreateDefaultExternalCommands()
    {
        return
        [
            new ExternalCommandDefinition("Show in Explorer", "explorer.exe /select, %local_path% & exit 0"),
            new ExternalCommandDefinition("Copy path to clipboard", "echo %item_path%| clip")
        ];
    }

    private void ReplaceExternalCommands(IEnumerable<ExternalCommandDefinition> commands)
    {
        ExternalCommands.Clear();
        foreach (var command in commands.Where(command =>
            !string.IsNullOrWhiteSpace(command.Name)
            && !string.IsNullOrWhiteSpace(command.CommandLine)))
        {
            ExternalCommands.Add(command);
        }
    }

    private IReadOnlyList<FolderPairConfiguration> GetSavedFolderPairs()
    {
        if (_folderPairs.Count > 1
            && string.Equals(_folderPairs[0].LeftPath, LeftPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_folderPairs[0].RightPath, RightPath, StringComparison.OrdinalIgnoreCase))
        {
            var pairs = _folderPairs.ToArray();
            pairs[0] = pairs[0] with
            {
                IncludePatterns = IncludePatterns,
                ExcludePatterns = ExcludePatterns
            };
            return pairs;
        }

        if (!string.IsNullOrWhiteSpace(LeftPath) || !string.IsNullOrWhiteSpace(RightPath))
        {
            return [new FolderPairConfiguration(LeftPath, RightPath, IncludePatterns, ExcludePatterns)];
        }

        return [];
    }

    private Task OpenDocumentationAsync()
    {
        var docsPath = FindDocumentationPath();
        if (docsPath is not null)
        {
            Process.Start(new ProcessStartInfo(docsPath) { UseShellExecute = true });
            return SetStatusAsync("Opened documentation.");
        }

        return SetStatusAsync("Documentation file was not found.");
    }

    private static string? FindDocumentationPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.html"),
            Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.md"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "USER_GUIDE.html")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "USER_GUIDE.md")),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "USER_GUIDE.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "USER_GUIDE.md")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private Task AboutAsync()
    {
        MessageBox.Show(
            "FolderSyncr\nFolder comparison and synchronization for Windows.",
            "About FolderSyncr",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return Task.CompletedTask;
    }

    private Task ExitAsync()
    {
        Application.Current.Shutdown();
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
            FileTimeToleranceSeconds = FileTimeToleranceSeconds,
            IgnoreDaylightSavingTimeShift = IgnoreDaylightSavingTimeShift,
            VerifyCopiedFiles = VerifyCopiedFiles,
            DeletionHandling = SelectedDeletionHandling,
            VersioningMode = SelectedVersioningMode,
            VersioningFolderPath = VersioningFolderPath,
            ErrorHandling = SelectedErrorHandling,
            SymbolicLinkHandling = SelectedSymbolicLinkHandling,
            CustomRules = CustomRules,
            UseSynchronizationDatabase = UseSynchronizationDatabase,
            RemoteConnectionCount = RemoteConnectionCount,
            SftpCompression = SftpCompression,
            UseVolumeShadowCopy = UseVolumeShadowCopy,
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
        OnPropertyChanged(nameof(EqualCount));
        OnPropertyChanged(nameof(CopyLeftToRightCount));
        OnPropertyChanged(nameof(CopyRightToLeftCount));
        OnPropertyChanged(nameof(DeleteLeftCount));
        OnPropertyChanged(nameof(DeleteRightCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(LeftFileCount));
        OnPropertyChanged(nameof(RightFileCount));
        RefreshOverview();
        OperationsView.Refresh();
        SyncCommand.RaiseCanExecuteChanged();
    }

    private void AddOperations(IEnumerable<SyncOperation> operations)
    {
        foreach (var operation in operations)
        {
            operation.DisplayIndex = Operations.Count + 1;
            operation.PropertyChanged += Operation_PropertyChanged;
            Operations.Add(operation);
        }
    }

    private void ClearOperations()
    {
        foreach (var operation in Operations)
        {
            operation.PropertyChanged -= Operation_PropertyChanged;
        }

        _overviewFolderFilter = null;
        SetSelectedOverviewItemSilently(null);
        Operations.Clear();
    }

    private void Operation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SyncOperation.IsSelectedForSync) or nameof(SyncOperation.ShouldExecute) or nameof(SyncOperation.EffectiveKind))
        {
            OnOperationSummaryChanged();
        }
    }

    private bool FilterOperation(object item)
    {
        if (item is not SyncOperation operation)
        {
            return false;
        }

        if (_overviewFolderFilter is not null
            && !string.Equals(GetTopFolder(operation.RelativePath), _overviewFolderFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _operationViewFilter switch
        {
            OperationViewFilter.Changes => operation.ShouldExecute,
            OperationViewFilter.Equal => operation.EffectiveKind == OperationKind.Equal,
            OperationViewFilter.CopyLeftToRight => operation.EffectiveKind == OperationKind.CopyLeftToRight,
            OperationViewFilter.CopyRightToLeft => operation.EffectiveKind == OperationKind.CopyRightToLeft,
            OperationViewFilter.DeleteLeft => operation.EffectiveKind == OperationKind.DeleteLeft,
            OperationViewFilter.DeleteRight => operation.EffectiveKind == OperationKind.DeleteRight,
            OperationViewFilter.Conflicts => operation.EffectiveKind == OperationKind.Conflict,
            _ => true
        };
    }

    private int CountOperations(OperationKind kind)
    {
        return Operations.Count(operation => operation.EffectiveKind == kind);
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

    private void SetSelectedOverviewItemSilently(OverviewItem? item)
    {
        _selectedOverviewItem = item;
        OnPropertyChanged(nameof(SelectedOverviewItem));
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

    private static long GetOperationBytes(SyncOperation operation)
    {
        return operation.EffectiveKind switch
        {
            OperationKind.CopyLeftToRight => operation.Left?.Length ?? 0,
            OperationKind.CopyRightToLeft => operation.Right?.Length ?? 0,
            OperationKind.DeleteLeft => operation.Left?.Length ?? 0,
            OperationKind.DeleteRight => operation.Right?.Length ?? 0,
            _ => operation.Left?.Length ?? operation.Right?.Length ?? 0
        };
    }

    private void RaiseCommandStates()
    {
        BrowseLeftCommand.RaiseCanExecuteChanged();
        BrowseRightCommand.RaiseCanExecuteChanged();
        CompareCommand.RaiseCanExecuteChanged();
        SyncCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        NewConfigurationCommand.RaiseCanExecuteChanged();
        OpenConfigurationCommand.RaiseCanExecuteChanged();
        SaveConfigurationCommand.RaiseCanExecuteChanged();
        SaveAsConfigurationCommand.RaiseCanExecuteChanged();
        ExportFreeFileSyncConfigurationCommand.RaiseCanExecuteChanged();
        ReloadConfigurationCommand.RaiseCanExecuteChanged();
    }

    private enum OperationViewFilter
    {
        All,
        Changes,
        Equal,
        CopyLeftToRight,
        CopyRightToLeft,
        DeleteLeft,
        DeleteRight,
        Conflicts
    }

    private enum SettingsDialogTab
    {
        Compare,
        Filter,
        Synchronization
    }

    private sealed class FolderPairEditorRow
    {
        public string LeftPath { get; set; } = string.Empty;

        public string RightPath { get; set; } = string.Empty;

        public string IncludePatterns { get; set; } = "*";

        public string ExcludePatterns { get; set; } = string.Empty;
    }
}
