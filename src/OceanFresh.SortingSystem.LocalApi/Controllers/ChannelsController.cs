using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/channels")]
public sealed class ChannelsController(
    ChannelConfigService channelService,
    ISeafoodProductRepository productRepository,
    IModelRegistryRepository modelRepository,
    OperationAuditService auditService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ChannelConfigDetailDto>> GetAll(CancellationToken cancellationToken) =>
        channelService.GetAllAsync(cancellationToken);

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<ActionResult<ChannelConfig>> Upsert(
        [FromBody] UpsertChannelConfigRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var before = request.Id is { } id ? await channelService.GetByIdAsync(id, cancellationToken) : null;
            var channelsBefore = await channelService.GetAllAsync(cancellationToken);
            var products = await productRepository.GetAllAsync(cancellationToken);
            var models = await modelRepository.GetAllAsync(cancellationToken);
            var saved = await channelService.UpsertAsync(request, cancellationToken);
            var changes = ManagementAuditChanges.Channel(
                before,
                saved,
                before is null ? null : ManagementAuditChanges.ProductName(before.SeafoodProductId, products),
                ManagementAuditChanges.ProductName(saved.SeafoodProductId, products),
                before is null ? null : ManagementAuditChanges.ModelName(before.ModelVersionId, models),
                ManagementAuditChanges.ModelName(saved.ModelVersionId, models));
            var relatedChanges = saved.IsEnabled
                ? channelsBefore
                    .Where(x => x.Channel.Id != saved.Id && x.Channel.IsEnabled)
                    .Select(x => $"自动停用通道：{x.Channel.Name}")
                    .ToArray()
                : [];
            if (before is null || changes.Count > 0 || relatedChanges.Length > 0)
            {
                await auditService.RecordAsync(
                    User.ToAuditActor(),
                    new OperationAuditWriteRequest(
                        OperationAuditCategory.Management,
                        before is null ? "Management.ChannelCreated" : "Management.ChannelUpdated",
                        $"{(before is null ? "新增" : "修改")}通道：{saved.Name}",
                        "ChannelConfig",
                        saved.Id.ToString(),
                        saved.Name,
                        changes,
                        relatedChanges),
                    cancellationToken);
            }

            return saved;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{channelId:guid}")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<IActionResult> Delete(Guid channelId, CancellationToken cancellationToken)
    {
        try
        {
            var before = await channelService.GetByIdAsync(channelId, cancellationToken);
            var products = await productRepository.GetAllAsync(cancellationToken);
            var models = await modelRepository.GetAllAsync(cancellationToken);
            await channelService.DeleteAsync(channelId, cancellationToken);
            if (before is not null)
            {
                await auditService.RecordAsync(
                    User.ToAuditActor(),
                    new OperationAuditWriteRequest(
                        OperationAuditCategory.Management,
                        "Management.ChannelDeleted",
                        $"删除通道：{before.Name}",
                        "ChannelConfig",
                        before.Id.ToString(),
                        before.Name,
                        ManagementAuditChanges.AsDeletionSnapshot(
                            ManagementAuditChanges.Channel(
                                null,
                                before,
                                null,
                                ManagementAuditChanges.ProductName(before.SeafoodProductId, products),
                                null,
                                ManagementAuditChanges.ModelName(before.ModelVersionId, models)))),
                    cancellationToken);
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
