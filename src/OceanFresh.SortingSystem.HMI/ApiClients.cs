using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.IO;
using System.Text.Json.Serialization;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.HMI;

internal sealed class OceanFreshLocalApiClient
{
    private static string _sessionToken = string.Empty;

    private readonly HttpClient _httpClient = new(new LocalSessionHandler())
    {
        BaseAddress = new Uri("http://127.0.0.1:5188/")
    };

    private readonly HttpClient _yoloHttpClient = new()
    {
        BaseAddress = new Uri(((Environment.GetEnvironmentVariable("OCEANFRESH_YOLO_SERVICE_URL")?.TrimEnd('/'))
            ?? "http://127.0.0.1:8010") + "/")
    };

    public async Task<IReadOnlyList<SeafoodProductProfileDto>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/products", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<SeafoodProductProfileDto>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<LoginResultDto> LoginOperatorAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync("api/auth/operator-login", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await ReadRequiredJsonAsync<LoginResultDto>(response, "本地 API 未返回操作员登录结果。", cancellationToken);
        SetSession(result.SessionToken);
        return result;
    }

    public async Task<LoginResultDto> LoginAdminAsync(string password, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/admin-login", new AdminLoginRequest(password), cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await ReadRequiredJsonAsync<LoginResultDto>(response, "本地 API 未返回管理员登录结果。", cancellationToken);
        SetSession(result.SessionToken);
        return result;
    }

    public static void ClearSession() => Volatile.Write(ref _sessionToken, string.Empty);

    private static void SetSession(string token) => Volatile.Write(ref _sessionToken, token ?? string.Empty);

    public async Task ChangeAdminPasswordAsync(ChangeAdminPasswordRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/admin-password", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<OperationAuditPageDto> GetAuditLogsAsync(
        string range,
        string operationType,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = $"api/audit-logs?range={Uri.EscapeDataString(range)}" +
                    $"&operationType={Uri.EscapeDataString(operationType)}" +
                    $"&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search.Trim())}";
        }

        var response = await _httpClient.GetAsync(query, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<OperationAuditPageDto>(response, "本地 API 未返回操作记录。", cancellationToken);
    }

    public async Task<string> GetDefaultProductPredictConfigTemplateAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/products/predict-config-template", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SeafoodCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/categories", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<SeafoodCategory>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<SeafoodProductProfileDto> SaveProductAsync(UpsertSeafoodProductRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/products", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var product = await response.Content.ReadFromJsonAsync<SeafoodProductProfileDto>(cancellationToken: cancellationToken);
        if (product is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的海鲜产品。");
        }

        return product;
    }

    public async Task DeleteProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"api/products/{productId}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelConfigDetailDto>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/channels", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<ChannelConfigDetailDto>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<ChannelConfig> SaveChannelAsync(UpsertChannelConfigRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/channels", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var channel = await response.Content.ReadFromJsonAsync<ChannelConfig>(cancellationToken: cancellationToken);
        if (channel is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的通道配置。");
        }

        return channel;
    }

    public async Task<ChannelConfig> SelectRuntimeChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync($"api/runtime/channel/{channelId}/select", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<ChannelConfig>(
            response,
            "本地 API 未返回当前通道。",
            cancellationToken);
    }

    public async Task DeleteChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"api/channels/{channelId}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelVersion>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/models", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<ModelVersion>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<ModelVersion> SaveModelAsync(UpsertModelRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/models", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var model = await response.Content.ReadFromJsonAsync<ModelVersion>(cancellationToken: cancellationToken);
        if (model is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的模型。");
        }

        return model;
    }

    public async Task DeleteModelAsync(Guid modelId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"api/models/{modelId}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<HardwareDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/devices", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<HardwareDevice>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<HardwareDevice> SaveDeviceAsync(UpsertHardwareDeviceRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/devices", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var device = await response.Content.ReadFromJsonAsync<HardwareDevice>(cancellationToken: cancellationToken);
        if (device is null)
        {
            throw new InvalidOperationException("本地 API 未返回保存后的设备。");
        }

        return device;
    }

    public async Task<DeviceSelfCheckResultDto> RunDeviceSelfCheckAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync($"api/devices/{deviceId}/self-check", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<DeviceSelfCheckResultDto>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("本地 API 未返回设备自检结果。");
        }

        return result;
    }

    public async Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync($"api/devices/{deviceId}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<AlarmEvent>> GetActiveAlarmsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/alarms/active", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<AlarmEvent>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<HardwareInterlockDto> GetHardwareInterlockAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/devices/interlock", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<HardwareInterlockDto>(response, "本地 API 未返回硬件联锁状态。", cancellationToken);
    }

    public async Task<SoftwareVersionDto> GetSoftwareVersionAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/system/software-version", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<SoftwareVersionDto>(response, "本地 API 未返回软件版本信息。", cancellationToken);
    }

    public async Task<SoftwareUpdateRecord> ValidateSoftwareUpdatePackageAsync(SoftwareUpdatePackageRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/system/software-update/validate", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<SoftwareUpdateRecord>(response, "本地 API 未返回更新包校验结果。", cancellationToken);
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/runtime/dashboard", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<DashboardDto>(response, "本地 API 未返回首页数据。", cancellationToken);
    }

    public async Task<RuntimeDataSourceConfigDto> GetRuntimeDataSourceAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/runtime/data-source", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<RuntimeDataSourceConfigDto>(response, "本地 API 未返回数据源配置。", cancellationToken);
    }

    public async Task<RuntimeDataSourceConfigDto> UpdateRuntimeDataSourceAsync(UpdateRuntimeDataSourceRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/runtime/data-source", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<RuntimeDataSourceConfigDto>(response, "本地 API 未返回数据源配置。", cancellationToken);
    }

    public async Task StartMachineAsync(
        CancellationToken cancellationToken,
        bool externalLocalStream = false)
    {
        var endpoint = externalLocalStream
            ? "api/runtime/machine/start?externalLocalStream=true"
            : "api/runtime/machine/start";
        var response = await _httpClient.PostAsync(endpoint, null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task StopMachineAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync("api/runtime/machine/stop", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task ResetMachineAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync("api/runtime/machine/reset", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<DetectionSession> StartDetectionAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync("api/runtime/detection/start", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<DetectionSession>(response, "本地 API 未返回检测任务。", cancellationToken);
    }

    public async Task StopDetectionAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync("api/runtime/detection/stop", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task AddStreamInspectionRecordAsync(StreamInspectionRecordRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/runtime/stream-record", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<InspectionRecord>> GetRecentInspectionRecordsAsync(int take, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"api/records/recent?take={take}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<InspectionRecord>>(cancellationToken: cancellationToken);
        return items ?? [];
    }

    public async Task<ProductionStatisticsDto> GetProductionStatisticsAsync(string range, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"api/statistics/production?range={Uri.EscapeDataString(range)}", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<ProductionStatisticsDto>(response, "本地 API 未返回历史统计数据。", cancellationToken);
    }

    public async Task<ManualReviewSessionDto> GetManualReviewAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"api/statistics/sessions/{sessionId}/manual-review", cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<ManualReviewSessionDto>(response, "本地 API 未返回人工复核数据。", cancellationToken);
    }

    public async Task<ManualReviewPreviewDto> GetManualReviewPreviewAsync(
        Guid sessionId,
        Guid inspectionRecordId,
        Guid detectionId,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(
            $"api/statistics/sessions/{sessionId}/manual-review/preview?inspectionRecordId={inspectionRecordId}&detectionId={detectionId}",
            cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<ManualReviewPreviewDto>(response, "本地 API 未返回人工复核预览图。", cancellationToken);
    }

    public async Task<ManualReviewSessionDto> UpsertManualReviewAsync(Guid sessionId, UpsertManualReviewRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/statistics/sessions/{sessionId}/manual-review", request, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<ManualReviewSessionDto>(response, "本地 API 未返回人工复核数据。", cancellationToken);
    }

    public async Task AcknowledgeAlarmAsync(Guid alarmId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync($"api/alarms/{alarmId}/acknowledge", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
    }

    public async Task<ManualInferenceResultDto> RunManualInferenceAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            throw new InvalidOperationException($"未找到待联调图片: {imagePath}");
        }

        using var content = new MultipartFormDataContent();
        var stream = File.OpenRead(imagePath);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "image", Path.GetFileName(imagePath));

        var response = await _httpClient.PostAsync("api/runtime/manual-infer", content, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ManualInferenceResultDto>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("本地 API 未返回联调推理结果。");
        }

        return result;
    }

    public async Task<YoloStreamSessionStartResultDto> StartYoloStreamSessionAsync(
        string modelPath,
        string configPath,
        int frameIntervalMilliseconds,
        string? sessionName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model_path = modelPath,
            config_path = configPath,
            frame_interval_milliseconds = frameIntervalMilliseconds,
            session_name = sessionName,
            config_overrides = new
            {
                export_penalty_hits = false,
                render_adjacent_frame_guides = false,
                save_txt = false,
                save_conf = false
            }
        };

        var response = await _yoloHttpClient.PostAsJsonAsync("stream-sessions/start", payload, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<YoloStreamSessionStartResultDto>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("YOLO 服务未返回流式会话创建结果。");
        }

        return result;
    }

    public async Task<YoloModelPreloadResultDto> PreloadYoloModelAsync(
        string modelPath,
        string configPath,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model_path = modelPath,
            config_path = configPath,
            config_overrides = new
            {
                export_penalty_hits = false,
                render_adjacent_frame_guides = false,
                save_txt = false,
                save_conf = false
            }
        };

        var response = await _yoloHttpClient.PostAsJsonAsync("models/preload", payload, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        return await ReadRequiredJsonAsync<YoloModelPreloadResultDto>(
            response,
            "YOLO 服务未返回模型预加载结果。",
            cancellationToken);
    }

    public async Task<YoloStreamFrameResultDto> PushYoloStreamFrameAsync(
        string sessionId,
        string sourcePath,
        string frameName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            source_path = sourcePath,
            frame_name = frameName
        };

        var response = await _yoloHttpClient.PostAsJsonAsync($"stream-sessions/{sessionId}/frames", payload, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<YoloStreamFrameResultDto>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("YOLO 服务未返回流式帧结果。");
        }

        return result;
    }

    public async Task<YoloStreamFinishResultDto> FinishYoloStreamSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var response = await _yoloHttpClient.PostAsync($"stream-sessions/{sessionId}/finish", null, cancellationToken);
        await EnsureSuccessWithMessageAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<YoloStreamFinishResultDto>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("YOLO 服务未返回流式会话结束结果。");
        }

        return result;
    }

    private static async Task EnsureSuccessWithMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"本地 API 请求失败: {(int)response.StatusCode} {response.ReasonPhrase}"
            : body.Trim().Trim('"');
        throw new InvalidOperationException(message);
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(HttpResponseMessage response, string emptyMessage, CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException(emptyMessage);
        }

        return result;
    }

    private sealed class LocalSessionHandler : DelegatingHandler
    {
        public LocalSessionHandler() : base(new HttpClientHandler())
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = Volatile.Read(ref _sessionToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}

internal sealed record YoloStreamSessionStartResultDto(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("runtime_config_path")] string RuntimeConfigPath,
    [property: JsonPropertyName("output_directory")] string OutputDirectory,
    [property: JsonPropertyName("frame_interval_milliseconds")] int FrameIntervalMilliseconds,
    [property: JsonPropertyName("model_preload_milliseconds")] double ModelPreloadMilliseconds,
    [property: JsonPropertyName("message")] string Message);

