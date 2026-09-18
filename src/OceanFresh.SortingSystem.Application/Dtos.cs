using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public sealed record DashboardDto(
    RuntimeSnapshot Snapshot,
    DetectionSession? CurrentSession,
    DashboardSummaryDto Summary,
    RuntimeDataSourceConfigDto DataSource,
    IReadOnlyList<DashboardChannelKpiDto> ChannelKpis,
    IReadOnlyList<DashboardDefectStatDto> DefectStats,
    DashboardLatestAbnormalDto? LatestAbnormal,
    IReadOnlyList<ChannelConfigDetailDto> ActiveChannels,
    IReadOnlyList<ModelVersion> ActiveModels);

public sealed record DashboardSummaryDto(
    string CurrentChannel,
    string CurrentProduct,
    string CurrentModel,
    string MachineWorkDuration,
    int TotalCount,
    int NormalCount,
    int RejectCount,
    int EjectCommandCount,
    decimal YieldRate);

public sealed record DashboardChannelKpiDto(
    Guid ChannelId,
    string ChannelName,
    string Status,
    string ProductName,
    string ModelVersion,
    int TotalCount,
    int NormalCount,
    int RejectCount,
    int EjectCommandCount,
    decimal YieldRate,
    string TopDefectLabel);

public sealed record RuntimeDataSourceConfigDto(
    RuntimeDataSourceMode Mode,
    string ModeText,
    string? LocalDirectoryPath,
    int FrameIntervalMilliseconds,
    int ImageCount,
    int CurrentIndex,
    bool IsHardwareExecutionEnabled,
    string Status);

public sealed record UpdateRuntimeDataSourceRequest(
    RuntimeDataSourceMode Mode,
    string? LocalDirectoryPath,
    int? FrameIntervalMilliseconds);

public sealed record DashboardDefectStatDto(
    string Label,
    int Count,
    decimal Percent);

public sealed record DashboardLatestAbnormalDto(
    string ImagePath,
    string Summary,
    string Label,
    decimal Confidence,
    DateTimeOffset CapturedAt);

public sealed record ProductionStatisticsDto(
    string Range,
    DateTimeOffset Since,
    DateTimeOffset Until,
    IReadOnlyList<ProductProductionStatDto> Products,
    IReadOnlyList<DetectionSessionStatDto> RecentSessions,
    IReadOnlyList<YieldTrendPointDto> YieldTrend);

public sealed record ProductProductionStatDto(
    Guid ProductId,
    string ProductName,
    int TotalCount,
    int NormalCount,
    int RejectCount,
    decimal YieldRate,
    IReadOnlyList<DashboardDefectStatDto> DefectStats);

public sealed record DetectionSessionStatDto(
    Guid SessionId,
    string SessionCode,
    Guid ProductId,
    string ProductName,
    Guid ModelVersionId,
    string ModelVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DetectionSessionStatus Status,
    RuntimeDataSourceMode DataSourceMode,
    bool IsHardwareExecutionEnabled,
    string TaskType,
    int TotalCount,
    int NormalCount,
    int RejectCount,
    decimal YieldRate,
    IReadOnlyList<DashboardDefectStatDto> DefectStats);

public sealed record ManualReviewSessionDto(
    Guid SessionId,
    string SessionCode,
    ManualReviewSummaryDto Summary,
    IReadOnlyList<ManualReviewCandidateDto> Candidates);

public sealed record ManualReviewSummaryDto(
    int CandidateCount,
    int ReviewedCount,
    int ConfirmedAbnormalCount,
    int FalsePositiveCount,
    int RelabeledAbnormalCount,
    decimal ConfirmedAbnormalRate,
    decimal FalsePositiveRate,
    bool IsComplete,
    string RecallStatus);

public sealed record ManualReviewCandidateDto(
    Guid InspectionRecordId,
    Guid DetectionId,
    string ImagePath,
    string ReviewImagePath,
    string ModelLabel,
    decimal Confidence,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsReviewed,
    string HumanLabel,
    ManualReviewJudgement? Judgement,
    string JudgementText,
    string Reviewer,
    string Notes,
    DateTimeOffset CapturedAt,
    DateTimeOffset? ReviewedAt);

public sealed record ManualReviewPreviewDto(
    Guid InspectionRecordId,
    Guid DetectionId,
    string ReviewImagePath,
    bool IsAvailable,
    string Message);

public sealed record UpsertManualReviewRequest(
    Guid InspectionRecordId,
    Guid DetectionId,
    string HumanLabel,
    string Reviewer,
    string? Notes);

public sealed record YieldTrendPointDto(
    Guid SessionId,
    Guid ProductId,
    string ProductName,
    string Label,
    DateTimeOffset Timestamp,
    decimal YieldRate,
    int TotalCount,
    int RejectCount);

