using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController(
    HardwareDeviceService hardwareDeviceService,
    IHardwareInterlockService hardwareInterlockService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<HardwareDevice>> GetAll(CancellationToken cancellationToken) =>
        hardwareDeviceService.GetAllAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<HardwareDevice>> Upsert(
        [FromBody] UpsertHardwareDeviceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await hardwareDeviceService.UpsertAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{deviceId:guid}/self-check")]
    public async Task<ActionResult<DeviceSelfCheckResultDto>> RunSelfCheck(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await hardwareDeviceService.RunSelfCheckAsync(deviceId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("interlock")]
    public async Task<HardwareInterlockDto> GetInterlock(CancellationToken cancellationToken)
    {
        var result = await hardwareInterlockService.EvaluateAsync(cancellationToken);
        return new HardwareInterlockDto(
            result.CanRun,
            result.RecommendedRuntimeMode,
            result.RecommendedDeviceState,
            result.Summary,
            result.Devices,
            result.CriticalAlarms);
    }

    [HttpDelete("{deviceId:guid}")]
    public async Task<IActionResult> Delete(Guid deviceId, CancellationToken cancellationToken)
    {
        await hardwareDeviceService.DeleteAsync(deviceId, cancellationToken);
        return NoContent();
    }
}
