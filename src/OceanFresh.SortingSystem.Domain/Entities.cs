namespace OceanFresh.SortingSystem.Domain;

public sealed record SeafoodCategory(
    Guid Id,
    string Code,
    string Name,
    string Description);

public sealed record SeafoodProduct(
    Guid Id,
    string Code,
    string Name,
    bool IsEnabled);

public sealed record SeafoodTrait(
    Guid Id,
    Guid SeafoodProductId,
    string Name,
    bool IsNormal,
    int SortOrder,
    bool IsEnabled);

public sealed record ModelVersion(
    Guid Id,
    Guid SeafoodCategoryId,
    string Version,
    string SourceWeightPath,
    string DeploymentModelPath,
    string InputTensorShape,
    string LabelMapJson,
    decimal ConfidenceThreshold,
    decimal NmsThreshold,
    string Notes,
    ModelStatus Status,
    DateTimeOffset ExportedAt,
    DateTimeOffset CreatedAt);

public sealed record ProductRecipe(
    Guid Id,
    Guid SeafoodCategoryId,
    string Name,
    decimal ConveyorSpeedMetersPerSecond,
    int ImageWidth,
    int ImageHeight,
    decimal XrayVoltageKv,
    decimal XrayCurrentMa,
    DefectHandlingAction DefectHandlingAction,
    string NormalLabel,
    int EjectDelayMicroseconds,
    int EjectPulseWidthMicroseconds,
    int EncoderWindowStart,
    int EncoderWindowEnd,
    bool IsEnabled);

public sealed record ChannelConfig(
    Guid Id,
    int ChannelNo,
    string Name,
    Guid SeafoodProductId,
    Guid? ModelVersionId,
    DefectHandlingAction DefectHandlingAction,
    int ImageWidth,
    int ImageHeight,
    decimal ConveyorSpeedMetersPerSecond,
    decimal XrayVoltageKv,
    decimal XrayCurrentMa,
    decimal ConfidenceThreshold,
    bool IsEnabled);

public sealed record RecipeModelBinding(
    Guid Id,
    Guid RecipeId,
    Guid ModelVersionId,
    bool IsPrimary,
    bool IsPreloaded,
    DateTimeOffset BoundAt);

public sealed record DefectDetection(
    Guid Id,
    string Label,
    decimal Confidence,
    int X,
    int Y,
    int Width,
    int Height);

public sealed record EjectCommand(
    Guid Id,
    Guid RecipeId,
    string DefectLabel,
    DefectHandlingAction Action,
    int NozzleNumber,
    int TriggerEncoderPosition,
    int TriggerDelayMicroseconds,
    int PulseWidthMicroseconds,
    DateTimeOffset CreatedAt);

public sealed record InspectionRecord(
    Guid Id,
    Guid RecipeId,
    Guid ModelVersionId,
    string BatchCode,
    string ImagePath,
    bool IsRejected,
    bool IsTimedOut,
    DateTimeOffset CapturedAt,
    IReadOnlyList<DefectDetection> Detections,
    EjectCommand? EjectCommand);

public sealed record DeviceStatus(
    string DeviceKey,
    DeviceState State,
    string Message,
    DateTimeOffset UpdatedAt);

public sealed record AlarmEvent(
    Guid Id,
    AlarmSeverity Severity,
    string Source,
    string Code,
    string Message,
    DateTimeOffset RaisedAt,
    bool IsAcknowledged);

public sealed record BatchSession(
    Guid Id,
    string BatchCode,
    Guid RecipeId,
    Guid ModelVersionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TotalCount,
    int RejectCount);

public sealed record UserAccount(
    Guid Id,
    string UserName,
    string DisplayName,
    UserRole Role,
    bool IsEnabled);

public sealed record RuntimeSnapshot(
    RuntimeMode RuntimeMode,
    DeviceState DeviceState,
    string CurrentRecipe,
    string CurrentModelVersion,
    string CurrentBatch,
    int TotalInspected,
    int TotalRejected,
    decimal YieldRate,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DeviceStatus> Devices,
    IReadOnlyList<AlarmEvent> ActiveAlarms);

public sealed record InferenceRequest(
    Guid RecipeId,
    Guid ModelVersionId,
    string FrameId,
    byte[] ImageBytes,
    DateTimeOffset CapturedAt);

public sealed record InferenceResult(
    string FrameId,
    bool TimedOut,
    IReadOnlyList<DefectDetection> Detections,
    DateTimeOffset CompletedAt);
