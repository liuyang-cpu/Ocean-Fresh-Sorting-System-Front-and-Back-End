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
    bool IsEnabled,
    string ClassesFilePath,
    string LabelMapJson,
    string PredictConfigPath);

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
    string Notes,
    ModelStatus Status,
    DateTimeOffset CreatedAt,
    int? TrainingImageSize = null);

public sealed record ChannelConfig(
    Guid Id,
    int ChannelNo,
    string Name,
    Guid SeafoodProductId,
    Guid? ModelVersionId,
    string ModelPath,
    DefectHandlingAction DefectHandlingAction,
    decimal ConfidenceThreshold,
    bool IsEnabled,
    string? LastRuntimePredictConfigPath,
    string? LastRuntimeOutputDirectory,
    decimal CameraToEjectDistanceMillimeters,
    decimal MillimetersPerPixelY,
    int SoftwareLatencyMilliseconds,
    int ActuatorDelayMilliseconds,
    string HorizontalLaneMappingJson);

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
    Guid? DetectionSessionId,
    string BatchCode,
    string ImagePath,
    bool IsRejected,
    bool IsTimedOut,
    DateTimeOffset CapturedAt,
    IReadOnlyList<DefectDetection> Detections,
    IReadOnlyList<EjectCommand> EjectCommands);

public sealed record ManualReviewRecord(
    Guid Id,
    Guid DetectionSessionId,
    Guid InspectionRecordId,
    Guid DetectionId,
    string ModelLabel,
    string HumanLabel,
    ManualReviewJudgement Judgement,
    string Reviewer,
    string Notes,
    DateTimeOffset ReviewedAt);

public sealed record DetectionSession(
    Guid Id,
    string SessionCode,
    Guid ChannelId,
    Guid ProductId,
    Guid ModelVersionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DetectionSessionStatus Status,
    RuntimeDataSourceMode DataSourceMode = RuntimeDataSourceMode.XrayCamera,
    bool IsHardwareExecutionEnabled = true);

public sealed record RuntimeDataSourceConfig(
    RuntimeDataSourceMode Mode,
    string? LocalDirectoryPath,
    int FrameIntervalMilliseconds,
    int ImageCount,
    int CurrentIndex,
    bool IsHardwareExecutionEnabled,
    string Status)
{
    public const int DefaultFrameIntervalMilliseconds = 68;

    public static RuntimeDataSourceConfig Default { get; } = new(
        RuntimeDataSourceMode.XrayCamera,
        null,
        DefaultFrameIntervalMilliseconds,
        0,
        0,
        true,
        "X 光相机");
}

public sealed record DeviceStatus(
    string DeviceKey,
    DeviceState State,
    string Message,
    DateTimeOffset UpdatedAt);

public sealed record HardwareSignalStatus(
    string DeviceNo,
    DeviceType DeviceType,
    DeviceState State,
    HardwareSignalSeverity Severity,
    string Code,
    string Message,
    decimal? NumericValue,
    string Unit,
    DateTimeOffset UpdatedAt);

public sealed record HardwareDevice(
    Guid Id,
    string DeviceNo,
    string Name,
    DeviceType Type,
    string FirmwareVersion,
    DeviceState State,
    DateTimeOffset? LastSelfCheckAt,
    string LastSelfCheckResult,
    bool IsEnabled,
    string Notes);

public sealed record AlarmEvent(
    Guid Id,
    AlarmSeverity Severity,
    string Source,
    string Code,
    string Message,
    DateTimeOffset RaisedAt,
    bool IsAcknowledged);

public sealed record SoftwareVersionInfo(
    string ProductName,
    string CurrentVersion,
    string BuildTime,
    string DatabaseVersion,
    string YoloServiceVersion,
    string RuntimeEnvironment,
    string UpdatePolicy,
    IReadOnlyList<SoftwareUpdateRecord> RecentUpdates);

public sealed record SoftwareUpdateRecord(
    string Version,
    DateTimeOffset InstalledAt,
    string Result,
    string Notes);

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
    bool IsEnabled,
    string PasswordHash,
    DateTimeOffset? LastLoginAt);

public sealed record OperationAuditLog(
    Guid Id,
    OperationAuditCategory Category,
    string ActionCode,
    string Summary,
    string ActorUserName,
    string ActorDisplayName,
    UserRole ActorRole,
    string TargetType,
    string TargetId,
    string TargetName,
    string DetailsJson,
    string? DedupeKey,
    DateTimeOffset OccurredAt);

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
    IReadOnlyList<AlarmEvent> ActiveAlarms,
    DateTimeOffset? MachineStartedAt);

public record InferenceRequest(
    Guid RecipeId,
    Guid ModelVersionId,
    string FrameId,
    byte[] ImageBytes,
    DateTimeOffset CapturedAt,
    string? SourceFileName = null,
    RuntimeDataSourceMode DataSourceMode = RuntimeDataSourceMode.XrayCamera,
    bool IsHardwareExecutionEnabled = true);

public sealed record InferenceResult(
    string FrameId,
    bool TimedOut,
    IReadOnlyList<DefectDetection> Detections,
    DateTimeOffset CompletedAt);

public sealed record ChannelInferenceRequest(
    Guid ChannelId,
    ChannelConfig ChannelConfig,
    string ModelPath,
    string ModelLabelMapJson,
    string PredictConfigPath,
    decimal ConfidenceThreshold,
    string SourcePath,
    string SourceFileName,
    string RunName,
    string FrameId,
    DateTimeOffset CapturedAt,
    int? TrainingImageSize = null)
    : InferenceRequest(ChannelId, ChannelConfig.ModelVersionId ?? Guid.Empty, FrameId, [], CapturedAt, SourceFileName);

public static class ProductPostprocessDefaults
{
    public const decimal AdjacentFrameHeightMin = 35.64m;
    public const decimal AdjacentFrameHeightMax = 119.95m;
}

public static class ChannelRuntimeDefaults
{
    public const int ImageWidth = 1536;
    public const int ImageHeight = 300;
    public const decimal XrayVoltageKv = 50m;
    public const decimal XrayCurrentMa = 6m;

    public static string BuildPredictInputDirectory(int channelNo) => $@"predict-input\channel-{channelNo:00}";

    public static string BuildPredictOutputDirectory(int channelNo) => $@"predict-output\channel-{channelNo:00}";

    public static string BuildLastRuntimeConfigPath(int channelNo, string runName) =>
        $@"predict-output\channel-{channelNo:00}\{runName}\predict.runtime.json";

    public static string BuildLastRuntimeOutputDirectory(int channelNo, string runName) =>
        $@"predict-output\channel-{channelNo:00}\{runName}\predict_run";
}

public static class MachineRuntimeDefaults
{
    public const decimal ConveyorSpeedMetersPerSecond = 1.8m;
}
