using System.Windows;
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
}
