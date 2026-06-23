using System.Windows;
using EF_Config.Wpf.ViewModels;

namespace EF_Config.Wpf;

public partial class MainWindow : Window
{
    // ViewModel arrives via constructor injection, resolved by the DI
    // container in App.xaml.cs - this constructor signature is what the
    // container expects when it builds a MainWindow instance.
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
