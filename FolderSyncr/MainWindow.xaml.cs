using System.IO;
using System.Windows;
using System.Windows.Controls;
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
}
