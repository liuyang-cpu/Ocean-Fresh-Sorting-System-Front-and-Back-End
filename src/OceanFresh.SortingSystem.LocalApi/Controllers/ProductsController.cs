using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(SeafoodProductService productService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SeafoodProductProfileDto>> GetAll(CancellationToken cancellationToken) =>
        productService.GetAllAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<SeafoodProductProfileDto>> Upsert(
        [FromBody] UpsertSeafoodProductRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await productService.UpsertAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{productId:guid}")]
    public async Task<IActionResult> Delete(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            await productService.DeleteAsync(productId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
