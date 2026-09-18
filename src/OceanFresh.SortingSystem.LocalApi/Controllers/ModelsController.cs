using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/models")]
public sealed class ModelsController(
    ModelManagementService modelManagementService,
    ISeafoodCategoryRepository categoryRepository,
    OperationAuditService auditService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ModelVersion>> GetAll(CancellationToken cancellationToken) =>
        modelManagementService.GetAllAsync(cancellationToken);

    [HttpGet("category/{categoryId:guid}")]
    public Task<IReadOnlyList<ModelVersion>> GetByCategory(Guid categoryId, CancellationToken cancellationToken) =>
        modelManagementService.GetByCategoryAsync(categoryId, cancellationToken);

    [HttpPost("import")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<ActionResult<ModelImportResult>> Import([FromBody] ImportModelRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await modelManagementService.ImportAsync(request, cancellationToken);
            var category = await categoryRepository.GetByIdAsync(result.ModelVersion.SeafoodCategoryId, cancellationToken);
            await auditService.RecordAsync(
                User.ToAuditActor(),
                new OperationAuditWriteRequest(
                    OperationAuditCategory.Management,
                    "Management.ModelImported",
                    $"导入模型：{result.ModelVersion.Version}",
                    "ModelVersion",
                    result.ModelVersion.Id.ToString(),
                    result.ModelVersion.Version,
                    ManagementAuditChanges.Model(null, result.ModelVersion, null, category?.Name ?? "未知类别")),
                cancellationToken);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<ActionResult<ModelVersion>> Save([FromBody] UpsertModelRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var before = request.Id is { } id
                ? (await modelManagementService.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id)
                : null;
            var saved = await modelManagementService.SaveAsync(request, cancellationToken);
            var beforeCategory = before is null
                ? null
                : (await categoryRepository.GetByIdAsync(before.SeafoodCategoryId, cancellationToken))?.Name;
            var afterCategory = (await categoryRepository.GetByIdAsync(saved.SeafoodCategoryId, cancellationToken))?.Name
                                ?? "未知类别";
            var changes = ManagementAuditChanges.Model(before, saved, beforeCategory, afterCategory);
            if (before is null || changes.Count > 0)
            {
                await auditService.RecordAsync(
                    User.ToAuditActor(),
                    new OperationAuditWriteRequest(
                        OperationAuditCategory.Management,
                        before is null ? "Management.ModelImported" : "Management.ModelUpdated",
                        $"{(before is null ? "导入" : "修改")}模型：{saved.Version}",
                        "ModelVersion",
                        saved.Id.ToString(),
                        saved.Version,
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

    [HttpDelete("{modelId:guid}")]
    [Authorize(Roles = nameof(UserRole.Administrator))]
    public async Task<IActionResult> Delete(Guid modelId, CancellationToken cancellationToken)
    {
        try
        {
            var before = (await modelManagementService.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == modelId);
            var category = before is null
                ? null
                : await categoryRepository.GetByIdAsync(before.SeafoodCategoryId, cancellationToken);
            await modelManagementService.DeleteAsync(modelId, cancellationToken);
            if (before is not null)
            {
                await auditService.RecordAsync(
                    User.ToAuditActor(),
                    new OperationAuditWriteRequest(
                        OperationAuditCategory.Management,
                        "Management.ModelDeleted",
                        $"删除模型：{before.Version}",
                        "ModelVersion",
                        before.Id.ToString(),
                        before.Version,
                        ManagementAuditChanges.AsDeletionSnapshot(
                            ManagementAuditChanges.Model(null, before, null, category?.Name ?? "未知类别"))),
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
