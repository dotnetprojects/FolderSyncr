using System.Windows;
using FileSyncr.ViewModels;

namespace FileSyncr;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
