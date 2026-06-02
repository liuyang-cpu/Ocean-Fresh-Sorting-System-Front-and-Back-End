using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/records")]
public sealed class RecordsController(IInspectionRecordRepository inspectionRecordRepository) : ControllerBase
{
    [HttpGet("recent")]
    public Task<IReadOnlyList<InspectionRecord>> GetRecent([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        inspectionRecordRepository.GetRecentAsync(take, cancellationToken);
}
