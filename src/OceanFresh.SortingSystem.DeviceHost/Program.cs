using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.DeviceHost;
using OceanFresh.SortingSystem.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOceanFreshApplication();
builder.Services.AddOceanFreshInfrastructure();
builder.Services.AddHostedService<DeviceMonitorWorker>();

var host = builder.Build();
await host.Services.InitializeOceanFreshInfrastructureAsync();
host.Run();
