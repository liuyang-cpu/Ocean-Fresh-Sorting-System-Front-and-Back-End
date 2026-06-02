using System.Windows;

namespace OceanFresh.SortingSystem.HMI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        CompanionProcessManager.EnsureLocalApiStarted();
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
