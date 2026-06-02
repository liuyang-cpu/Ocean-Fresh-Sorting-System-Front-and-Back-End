using Microsoft.Extensions.DependencyInjection;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOceanFreshApplication(this IServiceCollection services)
    {
        services.AddSingleton<RecipeService>();
        services.AddSingleton<SeafoodProductService>();
        services.AddSingleton<ChannelConfigService>();
        services.AddSingleton<ModelManagementService>();
        services.AddSingleton<RuntimeDashboardService>();
        services.AddSingleton<ILocator, PixelToEjectLocator>();
        services.AddSingleton<IModelRegistry, ModelRegistryService>();
        services.AddSingleton<IModelValidator, FileSystemModelValidator>();
        services.AddSingleton<IRuntimeCoordinator, RuntimeCoordinator>();
        return services;
    }
}
