using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ISeafoodCategoryRepository categoryRepository) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SeafoodCategory>> GetAll(CancellationToken cancellationToken) =>
        categoryRepository.GetAllAsync(cancellationToken);
}
