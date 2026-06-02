using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/recipes")]
public sealed class RecipesController(RecipeService recipeService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ProductRecipe>> GetAll(CancellationToken cancellationToken) =>
        recipeService.GetAllAsync(cancellationToken);

    [HttpPost]
    public Task<OceanFresh.SortingSystem.Domain.ProductRecipe> Upsert(
        [FromBody] UpsertRecipeRequest request,
        CancellationToken cancellationToken) =>
        recipeService.UpsertAsync(request, cancellationToken);
}
