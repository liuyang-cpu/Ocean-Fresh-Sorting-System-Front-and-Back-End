using Microsoft.AspNetCore.Mvc;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.LocalApi;

namespace OceanFresh.SortingSystem.LocalApi.Controllers;

[ApiController]
[Route("api/runtime")]
public sealed class RuntimeController(
    RuntimeDashboardService dashboardService,
    RuntimeDataSourceService dataSourceService,
    ChannelConfigService channelService,
    IRuntimeCoordinator runtimeCoordinator,
    IRuntimeDataSourceStore dataSourceStore,
    ManualInferenceService manualInferenceService,
    StreamInspectionRecordService streamInspectionRecordService,
    OperationAuditService auditService) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> GetDashboard(CancellationToken cancellationToken) =>
        dashboardService.GetDashboardAsync(cancellationToken);

    [HttpGet("snapshot")]
    public RuntimeSnapshot GetSnapshot() => runtimeCoordinator.GetSnapshot();

    [HttpGet("data-source")]
    public ActionResult<RuntimeDataSourceConfigDto> GetDataSource() => dataSourceService.Get();

    [HttpPost("data-source")]
    public ActionResult<RuntimeDataSourceConfigDto> UpdateDataSource([FromBody] UpdateRuntimeDataSourceRequest request)
    {
        try
        {
            return dataSourceService.Update(request);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        try
        {
            await runtimeCoordinator.StartAsync(cancellationToken);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("machine/start")]
    public async Task<IActionResult> StartMachine(
        [FromQuery] bool externalLocalStream,
        CancellationToken cancellationToken)
    {
        try
        {
            if (externalLocalStream)
            {
                dataSourceStore.SetExternalLocalStreamActive(true);
            }

            await runtimeCoordinator.StartAsync(cancellationToken);
            return Accepted();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("channel/{channelId:guid}/select")]
    public async Task<ActionResult<ChannelConfig>> SelectChannel(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await channelService.SelectActiveAsync(channelId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken cancellationToken)
    {
        var before = await dashboardService.GetDashboardAsync(cancellationToken);
        await runtimeCoordinator.StopAsync(cancellationToken);
        var after = await dashboardService.GetDashboardAsync(cancellationToken);
        await RecordDetectionTaskCompletedAsync(before, after, cancellationToken);
        return Accepted();
    }

    [HttpPost("machine/stop")]
    public async Task<IActionResult> StopMachine(CancellationToken cancellationToken)
    {
        var before = await dashboardService.GetDashboardAsync(cancellationToken);
        await runtimeCoordinator.StopAsync(cancellationToken);
        dataSourceStore.SetExternalLocalStreamActive(false);
        var after = await dashboardService.GetDashboardAsync(cancellationToken);
        await RecordDetectionTaskCompletedAsync(before, after, cancellationToken);
        return Accepted();
    }

    [HttpPost("machine/reset")]
    public async Task<IActionResult> ResetMachine(CancellationToken cancellationToken)
    {
        try
        {
            await runtimeCoordinator.ResetAsync(cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("detection/start")]
    public async Task<ActionResult<DetectionSession>> StartDetection(CancellationToken cancellationToken)
    {
        try
        {
            return await runtimeCoordinator.StartDetectionAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("detection/stop")]
    public async Task<ActionResult<DetectionSession?>> StopDetection(CancellationToken cancellationToken) =>
        await runtimeCoordinator.StopDetectionAsync(cancellationToken);

    [HttpPost("manual-infer")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<ManualInferenceResultDto>> ManualInfer(
        IFormFile image,
        CancellationToken cancellationToken)
    {
        if (image is null || image.Length == 0)
        {
            return BadRequest("请先选择一张 X 光图片。");
        }

        await using var stream = image.OpenReadStream();
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        try
        {
            var result = await manualInferenceService.RunAsync(
                image.FileName,
                memory.ToArray(),
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("stream-record")]
    public async Task<ActionResult<InspectionRecord>> AddStreamRecord(
        [FromBody] StreamInspectionRecordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await streamInspectionRecordService.AddAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task RecordDetectionTaskCompletedAsync(
        DashboardDto before,
        DashboardDto after,
        CancellationToken cancellationToken)
    {
        var session = before.CurrentSession;
        if (session is null || before.Snapshot.RuntimeMode != RuntimeMode.Running)
        {
            return;
        }

        var stoppedAt = DateTimeOffset.Now;
        var dataSource = after.DataSource;
        var relatedChanges = new List<string>
        {
            $"检测时段：{session.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} 至 {stoppedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}",
            $"检测结果：总计 {after.Snapshot.TotalInspected} 个，正常 {Math.Max(0, after.Snapshot.TotalInspected - after.Snapshot.TotalRejected)} 个，异常 {after.Snapshot.TotalRejected} 个，良率 {after.Snapshot.YieldRate:0.##}%",
            $"图像源：{dataSource.ModeText}，进度 {dataSource.CurrentIndex}/{dataSource.ImageCount}",
            $"通道：{after.Summary.CurrentChannel}",
            $"产品：{after.Summary.CurrentProduct}",
            $"模型：{after.Summary.CurrentModel}",
            $"图像源状态：{dataSource.Status}"
        };

        await auditService.RecordAsync(
            User.ToAuditActor(),
            new OperationAuditWriteRequest(
                OperationAuditCategory.Detection,
                "Detection.TaskCompleted",
                $"完成检测任务：{session.SessionCode}",
                "DetectionSession",
                session.Id.ToString(),
                session.SessionCode,
                [],
                relatedChanges,
                $"Detection.TaskCompleted:{session.Id:N}"),
            cancellationToken);
    }
}
