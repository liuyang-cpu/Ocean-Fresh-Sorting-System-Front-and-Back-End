using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    SeafoodProductService productService,
    OperationAuditService auditService) : ControllerBase
{
    [HttpGet("predict-config-template")]
    public async Task<IActionResult> GetDefaultPredictTemplate(CancellationToken cancellationToken)
    {
        var content = await productService.GetDefaultPredictTemplateContentAsync(cancellationToken);
        return Content(content, "application/json; charset=utf-8");
    }

    [HttpGet]
    public Task<IReadOnlyList<SeafoodProductProfileDto>> GetAll(CancellationToken cancellationToken) =>
        productService.GetAllAsync(cancellationToken);

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<ActionResult<SeafoodProductProfileDto>> Upsert(
        [FromBody] UpsertSeafoodProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var before = request.Id is { } id
                ? (await productService.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Product.Id == id)
                : null;
            var saved = await productService.UpsertAsync(request, cancellationToken);
            var changes = ManagementAuditChanges.Product(before, saved);
            if (before is null || changes.Count > 0)
            {
                await auditService.RecordAsync(
                    User.ToAuditActor(),
                    new OperationAuditWriteRequest(
                        OperationAuditCategory.Management,
                        before is null ? "Management.ProductCreated" : "Management.ProductUpdated",
                        $"{(before is null ? "新增" : "修改")}海鲜产品：{saved.Product.Name}",
                        "SeafoodProduct",
                        saved.Product.Id.ToString(),
                        saved.Product.Name,
                        changes),
                    cancellationToken);
            }

            return saved;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{productId:guid}")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<IActionResult> Delete(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            var before = (await productService.GetAllAsync(cancellationToken))
                .FirstOrDefault(x => x.Product.Id == productId);
            await productService.DeleteAsync(productId, cancellationToken);
            if (before is not null)
            {
                await auditService.RecordAsync(
                    User.ToAuditActor(),
                    new OperationAuditWriteRequest(
                        OperationAuditCategory.Management,
                        "Management.ProductDeleted",
                        $"删除海鲜产品：{before.Product.Name}",
                        "SeafoodProduct",
                        before.Product.Id.ToString(),
                        before.Product.Name,
                        ManagementAuditChanges.AsDeletionSnapshot(ManagementAuditChanges.ProductSnapshot(before))),
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
