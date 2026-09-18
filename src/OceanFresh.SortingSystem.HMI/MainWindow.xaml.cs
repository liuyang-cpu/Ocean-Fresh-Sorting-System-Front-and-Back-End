using System.Windows;
using System.Windows.Controls;

namespace OceanFresh.SortingSystem.HMI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        CompanionProcessManager.EnsureLocalApiStarted();
        CompanionProcessManager.EnsureYoloServiceStarted();
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.AdminPassword) &&
                string.IsNullOrEmpty(viewModel.AdminPassword) &&
                !string.IsNullOrEmpty(AdminLoginPasswordBox.Password))
            {
                AdminLoginPasswordBox.Password = string.Empty;
            }
        };
        DataContext = viewModel;
    }

    private void AdminLoginPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.AdminPassword = passwordBox.Password;
        }
    }
}
