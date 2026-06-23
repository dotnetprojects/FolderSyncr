using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FolderSyncr.Models;
using FolderSyncr.Services;
using FolderSyncr.ViewModels;

namespace FolderSyncr;

public partial class MainWindow : Window
{
    public MainWindow(CommandLineStartupOptions? startupOptions = null)
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        if (startupOptions is not null)
        {
            if (startupOptions.StartMinimized)
            {
                WindowState = WindowState.Minimized;
            }

            Loaded += async (_, _) => await viewModel.ApplyStartupOptionsAsync(startupOptions);
        }

        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void PathBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedDirectory(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void PathBox_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not TextBox textBox)
        {
            return;
        }

        var path = GetDroppedDirectory(e);
        if (path is null)
        {
            return;
        }

        if (string.Equals(textBox.Tag?.ToString(), "Left", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.LeftPath = path;
        }
        else if (string.Equals(textBox.Tag?.ToString(), "Right", StringComparison.OrdinalIgnoreCase))
        {
            viewModel.RightPath = path;
        }

        e.Handled = true;
    }

    private static string? GetDroppedDirectory(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var path = paths?.FirstOrDefault(Directory.Exists);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private async void OperationRow_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: SyncOperation operation }
            && DataContext is MainViewModel viewModel)
        {
            await viewModel.OpenOperationDefaultAsync(operation);
            e.Handled = true;
        }
    }

    private void OperationRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: SyncOperation operation } row)
        {
            return;
        }

        var menu = new ContextMenu { DataContext = new OperationContext(operation, GetSelectedOperations(row, operation)) };
        menu.Items.Add(CreateOperationMenuItem("Open left item", async op => await GetViewModel().OpenOperationSideAsync(op, openLeftSide: true)));
        menu.Items.Add(CreateOperationMenuItem("Open right item", async op => await GetViewModel().OpenOperationSideAsync(op, openLeftSide: false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateOperationMenuItem("Copy relative path", async op => await GetViewModel().CopyOperationRelativePathAsync(op)));
        menu.Items.Add(CreateOperationMenuItem("Add to include filter", async op => await GetViewModel().IncludeOperationAsync(op)));
        menu.Items.Add(CreateOperationMenuItem("Exclude from comparison", async op => await GetViewModel().ExcludeOperationAsync(op)));

        var externalCommands = GetViewModel().ExternalCommands;
        if (externalCommands.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var command in externalCommands)
            {
                menu.Items.Add(CreateExternalCommandMenuItem(command));
            }
        }

        row.ContextMenu = menu;
    }

    private MenuItem CreateOperationMenuItem(string header, Func<SyncOperation, Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) =>
        {
            if (item.Parent is ContextMenu { DataContext: OperationContext context })
            {
                await action(context.Operation);
            }
        };
        return item;
    }

    private MenuItem CreateExternalCommandMenuItem(ExternalCommandDefinition command)
    {
        var item = new MenuItem { Header = command.Name };
        item.Click += async (_, _) =>
        {
            if (item.Parent is ContextMenu { DataContext: OperationContext context })
            {
                await GetViewModel().RunExternalCommandAsync(command, context.SelectedOperations);
            }
        };
        return item;
    }

    private MainViewModel GetViewModel() => (MainViewModel)DataContext;

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTextInputFocused(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var commandIndex = GetNumericCommandIndex(e.Key);
        if (commandIndex < 0)
        {
            return;
        }

        var viewModel = GetViewModel();
        if (commandIndex >= viewModel.ExternalCommands.Count)
        {
            return;
        }

        e.Handled = true;
        await viewModel.RunExternalCommandAsync(
            viewModel.ExternalCommands[commandIndex],
            GetSelectedOperationsFromGrids());
    }

    private static IReadOnlyList<SyncOperation> GetSelectedOperations(DataGridRow row, SyncOperation clickedOperation)
    {
        var selectedOperations = FindParent<DataGrid>(row)?.SelectedItems
            .OfType<SyncOperation>()
            .ToList() ?? [];

        if (!selectedOperations.Contains(clickedOperation))
        {
            selectedOperations.Insert(0, clickedOperation);
        }

        return selectedOperations;
    }

    private IReadOnlyList<SyncOperation> GetSelectedOperationsFromGrids()
    {
        var selectedOperations = new[]
            {
                LeftOperationsGrid,
                ActionOperationsGrid,
                RightOperationsGrid
            }
            .SelectMany(grid => grid.SelectedItems.OfType<SyncOperation>())
            .Distinct()
            .ToList();

        if (selectedOperations.Count == 0 && ActionOperationsGrid.CurrentItem is SyncOperation currentOperation)
        {
            selectedOperations.Add(currentOperation);
        }

        return selectedOperations;
    }

    private static int GetNumericCommandIndex(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return key - Key.D0;
        }

        return key is >= Key.NumPad0 and <= Key.NumPad9 ? key - Key.NumPad0 : -1;
    }

    private static bool IsTextInputFocused(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is TextBoxBase or PasswordBox or ComboBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static T? FindParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed record OperationContext(SyncOperation Operation, IReadOnlyList<SyncOperation> SelectedOperations);
}
