using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Infrastructure;
using OceanFresh.SortingSystem.Runtime;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOceanFreshApplication();
builder.Services.AddOceanFreshInfrastructure();
builder.Services.AddHostedService<RealtimeRuntimeWorker>();

var host = builder.Build();
await host.Services.InitializeOceanFreshInfrastructureAsync();
host.Run();
