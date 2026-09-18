using System.Runtime.CompilerServices;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class PlcXrayImageSource : IImageSource
{
    private const string InputDirectoryEnvVar = "OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY";
    private const string PollIntervalEnvVar = "OCEANFRESH_PLC_XRAY_POLL_MS";
    private const string StableMillisecondsEnvVar = "OCEANFRESH_PLC_XRAY_STABLE_MS";
    private const string ProcessExistingEnvVar = "OCEANFRESH_PLC_XRAY_PROCESS_EXISTING";
    private const int DefaultPollIntervalMilliseconds = 20;
    private const int DefaultStableMilliseconds = 40;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tif",
        ".tiff"
    };

    public async IAsyncEnumerable<InferenceRequest> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var inputDirectory = ResolveInputDirectory();
        Directory.CreateDirectory(inputDirectory);

        var pollInterval = ReadPositiveInt(PollIntervalEnvVar, DefaultPollIntervalMilliseconds);
        var stableMilliseconds = ReadPositiveInt(StableMillisecondsEnvVar, DefaultStableMilliseconds);
        var processedFiles = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);

        if (!ShouldProcessExistingFiles())
        {
            foreach (var file in EnumerateImageFiles(inputDirectory))
            {
                if (TryGetSignature(file) is { } signature)
                {
                    processedFiles[file] = signature;
                }
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var file in EnumerateImageFiles(inputDirectory))
            {
                var currentSignature = TryGetSignature(file);
                if (currentSignature is null)
                {
                    continue;
                }

                if (processedFiles.TryGetValue(file, out var processedSignature) &&
                    processedSignature.Equals(currentSignature.Value))
                {
                    continue;
                }

                var stableSignature = await TryGetStableSignatureAsync(file, stableMilliseconds, cancellationToken);
                if (stableSignature is null)
                {
                    continue;
                }

                byte[] imageBytes;
                try
                {
                    imageBytes = await File.ReadAllBytesAsync(file, cancellationToken);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (imageBytes.Length == 0)
                {
                    continue;
                }

                processedFiles[file] = stableSignature.Value;
                var fileName = Path.GetFileName(file);
                yield return new InferenceRequest(
                    Guid.Empty,
                    Guid.Empty,
                    BuildFrameId(fileName, stableSignature.Value.LastWriteUtc),
                    imageBytes,
                    new DateTimeOffset(stableSignature.Value.LastWriteUtc, TimeSpan.Zero),
                    fileName);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    public static string ResolveInputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(InputDirectoryEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(OceanFreshPaths.DataRoot, "plc-xray-input");
    }

    private static IEnumerable<string> EnumerateImageFiles(string inputDirectory)
    {
        if (!Directory.Exists(inputDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(inputDirectory)
            .Where(IsSupportedImageFile)
            .OrderBy(path => File.GetLastWriteTimeUtc(path))
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSupportedImageFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    private static async Task<FileSignature?> TryGetStableSignatureAsync(
        string path,
        int stableMilliseconds,
        CancellationToken cancellationToken)
    {
        var first = TryGetSignature(path);
        if (first is null)
        {
            return null;
        }

        await Task.Delay(stableMilliseconds, cancellationToken);
        var second = TryGetSignature(path);
        return first.Equals(second) ? second : null;
    }

    private static FileSignature? TryGetSignature(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return new FileSignature(info.Length, info.LastWriteTimeUtc);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string BuildFrameId(string fileName, DateTime lastWriteUtc)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var safeName = string.Concat(nameWithoutExtension.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "plc_xray";
        }

        return $"{lastWriteUtc:yyyyMMddHHmmssfff}-{safeName}";
    }

    private static bool ShouldProcessExistingFiles()
    {
        var raw = Environment.GetEnvironmentVariable(ProcessExistingEnvVar);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadPositiveInt(string variableName, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private readonly record struct FileSignature(long Length, DateTime LastWriteUtc);
}
