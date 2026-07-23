using System.Globalization;
using System.Reflection;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public sealed class SeafoodProductService(
    ISeafoodProductRepository productRepository,
    ISeafoodTraitRepository traitRepository,
    IChannelConfigRepository channelRepository,
    IModelRegistryRepository modelRepository,
    IProductPredictConfigStore productPredictConfigStore)
{
    public async Task<IReadOnlyList<SeafoodProductProfileDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var products = await productRepository.GetAllAsync(cancellationToken);
        var results = new List<SeafoodProductProfileDto>(products.Count);

        foreach (var product in products)
        {
            var traits = await traitRepository.GetByProductAsync(product.Id, cancellationToken);
            results.Add(new SeafoodProductProfileDto(product, traits));
        }

        return results;
    }

    public async Task<SeafoodProductProfileDto> UpsertAsync(UpsertSeafoodProductRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("海鲜名称不能为空。");
        }

        var existingProducts = await productRepository.GetAllAsync(cancellationToken);
        var existingProduct = request.Id is Guid productId
            ? existingProducts.FirstOrDefault(x => x.Id == productId)
            : null;
        if (request.Id is not null && existingProduct is null)
        {
            throw new InvalidOperationException("要编辑的海鲜产品不存在。");
        }

        var productCode = existingProduct?.Code ?? GenerateProductCode(existingProducts);

        if (existingProducts.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("海鲜名称不能与现有产品重复。");
        }

        if (request.Traits is null || request.Traits.Count == 0)
        {
            throw new InvalidOperationException("海鲜产品至少需要包含正常项和一个缺陷项。");
        }

        var normalCount = request.Traits.Count(x => x.IsNormal && !string.IsNullOrWhiteSpace(x.Name));
        var defectCount = request.Traits.Count(x => !x.IsNormal && !string.IsNullOrWhiteSpace(x.Name));
        if (normalCount != 1)
        {
            throw new InvalidOperationException("海鲜产品必须且只能保留一个正常项。");
        }

        if (defectCount < 1)
        {
            throw new InvalidOperationException("海鲜产品至少需要配置一个缺陷项。");
        }

        if (string.IsNullOrWhiteSpace(request.ClassesFilePath))
        {
            throw new InvalidOperationException("海鲜产品必须绑定一份 classes.txt 标签文件。");
        }

        if (string.IsNullOrWhiteSpace(request.LabelMapJson))
        {
            throw new InvalidOperationException("请先完成 classes.txt 标签与海鲜产品性状的一一对应映射。");
        }

        var managedPredictConfigPath = await productPredictConfigStore.SaveManagedConfigAsync(
            productCode,
            request.PredictConfigPath,
            request.PredictConfigJson,
            cancellationToken) ?? string.Empty;

        var product = new SeafoodProduct(
            request.Id ?? Guid.NewGuid(),
            productCode,
            request.Name.Trim(),
            request.IsEnabled,
            request.ClassesFilePath.Trim(),
            request.LabelMapJson.Trim(),
            managedPredictConfigPath);

        var savedProduct = await productRepository.UpsertAsync(product, cancellationToken);
        var traits = request.Traits
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select((trait, index) => new SeafoodTrait(
                Guid.NewGuid(),
                savedProduct.Id,
                trait.Name.Trim(),
                trait.IsNormal,
                index + 1,
                true))
            .ToList();

        await traitRepository.ReplaceForProductAsync(savedProduct.Id, traits, cancellationToken);
        return new SeafoodProductProfileDto(savedProduct, traits);
    }

    private static string GenerateProductCode(IReadOnlyList<SeafoodProduct> existingProducts)
    {
        var existingCodes = existingProducts
            .Select(product => product.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index <= 9999; index++)
        {
            var candidate = $"SP-{index:0000}";
            if (!existingCodes.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"SP-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    }

    public async Task DeleteAsync(Guid productId, CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAllAsync(cancellationToken);
        if (channels.Any(x => x.SeafoodProductId == productId))
        {
            throw new InvalidOperationException("该海鲜产品仍被通道使用，不能删除。请先解除通道绑定。");
        }

        await productRepository.DeleteAsync(productId, cancellationToken);
    }

    public Task<string> GetDefaultPredictTemplateContentAsync(CancellationToken cancellationToken) =>
        productPredictConfigStore.GetDefaultTemplateContentAsync(cancellationToken);
}

public sealed class ChannelConfigService(
    IChannelConfigRepository channelRepository,
    ISeafoodProductRepository productRepository,
    IModelRegistryRepository modelRepository,
    IRuntimeStateStore runtimeStateStore)
{
    private const decimal DefaultCameraToEjectDistanceMillimeters = 420m;
    private const decimal DefaultMillimetersPerPixelY = 1.0m;
    private const int DefaultSoftwareLatencyMilliseconds = 40;
    private const int DefaultActuatorDelayMilliseconds = 25;

    public async Task<IReadOnlyList<ChannelConfigDetailDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAllAsync(cancellationToken);
        var products = await productRepository.GetAllAsync(cancellationToken);
        var models = await modelRepository.GetAllAsync(cancellationToken);

        return channels
            .Select(channel => new ChannelConfigDetailDto(
                channel,
                products.FirstOrDefault(product => product.Id == channel.SeafoodProductId),
                channel.ModelVersionId is null ? null : models.FirstOrDefault(model => model.Id == channel.ModelVersionId.Value)))
            .OrderBy(x => x.Channel.ChannelNo)
            .ToList();
    }

    public Task<ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        channelRepository.GetByIdAsync(id, cancellationToken);

    public async Task<ChannelConfig> SelectActiveAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetByIdAsync(channelId, cancellationToken)
            ?? throw new InvalidOperationException("未找到要切换的通道。");

        return await UpsertAsync(
            new UpsertChannelConfigRequest(
                channel.Id,
                channel.ChannelNo,
                channel.Name,
                channel.SeafoodProductId,
                channel.ModelVersionId,
                channel.DefectHandlingAction,
                channel.ConfidenceThreshold,
                true,
                channel.CameraToEjectDistanceMillimeters,
                channel.MillimetersPerPixelY,
                channel.SoftwareLatencyMilliseconds,
                channel.ActuatorDelayMilliseconds,
                channel.HorizontalLaneMappingJson),
            cancellationToken);
    }

    public async Task<ChannelConfig> UpsertAsync(UpsertChannelConfigRequest request, CancellationToken cancellationToken)
    {
        var runtime = runtimeStateStore.GetSnapshot();
        if (runtime.RuntimeMode == RuntimeMode.Running)
        {
            var existingWhileRunning = request.Id is Guid runningChannelId
                ? (await channelRepository.GetByIdAsync(runningChannelId, cancellationToken))
                : null;
            if (existingWhileRunning is null || existingWhileRunning.IsEnabled != request.IsEnabled)
            {
                throw new InvalidOperationException("机器运行中不可切换通道，请先停止机器。");
            }
        }

        if (request.ChannelNo <= 0)
        {
            throw new InvalidOperationException("通道编号必须是大于 0 的整数。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("通道名称不能为空。");
        }

        if (request.SeafoodProductId == Guid.Empty)
        {
            throw new InvalidOperationException("通道必须绑定一个海鲜产品。");
        }

        if (request.ModelVersionId is null || request.ModelVersionId == Guid.Empty)
        {
            throw new InvalidOperationException("通道必须选择一个模型。");
        }

        var model = await modelRepository.GetByIdAsync(request.ModelVersionId.Value, cancellationToken);
        if (model is null)
        {
            throw new InvalidOperationException("未找到通道绑定的模型。");
        }

        var product = await productRepository.GetByIdAsync(request.SeafoodProductId, cancellationToken);
        if (product is null)
        {
            throw new InvalidOperationException("未找到通道绑定的海鲜产品。");
        }

        if (request.ConfidenceThreshold <= 0 || request.ConfidenceThreshold > 1)
        {
            throw new InvalidOperationException("置信度阈值必须是大于 0 且不超过 1.0 的数字。");
        }

        var existingChannels = await channelRepository.GetAllAsync(cancellationToken);
        if (existingChannels.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("通道名称必须唯一，不能与现有通道重复。");
        }

        if (request.Id is Guid channelId)
        {
            var existing = existingChannels.FirstOrDefault(x => x.Id == channelId);
            if (existing is not null)
            {
                if (existing.ChannelNo != request.ChannelNo)
                {
                    throw new InvalidOperationException("通道编号创建后不可修改。");
                }

            }
        }

        var previous = request.Id is Guid existingId
            ? existingChannels.FirstOrDefault(x => x.Id == existingId)
            : null;

        var channelConfig = new ChannelConfig(
            request.Id ?? Guid.NewGuid(),
            request.ChannelNo,
            request.Name.Trim(),
            request.SeafoodProductId,
            request.ModelVersionId,
            model.SourceWeightPath,
            request.DefectHandlingAction,
            request.ConfidenceThreshold,
            request.IsEnabled,
            previous?.LastRuntimePredictConfigPath,
            previous?.LastRuntimeOutputDirectory,
            request.CameraToEjectDistanceMillimeters ?? previous?.CameraToEjectDistanceMillimeters ?? DefaultCameraToEjectDistanceMillimeters,
            request.MillimetersPerPixelY ?? previous?.MillimetersPerPixelY ?? DefaultMillimetersPerPixelY,
            request.SoftwareLatencyMilliseconds ?? previous?.SoftwareLatencyMilliseconds ?? DefaultSoftwareLatencyMilliseconds,
            request.ActuatorDelayMilliseconds ?? previous?.ActuatorDelayMilliseconds ?? DefaultActuatorDelayMilliseconds,
            request.HorizontalLaneMappingJson ?? previous?.HorizontalLaneMappingJson ?? ChannelConfigDefaults.BuildDefaultLaneMappingJson());

        var saved = await channelRepository.UpsertAsync(channelConfig, cancellationToken);

        if (saved.IsEnabled)
        {
            foreach (var otherChannel in existingChannels.Where(x => x.Id != saved.Id && x.IsEnabled))
            {
                await channelRepository.UpsertAsync(otherChannel with { IsEnabled = false }, cancellationToken);
            }
        }

        return saved;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        channelRepository.DeleteAsync(id, cancellationToken);
}

public sealed class HardwareDeviceService(IHardwareDeviceRepository hardwareDeviceRepository)
{
    public Task<IReadOnlyList<HardwareDevice>> GetAllAsync(CancellationToken cancellationToken) =>
        hardwareDeviceRepository.GetAllAsync(cancellationToken);

    public async Task<HardwareDevice> UpsertAsync(UpsertHardwareDeviceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceNo))
        {
            throw new InvalidOperationException("设备编号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("设备名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.FirmwareVersion))
        {
            throw new InvalidOperationException("固件版本不能为空。");
        }

        var existingDevices = await hardwareDeviceRepository.GetAllAsync(cancellationToken);
        if (existingDevices.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.DeviceNo, request.DeviceNo.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("设备编号已存在，必须全局唯一。");
        }

        var previous = request.Id is Guid id
            ? existingDevices.FirstOrDefault(x => x.Id == id)
            : null;

        var device = new HardwareDevice(
            request.Id ?? Guid.NewGuid(),
            request.DeviceNo.Trim(),
            request.Name.Trim(),
            request.Type,
            request.FirmwareVersion.Trim(),
            request.State,
            previous?.LastSelfCheckAt,
            previous?.LastSelfCheckResult ?? "尚未自检",
            request.IsEnabled,
            request.Notes.Trim());

        return await hardwareDeviceRepository.UpsertAsync(device, cancellationToken);
    }

    public async Task<DeviceSelfCheckResultDto> RunSelfCheckAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await hardwareDeviceRepository.GetByIdAsync(deviceId, cancellationToken)
            ?? throw new InvalidOperationException("未找到要自检的设备。");

        var passed = device.IsEnabled && device.State is not DeviceState.Offline and not DeviceState.Faulted;
        var message = passed
            ? "自检通过，设备通讯与基础状态正常。"
            : "自检未通过，请检查设备启用状态、通讯线路或故障状态。";

        var updated = device with
        {
            State = passed ? DeviceState.Idle : DeviceState.Warning,
            LastSelfCheckAt = DateTimeOffset.UtcNow,
            LastSelfCheckResult = message
        };

        var saved = await hardwareDeviceRepository.UpsertAsync(updated, cancellationToken);
        return new DeviceSelfCheckResultDto(saved, passed, message);
    }

    public Task DeleteAsync(Guid deviceId, CancellationToken cancellationToken) =>
        hardwareDeviceRepository.DeleteAsync(deviceId, cancellationToken);
}

