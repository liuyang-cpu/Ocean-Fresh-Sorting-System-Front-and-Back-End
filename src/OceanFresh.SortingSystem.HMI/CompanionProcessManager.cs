using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace OceanFresh.SortingSystem.HMI;

internal static class CompanionProcessManager
{
    private const string LocalApiBaseUrl = "http://127.0.0.1:5188";

    private static readonly HttpClient HealthClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public static void EnsureLocalApiStarted()
    {
        if (IsLocalApiHealthy())
        {
            return;
        }

        var candidatePath = ResolveLocalApiPath();
        if (!File.Exists(candidatePath))
        {
            return;
        }

        // A terminated or suspended LocalApi can remain visible in the process
        // table without owning port 5188. Health is the source of truth here.
        var startInfo = new ProcessStartInfo
        {
            FileName = candidatePath,
            WorkingDirectory = Path.GetDirectoryName(candidatePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(startInfo);

        for (var attempt = 0; attempt < 30; attempt++)
        {
            Thread.Sleep(250);
            if (IsLocalApiHealthy())
            {
                return;
            }
        }
    }

    private static string ResolveLocalApiPath()
    {
        var currentDirectory = AppContext.BaseDirectory;
        var srcDirectory = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(srcDirectory, "OceanFresh.SortingSystem.LocalApi", "bin", "Release", "net8.0", "OceanFresh.SortingSystem.LocalApi.exe"),
            Path.Combine(srcDirectory, "OceanFresh.SortingSystem.LocalApi", "bin", "Debug", "net8.0", "OceanFresh.SortingSystem.LocalApi.exe"),
            Path.Combine(currentDirectory, "OceanFresh.SortingSystem.LocalApi.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static bool IsLocalApiHealthy()
    {
        try
        {
            using var response = HealthClient.GetAsync($"{LocalApiBaseUrl}/api/runtime/snapshot").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureYoloServiceStarted()
    {
        if (IsYoloServiceHealthy())
        {
            return;
        }

        var currentDirectory = AppContext.BaseDirectory;
        var repositoryRoot = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "predict", "yolo", "service", "start_yolo_service.bat"),
            Path.Combine(repositoryRoot, "predict", "youge", "service", "start_yolo_fastapi_service.bat")
        };
        var candidatePath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];

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

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(500);
            if (IsYoloServiceHealthy())
            {
                return;
            }
        }
    }

    private static bool IsYoloServiceHealthy()
    {
        try
        {
            var baseUrl = (Environment.GetEnvironmentVariable("OCEANFRESH_YOLO_SERVICE_URL")?.TrimEnd('/'))
                ?? "http://127.0.0.1:8010";
            using var response = HealthClient.GetAsync($"{baseUrl}/health").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
