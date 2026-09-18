using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/statistics")]
public sealed class StatisticsController(
    ProductionStatisticsService statisticsService,
    ManualReviewService manualReviewService,
    OperationAuditService auditService) : ControllerBase
{
    [HttpGet("production")]
    public Task<ProductionStatisticsDto> GetProduction(
        [FromQuery] string range = "1d",
        CancellationToken cancellationToken = default) =>
        statisticsService.GetProductionAsync(range, cancellationToken);

    [HttpGet("sessions/{sessionId:guid}/manual-review")]
    public async Task<ActionResult<ManualReviewSessionDto>> GetSessionManualReview(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await manualReviewService.GetSessionReviewAsync(sessionId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("sessions/{sessionId:guid}/manual-review/preview")]
    public async Task<ActionResult<ManualReviewPreviewDto>> GetManualReviewPreview(
        Guid sessionId,
        [FromQuery] Guid inspectionRecordId,
        [FromQuery] Guid detectionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await manualReviewService.GetPreviewAsync(sessionId, inspectionRecordId, detectionId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{sessionId:guid}/manual-review")]
    [Authorize]
    public async Task<ActionResult<ManualReviewSessionDto>> UpsertSessionManualReview(
        Guid sessionId,
        [FromBody] UpsertManualReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var before = await manualReviewService.GetSessionReviewAsync(sessionId, cancellationToken);
            var actor = User.ToAuditActor();
            var after = await manualReviewService.UpsertReviewAsync(
                sessionId,
                request with { Reviewer = actor.UserName },
                cancellationToken);

            await auditService.RecordReviewCompletionAsync(actor, before, after, cancellationToken);

            return after;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
