using Microsoft.Data.Sqlite;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

public sealed class SqliteSeafoodProductRepository(SqliteConnectionFactory connectionFactory) : ISeafoodProductRepository
{
    public async Task<IReadOnlyList<SeafoodProduct>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, is_enabled
            FROM seafood_products
            ORDER BY name;
            """;

        var results = new List<SeafoodProduct>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SeafoodProduct(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1));
        }

        return results;
    }

    public async Task<SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, is_enabled
            FROM seafood_products
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SeafoodProduct(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) == 1);
    }

    public async Task<SeafoodProduct> UpsertAsync(SeafoodProduct product, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var existingById = await FindByIdAsync(connection, product.Id, cancellationToken);
        if (existingById is not null && !string.Equals(existingById.Code, product.Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("产品编码创建后不可修改。");
        }

        var existingByCode = await FindByCodeAsync(connection, product.Code, cancellationToken);
        if (existingByCode is not null && existingByCode.Id != product.Id)
        {
            throw new InvalidOperationException("产品编码已存在，必须全局唯一。");
        }

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO seafood_products (id, code, name, is_enabled)
            VALUES ($id, $code, $name, $enabled)
            ON CONFLICT(id) DO UPDATE SET
                code = excluded.code,
                name = excluded.name,
                is_enabled = excluded.is_enabled;
            """;
        command.Parameters.AddWithValue("$id", product.Id.ToString());
        command.Parameters.AddWithValue("$code", product.Code);
        command.Parameters.AddWithValue("$name", product.Name);
        command.Parameters.AddWithValue("$enabled", product.IsEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return product;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var deleteTraits = connection.CreateCommand();
        deleteTraits.Transaction = transaction;
        deleteTraits.CommandText = "DELETE FROM seafood_traits WHERE seafood_product_id = $id;";
        deleteTraits.Parameters.AddWithValue("$id", id.ToString());
        await deleteTraits.ExecuteNonQueryAsync(cancellationToken);

        var deleteProduct = connection.CreateCommand();
        deleteProduct.Transaction = transaction;
        deleteProduct.CommandText = "DELETE FROM seafood_products WHERE id = $id;";
        deleteProduct.Parameters.AddWithValue("$id", id.ToString());
        await deleteProduct.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<SeafoodProduct?> FindByIdAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, is_enabled
            FROM seafood_products
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SeafoodProduct(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) == 1);
    }

    private static async Task<SeafoodProduct?> FindByCodeAsync(SqliteConnection connection, string code, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, is_enabled
            FROM seafood_products
            WHERE code = $code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$code", code);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SeafoodProduct(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) == 1);
    }
}