public sealed class AlarmService(IAlarmRepository alarmRepository)
{
    public Task<IReadOnlyList<AlarmEvent>> GetActiveAsync(CancellationToken cancellationToken) =>
        alarmRepository.GetActiveAsync(cancellationToken);

    public Task AcknowledgeAsync(Guid alarmId, CancellationToken cancellationToken) =>
        alarmRepository.AcknowledgeAsync(alarmId, cancellationToken);
}

public sealed class HardwareInterlockService(
    IHardwareProtocolClient hardwareProtocolClient,
    IAlarmRepository alarmRepository) : IHardwareInterlockService
{
    private static readonly DeviceType[] RequiredDeviceTypes =
    [
        DeviceType.XraySource,
        DeviceType.XrayDetector,
        DeviceType.Conveyor,
        DeviceType.Ejector
    ];

    public async Task<HardwareInterlockResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        var signals = await hardwareProtocolClient.ReadSignalsAsync(cancellationToken);
        var activeAlarms = await alarmRepository.GetActiveAsync(cancellationToken);
        var alarms = new List<AlarmEvent>();
        var now = DateTimeOffset.UtcNow;

        foreach (var requiredType in RequiredDeviceTypes)
        {
            if (signals.Any(signal => signal.DeviceType == requiredType))
            {
                continue;
            }

            alarms.Add(await EnsureActiveAlarmAsync(
                activeAlarms,
                AlarmSeverity.Critical,
                "hardware",
                $"MISSING_{BuildDeviceTypeCode(requiredType)}",
                $"关键硬件未配置或未启用: {MapDeviceTypeName(requiredType)}。",
                now,
                cancellationToken));
        }

        foreach (var signal in signals)
        {
            if (signal.Severity == HardwareSignalSeverity.Normal)
            {
                continue;
            }

            var severity = signal.Severity == HardwareSignalSeverity.Critical
                ? AlarmSeverity.Critical
                : AlarmSeverity.Warning;

            var alarm = await EnsureActiveAlarmAsync(
                activeAlarms,
                severity,
                signal.DeviceNo,
                signal.Code,
                signal.Message,
                signal.UpdatedAt,
                cancellationToken);

            if (severity == AlarmSeverity.Critical)
            {
                alarms.Add(alarm);
            }
        }

        foreach (var activeCritical in activeAlarms.Where(alarm =>
                     alarm.Severity == AlarmSeverity.Critical &&
                     (string.Equals(alarm.Source, "hardware", StringComparison.OrdinalIgnoreCase) ||
                      signals.Any(signal => string.Equals(signal.DeviceNo, alarm.Source, StringComparison.OrdinalIgnoreCase)))))
        {
            if (alarms.All(alarm => alarm.Id != activeCritical.Id))
            {
                alarms.Add(activeCritical);
            }
        }

        var devices = signals
            .Select(signal => new DeviceStatus(signal.DeviceNo, signal.State, signal.Message, signal.UpdatedAt))
            .ToList();
        var canRun = alarms.Count == 0;
        var summary = canRun
            ? "关键硬件联锁正常，允许启动。"
            : $"存在 {alarms.Count} 条关键硬件联锁告警，系统禁止运行。";

        return new HardwareInterlockResult(
            canRun,
            canRun ? RuntimeMode.Running : RuntimeMode.Faulted,
            canRun ? DeviceState.Running : DeviceState.Faulted,
            devices,
            alarms,
            summary);
    }

    private async Task<AlarmEvent> EnsureActiveAlarmAsync(
        IReadOnlyList<AlarmEvent> activeAlarms,
        AlarmSeverity severity,
        string source,
        string code,
        string message,
        DateTimeOffset raisedAt,
        CancellationToken cancellationToken)
    {
        var existing = activeAlarms.FirstOrDefault(alarm =>
            alarm.Severity == severity &&
            string.Equals(alarm.Source, source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(alarm.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var alarmEvent = new AlarmEvent(
            Guid.NewGuid(),
            severity,
            source,
            code,
            message,
            raisedAt,
            false);
        await alarmRepository.AddAsync(alarmEvent, cancellationToken);
        return alarmEvent;
    }

    private static string BuildDeviceTypeCode(DeviceType type) => type switch
    {
        DeviceType.XraySource => "XRAY_SOURCE",
        DeviceType.XrayDetector => "XRAY_DETECTOR",
        DeviceType.Conveyor => "CONVEYOR",
        DeviceType.Ejector => "EJECTOR",
        DeviceType.Controller => "CONTROLLER",
        _ => type.ToString().ToUpperInvariant()
    };

    private static string MapDeviceTypeName(DeviceType type) => type switch
    {
        DeviceType.XraySource => "X 光光源",
        DeviceType.XrayDetector => "X 光探测器",
        DeviceType.Conveyor => "传送带",
        DeviceType.Ejector => "剔除设备",
        DeviceType.Controller => "控制器",
        _ => type.ToString()
    };
}

public sealed class SoftwareVersionService : ISoftwareVersionProvider
{
    public Task<SoftwareVersionInfo> GetCurrentAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var assembly = typeof(SoftwareVersionService).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.1.0";
        var buildTime = File.GetLastWriteTimeUtc(assembly.Location).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        return Task.FromResult(new SoftwareVersionInfo(
            "Ocean Fresh Sorting System",
            version,
            buildTime,
            "SQLite schema: current",
            ResolveYoloServiceVersion(),
            $"{Environment.OSVersion.VersionString} · .NET {Environment.Version}",
            "离线更新包校验通过后，在机器停止状态下安装；安装前备份数据库和配置，失败时回滚上一版本。",
            LoadUpdateHistory()));
    }

    public Task<SoftwareUpdateRecord> ValidateUpdatePackageAsync(SoftwareUpdatePackageRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(request.PackagePath))
        {
            throw new InvalidOperationException("请先选择软件更新包。");
        }

        if (!File.Exists(request.PackagePath))
        {
            throw new InvalidOperationException($"未找到软件更新包: {request.PackagePath}");
        }

        var extension = Path.GetExtension(request.PackagePath);
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("软件更新包第一版仅支持 .zip 文件。");
        }

        var record = new SoftwareUpdateRecord(
            Path.GetFileNameWithoutExtension(request.PackagePath),
            DateTimeOffset.UtcNow,
            "待安装",
            string.IsNullOrWhiteSpace(request.Sha256)
                ? "已完成文件存在性校验，尚未配置 SHA256。"
                : $"已记录 SHA256: {request.Sha256}");
        return Task.FromResult(record);
    }

    private static string ResolveYoloServiceVersion()
    {
        var configured = Environment.GetEnvironmentVariable("OCEANFRESH_YOLO_SERVICE_VERSION");
        return string.IsNullOrWhiteSpace(configured) ? "未连接" : configured;
    }

    private static IReadOnlyList<SoftwareUpdateRecord> LoadUpdateHistory() =>
    [
        new SoftwareUpdateRecord("当前版本", DateTimeOffset.UtcNow, "运行中", "本机当前运行版本。")
    ];
}

