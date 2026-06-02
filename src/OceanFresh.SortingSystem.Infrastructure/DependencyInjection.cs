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
        services.AddSingleton<IProductRecipeRepository, SqliteProductRecipeRepository>();
        services.AddSingleton<IModelRegistryRepository, SqliteModelRegistryRepository>();
        services.AddSingleton<IRecipeModelBindingRepository, SqliteRecipeModelBindingRepository>();
        services.AddSingleton<IInspectionRecordRepository, SqliteInspectionRecordRepository>();
        services.AddSingleton<IAlarmRepository, SqliteAlarmRepository>();
        services.AddSingleton<IUserRepository, SqliteUserRepository>();
        services.AddSingleton<IRuntimeStateStore, InMemoryRuntimeStateStore>();
        services.AddSingleton<IDeviceHealthProvider, SimulatedDeviceHealthProvider>();
        services.AddSingleton<IEjectorController, SimulatedEjectorController>();
        services.AddSingleton<IImageSource, SimulatedImageSource>();
        services.AddSingleton<IInferenceEngine, SimulatedInferenceEngine>();
        return services;
    }

    public static async Task InitializeOceanFreshInfrastructureAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var initializer = services.GetRequiredService<SqliteDatabaseInitializer>();
        await initializer.InitializeAsync(cancellationToken);
    }
}
