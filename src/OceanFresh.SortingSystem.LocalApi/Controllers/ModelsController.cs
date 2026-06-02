using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/models")]
public sealed class ModelsController(ModelManagementService modelManagementService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ModelVersion>> GetAll(CancellationToken cancellationToken) =>
        modelManagementService.GetAllAsync(cancellationToken);

    [HttpGet("category/{categoryId:guid}")]
    public Task<IReadOnlyList<ModelVersion>> GetByCategory(Guid categoryId, CancellationToken cancellationToken) =>
        modelManagementService.GetByCategoryAsync(categoryId, cancellationToken);

    [HttpGet("recipe/{recipeId:guid}/bindings")]
    public Task<IReadOnlyList<RecipeModelBinding>> GetBindings(Guid recipeId, CancellationToken cancellationToken) =>
        modelManagementService.GetBindingsAsync(recipeId, cancellationToken);

    [HttpPost("import")]
    public Task<ModelImportResult> Import([FromBody] ImportModelRequest request, CancellationToken cancellationToken) =>
        modelManagementService.ImportAsync(request, cancellationToken);

    [HttpPost("recipe/{recipeId:guid}/activate/{modelVersionId:guid}")]
    public async Task<IActionResult> Activate(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken)
    {
        await modelManagementService.ActivateAsync(recipeId, modelVersionId, cancellationToken);
        return Accepted();
    }

    [HttpPost("recipe/{recipeId:guid}/rollback/{modelVersionId:guid}")]
    public async Task<IActionResult> Rollback(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken)
    {
        await modelManagementService.RollbackAsync(recipeId, modelVersionId, cancellationToken);
        return Accepted();
    }
}
