namespace OceanFresh.SortingSystem.HMI;

internal static class ShellNavigationService
{
    public static Action<object, string, string>? NavigateRequested { get; set; }

    public static void Navigate(object pageViewModel, string title, string subtitle)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            NavigateRequested?.Invoke(pageViewModel, title, subtitle);
            return;
        }

        dispatcher.BeginInvoke(() => NavigateRequested?.Invoke(pageViewModel, title, subtitle));
    }
}