public sealed record ManualInferenceResultDto(
    string ChannelName,
    string ProductName,
    string ModelVersion,
    string ModelPath,
    string PredictConfigPath,
    string SourceImagePath,
    string NormalLabel,
    bool TimedOut,
    IReadOnlyList<DefectDetection> Detections,
    IReadOnlyList<EjectCommand> EjectCommands);

public sealed record SeafoodProductProfileDto(
    SeafoodProduct Product,
    IReadOnlyList<SeafoodTrait> Traits);

public sealed record ChannelConfigDetailDto(
    ChannelConfig Channel,
    SeafoodProduct? Product,
    ModelVersion? ModelVersion);

public sealed record ImportModelRequest(
    Guid SeafoodCategoryId,
    string Version,
    string SourceWeightPath,
    string Notes,
    int? TrainingImageSize = null);

public sealed record UpsertModelRequest(
    Guid? Id,
    Guid SeafoodCategoryId,
    string Version,
    string SourceWeightPath,
    string Notes,
    int? TrainingImageSize = null);

public sealed record ModelImportResult(
    OceanFresh.SortingSystem.Domain.ModelVersion ModelVersion,
    OceanFresh.SortingSystem.Domain.ModelValidationResult Validation);

public sealed record UpsertSeafoodTraitRequest(
    string Name,
    bool IsNormal);

public sealed record UpsertSeafoodProductRequest(
    Guid? Id,
    string Code,
    string Name,
    bool IsEnabled,
    string ClassesFilePath,
    string LabelMapJson,
    string? PredictConfigPath,
    string? PredictConfigJson,
    IReadOnlyList<UpsertSeafoodTraitRequest> Traits);

public sealed record UpsertChannelConfigRequest(
    Guid? Id,
    int ChannelNo,
    string Name,
    Guid SeafoodProductId,
    Guid? ModelVersionId,
    DefectHandlingAction DefectHandlingAction,
    decimal ConfidenceThreshold,
    bool IsEnabled,
    decimal? CameraToEjectDistanceMillimeters = null,
    decimal? MillimetersPerPixelY = null,
    int? SoftwareLatencyMilliseconds = null,
    int? ActuatorDelayMilliseconds = null,
    string? HorizontalLaneMappingJson = null);

public sealed record UpsertHardwareDeviceRequest(
    Guid? Id,
    string DeviceNo,
    string Name,
    DeviceType Type,
    string FirmwareVersion,
    DeviceState State,
    bool IsEnabled,
    string Notes);

public sealed record DeviceSelfCheckResultDto(
    HardwareDevice Device,
    bool Passed,
    string Message);

public sealed record HardwareInterlockDto(
    bool CanRun,
    RuntimeMode RuntimeMode,
    DeviceState DeviceState,
    string Summary,
    IReadOnlyList<DeviceStatus> Devices,
    IReadOnlyList<AlarmEvent> CriticalAlarms);

public sealed record SoftwareVersionDto(
    string ProductName,
    string CurrentVersion,
    string BuildTime,
    string DatabaseVersion,
    string YoloServiceVersion,
    string RuntimeEnvironment,
    string UpdatePolicy,
    IReadOnlyList<SoftwareUpdateRecord> RecentUpdates);

public sealed record SoftwareUpdatePackageRequest(
    string PackagePath,
    string Sha256);

public sealed record LoginResultDto(
    string UserName,
    string DisplayName,
    UserRole Role,
    DateTimeOffset LoginAt,
    string SessionToken = "",
    DateTimeOffset? SessionExpiresAt = null);

public sealed record AdminLoginRequest(string Password);

public sealed record ChangeAdminPasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public sealed record OperationAuditActorDto(
    string UserName,
    string DisplayName,
    UserRole Role);

public sealed record OperationAuditChangeDto(
    string Field,
    string DisplayName,
    string OldValue,
    string NewValue);

public sealed record OperationAuditDetailsDto(
    IReadOnlyList<OperationAuditChangeDto> Changes,
    IReadOnlyList<string> RelatedChanges);

public sealed record OperationAuditRecordDto(
    Guid Id,
    string OperationType,
    string ActionCode,
    string Content,
    string OperatorName,
    string OperatorDisplayName,
    string TargetType,
    string TargetId,
    string TargetName,
    DateTimeOffset OccurredAt,
    IReadOnlyList<OperationAuditChangeDto> Changes,
    IReadOnlyList<string> RelatedChanges);

public sealed record OperationAuditPageDto(
    IReadOnlyList<OperationAuditRecordDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record OperationAuditWriteRequest(
    OperationAuditCategory Category,
    string ActionCode,
    string Summary,
    string TargetType,
    string TargetId,
    string TargetName,
    IReadOnlyList<OperationAuditChangeDto> Changes,
    IReadOnlyList<string>? RelatedChanges = null,
    string? DedupeKey = null);

public sealed record StreamInspectionRecordRequest(
    Guid ChannelId,
    Guid ModelVersionId,
    string ImagePath,
    bool IsTimedOut,
    IReadOnlyList<DefectDetection> Detections,
    IReadOnlyList<EjectCommand> EjectCommands);