internal sealed record YoloModelPreloadResultDto(
    [property: JsonPropertyName("model_path")] string ModelPath,
    [property: JsonPropertyName("preload_milliseconds")] double PreloadMilliseconds,
    [property: JsonPropertyName("warmup_milliseconds")] double WarmupMilliseconds,
    [property: JsonPropertyName("already_warmed")] bool AlreadyWarmed);

internal sealed record YoloStreamFrameResultDto(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("received_frame_name")] string ReceivedFrameName,
    [property: JsonPropertyName("finalized_frame_name")] string? FinalizedFrameName,
    [property: JsonPropertyName("finalized_image_path")] string? FinalizedImagePath,
    [property: JsonPropertyName("has_finalized_output")] bool HasFinalizedOutput,
    [property: JsonPropertyName("buffered_frame_name")] string? BufferedFrameName,
    [property: JsonPropertyName("summary_path")] string SummaryPath,
    [property: JsonPropertyName("model_inference_milliseconds")] double ModelInferenceMilliseconds,
    [property: JsonPropertyName("postprocess_milliseconds")] double PostprocessMilliseconds,
    [property: JsonPropertyName("service_overhead_milliseconds")] double ServiceOverheadMilliseconds,
    [property: JsonPropertyName("request_total_milliseconds")] double RequestTotalMilliseconds,
    [property: JsonPropertyName("detections")] IReadOnlyList<YoloStreamDetectionItemDto> Detections,
    [property: JsonPropertyName("message")] string Message);

