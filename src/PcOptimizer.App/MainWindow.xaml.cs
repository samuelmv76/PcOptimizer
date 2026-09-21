using System.Windows;
using PcOptimizer.App.ViewModels;

namespace PcOptimizer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
