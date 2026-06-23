using System.Windows;
using FolderSyncr.ViewModels;

namespace FolderSyncr;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
