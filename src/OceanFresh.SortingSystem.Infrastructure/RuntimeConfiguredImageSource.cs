using System.Diagnostics;
using System.Runtime.CompilerServices;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class RuntimeConfiguredImageSource(
    IRuntimeDataSourceStore dataSourceStore,
    IRuntimeStateStore runtimeStateStore,
    TechikDetectorBridgeProcess? detectorBridge = null) : IImageSource
{
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
        ".tiff",
        ".ofxraw"
    };

    public async IAsyncEnumerable<InferenceRequest> CaptureAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processedCameraFiles = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
        var cameraInitialized = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (runtimeStateStore.GetSnapshot().RuntimeMode != RuntimeMode.Running)
            {
                await Task.Delay(100, cancellationToken);
                continue;
            }

            var config = dataSourceStore.Get();
            if (config.Mode == RuntimeDataSourceMode.LocalImageDirectory)
            {
                cameraInitialized = false;
                if (dataSourceStore.IsExternalLocalStreamActive)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                var emitted = await TryReadNextLocalImageAsync(config, cancellationToken);
                if (emitted is not null)
                {
                    var emittedAt = Stopwatch.GetTimestamp();
                    yield return emitted;
                    var processingMilliseconds = Stopwatch.GetElapsedTime(emittedAt).TotalMilliseconds;
                    var remainingDelay = config.FrameIntervalMilliseconds - processingMilliseconds;
                    if (remainingDelay > 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(remainingDelay), cancellationToken);
                    }

                    continue;
                }

                await Task.Delay(200, cancellationToken);
                continue;
            }

            if (detectorBridge is not null && !detectorBridge.IsRunning)
            {
                try
                {
                    await detectorBridge.EnsureStartedAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    dataSourceStore.UpdateProgress(0, $"Techik 探测器启动失败: {exception.Message}");
                    await Task.Delay(1_000, cancellationToken);
                    continue;
                }
            }

            var cameraDirectory = PlcXrayImageSource.ResolveInputDirectory();
            Directory.CreateDirectory(cameraDirectory);

            if (!cameraInitialized)
            {
                processedCameraFiles.Clear();
                if (!ShouldProcessExistingFiles())
                {
                    foreach (var file in EnumerateImageFiles(cameraDirectory))
                    {
                        if (TryGetSignature(file) is { } signature)
                        {
                            processedCameraFiles[file] = signature;
                        }
                    }
                }

                cameraInitialized = true;
            }

            var pollInterval = ReadPositiveInt(PollIntervalEnvVar, DefaultPollIntervalMilliseconds);
            var stableMilliseconds = ReadPositiveInt(StableMillisecondsEnvVar, DefaultStableMilliseconds);
            var emittedCameraFrame = false;

            foreach (var file in EnumerateImageFiles(cameraDirectory))
            {
                var currentSignature = TryGetSignature(file);
                if (currentSignature is null)
                {
                    continue;
                }

                if (processedCameraFiles.TryGetValue(file, out var processedSignature) &&
                    processedSignature.Equals(currentSignature.Value))
                {
                    continue;
                }

                var stableSignature = await TryGetStableSignatureAsync(file, stableMilliseconds, cancellationToken);
                if (stableSignature is null)
                {
                    continue;
                }

                var imageBytes = await TryReadBytesAsync(file, cancellationToken);
                if (imageBytes is null || imageBytes.Length == 0)
                {
                    continue;
                }

                processedCameraFiles[file] = stableSignature.Value;
                dataSourceStore.UpdateProgress(0, $"X 光相机采集: {Path.GetFileName(file)}");
                emittedCameraFrame = true;
                yield return new InferenceRequest(
                    Guid.Empty,
                    Guid.Empty,
                    BuildFrameId("xray", Path.GetFileName(file), stableSignature.Value.LastWriteUtc),
                    imageBytes,
                    new DateTimeOffset(stableSignature.Value.LastWriteUtc, TimeSpan.Zero),
                    Path.GetFileName(file),
                    RuntimeDataSourceMode.XrayCamera,
                    true);
            }

            if (!emittedCameraFrame)
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
        }
    }

    public static IReadOnlyList<string> EnumerateSupportedImages(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedImageFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<InferenceRequest?> TryReadNextLocalImageAsync(
        RuntimeDataSourceConfig config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.LocalDirectoryPath))
        {
            dataSourceStore.UpdateProgress(0, "本地图片目录未配置");
            return null;
        }

        var files = EnumerateSupportedImages(config.LocalDirectoryPath);
        if (files.Count == 0)
        {
            dataSourceStore.UpdateProgress(0, "本地图片目录没有可检测图片");
            return null;
        }

        var currentIndex = Math.Clamp(config.CurrentIndex, 0, files.Count);
        if (currentIndex >= files.Count)
        {
            dataSourceStore.UpdateProgress(files.Count, $"本地图片目录检测完成，共 {files.Count} 张");
            return null;
        }

        var file = files[currentIndex];
        var imageBytes = await TryReadBytesAsync(file, cancellationToken);
        if (imageBytes is null || imageBytes.Length == 0)
        {
            dataSourceStore.UpdateProgress(currentIndex + 1, $"跳过无法读取的图片: {Path.GetFileName(file)}");
            return null;
        }

        var capturedAt = File.GetLastWriteTimeUtc(file);
        dataSourceStore.UpdateProgress(currentIndex + 1, $"本地图片目录检测 {currentIndex + 1}/{files.Count}: {Path.GetFileName(file)}");
        return new InferenceRequest(
            Guid.Empty,
            Guid.Empty,
            BuildFrameId("local", Path.GetFileName(file), capturedAt),
            imageBytes,
            new DateTimeOffset(capturedAt, TimeSpan.Zero),
            Path.GetFileName(file),
            RuntimeDataSourceMode.LocalImageDirectory,
            false);
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

    private static async Task<byte[]?> TryReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return string.Equals(Path.GetExtension(path), ".ofxraw", StringComparison.OrdinalIgnoreCase)
                ? await TechikRawFrameCodec.EncodePngAsync(bytes, cancellationToken)
                : bytes;
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

    private static string BuildFrameId(string sourcePrefix, string fileName, DateTime lastWriteUtc)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var safeName = string.Concat(nameWithoutExtension.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = sourcePrefix;
        }

        return $"{sourcePrefix}-{lastWriteUtc:yyyyMMddHHmmssfff}-{safeName}";
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
