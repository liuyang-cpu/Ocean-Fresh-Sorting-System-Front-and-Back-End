using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(SoftwareVersionService softwareVersionService) : ControllerBase
{
    [HttpGet("software-version")]
    public async Task<SoftwareVersionDto> GetSoftwareVersion(CancellationToken cancellationToken)
    {
        var info = await softwareVersionService.GetCurrentAsync(cancellationToken);
        return new SoftwareVersionDto(
            info.ProductName,
            info.CurrentVersion,
            info.BuildTime,
            info.DatabaseVersion,
            info.YoloServiceVersion,
            info.RuntimeEnvironment,
            info.UpdatePolicy,
            info.RecentUpdates);
    }

    [HttpPost("software-update/validate")]
    public async Task<ActionResult<SoftwareUpdateRecord>> ValidateUpdatePackage(
        [FromBody] SoftwareUpdatePackageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await softwareVersionService.ValidateUpdatePackageAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