public sealed class ModelManagementService(
    IModelRegistryRepository modelRepository,
    IModelValidator modelValidator,
    IChannelConfigRepository channelRepository)
{
    public Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        modelRepository.GetByCategoryAsync(categoryId, cancellationToken);

    public Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
        modelRepository.GetAllAsync(cancellationToken);

    public async Task<ModelImportResult> ImportAsync(ImportModelRequest request, CancellationToken cancellationToken)
    {
        var imported = await SaveAsync(
            new UpsertModelRequest(
                null,
                request.SeafoodCategoryId,
                request.Version,
                request.SourceWeightPath,
                request.Notes,
                request.TrainingImageSize),
            cancellationToken);

        var validation = await modelValidator.ValidateAsync(imported, cancellationToken);
        return new ModelImportResult(imported, validation);
    }

    public async Task<ModelVersion> SaveAsync(UpsertModelRequest request, CancellationToken cancellationToken)
    {
        if (request.SeafoodCategoryId == Guid.Empty)
        {
            throw new InvalidOperationException("模型导入必须选择海鲜类别。");
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            throw new InvalidOperationException("模型版本号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("模型描述不能为空。");
        }

        if (request.TrainingImageSize is null or <= 0)
        {
            throw new InvalidOperationException("模型训练分辨率 imgsz 必须为正整数。");
        }

        var existing = request.Id is { } id && id != Guid.Empty
            ? await modelRepository.GetByIdAsync(id, cancellationToken)
            : null;

        if (existing is not null && !string.Equals(existing.Version, request.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模型版本号创建后不可修改。");
        }

        var allModels = await modelRepository.GetAllAsync(cancellationToken);
        if (allModels.Any(x =>
                string.Equals(x.Version, request.Version, StringComparison.OrdinalIgnoreCase) &&
                x.Id != existing?.Id))
        {
            throw new InvalidOperationException("模型版本号已存在，必须全局唯一。");
        }

        var version = new ModelVersion(
            existing?.Id ?? Guid.NewGuid(),
            request.SeafoodCategoryId,
            request.Version,
            request.SourceWeightPath,
            request.Notes,
            existing?.Status ?? ModelStatus.Normal,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            request.TrainingImageSize);

        var validation = await modelValidator.ValidateAsync(version, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" | ", validation.Messages));
        }

        return await modelRepository.UpsertAsync(version, cancellationToken);
    }

    public async Task DeleteAsync(Guid modelId, CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAllAsync(cancellationToken);
        var boundChannels = channels
            .Where(x => x.ModelVersionId == modelId)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (boundChannels.Count > 0)
        {
            throw new InvalidOperationException($"当前模型仍绑定到 {string.Join("、", boundChannels)}，请先解除通道绑定后再删除。");
        }

        await modelRepository.DeleteAsync(modelId, cancellationToken);
    }
}

