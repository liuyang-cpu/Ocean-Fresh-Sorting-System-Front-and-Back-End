using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.DeviceHost;

public sealed class DeviceMonitorWorker(
    IHardwareInterlockService hardwareInterlockService,
    IRuntimeStateStore runtimeStateStore,
    ILogger<DeviceMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interlock = await hardwareInterlockService.EvaluateAsync(stoppingToken);
            foreach (var device in interlock.Devices)
            {
                logger.LogInformation("Device {DeviceKey} is {State} - {Message}", device.DeviceKey, device.State, device.Message);
            }

            var snapshot = runtimeStateStore.GetSnapshot();
            if (!interlock.CanRun)
            {
                runtimeStateStore.Update(snapshot with
                {
                    RuntimeMode = RuntimeMode.Faulted,
                    DeviceState = DeviceState.Faulted,
                    Devices = interlock.Devices,
                    ActiveAlarms = interlock.CriticalAlarms,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    MachineStartedAt = null
                });
                logger.LogCritical("Hardware interlock blocked runtime: {Summary}", interlock.Summary);
            }
            else
            {
                runtimeStateStore.Update(snapshot with
                {
                    Devices = interlock.Devices,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
