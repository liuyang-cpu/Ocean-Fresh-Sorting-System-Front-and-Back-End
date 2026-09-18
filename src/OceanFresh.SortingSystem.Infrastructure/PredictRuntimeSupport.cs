using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OceanFresh.SortingSystem.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class FileSystemPredictWorkspace : IPredictWorkspace
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<PreparedPredictRun> PrepareRunAsync(
        ChannelConfig channelConfig,
        string sourcePath,
        string runName,
        string predictConfigPath,
        string modelPath,
        int trainingImageSize,
        decimal confidenceThreshold,
        CancellationToken cancellationToken)
    {
        var scriptPath = GetPredictScriptPath();
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException($"未找到项目内置推理脚本: {scriptPath}");
        }

        var templatePath = GetPredictTemplatePath();
        if (!File.Exists(templatePath))
        {
            throw new InvalidOperationException($"未找到项目内置 YOLO 推理模板: {templatePath}");
        }

        var runDirectory = Path.Combine(OceanFreshPaths.PredictOutputDirectory, $"channel-{channelConfig.ChannelNo:00}", runName);
        Directory.CreateDirectory(runDirectory);
        var runtimeConfigPath = Path.Combine(runDirectory, "predict.runtime.json");
        File.Copy(templatePath, runtimeConfigPath, overwrite: true);

        var config = await LoadConfigAsync(runtimeConfigPath, cancellationToken);
        foreach (var item in await ProductPostprocessConfigFile.LoadOverridesAsync(predictConfigPath, cancellationToken))
        {
            config[item.Key] = item.Value;
        }
        config["weights"] = modelPath;
        config["imgsz"] = trainingImageSize;
        config["source"] = sourcePath;
        config["project"] = runDirectory;
        config["name"] = "predict_run";
        config["conf"] = confidenceThreshold;
        await SaveConfigAsync(runtimeConfigPath, config, cancellationToken);

        var summaryPath = Path.Combine(runDirectory, "predict_run", "phase2_postprocess_summary.json");
        return new PreparedPredictRun(
            scriptPath,
            runtimeConfigPath,
            Path.Combine(runDirectory, "predict_run"),
            summaryPath,
            [
                scriptPath,
                "--config",
                runtimeConfigPath
            ]);
    }

    public static string ResolveSharedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(OceanFreshPaths.DataRoot, path));
    }

    public static string? ResolveSharedPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : ResolveSharedPath(path);

    internal static string GetPredictTemplatePath() => ResolveBundledFile(
        Path.Combine("predict", "yolo", "predict.json"),
        Path.Combine("predict", "youge", "predict_youge.json"));

    private static string GetPredictScriptPath() => ResolveBundledFile(
        Path.Combine("predict", "yolo", "predict.py"),
        Path.Combine("predict", "youge", "predict_youge.py"));

    private static string ResolveBundledFile(params string[] relativePaths)
    {
        var candidates = relativePaths.Select(path => Path.Combine(AppContext.BaseDirectory, path)).ToArray();
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static async Task<Dictionary<string, object?>> LoadConfigAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, object?>>(stream, cancellationToken: cancellationToken);
        return payload ?? [];
    }

    private static async Task SaveConfigAsync(string path, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, cancellationToken);
    }
}