public sealed class ManualInferenceService(
    IChannelConfigRepository channelRepository,
    ISeafoodProductRepository productRepository,
    ISeafoodTraitRepository traitRepository,
    IModelRegistryRepository modelRepository,
    IInferenceEngine inferenceEngine,
    ILocator locator,
    IInspectionRecordRepository inspectionRecordRepository,
    IDetectionSessionRepository detectionSessionRepository)
{
    public async Task<ManualInferenceResultDto> RunAsync(
        string originalFileName,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("上传图片不能为空。");
        }

        var activeChannel = (await channelRepository.GetAllAsync(cancellationToken))
            .OrderBy(x => x.ChannelNo)
            .FirstOrDefault(x => x.IsEnabled);
        if (activeChannel is null)
        {
            throw new InvalidOperationException("当前没有启用中的通道，无法执行联调推理。");
        }

        if (activeChannel.ModelVersionId is null)
        {
            throw new InvalidOperationException($"当前启用通道 {activeChannel.Name} 尚未绑定模型。");
        }

        var model = await modelRepository.GetByIdAsync(activeChannel.ModelVersionId.Value, cancellationToken);
        if (model is null)
        {
            throw new InvalidOperationException($"未找到通道 {activeChannel.Name} 绑定的模型。");
        }

            var product = await productRepository.GetByIdAsync(activeChannel.SeafoodProductId, cancellationToken);
            var traits = await traitRepository.GetByProductAsync(activeChannel.SeafoodProductId, cancellationToken);
            var normalLabel = traits.FirstOrDefault(x => x.IsNormal)?.Name ?? "正常";
            var effectiveLabelMapJson = ResolveEffectiveLabelMapJson(product?.LabelMapJson, null);
            var frameId = $"manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            var sourcePath = await PersistFrameAsync(activeChannel, frameId, originalFileName, imageBytes, cancellationToken);
            var modelPath = ResolveManualModelPath(model);

            var inference = await inferenceEngine.RunAsync(
            new ChannelInferenceRequest(
                activeChannel.Id,
                activeChannel,
                modelPath,
                effectiveLabelMapJson,
                    product?.PredictConfigPath ?? string.Empty,
                    activeChannel.ConfidenceThreshold,
                    sourcePath,
                    Path.GetFileName(sourcePath),
                    frameId,
                frameId,
                DateTimeOffset.UtcNow,
                model.TrainingImageSize),
            cancellationToken);

        var commands = new List<EjectCommand>();
        foreach (var detection in inference.Detections)
        {
            var action = string.Equals(detection.Label, normalLabel, StringComparison.OrdinalIgnoreCase)
                ? DefectHandlingAction.Pass
                : activeChannel.DefectHandlingAction;

            if (action is DefectHandlingAction.Sink or DefectHandlingAction.AirJet or DefectHandlingAction.Pusher)
            {
                commands.Add(locator.BuildCommand(activeChannel, detection, action, DateTimeOffset.UtcNow));
            }
        }

        var activeSession = await detectionSessionRepository.GetActiveAsync(cancellationToken);

        await inspectionRecordRepository.AddAsync(new InspectionRecord(
            Guid.NewGuid(),
            activeChannel.Id,
            model.Id,
            activeSession?.Id,
            "MANUAL-DEBUG",
            sourcePath,
            inference.Detections.Any(detection => !string.Equals(detection.Label, normalLabel, StringComparison.OrdinalIgnoreCase)),
            inference.TimedOut,
            DateTimeOffset.UtcNow,
            inference.Detections,
            commands), cancellationToken);

        return new ManualInferenceResultDto(
            activeChannel.Name,
            product?.Name ?? "未绑定产品",
            model.Version,
            modelPath,
            product?.PredictConfigPath ?? string.Empty,
            sourcePath,
            normalLabel,
            inference.TimedOut,
            inference.Detections,
            commands);
    }

    private static string ResolveManualModelPath(ModelVersion model)
    {
        return model.SourceWeightPath;
    }

    private static string ResolveEffectiveLabelMapJson(string? productLabelMapJson, string? _)
    {
        if (!string.IsNullOrWhiteSpace(productLabelMapJson) && productLabelMapJson != "{}")
        {
            return productLabelMapJson;
        }

        return "{}";
    }

    private static async Task<string> PersistFrameAsync(
        ChannelConfig channelConfig,
        string frameId,
        string originalFileName,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var inputDirectory = ResolveSharedPath(ChannelRuntimeDefaults.BuildPredictInputDirectory(channelConfig.ChannelNo));
        Directory.CreateDirectory(inputDirectory);
        var sourcePath = Path.Combine(inputDirectory, $"{frameId}{extension}");
        await File.WriteAllBytesAsync(sourcePath, imageBytes, cancellationToken);
        return sourcePath;
    }

    private static string ResolveSharedPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(GetDataRoot(), path));
    }

    private static string GetDataRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("OCEANFRESH_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            Directory.CreateDirectory(overridden);
            return overridden;
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OceanFreshSortingSystem");
        Directory.CreateDirectory(root);
        return root;
    }
}

public sealed class RuntimeDataSourceService(
    IRuntimeDataSourceStore dataSourceStore,
    IRuntimeStateStore runtimeStateStore)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tif",
        ".tiff"
    };

    public RuntimeDataSourceConfigDto Get() => ToDto(dataSourceStore.Get());

    public RuntimeDataSourceConfigDto Update(UpdateRuntimeDataSourceRequest request)
    {
        var snapshot = runtimeStateStore.GetSnapshot();
        if (snapshot.RuntimeMode is RuntimeMode.Running or RuntimeMode.Starting)
        {
            throw new InvalidOperationException("请先停止机器后再切换数据源。");
        }

        var frameInterval = request.FrameIntervalMilliseconds is > 0
            ? request.FrameIntervalMilliseconds.Value
            : RuntimeDataSourceConfig.DefaultFrameIntervalMilliseconds;

        RuntimeDataSourceConfig config;
        if (request.Mode == RuntimeDataSourceMode.LocalImageDirectory)
        {
            if (string.IsNullOrWhiteSpace(request.LocalDirectoryPath))
            {
                throw new InvalidOperationException("请选择本地图片目录。");
            }

            var directory = Path.GetFullPath(request.LocalDirectoryPath);
            if (!Directory.Exists(directory))
            {
                throw new InvalidOperationException($"本地图片目录不存在: {directory}");
            }

            var imageCount = CountSupportedImages(directory);
            if (imageCount == 0)
            {
                throw new InvalidOperationException("本地图片目录没有可检测的图片。");
            }

            config = new RuntimeDataSourceConfig(
                RuntimeDataSourceMode.LocalImageDirectory,
                directory,
                frameInterval,
                imageCount,
                0,
                false,
                $"本地图片目录已选择，共 {imageCount} 张图片");
        }
        else
        {
            config = new RuntimeDataSourceConfig(
                RuntimeDataSourceMode.XrayCamera,
                null,
                frameInterval,
                0,
                0,
                true,
                "X 光相机");
        }

        dataSourceStore.Update(config);
        return ToDto(config);
    }

    public static RuntimeDataSourceConfigDto ToDto(RuntimeDataSourceConfig config) =>
        new(
            config.Mode,
            config.Mode == RuntimeDataSourceMode.LocalImageDirectory ? "本地图片目录" : "X 光相机",
            config.LocalDirectoryPath,
            config.FrameIntervalMilliseconds,
            config.ImageCount,
            config.CurrentIndex,
            config.IsHardwareExecutionEnabled,
            config.Status);

    private static int CountSupportedImages(string directoryPath) =>
        Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
            .Count(path => SupportedExtensions.Contains(Path.GetExtension(path)));
}

