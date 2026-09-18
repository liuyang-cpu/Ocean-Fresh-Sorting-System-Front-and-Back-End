using Microsoft.Extensions.DependencyInjection;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOceanFreshInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteDatabaseInitializer>();
        services.AddSingleton<ISeafoodCategoryRepository, SqliteSeafoodCategoryRepository>();
        services.AddSingleton<ISeafoodProductRepository, SqliteSeafoodProductRepository>();
        services.AddSingleton<ISeafoodTraitRepository, SqliteSeafoodTraitRepository>();
        services.AddSingleton<IChannelConfigRepository, SqliteChannelConfigRepository>();
        services.AddSingleton<IModelRegistryRepository, SqliteModelRegistryRepository>();
        services.AddSingleton<IInspectionRecordRepository, SqliteInspectionRecordRepository>();
        services.AddSingleton<IManualReviewRepository, SqliteManualReviewRepository>();
        services.AddSingleton<IManualReviewPreviewGenerator, ImageSharpManualReviewPreviewGenerator>();
        services.AddSingleton<IDetectionSessionRepository, SqliteDetectionSessionRepository>();
        services.AddSingleton<IAlarmRepository, SqliteAlarmRepository>();
        services.AddSingleton<IHardwareDeviceRepository, SqliteHardwareDeviceRepository>();
        services.AddSingleton<IUserRepository, SqliteUserRepository>();
        services.AddSingleton<IOperationAuditRepository, SqliteOperationAuditRepository>();
        services.AddSingleton<IRuntimeStateStore, InMemoryRuntimeStateStore>();
        services.AddSingleton<IHardwareProtocolClient, SimulatedHardwareProtocolClient>();
        services.AddSingleton<IEjectorController, SimulatedEjectorController>();

        services.AddSingleton<IDeviceHealthProvider, SimulatedDeviceHealthProvider>();
        services.AddSingleton<IRuntimeDataSourceStore, InMemoryRuntimeDataSourceStore>();
        services.AddSingleton<IImageSource, RuntimeConfiguredImageSource>();
        services.AddSingleton<IPredictWorkspace, FileSystemPredictWorkspace>();
        services.AddSingleton<IProductPredictConfigStore, FileSystemProductPredictConfigStore>();
        services.AddSingleton<IInferenceEngine, HttpYoloServiceInferenceEngine>();
        return services;
    }

    public static async Task InitializeOceanFreshInfrastructureAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var initializer = services.GetRequiredService<SqliteDatabaseInitializer>();
        await initializer.InitializeAsync(cancellationToken);
    }
}
