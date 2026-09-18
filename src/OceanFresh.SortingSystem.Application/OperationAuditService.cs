using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public sealed record LocalUserSession(
    string Token,
    string UserName,
    string DisplayName,
    UserRole Role,
    DateTimeOffset LoginAt,
    DateTimeOffset ExpiresAt);

public sealed class LocalSessionManager
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, LocalUserSession> _sessions = new(StringComparer.Ordinal);

    public LocalUserSession Issue(LoginResultDto login)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new LocalUserSession(
            token,
            login.UserName,
            login.DisplayName,
            login.Role,
            login.LoginAt,
            DateTimeOffset.UtcNow.Add(SessionLifetime));
        _sessions[token] = session;
        return session;
    }

    public bool TryGet(string token, out LocalUserSession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var found))
        {
            return false;
        }

        if (found.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        session = found;
        return true;
    }
}

public sealed class OperationAuditService(IOperationAuditRepository repository)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<bool> RecordAsync(
        OperationAuditActorDto actor,
        OperationAuditWriteRequest request,
        CancellationToken cancellationToken)
    {
        var details = new OperationAuditDetailsDto(request.Changes, request.RelatedChanges ?? []);
        var operation = new OperationAuditLog(
            Guid.NewGuid(),
            request.Category,
            request.ActionCode.Trim(),
            request.Summary.Trim(),
            actor.UserName.Trim(),
            actor.DisplayName.Trim(),
            actor.Role,
            request.TargetType.Trim(),
            request.TargetId.Trim(),
            request.TargetName.Trim(),
            JsonSerializer.Serialize(details, JsonOptions),
            request.DedupeKey,
            DateTimeOffset.Now);
        return repository.TryAddAsync(operation, cancellationToken);
    }

    public Task<bool> RecordReviewCompletionAsync(
        OperationAuditActorDto actor,
        ManualReviewSessionDto before,
        ManualReviewSessionDto after,
        CancellationToken cancellationToken)
    {
        if (before.Summary.IsComplete || !after.Summary.IsComplete)
        {
            return Task.FromResult(false);
        }

        return RecordAsync(
            actor,
            new OperationAuditWriteRequest(
                OperationAuditCategory.Review,
                "Review.TaskCompleted",
                $"完成检测任务复核：{after.SessionCode}",
                "DetectionSession",
                after.SessionId.ToString(),
                after.SessionCode,
                [],
                [
                    $"复核进度：{after.Summary.ReviewedCount}/{after.Summary.CandidateCount}（已完成）",
                    $"确认异常 {after.Summary.ConfirmedAbnormalCount} 个",
                    $"正常误检 {after.Summary.FalsePositiveCount} 个",
                    $"类别修正 {after.Summary.RelabeledAbnormalCount} 个"
                ],
                $"Review.TaskCompleted:{after.SessionId:N}"),
            cancellationToken);
    }

    public async Task<OperationAuditPageDto> QueryAsync(
        OperationAuditActorDto actor,
        string range,
        string operationType,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var from = range switch
        {
            "today" => new DateTimeOffset(now.Date, now.Offset),
            "7d" => new DateTimeOffset(now.Date.AddDays(-6), now.Offset),
            "30d" => new DateTimeOffset(now.Date.AddDays(-29), now.Offset),
            _ => (DateTimeOffset?)null
        };
        DateTimeOffset? to = from is null ? null : new DateTimeOffset(now.Date.AddDays(1), now.Offset);
        var category = ParseCategory(operationType);
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 5000);
        var actorFilter = actor.Role == UserRole.Administrator ? null : actor.UserName;
        var result = await repository.QueryAsync(
            new OperationAuditQuery(from, to, category, search, actorFilter, normalizedPage, normalizedPageSize),
            cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)normalizedPageSize));

        return new OperationAuditPageDto(
            result.Items.Select(Map).ToArray(),
            result.TotalCount,
            normalizedPage,
            normalizedPageSize,
            totalPages);
    }

    private static OperationAuditCategory? ParseCategory(string operationType) => operationType switch
    {
        "登录" or "login" => OperationAuditCategory.Login,
        "执行检测" or "detection" => OperationAuditCategory.Detection,
        "复核" or "review" => OperationAuditCategory.Review,
        "管理" or "management" => OperationAuditCategory.Management,
        _ => null
    };

    private static OperationAuditRecordDto Map(OperationAuditLog operation)
    {
        OperationAuditDetailsDto details;
        try
        {
            details = JsonSerializer.Deserialize<OperationAuditDetailsDto>(operation.DetailsJson, JsonOptions)
                      ?? new OperationAuditDetailsDto([], []);
        }
        catch (JsonException)
        {
            details = new OperationAuditDetailsDto([], []);
        }

        return new OperationAuditRecordDto(
            operation.Id,
            MapCategory(operation.Category),
            operation.ActionCode,
            operation.Summary,
            operation.ActorUserName,
            operation.ActorDisplayName,
            operation.TargetType,
            operation.TargetId,
            operation.TargetName,
            operation.OccurredAt,
            details.Changes,
            details.RelatedChanges);
    }

    private static string MapCategory(OperationAuditCategory category) => category switch
    {
        OperationAuditCategory.Login => "登录",
        OperationAuditCategory.Detection => "执行检测",
        OperationAuditCategory.Review => "复核",
        OperationAuditCategory.Management => "管理",
        _ => "其他"
    };
}