public sealed class SqliteSeafoodTraitRepository(SqliteConnectionFactory connectionFactory) : ISeafoodTraitRepository
{
    public async Task<IReadOnlyList<SeafoodTrait>> GetByProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, seafood_product_id, name, is_normal, sort_order, is_enabled
            FROM seafood_traits
            WHERE seafood_product_id = $productId
            ORDER BY sort_order;
            """;
        command.Parameters.AddWithValue("$productId", productId.ToString());

        var results = new List<SeafoodTrait>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SeafoodTrait(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4),
                reader.GetInt32(5) == 1));
        }

        return results;
    }

    public async Task ReplaceForProductAsync(Guid productId, IReadOnlyList<SeafoodTrait> traits, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM seafood_traits WHERE seafood_product_id = $productId;";
        deleteCommand.Parameters.AddWithValue("$productId", productId.ToString());
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var trait in traits)
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO seafood_traits (id, seafood_product_id, name, is_normal, sort_order, is_enabled)
                VALUES ($id, $productId, $name, $isNormal, $sortOrder, $enabled);
                """;
            insertCommand.Parameters.AddWithValue("$id", trait.Id.ToString());
            insertCommand.Parameters.AddWithValue("$productId", trait.SeafoodProductId.ToString());
            insertCommand.Parameters.AddWithValue("$name", trait.Name);
            insertCommand.Parameters.AddWithValue("$isNormal", trait.IsNormal ? 1 : 0);
            insertCommand.Parameters.AddWithValue("$sortOrder", trait.SortOrder);
            insertCommand.Parameters.AddWithValue("$enabled", trait.IsEnabled ? 1 : 0);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class SqliteChannelConfigRepository(SqliteConnectionFactory connectionFactory) : IChannelConfigRepository
{
    public async Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, channel_no, name, seafood_product_id, model_version_id, defect_handling_action,
                   image_width, image_height, conveyor_speed, xray_voltage, xray_current, confidence_threshold, is_enabled
            FROM channel_configs
            ORDER BY channel_no;
            """;

        return await ReadChannelConfigsAsync(command, cancellationToken);
    }

    public async Task<ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, channel_no, name, seafood_product_id, model_version_id, defect_handling_action,
                   image_width, image_height, conveyor_speed, xray_voltage, xray_current, confidence_threshold, is_enabled
            FROM channel_configs
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        var results = await ReadChannelConfigsAsync(command, cancellationToken);
        return results.FirstOrDefault();
    }

    public async Task<ChannelConfig> UpsertAsync(ChannelConfig channelConfig, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO channel_configs (
                id, channel_no, name, seafood_product_id, model_version_id, defect_handling_action,
                image_width, image_height, conveyor_speed, xray_voltage, xray_current, confidence_threshold, is_enabled
            ) VALUES (
                $id, $channelNo, $name, $productId, $modelVersionId, $action,
                $imageWidth, $imageHeight, $speed, $voltage, $current, $confidenceThreshold, $enabled
            )
            ON CONFLICT(id) DO UPDATE SET
                channel_no = excluded.channel_no,
                name = excluded.name,
                seafood_product_id = excluded.seafood_product_id,
                model_version_id = excluded.model_version_id,
                defect_handling_action = excluded.defect_handling_action,
                image_width = excluded.image_width,
                image_height = excluded.image_height,
                conveyor_speed = excluded.conveyor_speed,
                xray_voltage = excluded.xray_voltage,
                xray_current = excluded.xray_current,
                confidence_threshold = excluded.confidence_threshold,
                is_enabled = excluded.is_enabled;
            """;
        command.Parameters.AddWithValue("$id", channelConfig.Id.ToString());
        command.Parameters.AddWithValue("$channelNo", channelConfig.ChannelNo);
        command.Parameters.AddWithValue("$name", channelConfig.Name);
        command.Parameters.AddWithValue("$productId", channelConfig.SeafoodProductId.ToString());
        command.Parameters.AddWithValue("$modelVersionId", channelConfig.ModelVersionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$action", (int)channelConfig.DefectHandlingAction);
        command.Parameters.AddWithValue("$imageWidth", channelConfig.ImageWidth);
        command.Parameters.AddWithValue("$imageHeight", channelConfig.ImageHeight);
        command.Parameters.AddWithValue("$speed", channelConfig.ConveyorSpeedMetersPerSecond);
        command.Parameters.AddWithValue("$voltage", channelConfig.XrayVoltageKv);
        command.Parameters.AddWithValue("$current", channelConfig.XrayCurrentMa);
        command.Parameters.AddWithValue("$confidenceThreshold", channelConfig.ConfidenceThreshold);
        command.Parameters.AddWithValue("$enabled", channelConfig.IsEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return channelConfig;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM channel_configs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<ChannelConfig>> ReadChannelConfigsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<ChannelConfig>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ChannelConfig(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetString(2),
                Guid.Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                (DefectHandlingAction)reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                Convert.ToDecimal(reader.GetDouble(8)),
                Convert.ToDecimal(reader.GetDouble(9)),
                Convert.ToDecimal(reader.GetDouble(10)),
                Convert.ToDecimal(reader.GetDouble(11)),
                reader.GetInt32(12) == 1));
        }

        return results;
    }
}
