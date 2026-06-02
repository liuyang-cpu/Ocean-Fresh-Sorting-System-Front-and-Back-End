using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace OceanFresh.SortingSystem.HMI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteExceptionLog("UI", e.Exception);
        MessageBox.Show(
            $"界面发生未处理异常:\n{e.Exception.Message}",
            "海洋生鲜分拣系统",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteExceptionLog("AppDomain", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteExceptionLog("Task", e.Exception);
        e.SetObserved();
    }

    private static void WriteExceptionLog(string source, Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "hmi-errors.log");
            var builder = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}")
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 80));
            File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Swallow logging failures so the app can keep responding.
        }
    }
}