public sealed class RuntimeDashboardService(
    IRuntimeStateStore runtimeStateStore,
    IRuntimeDataSourceStore dataSourceStore,
    IChannelConfigRepository channelRepository,
    ISeafoodProductRepository productRepository,
    IModelRegistryRepository modelRepository,
    IInspectionRecordRepository inspectionRecordRepository,
    IDetectionSessionRepository detectionSessionRepository)
{
    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAllAsync(cancellationToken);
        var products = await productRepository.GetAllAsync(cancellationToken);
        var models = await modelRepository.GetAllAsync(cancellationToken);
        var currentSession = await detectionSessionRepository.GetActiveAsync(cancellationToken)
            ?? await detectionSessionRepository.GetLatestAsync(cancellationToken);
        var records = currentSession is null
            ? []
            : await inspectionRecordRepository.GetBySessionAsync(currentSession.Id, cancellationToken);
        var activeModels = new List<ModelVersion>();
        var activeChannels = new List<ChannelConfigDetailDto>();

        foreach (var channel in channels.Where(x => x.IsEnabled).OrderBy(x => x.ChannelNo))
        {
            var product = products.FirstOrDefault(x => x.Id == channel.SeafoodProductId);
            var model = channel.ModelVersionId is Guid modelId
                ? models.FirstOrDefault(x => x.Id == modelId)
                : null;

            if (model is not null)
            {
                activeModels.Add(model);
            }

            activeChannels.Add(new ChannelConfigDetailDto(channel, product, model));
        }

        var channelDetails = channels
            .OrderBy(x => x.ChannelNo)
            .Select(channel => new ChannelConfigDetailDto(
                channel,
                products.FirstOrDefault(product => product.Id == channel.SeafoodProductId),
                channel.ModelVersionId is null ? null : models.FirstOrDefault(model => model.Id == channel.ModelVersionId.Value)))
            .ToList();
        var snapshot = runtimeStateStore.GetSnapshot();
        var summary = BuildSummary(snapshot, currentSession, records, channelDetails);
        var channelKpis = BuildChannelKpis(records, channelDetails, currentSession);
        var defectStats = BuildDefectStats(records);
        var latestAbnormal = BuildLatestAbnormal(records);
        var dataSource = RuntimeDataSourceService.ToDto(dataSourceStore.Get());

        return new DashboardDto(snapshot, currentSession, summary, dataSource, channelKpis, defectStats, latestAbnormal, activeChannels, activeModels);
    }

    private static DashboardSummaryDto BuildSummary(
        RuntimeSnapshot snapshot,
        DetectionSession? session,
        IReadOnlyList<InspectionRecord> records,
        IReadOnlyList<ChannelConfigDetailDto> channels)
    {
        var current = snapshot.RuntimeMode == RuntimeMode.Running && session is not null
            ? channels.FirstOrDefault(x => x.Channel.Id == session.ChannelId)
            : channels.FirstOrDefault(x => x.Channel.IsEnabled);
        var total = CountDetectedItems(records);
        var normal = CountNormalItems(records);
        var rejected = CountRejectedItems(records);
        var ejectCommandCount = CountEjectCommands(records);
        var yield = CalculateYield(total, normal);
        var duration = snapshot.MachineStartedAt is null || snapshot.RuntimeMode != RuntimeMode.Running
            ? "00:00:00"
            : FormatDuration(DateTimeOffset.UtcNow - snapshot.MachineStartedAt.Value);

        return new DashboardSummaryDto(
            current?.Channel.Name ?? "未启用通道",
            current?.Product?.Name ?? "未绑定产品",
            current?.ModelVersion?.Version ?? "未选择模型",
            duration,
            total,
            normal,
            rejected,
            ejectCommandCount,
            yield);
    }

    private static IReadOnlyList<DashboardChannelKpiDto> BuildChannelKpis(
        IReadOnlyList<InspectionRecord> records,
        IReadOnlyList<ChannelConfigDetailDto> channels,
        DetectionSession? session)
    {
        return channels.Select(channel =>
        {
            var channelRecords = session is null
                ? []
                : records.Where(x => x.RecipeId == channel.Channel.Id).ToList();
            var total = CountDetectedItems(channelRecords);
            var normal = CountNormalItems(channelRecords);
            var rejected = CountRejectedItems(channelRecords);
            var ejectCommandCount = CountEjectCommands(channelRecords);
            var topDefect = channelRecords
                .SelectMany(x => x.Detections)
                .Where(detection => !IsNormalDetection(detection))
                .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .Select(x => x.Key)
                .FirstOrDefault() ?? "暂无";

            return new DashboardChannelKpiDto(
                channel.Channel.Id,
                channel.Channel.Name,
                session?.ChannelId == channel.Channel.Id && session.Status == DetectionSessionStatus.Running ? "运行中" : channel.Channel.IsEnabled ? "待机" : "禁用",
                channel.Product?.Name ?? "未绑定产品",
                channel.ModelVersion?.Version ?? "未选择模型",
                total,
                normal,
                rejected,
                ejectCommandCount,
                CalculateYield(total, normal),
                topDefect);
        }).ToList();
    }

    internal static IReadOnlyList<DashboardDefectStatDto> BuildDefectStats(IReadOnlyList<InspectionRecord> records)
    {
        var groups = records
            .SelectMany(x => x.Detections)
            .Where(detection => !IsNormalDetection(detection))
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => new { Label = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        var total = groups.Sum(x => x.Count);
        return groups
            .Select(x => new DashboardDefectStatDto(x.Label, x.Count, total == 0 ? 0 : decimal.Round(x.Count * 100m / total, 2)))
            .ToList();
    }

    private static DashboardLatestAbnormalDto? BuildLatestAbnormal(IReadOnlyList<InspectionRecord> records)
    {
        var record = records
            .Where(x => x.IsRejected)
            .OrderByDescending(x => x.CapturedAt)
            .FirstOrDefault();
        if (record is null)
        {
            return null;
        }

        var command = record.EjectCommands.FirstOrDefault();
        var detection = command is null
            ? record.Detections.FirstOrDefault(x => !IsNormalDetection(x))
            : record.Detections.FirstOrDefault(x => string.Equals(x.Label, command.DefectLabel, StringComparison.OrdinalIgnoreCase));
        var label = command?.DefectLabel ?? detection?.Label ?? "异常";
        var confidence = detection?.Confidence ?? 0m;

        return new DashboardLatestAbnormalDto(
            record.ImagePath,
            $"{label} · 置信度 {confidence:0.###} · {record.CapturedAt.LocalDateTime:MM-dd HH:mm:ss}",
            label,
            confidence,
            record.CapturedAt);
    }

    internal static int CountDetectedItems(IReadOnlyList<InspectionRecord> records) =>
        records.Sum(x => x.Detections.Count);

    internal static int CountNormalItems(IReadOnlyList<InspectionRecord> records) =>
        records.SelectMany(x => x.Detections).Count(IsNormalDetection);

    internal static int CountRejectedItems(IReadOnlyList<InspectionRecord> records) =>
        records.SelectMany(x => x.Detections).Count(detection => !IsNormalDetection(detection));

    internal static int CountEjectCommands(IReadOnlyList<InspectionRecord> records) =>
        records.Sum(x => x.EjectCommands.Count);

    internal static bool IsNormalDetection(DefectDetection detection) =>
        string.Equals(detection.Label, "正常", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(detection.Label, "normal", StringComparison.OrdinalIgnoreCase);

    private static decimal CalculateYield(int total, int normal) =>
        total == 0 ? 100m : decimal.Round(normal * 100m / total, 2);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 100
            ? $"{(int)duration.TotalHours:0}:{duration.Minutes:00}:{duration.Seconds:00}"
            : duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}

public sealed class ProductionStatisticsService(
    IInspectionRecordRepository inspectionRecordRepository,
    IDetectionSessionRepository detectionSessionRepository,
    ISeafoodProductRepository productRepository,
    IModelRegistryRepository modelRepository)
{
    public async Task<ProductionStatisticsDto> GetProductionAsync(string range, CancellationToken cancellationToken)
    {
        var normalizedRange = string.IsNullOrWhiteSpace(range) ? "1d" : range.Trim().ToLowerInvariant();
        var days = normalizedRange switch
        {
            "7d" => 7,
            "30d" => 30,
            _ => 1
        };
        normalizedRange = days switch
        {
            7 => "7d",
            30 => "30d",
            _ => "1d"
        };

        var until = DateTimeOffset.UtcNow;
        var since = until.AddDays(-days);
        var records = await inspectionRecordRepository.GetSinceAsync(since, cancellationToken);
        var sessions = await detectionSessionRepository.GetSinceAsync(since, cancellationToken);
        var products = await productRepository.GetAllAsync(cancellationToken);
        var models = await modelRepository.GetAllAsync(cancellationToken);
        var productLookup = products.ToDictionary(x => x.Id);
        var modelLookup = models.ToDictionary(x => x.Id);
        var sessionLookup = sessions.ToDictionary(x => x.Id);
        var recordsBySession = records
            .Where(x => x.DetectionSessionId is not null && sessionLookup.ContainsKey(x.DetectionSessionId.Value))
            .GroupBy(x => x.DetectionSessionId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());

        var rows = records
            .Where(x => x.DetectionSessionId is not null && sessionLookup.ContainsKey(x.DetectionSessionId.Value))
            .GroupBy(x => sessionLookup[x.DetectionSessionId!.Value].ProductId)
            .Select(group =>
            {
                var groupRecords = group.ToList();
                var total = RuntimeDashboardService.CountDetectedItems(groupRecords);
                var normal = RuntimeDashboardService.CountNormalItems(groupRecords);
                var rejected = RuntimeDashboardService.CountRejectedItems(groupRecords);
                productLookup.TryGetValue(group.Key, out var product);
                return new ProductProductionStatDto(
                    group.Key,
                    product?.Name ?? "未知海鲜",
                    total,
                    normal,
                    rejected,
                    total == 0 ? 100m : decimal.Round(normal * 100m / total, 2),
                    RuntimeDashboardService.BuildDefectStats(groupRecords));
            })
            .OrderByDescending(x => x.TotalCount)
            .ToList();

        var sessionRows = sessions
            .OrderByDescending(x => x.StartedAt)
            .Select(session =>
            {
                recordsBySession.TryGetValue(session.Id, out var sessionRecords);
                sessionRecords ??= [];
                var total = RuntimeDashboardService.CountDetectedItems(sessionRecords);
                var normal = RuntimeDashboardService.CountNormalItems(sessionRecords);
                var rejected = RuntimeDashboardService.CountRejectedItems(sessionRecords);
                productLookup.TryGetValue(session.ProductId, out var product);
                modelLookup.TryGetValue(session.ModelVersionId, out var model);
                return new DetectionSessionStatDto(
                    session.Id,
                    session.SessionCode,
                    session.ProductId,
                    product?.Name ?? "未知海鲜",
                    session.ModelVersionId,
                    model?.Version ?? "未选择模型",
                    session.StartedAt,
                    session.EndedAt,
                    session.Status,
                    session.DataSourceMode,
                    session.IsHardwareExecutionEnabled,
                    session.IsHardwareExecutionEnabled ? "生产检测" : "离线检测",
                    total,
                    normal,
                    rejected,
                    total == 0 ? 100m : decimal.Round(normal * 100m / total, 2),
                    RuntimeDashboardService.BuildDefectStats(sessionRecords));
            })
            .ToList();
        var recentSessionRows = sessionRows.Take(20).ToList();

        var yieldTrend = BuildYieldTrend(sessionRows);

        return new ProductionStatisticsDto(normalizedRange, since, until, rows, recentSessionRows, yieldTrend);
    }

    private static IReadOnlyList<YieldTrendPointDto> BuildYieldTrend(IReadOnlyList<DetectionSessionStatDto> sessionRows)
    {
        var points = new List<YieldTrendPointDto>();
        foreach (var group in sessionRows
                     .OrderBy(x => x.StartedAt)
                     .GroupBy(x => new
                     {
                         x.ProductId,
                         Date = x.StartedAt.LocalDateTime.Date
                     }))
        {
            var sequence = 1;
            foreach (var session in group.OrderBy(x => x.StartedAt))
            {
                points.Add(new YieldTrendPointDto(
                    session.SessionId,
                    session.ProductId,
                    session.ProductName,
                    $"{session.StartedAt.LocalDateTime:MM-dd} #{sequence}",
                    session.StartedAt,
                    session.YieldRate,
                    session.TotalCount,
                    session.RejectCount));
                sequence++;
            }
        }

        return points;
    }
}

public sealed class ManualReviewService(
    IInspectionRecordRepository inspectionRecordRepository,
    IDetectionSessionRepository detectionSessionRepository,
    ISeafoodTraitRepository traitRepository,
    IManualReviewRepository manualReviewRepository,
    IManualReviewPreviewGenerator previewGenerator)
{
    public async Task<ManualReviewSessionDto> GetSessionReviewAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await detectionSessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("未找到检测任务。");
        var records = await inspectionRecordRepository.GetBySessionAsync(sessionId, cancellationToken);
        var traits = await traitRepository.GetByProductAsync(session.ProductId, cancellationToken);
        var normalLabels = traits
            .Where(x => x.IsNormal)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (normalLabels.Count == 0)
        {
            normalLabels.Add("正常");
            normalLabels.Add("normal");
        }

        var reviewLookup = (await manualReviewRepository.GetBySessionAsync(sessionId, cancellationToken))
            .ToDictionary(x => x.DetectionId);
        var candidates = records
            .OrderByDescending(x => x.CapturedAt)
            .SelectMany(record => record.Detections
                .Where(detection => !normalLabels.Contains(detection.Label))
                .Select(detection => BuildCandidate(record, detection, reviewLookup.GetValueOrDefault(detection.Id))))
            .ToList();

        return new ManualReviewSessionDto(
            session.Id,
            session.SessionCode,
            BuildSummary(candidates),
            candidates);
    }

    public async Task<ManualReviewPreviewDto> GetPreviewAsync(
        Guid sessionId,
        Guid inspectionRecordId,
        Guid detectionId,
        CancellationToken cancellationToken)
    {
        var session = await detectionSessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("未找到检测任务。");
        var records = await inspectionRecordRepository.GetBySessionAsync(session.Id, cancellationToken);
        var record = records.FirstOrDefault(x => x.Id == inspectionRecordId)
            ?? throw new InvalidOperationException("未找到该检测记录。");
        var detection = record.Detections.FirstOrDefault(x => x.Id == detectionId)
            ?? throw new InvalidOperationException("未找到该检测框。");
        var preview = previewGenerator.BuildPreviewImage(record, detection);
        return new ManualReviewPreviewDto(
            record.Id,
            detection.Id,
            preview.ImagePath,
            preview.IsAvailable,
            preview.Message);
    }

    public async Task<ManualReviewSessionDto> UpsertReviewAsync(Guid sessionId, UpsertManualReviewRequest request, CancellationToken cancellationToken)
    {
        var session = await detectionSessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException("未找到检测任务。");
        var records = await inspectionRecordRepository.GetBySessionAsync(sessionId, cancellationToken);
        var record = records.FirstOrDefault(x => x.Id == request.InspectionRecordId)
            ?? throw new InvalidOperationException("未找到该检测记录。");
        var detection = record.Detections.FirstOrDefault(x => x.Id == request.DetectionId)
            ?? throw new InvalidOperationException("未找到该检测框。");
        var traits = await traitRepository.GetByProductAsync(session.ProductId, cancellationToken);
        var normalLabels = traits
            .Where(x => x.IsNormal)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (normalLabels.Count == 0)
        {
            normalLabels.Add("正常");
            normalLabels.Add("normal");
        }

        var humanLabel = request.HumanLabel.Trim();
        if (string.IsNullOrWhiteSpace(humanLabel))
        {
            throw new InvalidOperationException("人工复核标签不能为空。");
        }

        var judgement = normalLabels.Contains(humanLabel)
            ? ManualReviewJudgement.FalsePositive
            : string.Equals(humanLabel, detection.Label, StringComparison.CurrentCultureIgnoreCase)
                ? ManualReviewJudgement.ConfirmedAbnormal
                : ManualReviewJudgement.RelabeledAbnormal;
        var reviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? "operator" : request.Reviewer.Trim();
        await manualReviewRepository.UpsertAsync(
            new ManualReviewRecord(
                Guid.NewGuid(),
                session.Id,
                record.Id,
                detection.Id,
                detection.Label,
                humanLabel,
                judgement,
                reviewer,
                request.Notes?.Trim() ?? string.Empty,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return await GetSessionReviewAsync(sessionId, cancellationToken);
    }

    private ManualReviewCandidateDto BuildCandidate(
        InspectionRecord record,
        DefectDetection detection,
        ManualReviewRecord? review)
    {
        var judgement = review?.Judgement;
        return new ManualReviewCandidateDto(
            record.Id,
            detection.Id,
            record.ImagePath,
            string.Empty,
            detection.Label,
            detection.Confidence,
            detection.X,
            detection.Y,
            detection.Width,
            detection.Height,
            review is not null,
            review?.HumanLabel ?? string.Empty,
            judgement,
            judgement is null ? "待复核" : FormatJudgement(judgement.Value),
            review?.Reviewer ?? string.Empty,
            review?.Notes ?? string.Empty,
            record.CapturedAt,
            review?.ReviewedAt);
    }

    private static ManualReviewSummaryDto BuildSummary(IReadOnlyList<ManualReviewCandidateDto> candidates)
    {
        var reviewed = candidates.Where(x => x.IsReviewed).ToList();
        var confirmed = reviewed.Count(x => x.Judgement is ManualReviewJudgement.ConfirmedAbnormal or ManualReviewJudgement.RelabeledAbnormal);
        var falsePositive = reviewed.Count(x => x.Judgement == ManualReviewJudgement.FalsePositive);
        var relabeled = reviewed.Count(x => x.Judgement == ManualReviewJudgement.RelabeledAbnormal);
        return new ManualReviewSummaryDto(
            candidates.Count,
            reviewed.Count,
            confirmed,
            falsePositive,
            relabeled,
            reviewed.Count == 0 ? 0m : decimal.Round(confirmed * 100m / reviewed.Count, 2),
            reviewed.Count == 0 ? 0m : decimal.Round(falsePositive * 100m / reviewed.Count, 2),
            candidates.Count > 0 && reviewed.Count == candidates.Count,
            "严格召回率暂不计算：当前只复核异常剔除集合，未覆盖未剔除集合抽检。");
    }

    private static string FormatJudgement(ManualReviewJudgement judgement) =>
        judgement switch
        {
            ManualReviewJudgement.ConfirmedAbnormal => "确认异常",
            ManualReviewJudgement.FalsePositive => "误剔",
            ManualReviewJudgement.RelabeledAbnormal => "类别修正",
            _ => "待复核"
        };
}

public sealed class StreamInspectionRecordService(
    IDetectionSessionRepository detectionSessionRepository,
    IInspectionRecordRepository inspectionRecordRepository,
    IRuntimeStateStore runtimeStateStore)
{
    public async Task<InspectionRecord> AddAsync(StreamInspectionRecordRequest request, CancellationToken cancellationToken)
    {
        var activeSession = await detectionSessionRepository.GetActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("当前没有运行中的检测任务，请先开始检测。");

        if (activeSession.ChannelId != request.ChannelId)
        {
            throw new InvalidOperationException("当前定稿帧所属通道与运行中的检测任务不一致。");
        }

        var record = new InspectionRecord(
            Guid.NewGuid(),
            request.ChannelId,
            request.ModelVersionId,
            activeSession.Id,
            activeSession.SessionCode,
            request.ImagePath,
            request.Detections.Any(detection => !RuntimeDashboardService.IsNormalDetection(detection)),
            request.IsTimedOut,
            DateTimeOffset.UtcNow,
            request.Detections,
            request.EjectCommands);

        await inspectionRecordRepository.AddAsync(record, cancellationToken);
        var snapshot = runtimeStateStore.GetSnapshot();
        var total = snapshot.TotalInspected + 1;
        var rejected = snapshot.TotalRejected + (record.IsRejected ? 1 : 0);
        runtimeStateStore.Update(snapshot with
        {
            TotalInspected = total,
            TotalRejected = rejected,
            YieldRate = decimal.Round((total - rejected) * 100m / total, 2),
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return record;
    }
}

public sealed class PixelToEjectLocator : ILocator
{
    public EjectCommand BuildCommand(ChannelConfig channelConfig, DefectDetection detection, DefectHandlingAction action, DateTimeOffset createdAt)
    {
        var nozzleNumber = ResolveNozzleNumber(channelConfig.HorizontalLaneMappingJson, detection, ChannelRuntimeDefaults.ImageWidth);
        var yCenter = detection.Y + (detection.Height / 2.0m);
        var remainingPixels = Math.Max(0m, ChannelRuntimeDefaults.ImageHeight - yCenter);
        var remainingMillimeters = (remainingPixels * channelConfig.MillimetersPerPixelY) + channelConfig.CameraToEjectDistanceMillimeters;
        var conveyorSpeedMmPerSecond = MachineRuntimeDefaults.ConveyorSpeedMetersPerSecond * 1000m;
        var travelMilliseconds = conveyorSpeedMmPerSecond <= 0
            ? 0m
            : (remainingMillimeters / conveyorSpeedMmPerSecond) * 1000m;
        var triggerDelayMicroseconds = (int)Math.Max(
            0m,
            decimal.Round((travelMilliseconds - channelConfig.SoftwareLatencyMilliseconds - channelConfig.ActuatorDelayMilliseconds) * 1000m, MidpointRounding.AwayFromZero));
        var triggerPosition = (int)Math.Round(yCenter, MidpointRounding.AwayFromZero);

        return new EjectCommand(
            Guid.NewGuid(),
            channelConfig.Id,
            detection.Label,
            action,
            nozzleNumber,
            triggerPosition,
            triggerDelayMicroseconds,
            50_000,
            createdAt);
    }

    private static int ResolveNozzleNumber(string laneMappingJson, DefectDetection detection, int imageWidth)
    {
        var xCenter = detection.X + (detection.Width / 2.0m);

        try
        {
            var mappings = System.Text.Json.JsonSerializer.Deserialize<List<LaneMapping>>(laneMappingJson) ?? [];
            var match = mappings.FirstOrDefault(x => x.StartX <= xCenter && xCenter <= x.EndX);
            if (match is not null)
            {
                return Math.Max(1, match.NozzleNumber);
            }
        }
        catch
        {
        }

        var bandWidth = Math.Max(1, imageWidth / 4);
        return Math.Max(1, ((int)xCenter / bandWidth) + 1);
    }

    private sealed record LaneMapping(int NozzleNumber, decimal StartX, decimal EndX);
}

public sealed class FileSystemModelValidator : IModelValidator
{
    public Task<ModelValidationResult> ValidateAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(modelVersion.Version))
        {
            messages.Add("模型版本不能为空。");
        }

        if (!File.Exists(modelVersion.SourceWeightPath))
        {
            messages.Add($"未找到 YOLO 源权重文件: {modelVersion.SourceWeightPath}");
        }

        if (!string.Equals(Path.GetExtension(modelVersion.SourceWeightPath), ".onnx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(modelVersion.SourceWeightPath), ".pt", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add("模型权重文件必须是 .pt 或 .onnx 文件。");
        }

        return Task.FromResult(new ModelValidationResult(messages.Count == 0, messages));
    }
}

public sealed class RuntimeCoordinator(
    IRuntimeStateStore runtimeStateStore,
    IRuntimeDataSourceStore dataSourceStore,
    IChannelConfigRepository channelRepository,
    IModelRegistryRepository modelRepository,
    IDetectionSessionRepository detectionSessionRepository,
    IHardwareInterlockService hardwareInterlockService,
    IProductionHardwareController? productionHardwareController = null) : IRuntimeCoordinator
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        dataSourceStore.ResetRunProgress();
        var current = runtimeStateStore.GetSnapshot();
        var changedMachineState = current.RuntimeMode != RuntimeMode.Running;
        try
        {
            if (changedMachineState)
            {
                var interlock = await hardwareInterlockService.EvaluateAsync(cancellationToken);
                if (!interlock.CanRun)
                {
                    runtimeStateStore.Update(current with
                    {
                        RuntimeMode = RuntimeMode.Faulted,
                        DeviceState = DeviceState.Faulted,
                        Devices = interlock.Devices,
                        ActiveAlarms = interlock.CriticalAlarms,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        MachineStartedAt = null
                    });
                    throw new InvalidOperationException(interlock.Summary);
                }

                if (productionHardwareController is not null)
                {
                    await productionHardwareController.StartAsync(cancellationToken);
                }
                runtimeStateStore.Update(new RuntimeSnapshot(
                    RuntimeMode.Running,
                    DeviceState.Running,
                    current.CurrentRecipe,
                    current.CurrentModelVersion,
                    "BATCH-20260601-01",
                    current.TotalInspected,
                    current.TotalRejected,
                    current.YieldRate,
                    DateTimeOffset.UtcNow,
                    interlock.Devices,
                    current.ActiveAlarms,
                    DateTimeOffset.UtcNow));
            }

            if (await detectionSessionRepository.GetActiveAsync(cancellationToken) is null)
            {
                await CreateDetectionSessionForActiveChannelAsync(cancellationToken);
            }
        }
        catch
        {
            if (changedMachineState)
            {
                try
                {
                    if (productionHardwareController is not null)
                    {
                        await productionHardwareController.StopAsync(CancellationToken.None);
                    }
                }
                catch
                {
                }
                var latest = runtimeStateStore.GetSnapshot();
                if (latest.RuntimeMode != RuntimeMode.Faulted)
                {
                    runtimeStateStore.Update(current with { UpdatedAt = DateTimeOffset.UtcNow });
                }
            }

            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopDetectionAsync(cancellationToken);
        if (productionHardwareController is not null)
        {
            await productionHardwareController.StopAsync(cancellationToken);
        }
        var current = runtimeStateStore.GetSnapshot();
        runtimeStateStore.Update(current with
        {
            RuntimeMode = RuntimeMode.SafeStop,
            DeviceState = DeviceState.Idle,
            UpdatedAt = DateTimeOffset.UtcNow,
            MachineStartedAt = null
        });
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        var current = runtimeStateStore.GetSnapshot();
        if (current.RuntimeMode is RuntimeMode.Running or RuntimeMode.Starting)
        {
            throw new InvalidOperationException("设备运行中无法重置，请先停止设备。");
        }

        if (await detectionSessionRepository.GetActiveAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException("当前检测任务尚未停止，无法重置。");
        }

        dataSourceStore.SetExternalLocalStreamActive(false);
        dataSourceStore.ResetRunProgress();
        runtimeStateStore.Update(current with
        {
            RuntimeMode = RuntimeMode.Stopped,
            DeviceState = DeviceState.Idle,
            CurrentBatch = string.Empty,
            TotalInspected = 0,
            TotalRejected = 0,
            YieldRate = 100m,
            UpdatedAt = DateTimeOffset.UtcNow,
            MachineStartedAt = null
        });
    }

    public async Task<DetectionSession> StartDetectionAsync(CancellationToken cancellationToken)
    {
        var snapshot = runtimeStateStore.GetSnapshot();
        if (snapshot.RuntimeMode != RuntimeMode.Running)
        {
            throw new InvalidOperationException("机器尚未启动，无法开始检测。");
        }

        if (await detectionSessionRepository.GetActiveAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException("当前已有检测任务运行中，请先停止检测。");
        }

        return await CreateDetectionSessionForActiveChannelAsync(cancellationToken);
    }

    private async Task<DetectionSession> CreateDetectionSessionForActiveChannelAsync(CancellationToken cancellationToken)
    {
        var channel = (await channelRepository.GetAllAsync(cancellationToken))
            .OrderBy(x => x.ChannelNo)
            .FirstOrDefault(x => x.IsEnabled)
            ?? throw new InvalidOperationException("当前没有启用中的通道，无法开始检测。");

        if (channel.ModelVersionId is null)
        {
            throw new InvalidOperationException($"当前通道 {channel.Name} 尚未绑定模型。");
        }

        var model = await modelRepository.GetByIdAsync(channel.ModelVersionId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"未找到当前通道 {channel.Name} 绑定的模型。");

        var session = new DetectionSession(
            Guid.NewGuid(),
            $"DS-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            channel.Id,
            channel.SeafoodProductId,
            model.Id,
            DateTimeOffset.UtcNow,
            null,
            DetectionSessionStatus.Running,
            dataSourceStore.Get().Mode,
            dataSourceStore.Get().IsHardwareExecutionEnabled);

        var saved = await detectionSessionRepository.AddAsync(session, cancellationToken);
        var snapshot = runtimeStateStore.GetSnapshot();
        runtimeStateStore.Update(snapshot with
        {
            CurrentRecipe = channel.Name,
            CurrentModelVersion = model.Version,
            CurrentBatch = saved.SessionCode,
            TotalInspected = 0,
            TotalRejected = 0,
            YieldRate = 100m,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return saved;
    }

    public async Task<DetectionSession?> StopDetectionAsync(CancellationToken cancellationToken)
    {
        var active = await detectionSessionRepository.GetActiveAsync(cancellationToken);
        if (active is null)
        {
            return null;
        }

        return await detectionSessionRepository.UpdateAsync(active with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Status = DetectionSessionStatus.Stopped
        }, cancellationToken);
    }

    public RuntimeSnapshot GetSnapshot() => runtimeStateStore.GetSnapshot();
}

internal static class ChannelConfigDefaults
{
    public static string BuildDefaultLaneMappingJson()
    {
        var imageWidth = ChannelRuntimeDefaults.ImageWidth;
        var bandWidth = Math.Max(1, imageWidth / 4);
        var lanes = Enumerable.Range(0, 4)
            .Select(index => new
            {
                nozzleNumber = index + 1,
                startX = index * bandWidth,
                endX = index == 3 ? imageWidth : ((index + 1) * bandWidth) - 1
            })
            .ToArray();

        return System.Text.Json.JsonSerializer.Serialize(lanes);
    }
}