public sealed class FileSystemProductPredictConfigStore : IProductPredictConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Task<string> GetDefaultTemplateContentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductPostprocessConfigFile.CreateDefaultJson());
    }

    public async Task<string?> SaveManagedConfigAsync(
        string productCode,
        string? requestedPredictConfigPath,
        string? requestedPredictConfigJson,
        CancellationToken cancellationToken)
    {
        var content = requestedPredictConfigJson;
        if (string.IsNullOrWhiteSpace(content))
        {
            var sourcePath = FileSystemPredictWorkspace.ResolveSharedPathOrNull(requestedPredictConfigPath);
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = ProductPostprocessConfigFile.CreateDefaultJson();
        }

        var normalizedContent = ProductPostprocessConfigFile.Normalize(content);

        var managedDirectory = OceanFreshPaths.ProductPredictConfigDirectory;
        var safeCode = string.Concat(productCode.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        var managedPath = Path.Combine(managedDirectory, $"{safeCode}-postprocess.json");
        await File.WriteAllTextAsync(managedPath, normalizedContent, cancellationToken);
        return MakeRelativeToDataRoot(managedPath);
    }

    private static string MakeRelativeToDataRoot(string path)
    {
        if (path.StartsWith(OceanFreshPaths.DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(OceanFreshPaths.DataRoot, path);
        }

        return path;
    }
}

internal static class ProductPostprocessConfigFile
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string CreateDefaultJson() => Serialize(
        ProductPostprocessDefaults.AdjacentFrameHeightMin,
        ProductPostprocessDefaults.AdjacentFrameHeightMax);

    public static string Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("产品后处理配置必须是一个 JSON 对象。");
        }

        var root = document.RootElement;
        var source = root.TryGetProperty("postprocess", out var postprocess) && postprocess.ValueKind == JsonValueKind.Object
            ? postprocess
            : root;
        var heightMin = ReadDecimal(source, "adjacent_frame_height_min", ProductPostprocessDefaults.AdjacentFrameHeightMin);
        var heightMax = ReadDecimal(source, "adjacent_frame_height_max", ProductPostprocessDefaults.AdjacentFrameHeightMax);
        Validate(heightMin, heightMax);
        return Serialize(heightMin, heightMax);
    }

    public static async Task<Dictionary<string, object>> LoadOverridesAsync(string? path, CancellationToken cancellationToken)
    {
        var heightMin = ProductPostprocessDefaults.AdjacentFrameHeightMin;
        var heightMax = ProductPostprocessDefaults.AdjacentFrameHeightMax;
        var resolvedPath = FileSystemPredictWorkspace.ResolveSharedPathOrNull(path);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
        {
            var json = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
            using var document = JsonDocument.Parse(Normalize(json));
            heightMin = document.RootElement.GetProperty("adjacent_frame_height_min").GetDecimal();
            heightMax = document.RootElement.GetProperty("adjacent_frame_height_max").GetDecimal();
        }

        return new Dictionary<string, object>
        {
            ["adjacent_frame_height_min"] = heightMin,
            ["adjacent_frame_height_max"] = heightMax
        };
    }

    private static decimal ReadDecimal(JsonElement source, string name, decimal defaultValue)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var result))
        {
            throw new InvalidOperationException($"产品后处理参数 {name} 必须是数字。");
        }

        return result;
    }

    private static void Validate(decimal heightMin, decimal heightMax)
    {
        if (heightMin <= 0 || heightMax <= heightMin)
        {
            throw new InvalidOperationException("产品后处理高度范围必须满足 0 < adjacent_frame_height_min < adjacent_frame_height_max。");
        }
    }

    private static string Serialize(decimal heightMin, decimal heightMax) => JsonSerializer.Serialize(
        new Dictionary<string, object>
        {
            ["schema_version"] = 1,
            ["adjacent_frame_height_min"] = heightMin,
            ["adjacent_frame_height_max"] = heightMax
        },
        JsonOptions);
}

