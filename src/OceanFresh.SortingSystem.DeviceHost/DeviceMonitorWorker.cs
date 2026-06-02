using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.DeviceHost;

public sealed class DeviceMonitorWorker(
    IDeviceHealthProvider deviceHealthProvider,
    ILogger<DeviceMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var devices = await deviceHealthProvider.GetStatusesAsync(stoppingToken);
            foreach (var device in devices)
            {
                logger.LogInformation("Device {DeviceKey} is {State} - {Message}", device.DeviceKey, device.State, device.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
