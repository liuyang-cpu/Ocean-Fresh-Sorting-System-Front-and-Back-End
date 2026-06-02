using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/channels")]
public sealed class ChannelsController(ChannelConfigService channelService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ChannelConfigDetailDto>> GetAll(CancellationToken cancellationToken) =>
        channelService.GetAllAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<ChannelConfig>> Upsert(
        [FromBody] UpsertChannelConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await channelService.UpsertAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{channelId:guid}")]
    public async Task<IActionResult> Delete(Guid channelId, CancellationToken cancellationToken)
    {
        try
        {
            await channelService.DeleteAsync(channelId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
