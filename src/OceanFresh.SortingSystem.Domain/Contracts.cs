namespace OceanFresh.SortingSystem.Domain;

public interface ISeafoodCategoryRepository
{
    Task<IReadOnlyList<SeafoodCategory>> GetAllAsync(CancellationToken cancellationToken);
    Task<SeafoodCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

public interface ISeafoodProductRepository
{
    Task<IReadOnlyList<SeafoodProduct>> GetAllAsync(CancellationToken cancellationToken);
    Task<SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SeafoodProduct> UpsertAsync(SeafoodProduct product, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ISeafoodTraitRepository
{
    Task<IReadOnlyList<SeafoodTrait>> GetByProductAsync(Guid productId, CancellationToken cancellationToken);
    Task ReplaceForProductAsync(Guid productId, IReadOnlyList<SeafoodTrait> traits, CancellationToken cancellationToken);
}

public interface IChannelConfigRepository
{
    Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken cancellationToken);
    Task<ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ChannelConfig> UpsertAsync(ChannelConfig channelConfig, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IModelRegistryRepository
{
    Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken);
    Task<ModelVersion> UpsertAsync(ModelVersion modelVersion, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IInspectionRecordRepository
{
    Task AddAsync(InspectionRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionRecord>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public interface IManualReviewRepository
{
    Task<IReadOnlyList<ManualReviewRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<ManualReviewRecord> UpsertAsync(ManualReviewRecord reviewRecord, CancellationToken cancellationToken);
}

public interface IManualReviewPreviewGenerator
{
    ManualReviewPreview BuildPreviewImage(InspectionRecord record, DefectDetection detection);
}

public interface IDetectionSessionRepository
{
    Task<DetectionSession> AddAsync(DetectionSession session, CancellationToken cancellationToken);
    Task<DetectionSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<DetectionSession?> GetActiveAsync(CancellationToken cancellationToken);
    Task<DetectionSession?> GetLatestAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DetectionSession>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
    Task<DetectionSession> UpdateAsync(DetectionSession session, CancellationToken cancellationToken);
}

public interface IAlarmRepository
{
    Task AddAsync(AlarmEvent alarmEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlarmEvent>> GetActiveAsync(CancellationToken cancellationToken);
    Task AcknowledgeAsync(Guid alarmId, CancellationToken cancellationToken);
}

public interface IHardwareDeviceRepository
{
    Task<IReadOnlyList<HardwareDevice>> GetAllAsync(CancellationToken cancellationToken);
    Task<HardwareDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<HardwareDevice> UpsertAsync(HardwareDevice device, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IUserRepository
{
    Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken cancellationToken);
    Task<UserAccount?> GetByUserNameAsync(string userName, CancellationToken cancellationToken);
    Task<UserAccount> UpsertAsync(UserAccount userAccount, CancellationToken cancellationToken);
}

public sealed record OperationAuditQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    OperationAuditCategory? Category,
    string? Search,
    string? ActorUserName,
    int Page,
    int PageSize);

public sealed record OperationAuditPage(
    IReadOnlyList<OperationAuditLog> Items,
    int TotalCount);

public interface IOperationAuditRepository
{
    Task<bool> TryAddAsync(OperationAuditLog operation, CancellationToken cancellationToken);
    Task<OperationAuditPage> QueryAsync(OperationAuditQuery query, CancellationToken cancellationToken);
}

public interface IRuntimeStateStore
{
    RuntimeSnapshot GetSnapshot();
    void Update(RuntimeSnapshot snapshot);
}

public interface IRuntimeDataSourceStore
{
    RuntimeDataSourceConfig Get();
    void Update(RuntimeDataSourceConfig config);
    void ResetRunProgress();
    void UpdateProgress(int currentIndex, string status);
    bool IsExternalLocalStreamActive { get; }
    void SetExternalLocalStreamActive(bool isActive);
}

public interface IImageSource
{
    IAsyncEnumerable<InferenceRequest> CaptureAsync(CancellationToken cancellationToken);
}

public interface IInferenceEngine
{
    Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken);
}

public interface IPredictWorkspace
{
    Task<PreparedPredictRun> PrepareRunAsync(
        ChannelConfig channelConfig,
        string sourcePath,
        string runName,
        string predictConfigPath,
        string modelPath,
        int trainingImageSize,
        decimal confidenceThreshold,
        CancellationToken cancellationToken);
}

public interface IProductPredictConfigStore
{
    Task<string> GetDefaultTemplateContentAsync(CancellationToken cancellationToken);

    Task<string?> SaveManagedConfigAsync(
        string productCode,
        string? requestedPredictConfigPath,
        string? requestedPredictConfigJson,
        CancellationToken cancellationToken);
}

public interface IModelValidator
{
    Task<ModelValidationResult> ValidateAsync(ModelVersion modelVersion, CancellationToken cancellationToken);
}

public interface ILocator
{
    EjectCommand BuildCommand(ChannelConfig channelConfig, DefectDetection detection, DefectHandlingAction action, DateTimeOffset createdAt);
}

public interface IEjectorController
{
    Task ExecuteAsync(EjectCommand command, CancellationToken cancellationToken);
}

public interface IProductionHardwareController
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IDeviceHealthProvider
{
    Task<IReadOnlyList<DeviceStatus>> GetStatusesAsync(CancellationToken cancellationToken);
}

public interface IHardwareProtocolClient
{
    Task<IReadOnlyList<HardwareSignalStatus>> ReadSignalsAsync(CancellationToken cancellationToken);
}

public interface IHardwareInterlockService
{
    Task<HardwareInterlockResult> EvaluateAsync(CancellationToken cancellationToken);
}

public interface ISoftwareVersionProvider
{
    Task<SoftwareVersionInfo> GetCurrentAsync(CancellationToken cancellationToken);
}

public interface IRuntimeCoordinator
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task ResetAsync(CancellationToken cancellationToken);
    Task<DetectionSession> StartDetectionAsync(CancellationToken cancellationToken);
    Task<DetectionSession?> StopDetectionAsync(CancellationToken cancellationToken);
    RuntimeSnapshot GetSnapshot();
}

public sealed record ModelValidationResult(
    bool IsValid,
    IReadOnlyList<string> Messages);

public sealed record PreparedPredictRun(
    string ScriptPath,
    string RuntimeConfigPath,
    string OutputDirectory,
    string SummaryPath,
    IReadOnlyList<string> Arguments);

public sealed record HardwareInterlockResult(
    bool CanRun,
    RuntimeMode RecommendedRuntimeMode,
    DeviceState RecommendedDeviceState,
    IReadOnlyList<DeviceStatus> Devices,
    IReadOnlyList<AlarmEvent> CriticalAlarms,
    string Summary);

public sealed record ManualReviewPreview(
    string ImagePath,
    bool IsAvailable,
    string Message);
