using System.Text.Json;
using Microsoft.Data.Sqlite;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Infrastructure;

internal static class SeedData
{
    public static readonly Guid OilClamCategoryId = Guid.Parse("8eeac4e9-0646-4a5d-a1ee-0d09a1776e7a");
    public static readonly Guid SurfClamCategoryId = Guid.Parse("7fc3d726-c789-4d97-8c39-1275521f9c8c");
    public static readonly Guid VenusClamCategoryId = Guid.Parse("b33199af-10e8-411a-b5e6-9821d467b4df");
    public static readonly Guid OilClamProductId = Guid.Parse("5f151acf-fb6e-4525-84ab-f12dfc2cb1e7");
    public static readonly Guid SurfClamProductId = Guid.Parse("5738168f-d9ca-440e-a5a8-4324f651f743");
    public static readonly Guid VenusClamProductId = Guid.Parse("e1a17fd7-c7a3-4cd2-921d-f05d05b00577");
    public static readonly Guid VenusRecipeId = Guid.Parse("6f1503ef-f421-4248-b0b9-7a1915b4eb6a");
    public static readonly Guid OilRecipeId = Guid.Parse("2423f71e-285b-463e-b98d-b0b18b322c74");
    public static readonly Guid VenusModelV1Id = Guid.Parse("5db4c85a-8362-4b39-a95c-dddb14fbe4cf");
    public static readonly Guid VenusModelV2Id = Guid.Parse("b90e8381-8730-4ef0-bc97-75d6050d6169");
    public static readonly Guid OilModelV1Id = Guid.Parse("6ea4a818-e0e0-4cd4-9628-ca53abcc1216");
    public static readonly Guid SurfModelV1Id = Guid.Parse("d4493887-c43e-4cf2-a31b-af503459de17");
    public static readonly Guid Channel1Id = Guid.Parse("6af90327-3063-49fd-a0b5-a7e315d4b2e7");
    public static readonly Guid Channel2Id = Guid.Parse("f3617c3e-17c6-4822-b38f-2ec91a0a1ef2");
    public const string VenusNormalLabel = "good";
    public const string OilNormalLabel = "正常";
}

public sealed class SqliteConnectionFactory
{
    public SqliteConnectionFactory()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "oceanfresh.db"),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string ConnectionString { get; }

    public SqliteConnection CreateConnection() => new(ConnectionString);
}

