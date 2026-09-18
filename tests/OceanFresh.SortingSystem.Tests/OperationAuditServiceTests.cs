using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.HMI.Pages;

namespace OceanFresh.SortingSystem.Tests;

public sealed class OperationAuditServiceTests
{
    [Fact]
    public async Task RecordAsync_PreservesFieldChanges_AndDeduplicatesCompletion()
    {
        var repository = new InMemoryOperationAuditRepository();
        var service = new OperationAuditService(repository);
        var actor = new OperationAuditActorDto("operator", "操作员", UserRole.Operator);
        var request = new OperationAuditWriteRequest(
            OperationAuditCategory.Review,
            "Review.TaskCompleted",
            "完成检测任务复核：DS-001",
            "DetectionSession",
            "session-1",
            "DS-001",
            [new OperationAuditChangeDto("ReviewProgress", "复核进度", "2/3", "3/3")],
            ["确认异常 2 个"],
            "Review.TaskCompleted:session-1");

        var first = await service.RecordAsync(actor, request, CancellationToken.None);
        var second = await service.RecordAsync(actor, request, CancellationToken.None);
        var result = await service.QueryAsync(actor, "all", "复核", null, 1, 10, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        var row = Assert.Single(result.Items);
        Assert.Equal("复核进度", Assert.Single(row.Changes).DisplayName);
        Assert.Equal("3/3", row.Changes[0].NewValue);
        Assert.Equal("确认异常 2 个", Assert.Single(row.RelatedChanges));
    }

    [Fact]
    public async Task QueryAsync_RestrictsOperatorToOwnRecords_ButAdminSeesAll()
    {
        var repository = new InMemoryOperationAuditRepository();
        var service = new OperationAuditService(repository);
        var operatorActor = new OperationAuditActorDto("operator", "操作员", UserRole.Operator);
        var adminActor = new OperationAuditActorDto("admin", "管理员", UserRole.Administrator);
        var request = new OperationAuditWriteRequest(
            OperationAuditCategory.Management,
            "Management.ChannelUpdated",
            "修改通道：CH-01",
            "ChannelConfig",
            "channel-1",
            "CH-01",
            []);

        await service.RecordAsync(operatorActor, request, CancellationToken.None);
        await service.RecordAsync(adminActor, request with { TargetId = "channel-2", TargetName = "CH-02" }, CancellationToken.None);

        var operatorResult = await service.QueryAsync(operatorActor, "all", "all", null, 1, 10, CancellationToken.None);
        var adminResult = await service.QueryAsync(adminActor, "all", "all", null, 1, 10, CancellationToken.None);

        Assert.Single(operatorResult.Items);
        Assert.Equal("operator", operatorResult.Items[0].OperatorName);
        Assert.Equal(2, adminResult.TotalCount);
    }

    [Fact]
    public async Task RecordReviewCompletionAsync_WritesOnlyIncompleteToCompleteTransition()
    {
        var repository = new InMemoryOperationAuditRepository();
        var service = new OperationAuditService(repository);
        var actor = new OperationAuditActorDto("operator", "操作员", UserRole.Operator);
        var sessionId = Guid.NewGuid();
        var before = ReviewSession(sessionId, reviewed: 2, complete: false);
        var completed = ReviewSession(sessionId, reviewed: 3, complete: true);

        var first = await service.RecordReviewCompletionAsync(actor, before, completed, CancellationToken.None);
        var duplicate = await service.RecordReviewCompletionAsync(actor, before, completed, CancellationToken.None);
        var alreadyComplete = await service.RecordReviewCompletionAsync(actor, completed, completed, CancellationToken.None);

        Assert.True(first);
        Assert.False(duplicate);
        Assert.False(alreadyComplete);
        var result = await service.QueryAsync(actor, "all", "复核", null, 1, 10, CancellationToken.None);
        var record = Assert.Single(result.Items);
        Assert.Equal("Review.TaskCompleted", record.ActionCode);
        Assert.Empty(record.Changes);
        Assert.Contains("复核进度：3/3（已完成）", record.RelatedChanges);
        Assert.Contains("确认异常 2 个", record.RelatedChanges);
        Assert.Contains("正常误检 1 个", record.RelatedChanges);
        Assert.Contains("类别修正 0 个", record.RelatedChanges);
    }

    [Fact]
    public void AuditRow_UsesBusinessSummaryForDetectionAndReview()
    {
        var detection = new UserAuditRecordRowViewModel(AuditRecord(
            "执行检测",
            "Detection.TaskCompleted",
            [new OperationAuditChangeDto("SessionTime", "检测时间", "10:00:00", "10:05:00")],
            ["检测结果：总计 20 个，正常 18 个，异常 2 个，良率 90%"]));
        var review = new UserAuditRecordRowViewModel(AuditRecord(
            "复核",
            "Review.TaskCompleted",
            [new OperationAuditChangeDto("ReviewProgress", "复核进度", "1/2", "2/2")],
            ["确认异常 1 个"]));

        Assert.Equal("检测摘要", detection.BusinessSummaryTitle);
        Assert.Contains("检测时段：10:00:00 至 10:05:00", detection.BusinessSummaryItems);
        Assert.False(detection.HasManagementChanges);
        Assert.Equal("复核摘要", review.BusinessSummaryTitle);
        Assert.Contains("复核进度：2/2（已完成）", review.BusinessSummaryItems);
        Assert.False(review.HasManagementChanges);
    }

    [Fact]
    public void AuditRow_KeepsFieldComparisonForManagement()
    {
        var management = new UserAuditRecordRowViewModel(AuditRecord(
            "管理",
            "Management.ChannelUpdated",
            [new OperationAuditChangeDto("Name", "通道名称", "旧通道", "新通道")],
            ["自动停用通道：旧通道"]));

        Assert.True(management.HasManagementChanges);
        Assert.False(management.HasBusinessSummary);
        Assert.Contains("自动停用通道：旧通道", management.ManagementRelatedChanges);
    }

    private static OperationAuditRecordDto AuditRecord(
        string operationType,
        string actionCode,
        IReadOnlyList<OperationAuditChangeDto> changes,
        IReadOnlyList<string> relatedChanges) =>
        new(
            Guid.NewGuid(),
            operationType,
            actionCode,
            "测试操作",
            "operator",
            "操作员",
            "DetectionSession",
            "target-1",
            "DS-TEST-001",
            DateTimeOffset.Now,
            changes,
            relatedChanges);

    private static ManualReviewSessionDto ReviewSession(Guid sessionId, int reviewed, bool complete) =>
        new(
            sessionId,
            "DS-TEST-001",
            new ManualReviewSummaryDto(
                3,
                reviewed,
                2,
                1,
                0,
                66.67m,
                33.33m,
                complete,
                string.Empty),
            []);

    private sealed class InMemoryOperationAuditRepository : IOperationAuditRepository
    {
        private readonly List<OperationAuditLog> _items = [];

        public Task<bool> TryAddAsync(OperationAuditLog operation, CancellationToken cancellationToken)
        {
            if (operation.DedupeKey is not null && _items.Any(x => x.DedupeKey == operation.DedupeKey))
            {
                return Task.FromResult(false);
            }

            _items.Add(operation);
            return Task.FromResult(true);
        }

        public Task<OperationAuditPage> QueryAsync(OperationAuditQuery query, CancellationToken cancellationToken)
        {
            var filtered = _items
                .Where(x => query.From is null || x.OccurredAt >= query.From)
                .Where(x => query.To is null || x.OccurredAt < query.To)
                .Where(x => query.Category is null || x.Category == query.Category)
                .Where(x => string.IsNullOrWhiteSpace(query.ActorUserName) || x.ActorUserName == query.ActorUserName)
                .Where(x => string.IsNullOrWhiteSpace(query.Search) || x.Summary.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.OccurredAt)
                .ToArray();
            var items = filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
            return Task.FromResult(new OperationAuditPage(items, filtered.Length));
        }
    }
}