internal sealed record YoloStreamFinishResultDto(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("finalized_frame_name")] string? FinalizedFrameName,
    [property: JsonPropertyName("finalized_image_path")] string? FinalizedImagePath,
    [property: JsonPropertyName("summary_path")] string SummaryPath,
    [property: JsonPropertyName("postprocess_milliseconds")] double PostprocessMilliseconds,
    [property: JsonPropertyName("service_overhead_milliseconds")] double ServiceOverheadMilliseconds,
    [property: JsonPropertyName("request_total_milliseconds")] double RequestTotalMilliseconds,
    [property: JsonPropertyName("detections")] IReadOnlyList<YoloStreamDetectionItemDto> Detections,
    [property: JsonPropertyName("message")] string Message);

internal sealed record YoloStreamDetectionItemDto(
    [property: JsonPropertyName("image_name")] string ImageName,
    [property: JsonPropertyName("class_id")] int ClassId,
    [property: JsonPropertyName("final_conf")] double FinalConf,
    [property: JsonPropertyName("xyxy")] IReadOnlyList<double> Xyxy,
    [property: JsonPropertyName("x_center_px")] double XCenterPx,
    [property: JsonPropertyName("y_center_px")] double YCenterPx,
    [property: JsonPropertyName("phase2_decision")] string? Phase2Decision,
    [property: JsonPropertyName("applied_rules")] IReadOnlyList<string> AppliedRules);
