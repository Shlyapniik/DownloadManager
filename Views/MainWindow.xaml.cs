using System.Windows;
using DownloadManager.ViewModels;

namespace DownloadManager.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}