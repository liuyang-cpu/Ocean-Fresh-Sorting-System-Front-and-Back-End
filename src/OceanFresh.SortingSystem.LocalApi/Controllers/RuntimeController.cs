using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeController(
    RuntimeDashboardService dashboardService,
    IRuntimeCoordinator runtimeCoordinator) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> GetDashboard(CancellationToken cancellationToken) =>
        dashboardService.GetDashboardAsync(cancellationToken);

    [HttpGet("snapshot")]
    public RuntimeSnapshot GetSnapshot() => runtimeCoordinator.GetSnapshot();

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        await runtimeCoordinator.StartAsync(cancellationToken);
        return Accepted();
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken cancellationToken)
    {
        await runtimeCoordinator.StopAsync(cancellationToken);
        return Accepted();
    }
}
