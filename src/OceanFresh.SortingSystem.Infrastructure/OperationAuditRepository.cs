using Microsoft.Data.Sqlite;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class SqliteOperationAuditRepository(SqliteConnectionFactory connectionFactory) : IOperationAuditRepository
{
    public async Task<bool> TryAddAsync(OperationAuditLog operation, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO operation_audit_logs (
                id, category, action_code, summary, actor_user_name, actor_display_name,
                actor_role, target_type, target_id, target_name, details_json, dedupe_key, occurred_at
            ) VALUES (
                $id, $category, $actionCode, $summary, $actorUserName, $actorDisplayName,
                $actorRole, $targetType, $targetId, $targetName, $detailsJson, $dedupeKey, $occurredAt
            );
            """;
        AddParameters(command, operation);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<OperationAuditPage> QueryAsync(OperationAuditQuery query, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var where = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        if (query.From is { } from)
        {
            where.Add("occurred_at >= $from");
            parameters.Add(("$from", from.ToString("O")));
        }

        if (query.To is { } to)
        {
            where.Add("occurred_at < $to");
            parameters.Add(("$to", to.ToString("O")));
        }

        if (query.Category is { } category)
        {
            where.Add("category = $category");
            parameters.Add(("$category", (int)category));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where.Add("(summary LIKE $search OR target_name LIKE $search OR actor_user_name LIKE $search)");
            parameters.Add(("$search", $"%{query.Search.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.ActorUserName))
        {
            where.Add("actor_user_name = $actorUserName");
            parameters.Add(("$actorUserName", query.ActorUserName.Trim()));
        }

        var whereSql = where.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", where)}";
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM operation_audit_logs {whereSql};";
        AddQueryParameters(countCommand, parameters);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 5000);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, category, action_code, summary, actor_user_name, actor_display_name,
                   actor_role, target_type, target_id, target_name, details_json, dedupe_key, occurred_at
            FROM operation_audit_logs
            {whereSql}
            ORDER BY occurred_at DESC, id DESC
            LIMIT $take OFFSET $skip;
            """;
        AddQueryParameters(command, parameters);
        command.Parameters.AddWithValue("$take", pageSize);
        command.Parameters.AddWithValue("$skip", (page - 1) * pageSize);

        var items = new List<OperationAuditLog>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OperationAuditLog(
                Guid.Parse(reader.GetString(0)),
                (OperationAuditCategory)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                (UserRole)reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                DateTimeOffset.Parse(reader.GetString(12))));
        }

        return new OperationAuditPage(items, totalCount);
    }

    private static void AddParameters(SqliteCommand command, OperationAuditLog operation)
    {
        command.Parameters.AddWithValue("$id", operation.Id.ToString());
        command.Parameters.AddWithValue("$category", (int)operation.Category);
        command.Parameters.AddWithValue("$actionCode", operation.ActionCode);
        command.Parameters.AddWithValue("$summary", operation.Summary);
        command.Parameters.AddWithValue("$actorUserName", operation.ActorUserName);
        command.Parameters.AddWithValue("$actorDisplayName", operation.ActorDisplayName);
        command.Parameters.AddWithValue("$actorRole", (int)operation.ActorRole);
        command.Parameters.AddWithValue("$targetType", operation.TargetType);
        command.Parameters.AddWithValue("$targetId", operation.TargetId);
        command.Parameters.AddWithValue("$targetName", operation.TargetName);
        command.Parameters.AddWithValue("$detailsJson", operation.DetailsJson);
        command.Parameters.AddWithValue("$dedupeKey", operation.DedupeKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$occurredAt", operation.OccurredAt.ToString("O"));
    }

    private static void AddQueryParameters(SqliteCommand command, IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
    }
}
