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
    private CancellationTokenSource? _cancellation;
    private string _leftPath = string.Empty;
    private string _rightPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.TwoWay;
    private CompareMethod _selectedCompareMethod = CompareMethod.TimeAndSize;
    private string _includePatterns = "*";
    private string _excludePatterns = "**/bin/**;**/obj/**;**/.git/**";
    private string _status = "Choose two folders, compare, then sync.";
    private bool _isBusy;
    private bool _isDarkMode;
    private bool _isConfigurationVisible = true;
    private bool _isOverviewVisible = true;
    private OperationViewFilter _operationViewFilter = OperationViewFilter.All;

    public MainViewModel()
    {
        OperationsView = CollectionViewSource.GetDefaultView(Operations);
        OperationsView.Filter = FilterOperation;

        BrowseLeftCommand = new RelayCommand(() => BrowseAsync(isLeft: true), () => !IsBusy);
        BrowseRightCommand = new RelayCommand(() => BrowseAsync(isLeft: false), () => !IsBusy);
        CompareCommand = new RelayCommand(CompareAsync, CanRunFolderAction);
        SyncCommand = new RelayCommand(SyncAsync, () => CanRunFolderAction() && Operations.Any(operation => operation.WillChangeFileSystem));
        CancelCommand = new RelayCommand(CancelAsync, () => IsBusy);
        ToggleThemeCommand = new RelayCommand(ToggleThemeAsync);
        OpenSettingsCommand = new RelayCommand(OpenSettingsAsync);
        OpenFilterCommand = new RelayCommand(OpenFilterAsync);
        SwapSidesCommand = new RelayCommand(SwapSidesAsync);
        CloudPathCommand = new RelayCommand(() => SetStatusAsync("Cloud path integration is not implemented yet. Use Browse or paste a local/cloud-synced folder path."));
        NewConfigurationCommand = new RelayCommand(() => SetStatusAsync("New configuration is not implemented yet."));
        OpenConfigurationCommand = new RelayCommand(() => SetStatusAsync("Open configuration is not implemented yet."));
        SaveConfigurationCommand = new RelayCommand(() => SetStatusAsync("Save configuration is not implemented yet."));
        SaveAsConfigurationCommand = new RelayCommand(() => SetStatusAsync("Save as is not implemented yet."));
        ReloadConfigurationCommand = new RelayCommand(() => SetStatusAsync("Configuration list refreshed."));
        OpenDocumentationCommand = new RelayCommand(OpenDocumentationAsync);
        AboutCommand = new RelayCommand(AboutAsync);
        ExitCommand = new RelayCommand(ExitAsync);
        ShowConfigurationCommand = new RelayCommand(() => SetConfigurationVisibleAsync(true));
        CloseConfigurationCommand = new RelayCommand(() => SetConfigurationVisibleAsync(false));
        ShowOverviewCommand = new RelayCommand(() => SetOverviewVisibleAsync(true));
        CloseOverviewCommand = new RelayCommand(() => SetOverviewVisibleAsync(false));
        ShowAllOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.All));
        ShowChangeOperationsCommand = new RelayCommand(() => SetOperationViewFilterAsync(OperationViewFilter.Changes));
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

    public IReadOnlyList<SyncMode> SyncModes { get; } = Enum.GetValues<SyncMode>();
    public IReadOnlyList<CompareMethod> CompareMethods { get; } = Enum.GetValues<CompareMethod>();

    public RelayCommand BrowseLeftCommand { get; }
    public RelayCommand BrowseRightCommand { get; }
    public RelayCommand CompareCommand { get; }
    public RelayCommand SyncCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenFilterCommand { get; }
    public RelayCommand SwapSidesCommand { get; }
    public RelayCommand CloudPathCommand { get; }
    public RelayCommand NewConfigurationCommand { get; }
    public RelayCommand OpenConfigurationCommand { get; }
    public RelayCommand SaveConfigurationCommand { get; }
    public RelayCommand SaveAsConfigurationCommand { get; }
    public RelayCommand ReloadConfigurationCommand { get; }
    public RelayCommand OpenDocumentationCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand ShowConfigurationCommand { get; }
    public RelayCommand CloseConfigurationCommand { get; }
    public RelayCommand ShowOverviewCommand { get; }
    public RelayCommand CloseOverviewCommand { get; }
    public RelayCommand ShowAllOperationsCommand { get; }
    public RelayCommand ShowChangeOperationsCommand { get; }
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

    public int ChangeCount => Operations.Count(operation => operation.WillChangeFileSystem);
    public int ConflictCount => Operations.Count(operation => operation.Kind == OperationKind.Conflict);
    public int TotalCount => Operations.Count;
    public int LeftFileCount => Operations.Count(operation => operation.Left is not null);
    public int RightFileCount => Operations.Count(operation => operation.Right is not null);

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

    private Task ToggleThemeAsync()
    {
        IsDarkMode = !IsDarkMode;
        ThemeManager.Apply(IsDarkMode);
        return Task.CompletedTask;
    }

    private Task OpenSettingsAsync()
    {
        var modeBox = new ComboBox
        {
            ItemsSource = SyncModes,
            SelectedItem = SelectedMode,
            Margin = new Thickness(0, 4, 0, 12),
            MinWidth = 260
        };

        var compareBox = new ComboBox
        {
            ItemsSource = CompareMethods,
            SelectedItem = SelectedCompareMethod,
            Margin = new Thickness(0, 4, 0, 16),
            MinWidth = 260
        };

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Synchronization mode", FontWeight = FontWeights.SemiBold });
        content.Children.Add(modeBox);
        content.Children.Add(new TextBlock { Text = "Compare method", FontWeight = FontWeights.SemiBold });
        content.Children.Add(compareBox);

        if (ShowDialog("Comparison settings", content))
        {
            SelectedMode = (SyncMode)modeBox.SelectedItem;
            SelectedCompareMethod = (CompareMethod)compareBox.SelectedItem;
            SetStatusAsync($"Settings updated: {SelectedMode}, {SelectedCompareMethod}.").GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
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

    private Task SwapSidesAsync()
    {
        (LeftPath, RightPath) = (RightPath, LeftPath);
        SetStatusAsync("Left and right folders swapped.").GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    private bool ShowDialog(string title, UIElement body)
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
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow,
            MinWidth = 360
        };

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
        OperationsView.Refresh();

        var label = filter switch
        {
            OperationViewFilter.Changes => "changes",
            OperationViewFilter.Conflicts => "conflicts",
            _ => "all items"
        };

        return SetStatusAsync($"Showing {label}.");
    }

    private Task OpenDocumentationAsync()
    {
        var docsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "USER_GUIDE.md"));
        if (!File.Exists(docsPath))
        {
            docsPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "USER_GUIDE.md");
        }

        if (File.Exists(docsPath))
        {
            Process.Start(new ProcessStartInfo(docsPath) { UseShellExecute = true });
            return SetStatusAsync("Opened documentation.");
        }

        return SetStatusAsync("Documentation file was not found.");
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
        OperationsView.Refresh();
        SyncCommand.RaiseCanExecuteChanged();
    }

    private bool FilterOperation(object item)
    {
        if (item is not SyncOperation operation)
        {
            return false;
        }

        return _operationViewFilter switch
        {
            OperationViewFilter.Changes => operation.WillChangeFileSystem,
            OperationViewFilter.Conflicts => operation.Kind == OperationKind.Conflict,
            _ => true
        };
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
        NewConfigurationCommand.RaiseCanExecuteChanged();
        OpenConfigurationCommand.RaiseCanExecuteChanged();
        SaveConfigurationCommand.RaiseCanExecuteChanged();
        SaveAsConfigurationCommand.RaiseCanExecuteChanged();
        ReloadConfigurationCommand.RaiseCanExecuteChanged();
    }

    private enum OperationViewFilter
    {
        All,
        Changes,
        Conflicts
    }
}
