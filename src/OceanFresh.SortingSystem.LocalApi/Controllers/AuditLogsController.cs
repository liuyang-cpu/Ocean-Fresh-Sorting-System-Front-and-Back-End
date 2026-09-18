using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public sealed class AuditLogsController(OperationAuditService auditService) : ControllerBase
{
    [HttpGet]
    public Task<OperationAuditPageDto> Get(
        [FromQuery] string range = "today",
        [FromQuery] string operationType = "all",
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default) =>
        auditService.QueryAsync(
            User.ToAuditActor(),
            range,
            operationType,
            search,
            page,
            pageSize,
            cancellationToken);
}
