using Microsoft.Extensions.Hosting;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.Infrastructure;

namespace OceanFresh.SortingSystem.Runtime;

public sealed class RealtimeRuntimeWorker(
    IImageSource imageSource,
    IInferenceEngine inferenceEngine,
    IChannelConfigRepository channelRepository,
    ISeafoodProductRepository productRepository,
    ISeafoodTraitRepository traitRepository,
    IModelRegistryRepository modelRepository,
    ILocator locator,
    IEjectorController ejectorController,
    IInspectionRecordRepository inspectionRecordRepository,
    IDetectionSessionRepository detectionSessionRepository,
    IAlarmRepository alarmRepository,
    IRuntimeStateStore runtimeStateStore,
    IDeviceHealthProvider deviceHealthProvider,
    IHardwareInterlockService hardwareInterlockService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PrimeSnapshotAsync(stoppingToken);

        await foreach (var request in imageSource.CaptureAsync(stoppingToken))
        {
            var snapshot = runtimeStateStore.GetSnapshot();
            if (snapshot.RuntimeMode != RuntimeMode.Running)
            {
                continue;
            }

            var interlock = await hardwareInterlockService.EvaluateAsync(stoppingToken);
            if (!interlock.CanRun)
            {
                runtimeStateStore.Update(snapshot with
                {
                    RuntimeMode = RuntimeMode.Faulted,
                    DeviceState = DeviceState.Faulted,
                    Devices = interlock.Devices,
                    ActiveAlarms = interlock.CriticalAlarms,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    MachineStartedAt = null
                });
                continue;
            }

            var activeSession = await detectionSessionRepository.GetActiveAsync(stoppingToken);
            if (activeSession is null)
            {
                continue;
            }

            var activeChannel = (await channelRepository.GetAllAsync(stoppingToken))
                .OrderBy(x => x.ChannelNo)
                .FirstOrDefault(x => x.IsEnabled);
            if (activeChannel is null)
            {
                continue;
            }

            if (activeChannel.ModelVersionId is null)
            {
                await alarmRepository.AddAsync(new AlarmEvent(
                    Guid.NewGuid(),
                    AlarmSeverity.Critical,
                    "runtime",
                    "CHANNEL_MODEL_NOT_FOUND",
                    $"Channel {activeChannel.Name} has no selected model.",
                    DateTimeOffset.UtcNow,
                    false), stoppingToken);
                continue;
            }

            var model = await modelRepository.GetByIdAsync(activeChannel.ModelVersionId.Value, stoppingToken);
            if (model is null)
            {
                await alarmRepository.AddAsync(new AlarmEvent(
                    Guid.NewGuid(),
                    AlarmSeverity.Critical,
                    "runtime",
                    "MODEL_NOT_FOUND",
                    $"Channel {activeChannel.Name} has no active model.",
                    DateTimeOffset.UtcNow,
                    false), stoppingToken);
                continue;
            }

            var product = await productRepository.GetByIdAsync(activeChannel.SeafoodProductId, stoppingToken);

            var sourcePath = await PersistFrameAsync(activeChannel, request, stoppingToken);
            var runName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{request.FrameId}";
            var channelInferenceRequest = new ChannelInferenceRequest(
                activeChannel.Id,
                activeChannel,
                ResolveModelPath(activeChannel, model),
                ResolveEffectiveLabelMapJson(product?.LabelMapJson),
                product?.PredictConfigPath ?? string.Empty,
                activeChannel.ConfidenceThreshold,
                sourcePath,
                Path.GetFileName(sourcePath),
                runName,
                request.FrameId,
                request.CapturedAt,
                model.TrainingImageSize);

            InferenceResult inference;
            try
            {
                inference = await inferenceEngine.RunAsync(channelInferenceRequest, stoppingToken);
            }
            catch (Exception ex)
            {
                await alarmRepository.AddAsync(new AlarmEvent(
                    Guid.NewGuid(),
                    AlarmSeverity.Critical,
                    "runtime",
                    "INFERENCE_FAILED",
                    ex.Message,
                    DateTimeOffset.UtcNow,
                    false), stoppingToken);
                continue;
            }

            var traits = await traitRepository.GetByProductAsync(activeChannel.SeafoodProductId, stoppingToken);
            var normalLabel = traits.FirstOrDefault(x => x.IsNormal)?.Name ?? "正常";
            var commands = new List<EjectCommand>();
            var hasAbnormalDetection = false;

            foreach (var detection in inference.Detections)
            {
                var isNormal = string.Equals(detection.Label, normalLabel, StringComparison.OrdinalIgnoreCase);
                hasAbnormalDetection |= !isNormal;

                if (!request.IsHardwareExecutionEnabled)
                {
                    continue;
                }

                var action = isNormal ? DefectHandlingAction.Pass : activeChannel.DefectHandlingAction;

                if (action is DefectHandlingAction.Sink or DefectHandlingAction.AirJet or DefectHandlingAction.Pusher)
                {
                    var command = locator.BuildCommand(activeChannel, detection, action, DateTimeOffset.UtcNow);
                    try
                    {
                        await ejectorController.ExecuteAsync(command, stoppingToken);
                        commands.Add(command);
                    }
                    catch (Exception ex)
                    {
                        await alarmRepository.AddAsync(new AlarmEvent(
                            Guid.NewGuid(),
                            AlarmSeverity.Critical,
                            "ejector",
                            "EJECTOR_COMMAND_FAILED",
                            ex.Message,
                            DateTimeOffset.UtcNow,
                            false), stoppingToken);

                        runtimeStateStore.Update(runtimeStateStore.GetSnapshot() with
                        {
                            RuntimeMode = RuntimeMode.Faulted,
                            DeviceState = DeviceState.Faulted,
                            UpdatedAt = DateTimeOffset.UtcNow,
                            MachineStartedAt = null
                        });
                        break;
                    }
                }

                if (action == DefectHandlingAction.StopLine)
                {
                    await alarmRepository.AddAsync(new AlarmEvent(
                        Guid.NewGuid(),
                        AlarmSeverity.Critical,
                        "runtime",
                        "STOP_LINE_RULE",
                        $"Channel {activeChannel.Name} detected {detection.Label} and requested stop-line action.",
                        DateTimeOffset.UtcNow,
                        false), stoppingToken);
                }
            }

            await inspectionRecordRepository.AddAsync(new InspectionRecord(
                Guid.NewGuid(),
                activeChannel.Id,
                model.Id,
                activeSession?.Id,
                runtimeStateStore.GetSnapshot().CurrentBatch,
                sourcePath,
                hasAbnormalDetection,
                inference.TimedOut,
                request.CapturedAt,
                inference.Detections,
                commands), stoppingToken);

            UpdateSnapshot(activeChannel.Name, model.Version, hasAbnormalDetection, stoppingToken);
        }
    }

    private async Task PrimeSnapshotAsync(CancellationToken cancellationToken)
    {
        var devices = await deviceHealthProvider.GetStatusesAsync(cancellationToken);
        runtimeStateStore.Update(new RuntimeSnapshot(
            RuntimeMode.Stopped,
            DeviceState.Idle,
            "未启用通道",
            "未选择模型",
            "BATCH-20260601-01",
            0,
            0,
            100m,
            DateTimeOffset.UtcNow,
            devices,
            [],
            null));
    }

    private void UpdateSnapshot(string recipeName, string modelVersion, bool rejected, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var current = runtimeStateStore.GetSnapshot();
        var total = current.TotalInspected + 1;
        var rejectCount = current.TotalRejected + (rejected ? 1 : 0);
        var yieldRate = total == 0 ? 100m : decimal.Round((total - rejectCount) * 100m / total, 2);

        runtimeStateStore.Update(current with
        {
            RuntimeMode = RuntimeMode.Running,
            DeviceState = DeviceState.Running,
            CurrentRecipe = recipeName,
            CurrentModelVersion = modelVersion,
            TotalInspected = total,
            TotalRejected = rejectCount,
            YieldRate = yieldRate,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<string> PersistFrameAsync(ChannelConfig channelConfig, InferenceRequest request, CancellationToken cancellationToken)
    {
        var inputDirectory = FileSystemPredictWorkspace.ResolveSharedPath(ChannelRuntimeDefaults.BuildPredictInputDirectory(channelConfig.ChannelNo));
        Directory.CreateDirectory(inputDirectory);
        var sourcePath = Path.Combine(inputDirectory, $"{BuildSafeFileName(request.FrameId)}{ResolveFrameExtension(request.SourceFileName)}");
        if (request.ImageBytes.Length == 0)
        {
            await File.WriteAllBytesAsync(sourcePath, EmptyPngBytes, cancellationToken);
            return sourcePath;
        }

        await File.WriteAllBytesAsync(sourcePath, request.ImageBytes, cancellationToken);
        return sourcePath;
    }

    private static string BuildSafeFileName(string frameId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = string.Concat(frameId.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? $"FRAME-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}" : safe;
    }

    private static string ResolveFrameExtension(string? sourceFileName)
    {
        var extension = Path.GetExtension(sourceFileName);
        return extension?.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" => extension,
            _ => ".png"
        };
    }

    // Minimal placeholder image for legacy simulated capture when no real X-ray frame bytes are available.
    private static readonly byte[] EmptyPngBytes =
    [
        137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,1,0,0,0,1,8,2,0,0,0,144,119,83,222,
        0,0,0,12,73,68,65,84,8,153,99,248,255,255,63,0,5,254,2,254,167,53,129,38,0,0,0,0,73,69,78,68,174,66,96,130
    ];

    private static string ResolveEffectiveLabelMapJson(string? productLabelMapJson)
    {
        if (!string.IsNullOrWhiteSpace(productLabelMapJson) && productLabelMapJson != "{}")
        {
            return productLabelMapJson;
        }

        return "{}";
    }

    private static string ResolveModelPath(ChannelConfig channelConfig, ModelVersion model)
    {
        foreach (var candidate in new[] { channelConfig.ModelPath, model.SourceWeightPath })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
