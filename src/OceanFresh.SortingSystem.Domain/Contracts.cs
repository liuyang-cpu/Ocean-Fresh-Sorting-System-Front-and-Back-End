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

public interface IProductRecipeRepository
{
    Task<IReadOnlyList<ProductRecipe>> GetAllAsync(CancellationToken cancellationToken);
    Task<ProductRecipe?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ProductRecipe> UpsertAsync(ProductRecipe recipe, CancellationToken cancellationToken);
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
}

public interface IRecipeModelBindingRepository
{
    Task<IReadOnlyList<RecipeModelBinding>> GetByRecipeAsync(Guid recipeId, CancellationToken cancellationToken);
    Task SetPrimaryAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken);
    Task BindAsync(RecipeModelBinding binding, CancellationToken cancellationToken);
}

public interface IInspectionRecordRepository
{
    Task AddAsync(InspectionRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<InspectionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken);
}

public interface IAlarmRepository
{
    Task AddAsync(AlarmEvent alarmEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlarmEvent>> GetActiveAsync(CancellationToken cancellationToken);
}

public interface IUserRepository
{
    Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IRuntimeStateStore
{
    RuntimeSnapshot GetSnapshot();
    void Update(RuntimeSnapshot snapshot);
}

public interface IImageSource
{
    IAsyncEnumerable<InferenceRequest> CaptureAsync(CancellationToken cancellationToken);
}

public interface IInferenceEngine
{
    Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken);
}

public interface IModelRegistry
{
    Task<ModelVersion?> GetActiveForRecipeAsync(Guid recipeId, CancellationToken cancellationToken);
    Task<ModelVersion> ImportAsync(ModelVersion modelVersion, CancellationToken cancellationToken);
    Task ActivateAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken);
    Task RollbackAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken);
}

public interface IModelValidator
{
    Task<ModelValidationResult> ValidateAsync(ModelVersion modelVersion, CancellationToken cancellationToken);
}

public interface ILocator
{
    EjectCommand BuildCommand(ProductRecipe recipe, DefectDetection detection, DefectHandlingAction action, DateTimeOffset createdAt);
}

public interface IEjectorController
{
    Task ExecuteAsync(EjectCommand command, CancellationToken cancellationToken);
}

public interface IDeviceHealthProvider
{
    Task<IReadOnlyList<DeviceStatus>> GetStatusesAsync(CancellationToken cancellationToken);
}

public interface IRuntimeCoordinator
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    RuntimeSnapshot GetSnapshot();
}

public sealed record ModelValidationResult(
    bool IsValid,
    IReadOnlyList<string> Messages);
