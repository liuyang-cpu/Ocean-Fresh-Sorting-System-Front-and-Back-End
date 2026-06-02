using Microsoft.Extensions.Hosting;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Runtime;

public sealed class RealtimeRuntimeWorker(
    IImageSource imageSource,
    IInferenceEngine inferenceEngine,
    IProductRecipeRepository recipeRepository,
    IModelRegistry modelRegistry,
    ILocator locator,
    IEjectorController ejectorController,
    IInspectionRecordRepository inspectionRecordRepository,
    IAlarmRepository alarmRepository,
    IRuntimeStateStore runtimeStateStore,
    IDeviceHealthProvider deviceHealthProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PrimeSnapshotAsync(stoppingToken);

        await foreach (var request in imageSource.CaptureAsync(stoppingToken))
        {
            var recipe = await recipeRepository.GetByIdAsync(request.RecipeId, stoppingToken);
            if (recipe is null)
            {
                continue;
            }

            var model = await modelRegistry.GetActiveForRecipeAsync(recipe.Id, stoppingToken);
            if (model is null)
            {
                await alarmRepository.AddAsync(new AlarmEvent(
                    Guid.NewGuid(),
                    AlarmSeverity.Critical,
                    "runtime",
                    "MODEL_NOT_FOUND",
                    $"Recipe {recipe.Name} has no active model.",
                    DateTimeOffset.UtcNow,
                    false), stoppingToken);
                continue;
            }

            var inference = await inferenceEngine.RunAsync(request with { ModelVersionId = model.Id }, stoppingToken);
            EjectCommand? command = null;

            if (inference.Detections.Count > 0)
            {
                var primaryDetection = inference.Detections[0];
                var action = DefectActionPolicy.Resolve(recipe, primaryDetection.Label);

                if (action is DefectHandlingAction.Sink or DefectHandlingAction.AirJet or DefectHandlingAction.Pusher)
                {
                    command = locator.BuildCommand(recipe, primaryDetection, action, DateTimeOffset.UtcNow);
                    await ejectorController.ExecuteAsync(command, stoppingToken);
                }

                if (action == DefectHandlingAction.StopLine)
                {
                    await alarmRepository.AddAsync(new AlarmEvent(
                        Guid.NewGuid(),
                        AlarmSeverity.Critical,
                        "runtime",
                        "STOP_LINE_RULE",
                        $"Recipe {recipe.Name} detected {primaryDetection.Label} and requested stop-line action.",
                        DateTimeOffset.UtcNow,
                        false), stoppingToken);
                }
            }

            await inspectionRecordRepository.AddAsync(new InspectionRecord(
                Guid.NewGuid(),
                recipe.Id,
                model.Id,
                runtimeStateStore.GetSnapshot().CurrentBatch,
                $@"images\{DateTime.UtcNow:yyyyMMdd}\{request.FrameId}.png",
                command is not null,
                inference.TimedOut,
                request.CapturedAt,
                inference.Detections,
                command), stoppingToken);

            UpdateSnapshot(recipe.Name, model.Version, command is not null, stoppingToken);
        }
    }

    private async Task PrimeSnapshotAsync(CancellationToken cancellationToken)
    {
        var devices = await deviceHealthProvider.GetStatusesAsync(cancellationToken);
        runtimeStateStore.Update(new RuntimeSnapshot(
            RuntimeMode.Running,
            DeviceState.Running,
            "花蛤标准线",
            "花蛤-v2",
            "BATCH-20260601-01",
            0,
            0,
            100m,
            DateTimeOffset.UtcNow,
            devices,
            []));
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
}
