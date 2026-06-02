using System.Diagnostics;
using System.IO;

namespace OceanFresh.SortingSystem.HMI;

internal static class CompanionProcessManager
{
    public static void EnsureLocalApiStarted()
    {
        if (Process.GetProcessesByName("OceanFresh.SortingSystem.LocalApi").Length > 0)
        {
            return;
        }

        var currentDirectory = AppContext.BaseDirectory;
        var candidatePath = Path.GetFullPath(Path.Combine(
            currentDirectory,
            "..",
            "..",
            "..",
            "..",
            "OceanFresh.SortingSystem.LocalApi",
            "bin",
            "Debug",
            "net8.0",
            "OceanFresh.SortingSystem.LocalApi.exe"));

        if (!File.Exists(candidatePath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = candidatePath,
            WorkingDirectory = Path.GetDirectoryName(candidatePath) ?? currentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(startInfo);
    }
}
