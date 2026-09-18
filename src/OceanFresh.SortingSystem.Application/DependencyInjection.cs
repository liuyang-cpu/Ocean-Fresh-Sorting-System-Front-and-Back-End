using Microsoft.Extensions.DependencyInjection;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOceanFreshApplication(this IServiceCollection services)
    {
        services.AddSingleton<SeafoodProductService>();
        services.AddSingleton<ChannelConfigService>();
        services.AddSingleton<ModelManagementService>();
        services.AddSingleton<RuntimeDashboardService>();
        services.AddSingleton<RuntimeDataSourceService>();
        services.AddSingleton<ProductionStatisticsService>();
        services.AddSingleton<ManualReviewService>();
        services.AddSingleton<StreamInspectionRecordService>();
        services.AddSingleton<ManualInferenceService>();
        services.AddSingleton<HardwareDeviceService>();
        services.AddSingleton<AlarmService>();
        services.AddSingleton<IHardwareInterlockService, HardwareInterlockService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<LocalSessionManager>();
        services.AddSingleton<OperationAuditService>();
        services.AddSingleton<SoftwareVersionService>();
        services.AddSingleton<ISoftwareVersionProvider>(provider => provider.GetRequiredService<SoftwareVersionService>());
        services.AddSingleton<ILocator, PixelToEjectLocator>();
        services.AddSingleton<IModelValidator, FileSystemModelValidator>();
        services.AddSingleton<IRuntimeCoordinator, RuntimeCoordinator>();
        return services;
    }
}