public sealed class PythonPredictInferenceEngine(IPredictWorkspace predictWorkspace) : IInferenceEngine
{
    public async Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken)
    {
        if (request is not ChannelInferenceRequest channelRequest)
        {
            throw new InvalidOperationException("真实 Python 推理要求使用 ChannelInferenceRequest。");
        }

        if (channelRequest.TrainingImageSize is null or <= 0)
        {
            throw new InvalidOperationException("当前模型没有有效的训练分辨率 imgsz，请先完善模型版本信息。");
        }

        var preparedRun = await predictWorkspace.PrepareRunAsync(
            channelRequest.ChannelConfig,
            channelRequest.SourcePath,
            channelRequest.RunName,
            channelRequest.PredictConfigPath,
            channelRequest.ModelPath,
            channelRequest.TrainingImageSize.Value,
            channelRequest.ConfidenceThreshold,
            cancellationToken);

        var pythonExecutable = Environment.GetEnvironmentVariable("OCEANFRESH_PYTHON_EXE");
        if (string.IsNullOrWhiteSpace(pythonExecutable))
        {
            pythonExecutable = "python";
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                WorkingDirectory = Path.GetDirectoryName(preparedRun.ScriptPath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in preparedRun.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var logDirectory = OceanFreshPaths.PredictLogDirectory;
        await File.WriteAllTextAsync(Path.Combine(logDirectory, $"{channelRequest.RunName}.stdout.log"), stdout.ToString(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(logDirectory, $"{channelRequest.RunName}.stderr.log"), stderr.ToString(), cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python predict 执行失败，退出码 {process.ExitCode}。{stderr.ToString().Trim()}");
        }

        if (!File.Exists(preparedRun.SummaryPath))
        {
            throw new InvalidOperationException($"未找到 phase2 输出摘要: {preparedRun.SummaryPath}");
        }

        await using var summaryStream = File.OpenRead(preparedRun.SummaryPath);
        var payload = await JsonSerializer.DeserializeAsync<List<Phase2SummaryItem>>(
            summaryStream,
            cancellationToken: cancellationToken) ?? [];
        var labelMap = ResolveLabelMap(channelRequest.ModelLabelMapJson);
        var detections = payload
            .Where(x => string.Equals(x.ImageName, Path.GetFileName(channelRequest.SourcePath), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.ImageName, channelRequest.SourceFileName, StringComparison.OrdinalIgnoreCase))
            .Select(x => ToDetection(x, labelMap))
            .ToList();

        return new InferenceResult(channelRequest.FrameId, false, detections, DateTimeOffset.UtcNow);
    }

    private static Dictionary<int, string> ResolveLabelMap(string labelMapJson)
        => ModelLabelMapParser.Parse(labelMapJson);

    private static DefectDetection ToDetection(Phase2SummaryItem item, IReadOnlyDictionary<int, string> labelMap)
    {
        var label = labelMap.TryGetValue(item.ClassId, out var mappedLabel)
            ? mappedLabel
            : $"class_{item.ClassId}";
        var width = Math.Max(0, (int)Math.Round(item.X2 - item.X1, MidpointRounding.AwayFromZero));
        var height = Math.Max(0, (int)Math.Round(item.Y2 - item.Y1, MidpointRounding.AwayFromZero));

        return new DefectDetection(
            Guid.NewGuid(),
            label,
            (decimal)item.FinalConf,
            (int)Math.Round(item.X1, MidpointRounding.AwayFromZero),
            (int)Math.Round(item.Y1, MidpointRounding.AwayFromZero),
            width,
            height);
    }

    private sealed record Phase2SummaryItem(
        [property: JsonPropertyName("image_name")] string ImageName,
        [property: JsonPropertyName("class_id")] int ClassId,
        [property: JsonPropertyName("final_conf")] double FinalConf,
        [property: JsonPropertyName("xyxy")] double[] Xyxy)
    {
        public double X1 => Xyxy.Length > 0 ? Xyxy[0] : 0;
        public double Y1 => Xyxy.Length > 1 ? Xyxy[1] : 0;
        public double X2 => Xyxy.Length > 2 ? Xyxy[2] : 0;
        public double Y2 => Xyxy.Length > 3 ? Xyxy[3] : 0;
    }
}

public sealed class HttpYoloServiceInferenceEngine : IInferenceEngine, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpYoloServiceInferenceEngine()
    {
        _baseUrl = Environment.GetEnvironmentVariable("OCEANFRESH_YOLO_SERVICE_URL")?.Trim()
            ?? "http://127.0.0.1:8010/";
        if (!_baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            _baseUrl += "/";
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken)
    {
        if (request is not ChannelInferenceRequest channelRequest)
        {
            throw new InvalidOperationException("HTTP YOLO 服务推理要求使用 ChannelInferenceRequest。");
        }

        if (channelRequest.TrainingImageSize is null or <= 0)
        {
            throw new InvalidOperationException("当前模型没有有效的训练分辨率 imgsz，请先完善模型版本信息。");
        }

        var productOverrides = await ProductPostprocessConfigFile.LoadOverridesAsync(
            channelRequest.PredictConfigPath,
            cancellationToken);
        var payload = new HttpInferRequest(
            channelRequest.ModelPath,
            channelRequest.SourcePath,
            FileSystemPredictWorkspace.GetPredictTemplatePath(),
            channelRequest.TrainingImageSize.Value,
            (double)channelRequest.ConfidenceThreshold,
            channelRequest.RunName,
            ResolvePredictOutputRoot(ChannelRuntimeDefaults.BuildPredictOutputDirectory(channelRequest.ChannelConfig.ChannelNo)),
            300,
            productOverrides);

        using var response = await _httpClient.PostAsJsonAsync("infer", payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorMessageAsync(response, cancellationToken);
            throw new InvalidOperationException(error);
        }

        var inferResponse = await response.Content.ReadFromJsonAsync<HttpInferResponse>(JsonOptions, cancellationToken);
        if (inferResponse is null)
        {
            throw new InvalidOperationException("YOLO 服务未返回有效的推理结果。");
        }

        var labelMap = ResolveLabelMap(channelRequest.ModelLabelMapJson);
        var detections = inferResponse.Detections
            .Select(item => ToDetection(item, labelMap))
            .ToList();

        return new InferenceResult(channelRequest.FrameId, false, detections, DateTimeOffset.UtcNow);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string ResolvePredictConfigPath(string path) =>
        FileSystemPredictWorkspace.ResolveSharedPath(path);

    private static string ResolvePredictOutputRoot(string path) =>
        FileSystemPredictWorkspace.ResolveSharedPath(path);

    private static Dictionary<int, string> ResolveLabelMap(string labelMapJson)
        => ModelLabelMapParser.Parse(labelMapJson);

    private static DefectDetection ToDetection(HttpDetectionItem item, IReadOnlyDictionary<int, string> labelMap)
    {
        var label = labelMap.TryGetValue(item.ClassId, out var mappedLabel)
            ? mappedLabel
            : $"class_{item.ClassId}";

        var x1 = item.Xyxy.Count > 0 ? item.Xyxy[0] : 0d;
        var y1 = item.Xyxy.Count > 1 ? item.Xyxy[1] : 0d;
        var x2 = item.Xyxy.Count > 2 ? item.Xyxy[2] : x1;
        var y2 = item.Xyxy.Count > 3 ? item.Xyxy[3] : y1;

        return new DefectDetection(
            Guid.NewGuid(),
            label,
            (decimal)item.FinalConf,
            (int)Math.Round(x1, MidpointRounding.AwayFromZero),
            (int)Math.Round(y1, MidpointRounding.AwayFromZero),
            Math.Max(0, (int)Math.Round(x2 - x1, MidpointRounding.AwayFromZero)),
            Math.Max(0, (int)Math.Round(y2 - y1, MidpointRounding.AwayFromZero)));
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"YOLO 服务请求失败: {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.ValueKind switch
                {
                    JsonValueKind.String => detail.GetString() ?? body,
                    JsonValueKind.Object when detail.TryGetProperty("message", out var message) => message.GetString() ?? detail.ToString(),
                    _ => detail.ToString()
                };
            }
        }
        catch (JsonException)
        {
            // Ignore and fall back to raw body.
        }

        return body.Trim().Trim('"');
    }

    private sealed record HttpInferRequest(
        [property: JsonPropertyName("model_path")] string ModelPath,
        [property: JsonPropertyName("source_path")] string SourcePath,
        [property: JsonPropertyName("config_path")] string ConfigPath,
        [property: JsonPropertyName("image_size")] int ImageSize,
        [property: JsonPropertyName("confidence_threshold")] double? ConfidenceThreshold,
        [property: JsonPropertyName("run_name")] string RunName,
        [property: JsonPropertyName("output_root")] string OutputRoot,
        [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds,
        [property: JsonPropertyName("config_overrides")] Dictionary<string, object> ConfigOverrides);

    private sealed record HttpInferResponse(
        [property: JsonPropertyName("detections")] List<HttpDetectionItem> Detections);

    private sealed record HttpDetectionItem(
        [property: JsonPropertyName("class_id")] int ClassId,
        [property: JsonPropertyName("final_conf")] double FinalConf,
        [property: JsonPropertyName("xyxy")] List<double> Xyxy);
}

public sealed class OnnxRuntimeInferenceEngine : IInferenceEngine, IDisposable
{
    private static readonly ConcurrentDictionary<string, InferenceSession> SessionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new();

    public Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken)
    {
        if (request is not ChannelInferenceRequest channelRequest)
        {
            throw new InvalidOperationException("ONNX Runtime 推理要求使用 ChannelInferenceRequest。");
        }

        if (!File.Exists(channelRequest.ModelPath))
        {
            throw new FileNotFoundException("未找到 ONNX 模型文件。", channelRequest.ModelPath);
        }

        if (!string.Equals(Path.GetExtension(channelRequest.ModelPath), ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前 ONNX Runtime 只支持 .onnx 部署模型。");
        }

        if (!File.Exists(channelRequest.SourcePath))
        {
            throw new FileNotFoundException("未找到待推理图像。", channelRequest.SourcePath);
        }

        var labelMap = ResolveLabelMap(channelRequest.ModelLabelMapJson);
        var session = GetOrCreateSession(channelRequest.ModelPath);
        var modelInput = PrepareInput(session, channelRequest.SourcePath);
        var container = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(modelInput.InputName, modelInput.Tensor)
        };
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(container);

        var detections = ParseDetections(
            results,
            labelMap,
            modelInput.OriginalWidth,
            modelInput.OriginalHeight,
            modelInput.ModelWidth,
            modelInput.ModelHeight,
            channelRequest.ConfidenceThreshold,
            0.45m);

        return Task.FromResult(new InferenceResult(channelRequest.FrameId, false, detections, DateTimeOffset.UtcNow));
    }

    private static InferenceSession GetOrCreateSession(string modelPath)
    {
        return SessionCache.GetOrAdd(modelPath, static path =>
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
            };
            return new InferenceSession(path, options);
        });
    }

    private static PreparedImageTensor PrepareInput(InferenceSession session, string imagePath)
    {
        var input = session.InputMetadata.First();
        var dimensions = input.Value.Dimensions.ToArray();
        if (dimensions.Length < 4)
        {
            throw new InvalidOperationException("当前 ONNX 模型输入维度不是标准图像张量。");
        }

        var channelCount = dimensions[1] > 0 ? dimensions[1] : 3;
        var modelHeight = dimensions[2] > 0 ? dimensions[2] : 300;
        var modelWidth = dimensions[3] > 0 ? dimensions[3] : 1536;

        using Image<Rgb24> image = Image.Load<Rgb24>(imagePath);
        var originalWidth = image.Width;
        var originalHeight = image.Height;
        image.Mutate(x => x.Resize(modelWidth, modelHeight));

        var tensor = new DenseTensor<float>(new[] { 1, channelCount, modelHeight, modelWidth });
        for (var y = 0; y < modelHeight; y++)
        {
            for (var x = 0; x < modelWidth; x++)
            {
                var pixel = image[x, y];
                if (channelCount == 1)
                {
                    tensor[0, 0, y, x] = (pixel.R + pixel.G + pixel.B) / (3f * 255f);
                }
                else
                {
                    tensor[0, 0, y, x] = pixel.R / 255f;
                    tensor[0, 1, y, x] = pixel.G / 255f;
                    tensor[0, 2, y, x] = pixel.B / 255f;
                }
            }
        }

        return new PreparedImageTensor(input.Key, tensor, originalWidth, originalHeight, modelWidth, modelHeight);
    }

    private static IReadOnlyList<DefectDetection> ParseDetections(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
        IReadOnlyDictionary<int, string> labelMap,
        int originalWidth,
        int originalHeight,
        int modelWidth,
        int modelHeight,
        decimal confidenceThreshold,
        decimal nmsThreshold)
    {
        var output = outputs.FirstOrDefault();
        if (output is null)
        {
            return [];
        }

        var tensor = output.AsTensor<float>();
        var vectors = FlattenPredictions(tensor, labelMap.Count);
        var candidates = new List<DetectionCandidate>(vectors.Count);
        var confidenceFloor = (float)confidenceThreshold;

        foreach (var vector in vectors)
        {
            var candidate = TryParseCandidate(vector, labelMap.Count, confidenceFloor);
            if (candidate is null)
            {
                continue;
            }

            var scaled = ScaleCandidate(candidate, originalWidth, originalHeight, modelWidth, modelHeight);
            candidates.Add(scaled);
        }

        var finalCandidates = ApplyNonMaximumSuppression(candidates, (float)nmsThreshold);
        return finalCandidates.Select(candidate =>
        {
            var label = labelMap.TryGetValue(candidate.ClassId, out var mappedLabel)
                ? mappedLabel
                : $"class_{candidate.ClassId}";
            return new DefectDetection(
                Guid.NewGuid(),
                label,
                (decimal)candidate.Confidence,
                (int)Math.Round(candidate.X1, MidpointRounding.AwayFromZero),
                (int)Math.Round(candidate.Y1, MidpointRounding.AwayFromZero),
                Math.Max(0, (int)Math.Round(candidate.X2 - candidate.X1, MidpointRounding.AwayFromZero)),
                Math.Max(0, (int)Math.Round(candidate.Y2 - candidate.Y1, MidpointRounding.AwayFromZero)));
        }).ToList();
    }

    private static List<float[]> FlattenPredictions(Tensor<float> tensor, int labelCount)
    {
        var dims = tensor.Dimensions.ToArray();
        if (dims.Length == 2)
        {
            return EnumerateRank2(tensor, dims[0], dims[1]);
        }

        if (dims.Length == 3)
        {
            if (dims[0] != 1)
            {
                throw new InvalidOperationException("暂不支持 batch > 1 的 ONNX 输出。");
            }

            var dim1 = dims[1];
            var dim2 = dims[2];
            if (LooksLikeNByAttributes(dim1, dim2, labelCount))
            {
                return EnumerateRank3AsBoxes(tensor, dim1, dim2);
            }

            return EnumerateRank3AsChannels(tensor, dim1, dim2);
        }

        throw new InvalidOperationException("当前 ONNX 输出维度暂不支持。");
    }

    private static bool LooksLikeNByAttributes(int dim1, int dim2, int labelCount)
    {
        var attrCandidate = dim2;
        return attrCandidate == 6 || attrCandidate == labelCount + 4 || attrCandidate == labelCount + 5;
    }

    private static List<float[]> EnumerateRank2(Tensor<float> tensor, int d0, int d1)
    {
        var vectors = new List<float[]>(d0);
        for (var row = 0; row < d0; row++)
        {
            var vector = new float[d1];
            for (var col = 0; col < d1; col++)
            {
                vector[col] = tensor[row, col];
            }
            vectors.Add(vector);
        }
        return vectors;
    }

    private static List<float[]> EnumerateRank3AsBoxes(Tensor<float> tensor, int boxes, int attributes)
    {
        var vectors = new List<float[]>(boxes);
        for (var box = 0; box < boxes; box++)
        {
            var vector = new float[attributes];
            for (var attr = 0; attr < attributes; attr++)
            {
                vector[attr] = tensor[0, box, attr];
            }
            vectors.Add(vector);
        }
        return vectors;
    }

    private static List<float[]> EnumerateRank3AsChannels(Tensor<float> tensor, int channels, int boxes)
    {
        var vectors = new List<float[]>(boxes);
        for (var box = 0; box < boxes; box++)
        {
            var vector = new float[channels];
            for (var channel = 0; channel < channels; channel++)
            {
                vector[channel] = tensor[0, channel, box];
            }
            vectors.Add(vector);
        }
        return vectors;
    }

    private static DetectionCandidate? TryParseCandidate(float[] values, int labelCount, float confidenceThreshold)
    {
        if (values.Length < 6)
        {
            return null;
        }

        if (values.Length == 6)
        {
            var score = values[4];
            if (score < confidenceThreshold)
            {
                return null;
            }

            return new DetectionCandidate(values[0], values[1], values[2], values[3], score, (int)Math.Round(values[5]));
        }

        if (values.Length == labelCount + 4)
        {
            return ParseCxCyWhCandidate(values, values.Skip(4).ToArray(), 1f, confidenceThreshold);
        }

        if (values.Length == labelCount + 5)
        {
            return ParseCxCyWhCandidate(values, values.Skip(5).ToArray(), values[4], confidenceThreshold);
        }

        var objectness = values[4];
        var classScores = values.Skip(5).ToArray();
        if (classScores.Length == 0)
        {
            return null;
        }

        return ParseCxCyWhCandidate(values, classScores, objectness, confidenceThreshold);
    }

    private static DetectionCandidate? ParseCxCyWhCandidate(float[] rawValues, float[] classScores, float objectness, float confidenceThreshold)
    {
        var bestIndex = -1;
        var bestScore = float.MinValue;
        for (var i = 0; i < classScores.Length; i++)
        {
            if (classScores[i] > bestScore)
            {
                bestScore = classScores[i];
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return null;
        }

        var finalScore = Math.Clamp(bestScore * Math.Max(0f, objectness), 0f, 1f);
        if (objectness == 1f && rawValues.Length >= 5 && classScores.Length > 0 && rawValues.Length == classScores.Length + 4)
        {
            finalScore = Math.Clamp(bestScore, 0f, 1f);
        }

        if (finalScore < confidenceThreshold)
        {
            return null;
        }

        var cx = rawValues[0];
        var cy = rawValues[1];
        var width = rawValues[2];
        var height = rawValues[3];
        return new DetectionCandidate(
            cx - (width / 2f),
            cy - (height / 2f),
            cx + (width / 2f),
            cy + (height / 2f),
            finalScore,
            bestIndex);
    }

    private static DetectionCandidate ScaleCandidate(DetectionCandidate candidate, int originalWidth, int originalHeight, int modelWidth, int modelHeight)
    {
        var scaleX = modelWidth <= 0 ? 1f : (float)originalWidth / modelWidth;
        var scaleY = modelHeight <= 0 ? 1f : (float)originalHeight / modelHeight;
        return candidate with
        {
            X1 = Math.Clamp(candidate.X1 * scaleX, 0, originalWidth),
            X2 = Math.Clamp(candidate.X2 * scaleX, 0, originalWidth),
            Y1 = Math.Clamp(candidate.Y1 * scaleY, 0, originalHeight),
            Y2 = Math.Clamp(candidate.Y2 * scaleY, 0, originalHeight)
        };
    }

    private static IReadOnlyList<DetectionCandidate> ApplyNonMaximumSuppression(IReadOnlyList<DetectionCandidate> candidates, float iouThreshold)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var kept = new List<DetectionCandidate>(candidates.Count);
        foreach (var group in candidates.GroupBy(x => x.ClassId))
        {
            var pending = group.OrderByDescending(x => x.Confidence).ToList();
            while (pending.Count > 0)
            {
                var current = pending[0];
                kept.Add(current);
                pending.RemoveAt(0);
                pending.RemoveAll(other => CalculateIoU(current, other) > iouThreshold);
            }
        }

        return kept.OrderByDescending(x => x.Confidence).ToList();
    }

    private static float CalculateIoU(DetectionCandidate left, DetectionCandidate right)
    {
        var intersectX1 = Math.Max(left.X1, right.X1);
        var intersectY1 = Math.Max(left.Y1, right.Y1);
        var intersectX2 = Math.Min(left.X2, right.X2);
        var intersectY2 = Math.Min(left.Y2, right.Y2);
        var intersectWidth = Math.Max(0, intersectX2 - intersectX1);
        var intersectHeight = Math.Max(0, intersectY2 - intersectY1);
        var intersection = intersectWidth * intersectHeight;
        if (intersection <= 0)
        {
            return 0f;
        }

        var leftArea = Math.Max(0, left.X2 - left.X1) * Math.Max(0, left.Y2 - left.Y1);
        var rightArea = Math.Max(0, right.X2 - right.X1) * Math.Max(0, right.Y2 - right.Y1);
        var union = leftArea + rightArea - intersection;
        return union <= 0 ? 0f : intersection / union;
    }

    private static Dictionary<int, string> ResolveLabelMap(string labelMapJson)
        => ModelLabelMapParser.Parse(labelMapJson);

    public void Dispose()
    {
        foreach (var session in SessionCache.Values)
        {
            session.Dispose();
        }

        SessionCache.Clear();
    }

    private sealed record PreparedImageTensor(
        string InputName,
        DenseTensor<float> Tensor,
        int OriginalWidth,
        int OriginalHeight,
        int ModelWidth,
        int ModelHeight);

    private sealed record DetectionCandidate(
        float X1,
        float Y1,
        float X2,
        float Y2,
        float Confidence,
        int ClassId);
}