public sealed class SqliteDatabaseInitializer(SqliteConnectionFactory connectionFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS seafood_categories (
                id TEXT PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS product_recipes (
                id TEXT PRIMARY KEY,
                seafood_category_id TEXT NOT NULL,
                name TEXT NOT NULL,
                conveyor_speed REAL NOT NULL,
                image_width INTEGER NOT NULL,
                image_height INTEGER NOT NULL,
                xray_voltage REAL NOT NULL,
                xray_current REAL NOT NULL,
                default_defect_action INTEGER NOT NULL,
                normal_label TEXT NOT NULL,
                eject_delay_us INTEGER NOT NULL,
                eject_pulse_width_us INTEGER NOT NULL,
                encoder_window_start INTEGER NOT NULL,
                encoder_window_end INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS model_versions (
                id TEXT PRIMARY KEY,
                seafood_category_id TEXT NOT NULL,
                version TEXT NOT NULL,
                source_weight_path TEXT NOT NULL,
                deployment_model_path TEXT NOT NULL,
                input_tensor_shape TEXT NOT NULL,
                label_map_json TEXT NOT NULL,
                confidence_threshold REAL NOT NULL,
                nms_threshold REAL NOT NULL,
                notes TEXT NOT NULL,
                status INTEGER NOT NULL,
                exported_at TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS recipe_model_bindings (
                id TEXT PRIMARY KEY,
                recipe_id TEXT NOT NULL,
                model_version_id TEXT NOT NULL,
                is_primary INTEGER NOT NULL,
                is_preloaded INTEGER NOT NULL,
                bound_at TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS inspection_records (
                id TEXT PRIMARY KEY,
                recipe_id TEXT NOT NULL,
                model_version_id TEXT NOT NULL,
                batch_code TEXT NOT NULL,
                image_path TEXT NOT NULL,
                is_rejected INTEGER NOT NULL,
                is_timed_out INTEGER NOT NULL,
                captured_at TEXT NOT NULL,
                detections_json TEXT NOT NULL,
                eject_command_json TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS alarm_events (
                id TEXT PRIMARY KEY,
                severity INTEGER NOT NULL,
                source TEXT NOT NULL,
                code TEXT NOT NULL,
                message TEXT NOT NULL,
                raised_at TEXT NOT NULL,
                is_acknowledged INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS user_accounts (
                id TEXT PRIMARY KEY,
                user_name TEXT NOT NULL,
                display_name TEXT NOT NULL,
                role INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS seafood_products (
                id TEXT PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                is_enabled INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS seafood_traits (
                id TEXT PRIMARY KEY,
                seafood_product_id TEXT NOT NULL,
                name TEXT NOT NULL,
                is_normal INTEGER NOT NULL,
                sort_order INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS channel_configs (
                id TEXT PRIMARY KEY,
                channel_no INTEGER NOT NULL,
                name TEXT NOT NULL,
                seafood_product_id TEXT NOT NULL,
                model_version_id TEXT NULL,
                defect_handling_action INTEGER NOT NULL,
                image_width INTEGER NOT NULL,
                image_height INTEGER NOT NULL,
                conveyor_speed REAL NOT NULL,
                xray_voltage REAL NOT NULL,
                xray_current REAL NOT NULL,
                confidence_threshold REAL NOT NULL,
                is_enabled INTEGER NOT NULL
            );
            """
        };

        foreach (var sql in commands)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureColumnAsync(connection, "product_recipes", "image_width", "INTEGER NOT NULL DEFAULT 1536", cancellationToken);
        await EnsureColumnAsync(connection, "product_recipes", "image_height", "INTEGER NOT NULL DEFAULT 300", cancellationToken);
        await EnsureColumnAsync(connection, "product_recipes", "default_defect_action", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "product_recipes", "normal_label", "TEXT NOT NULL DEFAULT '正常'", cancellationToken);
        await TryMigrateLegacyRecipeColumnsAsync(connection, cancellationToken);
        await EnsureUniqueIndexAsync(connection, "idx_seafood_products_code", "seafood_products", "code", cancellationToken);

        await SeedAsync(connection, cancellationToken);
        await EnsureModernProductSeedAsync(connection, cancellationToken);
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (await TableHasRowsAsync(connection, "seafood_categories", cancellationToken))
        {
            return;
        }

        await ExecuteAsync(connection, """
            INSERT INTO seafood_categories (id, code, name, description) VALUES
            ($id1, 'oil_clam', '油蛤', '油蛤分拣品类'),
            ($id2, 'surf_clam', '美贝', '美贝分拣品类'),
            ($id3, 'venus_clam', '花蛤', '花蛤分拣品类');
            """,
            [
                ("$id1", SeedData.OilClamCategoryId.ToString()),
                ("$id2", SeedData.SurfClamCategoryId.ToString()),
                ("$id3", SeedData.VenusClamCategoryId.ToString())
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO product_recipes (
                id, seafood_category_id, name, conveyor_speed, image_width, image_height,
                xray_voltage, xray_current, default_defect_action, normal_label, eject_delay_us, eject_pulse_width_us,
                encoder_window_start, encoder_window_end, is_enabled
            ) VALUES
            ($id1, $cat1, '花蛤标准线', 1.5, 1536, 300, 40, 8, 1, $normal1, 128, 50, 57, 1464, 1),
            ($id2, $cat2, '油蛤高速线', 1.8, 1536, 300, 50, 6, 1, $normal2, 120, 40, 50, 1320, 1);
            """,
            [
                ("$id1", SeedData.VenusRecipeId.ToString()),
                ("$cat1", SeedData.VenusClamCategoryId.ToString()),
                ("$id2", SeedData.OilRecipeId.ToString()),
                ("$cat2", SeedData.OilClamCategoryId.ToString()),
                ("$normal1", SeedData.VenusNormalLabel),
                ("$normal2", SeedData.OilNormalLabel)
            ], cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await ExecuteAsync(connection, """
            INSERT INTO model_versions (
                id, seafood_category_id, version, source_weight_path, deployment_model_path,
                input_tensor_shape, label_map_json, confidence_threshold, nms_threshold,
                notes, status, exported_at, created_at
            ) VALUES
            ($id1, $cat1, '花蛤-v1', 'models\venus_clam\v1\best.pt', 'models\venus_clam\v1\best.onnx', '[1,1,640,640]', '{"good":0,"empty":1,"sand":2,"broken":3}', 0.55, 0.45, '初始稳定版', 2, $dt1, $dt1),
            ($id2, $cat1, '花蛤-v2', 'models\venus_clam\v2\best.pt', 'models\venus_clam\v2\best.onnx', '[1,1,640,640]', '{"good":0,"empty":1,"sand":2,"broken":3}', 0.60, 0.45, '增强空心和砂石识别', 2, $dt2, $dt2),
            ($id3, $cat2, '油蛤-v1', 'models\oil_clam\v1\best.pt', 'models\oil_clam\v1\best.onnx', '[1,1,640,640]', '{"good":0,"empty":1,"stone":2}', 0.58, 0.40, '油蛤标准模型', 2, $dt3, $dt3);
            """,
            [
                ("$id1", SeedData.VenusModelV1Id.ToString()),
                ("$id2", SeedData.VenusModelV2Id.ToString()),
                ("$id3", SeedData.OilModelV1Id.ToString()),
                ("$cat1", SeedData.VenusClamCategoryId.ToString()),
                ("$cat2", SeedData.OilClamCategoryId.ToString()),
                ("$dt1", now.AddDays(-10).ToString("O")),
                ("$dt2", now.AddDays(-3).ToString("O")),
                ("$dt3", now.AddDays(-5).ToString("O"))
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO recipe_model_bindings (
                id, recipe_id, model_version_id, is_primary, is_preloaded, bound_at
            ) VALUES
            ($b1, $r1, $m2, 1, 1, $dt1),
            ($b2, $r1, $m1, 0, 0, $dt2),
            ($b3, $r2, $m3, 1, 1, $dt3);
            """,
            [
                ("$b1", Guid.NewGuid().ToString()),
                ("$b2", Guid.NewGuid().ToString()),
                ("$b3", Guid.NewGuid().ToString()),
                ("$r1", SeedData.VenusRecipeId.ToString()),
                ("$r2", SeedData.OilRecipeId.ToString()),
                ("$m1", SeedData.VenusModelV1Id.ToString()),
                ("$m2", SeedData.VenusModelV2Id.ToString()),
                ("$m3", SeedData.OilModelV1Id.ToString()),
                ("$dt1", now.AddDays(-2).ToString("O")),
                ("$dt2", now.AddDays(-9).ToString("O")),
                ("$dt3", now.AddDays(-4).ToString("O"))
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO user_accounts (id, user_name, display_name, role, is_enabled) VALUES
            ($id1, 'operator', '操作员', 1, 1),
            ($id2, 'admin', '管理员', 2, 1);
            """,
            [
                ("$id1", Guid.NewGuid().ToString()),
                ("$id2", Guid.NewGuid().ToString())
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO seafood_products (id, code, name, is_enabled) VALUES
            ($id1, 'SP-YG', '油蛤', 1),
            ($id2, 'SP-HG', '花蛤', 1),
            ($id3, 'SP-MB', '美贝', 1);
            """,
            [
                ("$id1", SeedData.OilClamProductId.ToString()),
                ("$id2", SeedData.VenusClamProductId.ToString()),
                ("$id3", SeedData.SurfClamProductId.ToString())
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO seafood_traits (id, seafood_product_id, name, is_normal, sort_order, is_enabled) VALUES
            ($id1, $product1, '正常', 1, 1, 1),
            ($id2, $product1, '碎壳', 0, 2, 1),
            ($id3, $product1, '泥包', 0, 3, 1),
            ($id4, $product1, '空壳', 0, 4, 1),
            ($id5, $product2, 'good', 1, 1, 1),
            ($id6, $product2, 'broken', 0, 2, 1),
            ($id7, $product2, 'sand', 0, 3, 1),
            ($id8, $product2, 'empty', 0, 4, 1),
            ($id9, $product3, '正常', 1, 1, 1),
            ($id10, $product3, '碎壳', 0, 2, 1),
            ($id11, $product3, '泥包', 0, 3, 1),
            ($id12, $product3, '空壳', 0, 4, 1);
            """,
            [
                ("$id1", Guid.NewGuid().ToString()),
                ("$id2", Guid.NewGuid().ToString()),
                ("$id3", Guid.NewGuid().ToString()),
                ("$id4", Guid.NewGuid().ToString()),
                ("$id5", Guid.NewGuid().ToString()),
                ("$id6", Guid.NewGuid().ToString()),
                ("$id7", Guid.NewGuid().ToString()),
                ("$id8", Guid.NewGuid().ToString()),
                ("$id9", Guid.NewGuid().ToString()),
                ("$id10", Guid.NewGuid().ToString()),
                ("$id11", Guid.NewGuid().ToString()),
                ("$id12", Guid.NewGuid().ToString()),
                ("$product1", SeedData.OilClamProductId.ToString()),
                ("$product2", SeedData.VenusClamProductId.ToString()),
                ("$product3", SeedData.SurfClamProductId.ToString())
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO model_versions (
                id, seafood_category_id, version, source_weight_path, deployment_model_path,
                input_tensor_shape, label_map_json, confidence_threshold, nms_threshold,
                notes, status, exported_at, created_at
            ) VALUES
            ($id1, $cat1, '美贝-v1', 'models\surf_clam\v1\best.pt', 'models\surf_clam\v1\best.onnx', '[1,1,640,640]', '{"正常":0,"碎壳":1,"泥包":2,"空壳":3}', 0.57, 0.42, '美贝基础模型', 2, $dt1, $dt1)
            ON CONFLICT(id) DO NOTHING;
            """,
            [
                ("$id1", SeedData.SurfModelV1Id.ToString()),
                ("$cat1", SeedData.SurfClamCategoryId.ToString()),
                ("$dt1", now.AddDays(-1).ToString("O"))
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO channel_configs (
                id, channel_no, name, seafood_product_id, model_version_id, defect_handling_action,
                image_width, image_height, conveyor_speed, xray_voltage, xray_current, confidence_threshold, is_enabled
            ) VALUES
            ($id1, 1, '1号通道', $product1, $model1, 1, 1536, 300, 1.8, 50, 6, 0.58, 1),
            ($id2, 2, '2号通道', $product2, $model2, 1, 1536, 300, 1.5, 40, 8, 0.60, 1);
            """,
            [
                ("$id1", SeedData.Channel1Id.ToString()),
                ("$id2", SeedData.Channel2Id.ToString()),
                ("$product1", SeedData.OilClamProductId.ToString()),
                ("$product2", SeedData.VenusClamProductId.ToString()),
                ("$model1", SeedData.OilModelV1Id.ToString()),
                ("$model2", SeedData.VenusModelV2Id.ToString())
            ], cancellationToken);
    }

    private static async Task EnsureModernProductSeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await TableHasRowsAsync(connection, "seafood_products", cancellationToken))
        {
            await ExecuteAsync(connection, """
                INSERT INTO seafood_products (id, code, name, is_enabled) VALUES
                ($id1, 'SP-YG', '油蛤', 1),
                ($id2, 'SP-HG', '花蛤', 1),
                ($id3, 'SP-MB', '美贝', 1);
                """,
                [
                    ("$id1", SeedData.OilClamProductId.ToString()),
                    ("$id2", SeedData.VenusClamProductId.ToString()),
                    ("$id3", SeedData.SurfClamProductId.ToString())
                ], cancellationToken);
        }

        if (!await TableHasRowsAsync(connection, "seafood_traits", cancellationToken))
        {
            await ExecuteAsync(connection, """
                INSERT INTO seafood_traits (id, seafood_product_id, name, is_normal, sort_order, is_enabled) VALUES
                ($id1, $product1, '正常', 1, 1, 1),
                ($id2, $product1, '碎壳', 0, 2, 1),
                ($id3, $product1, '泥包', 0, 3, 1),
                ($id4, $product1, '空壳', 0, 4, 1),
                ($id5, $product2, 'good', 1, 1, 1),
                ($id6, $product2, 'broken', 0, 2, 1),
                ($id7, $product2, 'sand', 0, 3, 1),
                ($id8, $product2, 'empty', 0, 4, 1),
                ($id9, $product3, '正常', 1, 1, 1),
                ($id10, $product3, '碎壳', 0, 2, 1),
                ($id11, $product3, '泥包', 0, 3, 1),
                ($id12, $product3, '空壳', 0, 4, 1);
                """,
                [
                    ("$id1", Guid.NewGuid().ToString()),
                    ("$id2", Guid.NewGuid().ToString()),
                    ("$id3", Guid.NewGuid().ToString()),
                    ("$id4", Guid.NewGuid().ToString()),
                    ("$id5", Guid.NewGuid().ToString()),
                    ("$id6", Guid.NewGuid().ToString()),
                    ("$id7", Guid.NewGuid().ToString()),
                    ("$id8", Guid.NewGuid().ToString()),
                    ("$id9", Guid.NewGuid().ToString()),
                    ("$id10", Guid.NewGuid().ToString()),
                    ("$id11", Guid.NewGuid().ToString()),
                    ("$id12", Guid.NewGuid().ToString()),
                    ("$product1", SeedData.OilClamProductId.ToString()),
                    ("$product2", SeedData.VenusClamProductId.ToString()),
                    ("$product3", SeedData.SurfClamProductId.ToString())
                ], cancellationToken);
        }

        if (!await TableHasRowsAsync(connection, "channel_configs", cancellationToken))
        {
            await ExecuteAsync(connection, """
                INSERT INTO channel_configs (
                    id, channel_no, name, seafood_product_id, model_version_id, defect_handling_action,
                    image_width, image_height, conveyor_speed, xray_voltage, xray_current, confidence_threshold, is_enabled
                ) VALUES
                ($id1, 1, '1号通道', $product1, $model1, 1, 1536, 300, 1.8, 50, 6, 0.58, 1),
                ($id2, 2, '2号通道', $product2, $model2, 1, 1536, 300, 1.5, 40, 8, 0.60, 1);
                """,
                [
                    ("$id1", SeedData.Channel1Id.ToString()),
                    ("$id2", SeedData.Channel2Id.ToString()),
                    ("$product1", SeedData.OilClamProductId.ToString()),
                    ("$product2", SeedData.VenusClamProductId.ToString()),
                    ("$model1", SeedData.OilModelV1Id.ToString()),
                    ("$model2", SeedData.VenusModelV2Id.ToString())
                ], cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM model_versions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", SeedData.SurfModelV1Id.ToString());
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            await ExecuteAsync(connection, """
                INSERT INTO model_versions (
                    id, seafood_category_id, version, source_weight_path, deployment_model_path,
                    input_tensor_shape, label_map_json, confidence_threshold, nms_threshold,
                    notes, status, exported_at, created_at
                ) VALUES
                ($id1, $cat1, '美贝-v1', 'models\surf_clam\v1\best.pt', 'models\surf_clam\v1\best.onnx', '[1,1,640,640]', '{"正常":0,"碎壳":1,"泥包":2,"空壳":3}', 0.57, 0.42, '美贝基础模型', 2, $dt1, $dt1);
                """,
                [
                    ("$id1", SeedData.SurfModelV1Id.ToString()),
                    ("$cat1", SeedData.SurfClamCategoryId.ToString()),
                    ("$dt1", now.AddDays(-1).ToString("O"))
                ], cancellationToken);
        }
    }

    private static async Task<bool> TableHasRowsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {tableName} LIMIT 1);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, IEnumerable<(string, object)> parameters, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string tableName, string columnName, string definition, CancellationToken cancellationToken)
    {
        var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        var exists = false;
        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureUniqueIndexAsync(SqliteConnection connection, string indexName, string tableName, string columnName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {tableName}({columnName});";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TryMigrateLegacyRecipeColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var migrate = connection.CreateCommand();
        migrate.CommandText = """
            UPDATE product_recipes
            SET image_width = COALESCE(image_width, 1536),
                image_height = COALESCE(image_height, 300),
                default_defect_action = COALESCE(default_defect_action, 1),
                normal_label = CASE
                    WHEN normal_label IS NULL OR normal_label = ''
                    THEN CASE
                        WHEN name LIKE '%油蛤%' THEN $oilNormal
                        ELSE $venusNormal
                    END
                    ELSE normal_label
                END;
            """;
        migrate.Parameters.AddWithValue("$oilNormal", SeedData.OilNormalLabel);
        migrate.Parameters.AddWithValue("$venusNormal", SeedData.VenusNormalLabel);
        await migrate.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqliteSeafoodCategoryRepository(SqliteConnectionFactory connectionFactory) : ISeafoodCategoryRepository
{
    public async Task<IReadOnlyList<SeafoodCategory>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, code, name, description FROM seafood_categories ORDER BY name;";

        var results = new List<SeafoodCategory>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SeafoodCategory(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return results;
    }

    public async Task<SeafoodCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, code, name, description FROM seafood_categories WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SeafoodCategory(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }
}

public sealed class SqliteProductRecipeRepository(SqliteConnectionFactory connectionFactory) : IProductRecipeRepository
{
    public async Task<IReadOnlyList<ProductRecipe>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, seafood_category_id, name, conveyor_speed, image_width, image_height, xray_voltage, xray_current,
                   default_defect_action, normal_label, eject_delay_us, eject_pulse_width_us, encoder_window_start, encoder_window_end, is_enabled
            FROM product_recipes
            ORDER BY name;
            """;

        return await ReadRecipesAsync(command, cancellationToken);
    }

    public async Task<ProductRecipe?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, seafood_category_id, name, conveyor_speed, image_width, image_height, xray_voltage, xray_current,
                   default_defect_action, normal_label, eject_delay_us, eject_pulse_width_us, encoder_window_start, encoder_window_end, is_enabled
            FROM product_recipes
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        return (await ReadRecipesAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<ProductRecipe> UpsertAsync(ProductRecipe recipe, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO product_recipes (
                id, seafood_category_id, name, conveyor_speed, image_width, image_height, xray_voltage, xray_current,
                default_defect_action, normal_label, eject_delay_us, eject_pulse_width_us, encoder_window_start, encoder_window_end, is_enabled
            ) VALUES (
                $id, $categoryId, $name, $speed, $imageWidth, $imageHeight, $voltage, $current, $action, $normalLabel,
                $delay, $pulse, $windowStart, $windowEnd, $enabled
            )
            ON CONFLICT(id) DO UPDATE SET
                seafood_category_id = excluded.seafood_category_id,
                name = excluded.name,
                conveyor_speed = excluded.conveyor_speed,
                image_width = excluded.image_width,
                image_height = excluded.image_height,
                xray_voltage = excluded.xray_voltage,
                xray_current = excluded.xray_current,
                default_defect_action = excluded.default_defect_action,
                normal_label = excluded.normal_label,
                eject_delay_us = excluded.eject_delay_us,
                eject_pulse_width_us = excluded.eject_pulse_width_us,
                encoder_window_start = excluded.encoder_window_start,
                encoder_window_end = excluded.encoder_window_end,
                is_enabled = excluded.is_enabled;
            """;
        BindRecipe(command, recipe);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return recipe;
    }

    private static void BindRecipe(SqliteCommand command, ProductRecipe recipe)
    {
        command.Parameters.AddWithValue("$id", recipe.Id.ToString());
        command.Parameters.AddWithValue("$categoryId", recipe.SeafoodCategoryId.ToString());
        command.Parameters.AddWithValue("$name", recipe.Name);
        command.Parameters.AddWithValue("$speed", recipe.ConveyorSpeedMetersPerSecond);
        command.Parameters.AddWithValue("$imageWidth", recipe.ImageWidth);
        command.Parameters.AddWithValue("$imageHeight", recipe.ImageHeight);
        command.Parameters.AddWithValue("$voltage", recipe.XrayVoltageKv);
        command.Parameters.AddWithValue("$current", recipe.XrayCurrentMa);
        command.Parameters.AddWithValue("$action", (int)recipe.DefectHandlingAction);
        command.Parameters.AddWithValue("$normalLabel", recipe.NormalLabel);
        command.Parameters.AddWithValue("$delay", recipe.EjectDelayMicroseconds);
        command.Parameters.AddWithValue("$pulse", recipe.EjectPulseWidthMicroseconds);
        command.Parameters.AddWithValue("$windowStart", recipe.EncoderWindowStart);
        command.Parameters.AddWithValue("$windowEnd", recipe.EncoderWindowEnd);
        command.Parameters.AddWithValue("$enabled", recipe.IsEnabled ? 1 : 0);
    }

    private static async Task<IReadOnlyList<ProductRecipe>> ReadRecipesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<ProductRecipe>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ProductRecipe(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                Convert.ToDecimal(reader.GetDouble(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                Convert.ToDecimal(reader.GetDouble(6)),
                Convert.ToDecimal(reader.GetDouble(7)),
                (DefectHandlingAction)reader.GetInt32(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13),
                reader.GetInt32(14) == 1));
        }

        return results;
    }
}

public sealed class SqliteModelRegistryRepository(SqliteConnectionFactory connectionFactory) : IModelRegistryRepository
{
    public Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        QueryAsync("SELECT * FROM model_versions WHERE seafood_category_id = $categoryId ORDER BY created_at DESC;", ("$categoryId", categoryId.ToString()), cancellationToken);

    public async Task<ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var results = await QueryAsync("SELECT * FROM model_versions WHERE id = $id LIMIT 1;", ("$id", id.ToString()), cancellationToken);
        return results.FirstOrDefault();
    }

    public Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
        QueryAsync("SELECT * FROM model_versions ORDER BY created_at DESC;", cancellationToken: cancellationToken);

    public async Task<ModelVersion> UpsertAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO model_versions (
                id, seafood_category_id, version, source_weight_path, deployment_model_path,
                input_tensor_shape, label_map_json, confidence_threshold, nms_threshold,
                notes, status, exported_at, created_at
            ) VALUES (
                $id, $categoryId, $version, $sourcePath, $deploymentPath, $shape, $labelMap,
                $confidence, $nms, $notes, $status, $exportedAt, $createdAt
            )
            ON CONFLICT(id) DO UPDATE SET
                seafood_category_id = excluded.seafood_category_id,
                version = excluded.version,
                source_weight_path = excluded.source_weight_path,
                deployment_model_path = excluded.deployment_model_path,
                input_tensor_shape = excluded.input_tensor_shape,
                label_map_json = excluded.label_map_json,
                confidence_threshold = excluded.confidence_threshold,
                nms_threshold = excluded.nms_threshold,
                notes = excluded.notes,
                status = excluded.status,
                exported_at = excluded.exported_at,
                created_at = excluded.created_at;
            """;
        BindModel(command, modelVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return modelVersion;
    }

    private async Task<IReadOnlyList<ModelVersion>> QueryAsync(string sql, (string Name, object Value)? parameter = null, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is { } item)
        {
            command.Parameters.AddWithValue(item.Name, item.Value);
        }

        var results = new List<ModelVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ModelVersion(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Convert.ToDecimal(reader.GetDouble(7)),
                Convert.ToDecimal(reader.GetDouble(8)),
                reader.GetString(9),
                (ModelStatus)reader.GetInt32(10),
                DateTimeOffset.Parse(reader.GetString(11)),
                DateTimeOffset.Parse(reader.GetString(12))));
        }

        return results;
    }

    private static void BindModel(SqliteCommand command, ModelVersion modelVersion)
    {
        command.Parameters.AddWithValue("$id", modelVersion.Id.ToString());
        command.Parameters.AddWithValue("$categoryId", modelVersion.SeafoodCategoryId.ToString());
        command.Parameters.AddWithValue("$version", modelVersion.Version);
        command.Parameters.AddWithValue("$sourcePath", modelVersion.SourceWeightPath);
        command.Parameters.AddWithValue("$deploymentPath", modelVersion.DeploymentModelPath);
        command.Parameters.AddWithValue("$shape", modelVersion.InputTensorShape);
        command.Parameters.AddWithValue("$labelMap", modelVersion.LabelMapJson);
        command.Parameters.AddWithValue("$confidence", modelVersion.ConfidenceThreshold);
        command.Parameters.AddWithValue("$nms", modelVersion.NmsThreshold);
        command.Parameters.AddWithValue("$notes", modelVersion.Notes);
        command.Parameters.AddWithValue("$status", (int)modelVersion.Status);
        command.Parameters.AddWithValue("$exportedAt", modelVersion.ExportedAt.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", modelVersion.CreatedAt.ToString("O"));
    }
}

public sealed class SqliteRecipeModelBindingRepository(SqliteConnectionFactory connectionFactory) : IRecipeModelBindingRepository
{
    public async Task<IReadOnlyList<RecipeModelBinding>> GetByRecipeAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipe_id, model_version_id, is_primary, is_preloaded, bound_at
            FROM recipe_model_bindings
            WHERE recipe_id = $recipeId
            ORDER BY bound_at DESC;
            """;
        command.Parameters.AddWithValue("$recipeId", recipeId.ToString());

        var results = new List<RecipeModelBinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RecipeModelBinding(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4) == 1,
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return results;
    }

    public async Task SetPrimaryAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var updateAll = connection.CreateCommand();
        updateAll.Transaction = transaction;
        updateAll.CommandText = """
            UPDATE recipe_model_bindings
            SET is_primary = 0, is_preloaded = 0
            WHERE recipe_id = $recipeId;
            """;
        updateAll.Parameters.AddWithValue("$recipeId", recipeId.ToString());
        await updateAll.ExecuteNonQueryAsync(cancellationToken);

        var updateTarget = connection.CreateCommand();
        updateTarget.Transaction = transaction;
        updateTarget.CommandText = """
            UPDATE recipe_model_bindings
            SET is_primary = 1, is_preloaded = 1, bound_at = $boundAt
            WHERE recipe_id = $recipeId AND model_version_id = $modelVersionId;
            """;
        updateTarget.Parameters.AddWithValue("$recipeId", recipeId.ToString());
        updateTarget.Parameters.AddWithValue("$modelVersionId", modelVersionId.ToString());
        updateTarget.Parameters.AddWithValue("$boundAt", DateTimeOffset.UtcNow.ToString("O"));
        var affected = await updateTarget.ExecuteNonQueryAsync(cancellationToken);

        if (affected == 0)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO recipe_model_bindings (id, recipe_id, model_version_id, is_primary, is_preloaded, bound_at)
                VALUES ($id, $recipeId, $modelVersionId, 1, 1, $boundAt);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$recipeId", recipeId.ToString());
            insert.Parameters.AddWithValue("$modelVersionId", modelVersionId.ToString());
            insert.Parameters.AddWithValue("$boundAt", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task BindAsync(RecipeModelBinding binding, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recipe_model_bindings (id, recipe_id, model_version_id, is_primary, is_preloaded, bound_at)
            VALUES ($id, $recipeId, $modelVersionId, $primary, $preloaded, $boundAt);
            """;
        command.Parameters.AddWithValue("$id", binding.Id.ToString());
        command.Parameters.AddWithValue("$recipeId", binding.RecipeId.ToString());
        command.Parameters.AddWithValue("$modelVersionId", binding.ModelVersionId.ToString());
        command.Parameters.AddWithValue("$primary", binding.IsPrimary ? 1 : 0);
        command.Parameters.AddWithValue("$preloaded", binding.IsPreloaded ? 1 : 0);
        command.Parameters.AddWithValue("$boundAt", binding.BoundAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqliteInspectionRecordRepository(SqliteConnectionFactory connectionFactory) : IInspectionRecordRepository
{
    public async Task AddAsync(InspectionRecord record, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inspection_records (
                id, recipe_id, model_version_id, batch_code, image_path, is_rejected,
                is_timed_out, captured_at, detections_json, eject_command_json
            ) VALUES (
                $id, $recipeId, $modelVersionId, $batchCode, $imagePath, $rejected,
                $timedOut, $capturedAt, $detections, $ejectCommand
            );
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$recipeId", record.RecipeId.ToString());
        command.Parameters.AddWithValue("$modelVersionId", record.ModelVersionId.ToString());
        command.Parameters.AddWithValue("$batchCode", record.BatchCode);
        command.Parameters.AddWithValue("$imagePath", record.ImagePath);
        command.Parameters.AddWithValue("$rejected", record.IsRejected ? 1 : 0);
        command.Parameters.AddWithValue("$timedOut", record.IsTimedOut ? 1 : 0);
        command.Parameters.AddWithValue("$capturedAt", record.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$detections", JsonSerializer.Serialize(record.Detections));
        command.Parameters.AddWithValue("$ejectCommand", record.EjectCommand is null ? DBNull.Value : JsonSerializer.Serialize(record.EjectCommand));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InspectionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipe_id, model_version_id, batch_code, image_path, is_rejected, is_timed_out, captured_at, detections_json, eject_command_json
            FROM inspection_records
            ORDER BY captured_at DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", take);

        var results = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var detections = JsonSerializer.Deserialize<List<DefectDetection>>(reader.GetString(8)) ?? [];
            var ejectCommand = reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<EjectCommand>(reader.GetString(9));

            results.Add(new InspectionRecord(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.GetInt32(6) == 1,
                DateTimeOffset.Parse(reader.GetString(7)),
                detections,
                ejectCommand));
        }

        return results;
    }
}

public sealed class SqliteAlarmRepository(SqliteConnectionFactory connectionFactory) : IAlarmRepository
{
    public async Task AddAsync(AlarmEvent alarmEvent, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO alarm_events (id, severity, source, code, message, raised_at, is_acknowledged)
            VALUES ($id, $severity, $source, $code, $message, $raisedAt, $acknowledged);
            """;
        command.Parameters.AddWithValue("$id", alarmEvent.Id.ToString());
        command.Parameters.AddWithValue("$severity", (int)alarmEvent.Severity);
        command.Parameters.AddWithValue("$source", alarmEvent.Source);
        command.Parameters.AddWithValue("$code", alarmEvent.Code);
        command.Parameters.AddWithValue("$message", alarmEvent.Message);
        command.Parameters.AddWithValue("$raisedAt", alarmEvent.RaisedAt.ToString("O"));
        command.Parameters.AddWithValue("$acknowledged", alarmEvent.IsAcknowledged ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlarmEvent>> GetActiveAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, severity, source, code, message, raised_at, is_acknowledged
            FROM alarm_events
            WHERE is_acknowledged = 0
            ORDER BY raised_at DESC;
            """;

        var results = new List<AlarmEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AlarmEvent(
                Guid.Parse(reader.GetString(0)),
                (AlarmSeverity)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.GetInt32(6) == 1));
        }

        return results;
    }
}

public sealed class SqliteUserRepository(SqliteConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<IReadOnlyList<UserAccount>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, user_name, display_name, role, is_enabled FROM user_accounts ORDER BY role DESC, user_name;";

        var results = new List<UserAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new UserAccount(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                (UserRole)reader.GetInt32(3),
                reader.GetInt32(4) == 1));
        }

        return results;
    }
}

public sealed class InMemoryRuntimeStateStore : IRuntimeStateStore
{
    private RuntimeSnapshot _snapshot = new(
        RuntimeMode.Stopped,
        DeviceState.Idle,
        "花蛤标准线",
        "花蛤-v2",
        "BATCH-20260601-01",
        0,
        0,
        100m,
        DateTimeOffset.UtcNow,
        [],
        []);

    public RuntimeSnapshot GetSnapshot() => _snapshot;

    public void Update(RuntimeSnapshot snapshot) => _snapshot = snapshot;
}

public sealed class SimulatedDeviceHealthProvider : IDeviceHealthProvider
{
    public Task<IReadOnlyList<DeviceStatus>> GetStatusesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceStatus> devices =
        [
            new("xray-source", DeviceState.Running, "X光源运行正常", DateTimeOffset.UtcNow),
            new("detector", DeviceState.Running, "探测器同步正常", DateTimeOffset.UtcNow),
            new("encoder", DeviceState.Running, "编码器脉冲稳定", DateTimeOffset.UtcNow),
            new("ejector-bank-a", DeviceState.Running, "气吹通道正常", DateTimeOffset.UtcNow)
        ];

        return Task.FromResult(devices);
    }
}

public sealed class SimulatedEjectorController : IEjectorController
{
    public Task ExecuteAsync(EjectCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SimulatedImageSource : IImageSource
{
    private static readonly Guid DefaultRecipeId = SeedData.VenusRecipeId;
    private static readonly Guid DefaultModelId = SeedData.VenusModelV2Id;

    public async IAsyncEnumerable<InferenceRequest> CaptureAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var frame = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            frame++;
            yield return new InferenceRequest(DefaultRecipeId, DefaultModelId, $"FRAME-{frame:D6}", [], DateTimeOffset.UtcNow);
            await Task.Delay(300, cancellationToken);
        }
    }
}

public sealed class SimulatedInferenceEngine : IInferenceEngine
{
    private readonly Random _random = new();

    public Task<InferenceResult> RunAsync(InferenceRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var detections = new List<DefectDetection>();
        if (_random.NextDouble() > 0.45)
        {
            detections.Add(new DefectDetection(
                Guid.NewGuid(),
                _random.NextDouble() > 0.5 ? "泥包" : "空壳",
                0.72m,
                _random.Next(100, 500),
                _random.Next(50, 900),
                96,
                96));
        }

        return Task.FromResult(new InferenceResult(request.FrameId, false, detections, DateTimeOffset.UtcNow));
    }
}
