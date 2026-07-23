using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/alarms")]
public sealed class AlarmsController(AlarmService alarmService) : ControllerBase
{
    [HttpGet("active")]
    public Task<IReadOnlyList<AlarmEvent>> GetActive(CancellationToken cancellationToken) =>
        alarmService.GetActiveAsync(cancellationToken);

    [HttpPost("{alarmId:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid alarmId, CancellationToken cancellationToken)
    {
        await alarmService.AcknowledgeAsync(alarmId, cancellationToken);
        return Accepted();
    }
}
