using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            Loaded += async (_, _) => await viewModel.ApplyStartupOptionsAsync(startupOptions);
        }
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

        var menu = new ContextMenu { DataContext = operation };
        menu.Items.Add(CreateOperationMenuItem("Open left item", async op => await GetViewModel().OpenOperationSideAsync(op, openLeftSide: true)));
        menu.Items.Add(CreateOperationMenuItem("Open right item", async op => await GetViewModel().OpenOperationSideAsync(op, openLeftSide: false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateOperationMenuItem("Copy relative path", async op => await GetViewModel().CopyOperationRelativePathAsync(op)));
        menu.Items.Add(CreateOperationMenuItem("Exclude from comparison", async op => await GetViewModel().ExcludeOperationAsync(op)));
        row.ContextMenu = menu;
    }

    private MenuItem CreateOperationMenuItem(string header, Func<SyncOperation, Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) =>
        {
            if (item.Parent is ContextMenu { DataContext: SyncOperation operation })
            {
                await action(operation);
            }
        };
        return item;
    }

    private MainViewModel GetViewModel() => (MainViewModel)DataContext;
}
