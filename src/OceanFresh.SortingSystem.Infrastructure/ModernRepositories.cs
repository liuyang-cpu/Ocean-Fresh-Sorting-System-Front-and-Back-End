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
            SELECT id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path
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
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? "{}" : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
        }

        return results;
    }

    public async Task<SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path
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
            reader.GetInt32(3) == 1,
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? "{}" : reader.GetString(5),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6));
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
            INSERT INTO seafood_products (id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path)
            VALUES ($id, $code, $name, $enabled, $classesFilePath, $labelMapJson, $predictConfigPath)
            ON CONFLICT(id) DO UPDATE SET
                code = excluded.code,
                name = excluded.name,
                is_enabled = excluded.is_enabled,
                classes_file_path = excluded.classes_file_path,
                label_map_json = excluded.label_map_json,
                predict_config_path = excluded.predict_config_path;
            """;
        command.Parameters.AddWithValue("$id", product.Id.ToString());
        command.Parameters.AddWithValue("$code", product.Code);
        command.Parameters.AddWithValue("$name", product.Name);
        command.Parameters.AddWithValue("$enabled", product.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$classesFilePath", product.ClassesFilePath);
        command.Parameters.AddWithValue("$labelMapJson", product.LabelMapJson);
        command.Parameters.AddWithValue("$predictConfigPath", product.PredictConfigPath);
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
            SELECT id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path
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
            reader.GetInt32(3) == 1,
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? "{}" : reader.GetString(5),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6));
    }

    private static async Task<SeafoodProduct?> FindByCodeAsync(SqliteConnection connection, string code, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path
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
            reader.GetInt32(3) == 1,
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? "{}" : reader.GetString(5),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6));
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
            SELECT id, channel_no, name, seafood_product_id, model_version_id, model_path, defect_handling_action,
                   conveyor_speed, confidence_threshold, is_enabled,
                   last_runtime_predict_config_path, last_runtime_output_directory, camera_to_eject_distance_mm,
                   mm_per_pixel_y, software_latency_ms, actuator_delay_ms, horizontal_lane_mapping_json
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
            SELECT id, channel_no, name, seafood_product_id, model_version_id, model_path, defect_handling_action,
                   conveyor_speed, confidence_threshold, is_enabled,
                   last_runtime_predict_config_path, last_runtime_output_directory, camera_to_eject_distance_mm,
                   mm_per_pixel_y, software_latency_ms, actuator_delay_ms, horizontal_lane_mapping_json
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
                id, channel_no, name, seafood_product_id, model_version_id, model_path, defect_handling_action,
                conveyor_speed, confidence_threshold, is_enabled, last_runtime_predict_config_path, last_runtime_output_directory, camera_to_eject_distance_mm,
                mm_per_pixel_y, software_latency_ms, actuator_delay_ms, horizontal_lane_mapping_json
            ) VALUES (
                $id, $channelNo, $name, $productId, $modelVersionId, $modelPath, $action,
                $speed, $confidenceThreshold, $enabled, $lastRuntimePredictConfigPath, $lastRuntimeOutputDirectory, $cameraToEjectDistanceMm,
                $millimetersPerPixelY, $softwareLatencyMs, $actuatorDelayMs, $horizontalLaneMappingJson
            )
            ON CONFLICT(id) DO UPDATE SET
                channel_no = excluded.channel_no,
                name = excluded.name,
                seafood_product_id = excluded.seafood_product_id,
                model_version_id = excluded.model_version_id,
                model_path = excluded.model_path,
                defect_handling_action = excluded.defect_handling_action,
                conveyor_speed = excluded.conveyor_speed,
                confidence_threshold = excluded.confidence_threshold,
                is_enabled = excluded.is_enabled,
                last_runtime_predict_config_path = excluded.last_runtime_predict_config_path,
                last_runtime_output_directory = excluded.last_runtime_output_directory,
                camera_to_eject_distance_mm = excluded.camera_to_eject_distance_mm,
                mm_per_pixel_y = excluded.mm_per_pixel_y,
                software_latency_ms = excluded.software_latency_ms,
                actuator_delay_ms = excluded.actuator_delay_ms,
                horizontal_lane_mapping_json = excluded.horizontal_lane_mapping_json;
            """;
        command.Parameters.AddWithValue("$id", channelConfig.Id.ToString());
        command.Parameters.AddWithValue("$channelNo", channelConfig.ChannelNo);
        command.Parameters.AddWithValue("$name", channelConfig.Name);
        command.Parameters.AddWithValue("$productId", channelConfig.SeafoodProductId.ToString());
        command.Parameters.AddWithValue("$modelVersionId", channelConfig.ModelVersionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$modelPath", channelConfig.ModelPath);
        command.Parameters.AddWithValue("$action", (int)channelConfig.DefectHandlingAction);
        command.Parameters.AddWithValue("$speed", MachineRuntimeDefaults.ConveyorSpeedMetersPerSecond);
        command.Parameters.AddWithValue("$confidenceThreshold", channelConfig.ConfidenceThreshold);
        command.Parameters.AddWithValue("$enabled", channelConfig.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastRuntimePredictConfigPath", (object?)channelConfig.LastRuntimePredictConfigPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastRuntimeOutputDirectory", (object?)channelConfig.LastRuntimeOutputDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("$cameraToEjectDistanceMm", channelConfig.CameraToEjectDistanceMillimeters);
        command.Parameters.AddWithValue("$millimetersPerPixelY", channelConfig.MillimetersPerPixelY);
        command.Parameters.AddWithValue("$softwareLatencyMs", channelConfig.SoftwareLatencyMilliseconds);
        command.Parameters.AddWithValue("$actuatorDelayMs", channelConfig.ActuatorDelayMilliseconds);
        command.Parameters.AddWithValue("$horizontalLaneMappingJson", channelConfig.HorizontalLaneMappingJson);
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
                reader.GetString(5),
                (DefectHandlingAction)reader.GetInt32(6),
                Convert.ToDecimal(reader.GetDouble(8)),
                reader.GetInt32(9) == 1,
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                Convert.ToDecimal(reader.GetDouble(12)),
                Convert.ToDecimal(reader.GetDouble(13)),
                reader.GetInt32(14),
                reader.GetInt32(15),
                reader.GetString(16)));
        }

        return results;
    }
}
