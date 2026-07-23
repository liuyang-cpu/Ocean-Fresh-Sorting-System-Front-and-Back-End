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
    public static readonly Guid VenusModelV1Id = Guid.Parse("5db4c85a-8362-4b39-a95c-dddb14fbe4cf");
    public static readonly Guid VenusModelV2Id = Guid.Parse("b90e8381-8730-4ef0-bc97-75d6050d6169");
    public static readonly Guid OilModelV1Id = Guid.Parse("6ea4a818-e0e0-4cd4-9628-ca53abcc1216");
    public static readonly Guid SurfModelV1Id = Guid.Parse("d4493887-c43e-4cf2-a31b-af503459de17");
    public static readonly Guid Channel1Id = Guid.Parse("6af90327-3063-49fd-a0b5-a7e315d4b2e7");
    public static readonly Guid Channel2Id = Guid.Parse("f3617c3e-17c6-4822-b38f-2ec91a0a1ef2");
    public static readonly Guid ConveyorDeviceId = Guid.Parse("11c2e1de-01a6-4a4b-8243-9bc6b7905821");
    public static readonly Guid XrayDetectorDeviceId = Guid.Parse("cd5f2c88-0bd6-44e9-89e8-399d71d559c9");
    public static readonly Guid EjectorDeviceId = Guid.Parse("86c6806a-8dbc-47da-8f49-7d98268e4a71");
    public static readonly Guid XraySourceDeviceId = Guid.Parse("c7f5d014-21e4-4d89-9e3f-a889092a8c17");
}

public sealed class SqliteConnectionFactory
{
    public SqliteConnectionFactory()
    {
        var dataDirectory = OceanFreshPaths.DataDirectory;
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
            CREATE TABLE IF NOT EXISTS model_versions (
                id TEXT PRIMARY KEY,
                seafood_category_id TEXT NOT NULL,
                version TEXT NOT NULL,
                source_weight_path TEXT NOT NULL,
                notes TEXT NOT NULL,
                status INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                training_image_size INTEGER NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS inspection_records (
                id TEXT PRIMARY KEY,
                recipe_id TEXT NOT NULL,
                model_version_id TEXT NOT NULL,
                detection_session_id TEXT NULL,
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
            CREATE TABLE IF NOT EXISTS detection_sessions (
                id TEXT PRIMARY KEY,
                session_code TEXT NOT NULL,
                channel_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                model_version_id TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                status INTEGER NOT NULL,
                data_source_mode INTEGER NOT NULL DEFAULT 1,
                is_hardware_execution_enabled INTEGER NOT NULL DEFAULT 1
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS manual_review_records (
                id TEXT PRIMARY KEY,
                detection_session_id TEXT NOT NULL,
                inspection_record_id TEXT NOT NULL,
                detection_id TEXT NOT NULL,
                model_label TEXT NOT NULL,
                human_label TEXT NOT NULL,
                judgement INTEGER NOT NULL,
                reviewer TEXT NOT NULL,
                notes TEXT NOT NULL,
                reviewed_at TEXT NOT NULL
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
            CREATE TABLE IF NOT EXISTS hardware_devices (
                id TEXT PRIMARY KEY,
                device_no TEXT NOT NULL,
                name TEXT NOT NULL,
                type INTEGER NOT NULL,
                firmware_version TEXT NOT NULL,
                state INTEGER NOT NULL,
                last_self_check_at TEXT NULL,
                last_self_check_result TEXT NOT NULL,
                is_enabled INTEGER NOT NULL,
                notes TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS user_accounts (
                id TEXT PRIMARY KEY,
                user_name TEXT NOT NULL,
                display_name TEXT NOT NULL,
                role INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL,
                password_hash TEXT NOT NULL DEFAULT '',
                last_login_at TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS operation_audit_logs (
                id TEXT PRIMARY KEY,
                category INTEGER NOT NULL,
                action_code TEXT NOT NULL,
                summary TEXT NOT NULL,
                actor_user_name TEXT NOT NULL,
                actor_display_name TEXT NOT NULL,
                actor_role INTEGER NOT NULL,
                target_type TEXT NOT NULL,
                target_id TEXT NOT NULL,
                target_name TEXT NOT NULL,
                details_json TEXT NOT NULL,
                dedupe_key TEXT NULL,
                occurred_at TEXT NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS idx_operation_audit_logs_dedupe_key
            ON operation_audit_logs(dedupe_key)
            WHERE dedupe_key IS NOT NULL;
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_operation_audit_logs_occurred_at
            ON operation_audit_logs(occurred_at DESC);
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_operation_audit_logs_actor
            ON operation_audit_logs(actor_user_name, occurred_at DESC);
            """,
            """
            CREATE TABLE IF NOT EXISTS seafood_products (
                id TEXT PRIMARY KEY,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                is_enabled INTEGER NOT NULL,
                classes_file_path TEXT NOT NULL DEFAULT '',
                label_map_json TEXT NOT NULL DEFAULT '{}',
                predict_config_path TEXT NOT NULL DEFAULT ''
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
                model_path TEXT NOT NULL DEFAULT '',
                defect_handling_action INTEGER NOT NULL,
                conveyor_speed REAL NOT NULL,
                confidence_threshold REAL NOT NULL,
                is_enabled INTEGER NOT NULL,
                last_runtime_predict_config_path TEXT NULL,
                last_runtime_output_directory TEXT NULL,
                camera_to_eject_distance_mm REAL NOT NULL DEFAULT 420,
                mm_per_pixel_y REAL NOT NULL DEFAULT 1,
                software_latency_ms INTEGER NOT NULL DEFAULT 40,
                actuator_delay_ms INTEGER NOT NULL DEFAULT 25,
                horizontal_lane_mapping_json TEXT NOT NULL DEFAULT '[]'
            );
            """
        };

        foreach (var sql in commands)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await MigrateModelVersionsAsync(connection, cancellationToken);
        await ExecuteAsync(
            connection,
            "UPDATE model_versions SET status = 0 WHERE status NOT IN (0, 9);",
            [],
            cancellationToken);
        await MigrateChannelConfigsAsync(connection, cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "model_path", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "last_runtime_predict_config_path", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "last_runtime_output_directory", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "camera_to_eject_distance_mm", "REAL NOT NULL DEFAULT 420", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "mm_per_pixel_y", "REAL NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "software_latency_ms", "INTEGER NOT NULL DEFAULT 40", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "actuator_delay_ms", "INTEGER NOT NULL DEFAULT 25", cancellationToken);
        await EnsureColumnAsync(connection, "channel_configs", "horizontal_lane_mapping_json", "TEXT NOT NULL DEFAULT '[]'", cancellationToken);
        await EnsureColumnAsync(connection, "seafood_products", "classes_file_path", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "seafood_products", "label_map_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await EnsureColumnAsync(connection, "seafood_products", "predict_config_path", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "user_accounts", "password_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "user_accounts", "last_login_at", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "inspection_records", "detection_session_id", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "detection_sessions", "data_source_mode", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "detection_sessions", "is_hardware_execution_enabled", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureUniqueIndexAsync(connection, "idx_manual_review_records_detection_id", "manual_review_records", "detection_id", cancellationToken);
        await EnsureUniqueIndexAsync(connection, "idx_seafood_products_code", "seafood_products", "code", cancellationToken);
        await EnsureUniqueIndexAsync(connection, "idx_user_accounts_user_name", "user_accounts", "user_name", cancellationToken);
        await EnsureUniqueIndexAsync(connection, "idx_hardware_devices_device_no", "hardware_devices", "device_no", cancellationToken);
        await DropLegacyTableIfExistsAsync(connection, "recipe_model_bindings", cancellationToken);
        await DropLegacyTableIfExistsAsync(connection, "product_recipes", cancellationToken);

        await SeedAsync(connection, cancellationToken);
        await EnsureModernProductSeedAsync(connection, cancellationToken);
        await EnsureHardwareDeviceSeedAsync(connection, cancellationToken);
        await EnsureXraySourceDeviceSeedAsync(connection, cancellationToken);
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

        var now = DateTimeOffset.UtcNow;
        await ExecuteAsync(connection, """
            INSERT INTO model_versions (
                id, seafood_category_id, version, source_weight_path,
                notes, status, created_at
            ) VALUES
            ($id1, $cat1, '花蛤-v1', 'models\venus_clam\v1\best.pt', '初始稳定版', 0, $dt1),
            ($id2, $cat1, '花蛤-v2', 'models\venus_clam\v2\best.pt', '增强空心和砂石识别', 0, $dt2),
            ($id3, $cat2, '油蛤-v1', 'models\oil_clam\v1\best.pt', '油蛤标准模型', 0, $dt3);
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
            INSERT INTO user_accounts (id, user_name, display_name, role, is_enabled, password_hash, last_login_at) VALUES
            ($id1, 'operator', '操作员', 1, 1, '', NULL),
            ($id2, 'admin', '管理员', 2, 1, '', NULL);
            """,
            [
                ("$id1", Guid.NewGuid().ToString()),
                ("$id2", Guid.NewGuid().ToString())
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO seafood_products (id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path) VALUES
            ($id1, 'SP-YG', '油蛤', 1, 'predict\\youge\\oil_clam.classes.txt', '{"0":"碎壳","1":"正常","2":"泥包","3":"空壳"}', ''),
            ($id2, 'SP-HG', '花蛤', 1, 'predict\\youge\\venus_clam.classes.txt', '{"0":"正常","1":"碎壳","2":"泥包","3":"空壳"}', ''),
            ($id3, 'SP-MB', '美贝', 1, 'predict\\youge\\surf_clam.classes.txt', '{"0":"正常","1":"碎壳","2":"泥包","3":"空壳"}', '');
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
                id, seafood_category_id, version, source_weight_path,
                notes, status, created_at
            ) VALUES
            ($id1, $cat1, '美贝-v1', 'models\surf_clam\v1\best.pt', '美贝基础模型', 0, $dt1)
            ON CONFLICT(id) DO NOTHING;
            """,
            [
                ("$id1", SeedData.SurfModelV1Id.ToString()),
                ("$cat1", SeedData.SurfClamCategoryId.ToString()),
                ("$dt1", now.AddDays(-1).ToString("O"))
            ], cancellationToken);

        await ExecuteAsync(connection, """
            INSERT INTO channel_configs (
                id, channel_no, name, seafood_product_id, model_version_id, model_path, defect_handling_action,
                conveyor_speed, confidence_threshold, is_enabled,
                camera_to_eject_distance_mm, mm_per_pixel_y, software_latency_ms, actuator_delay_ms, horizontal_lane_mapping_json
            ) VALUES
            ($id1, 1, '1号通道', $product1, $model1, 'models\\oil_clam\\v1\\best.pt', 1, 1.8, 0.58, 1, 420, 1, 40, 25, '[{\"nozzleNumber\":1,\"startX\":0,\"endX\":383},{\"nozzleNumber\":2,\"startX\":384,\"endX\":767},{\"nozzleNumber\":3,\"startX\":768,\"endX\":1151},{\"nozzleNumber\":4,\"startX\":1152,\"endX\":1536}]'),
            ($id2, 2, '2号通道', $product2, $model2, 'models\\venus_clam\\v2\\best.pt', 1, 1.5, 0.60, 0, 420, 1, 40, 25, '[{\"nozzleNumber\":1,\"startX\":0,\"endX\":383},{\"nozzleNumber\":2,\"startX\":384,\"endX\":767},{\"nozzleNumber\":3,\"startX\":768,\"endX\":1151},{\"nozzleNumber\":4,\"startX\":1152,\"endX\":1536}]');
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
                INSERT INTO seafood_products (id, code, name, is_enabled, classes_file_path, label_map_json, predict_config_path) VALUES
                ($id1, 'SP-YG', '油蛤', 1, 'predict\\youge\\oil_clam.classes.txt', '{"0":"碎壳","1":"正常","2":"泥包","3":"空壳"}', ''),
                ($id2, 'SP-HG', '花蛤', 1, 'predict\\youge\\venus_clam.classes.txt', '{"0":"正常","1":"碎壳","2":"泥包","3":"空壳"}', ''),
                ($id3, 'SP-MB', '美贝', 1, 'predict\\youge\\surf_clam.classes.txt', '{"0":"正常","1":"碎壳","2":"泥包","3":"空壳"}', '');
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
                    id, channel_no, name, seafood_product_id, model_version_id, model_path, defect_handling_action,
                    conveyor_speed, confidence_threshold, is_enabled, camera_to_eject_distance_mm, mm_per_pixel_y, software_latency_ms, actuator_delay_ms, horizontal_lane_mapping_json
                ) VALUES
                ($id1, 1, '1号通道', $product1, $model1, 'models\\oil_clam\\v1\\best.pt', 1, 1.8, 0.58, 1, 420, 1, 40, 25, '[{\"nozzleNumber\":1,\"startX\":0,\"endX\":383},{\"nozzleNumber\":2,\"startX\":384,\"endX\":767},{\"nozzleNumber\":3,\"startX\":768,\"endX\":1151},{\"nozzleNumber\":4,\"startX\":1152,\"endX\":1536}]'),
                ($id2, 2, '2号通道', $product2, $model2, 'models\\venus_clam\\v2\\best.pt', 1, 1.5, 0.60, 1, 420, 1, 40, 25, '[{\"nozzleNumber\":1,\"startX\":0,\"endX\":383},{\"nozzleNumber\":2,\"startX\":384,\"endX\":767},{\"nozzleNumber\":3,\"startX\":768,\"endX\":1151},{\"nozzleNumber\":4,\"startX\":1152,\"endX\":1536}]');
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
                    id, seafood_category_id, version, source_weight_path,
                    notes, status, created_at
                ) VALUES
                ($id1, $cat1, '美贝-v1', 'models\surf_clam\v1\best.pt', '美贝基础模型', 2, $dt1);
                """,
                [
                    ("$id1", SeedData.SurfModelV1Id.ToString()),
                    ("$cat1", SeedData.SurfClamCategoryId.ToString()),
                    ("$dt1", now.AddDays(-1).ToString("O"))
                ], cancellationToken);
        }
    }

    private static async Task EnsureHardwareDeviceSeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (await TableHasRowsAsync(connection, "hardware_devices", cancellationToken))
        {
            return;
        }

        await ExecuteAsync(connection, """
            INSERT INTO hardware_devices (
                id, device_no, name, type, firmware_version, state,
                last_self_check_at, last_self_check_result, is_enabled, notes
            ) VALUES
            ($id1, 'CV-001', '传送带', 1, 'FW-CV-1.0.0', 1, NULL, '尚未自检', 1, '负责连续输送 X 光图像对应的实物。'),
            ($id2, 'XR-001', 'X 光探测器', 2, 'FW-XR-1.0.0', 1, NULL, '尚未自检', 1, '负责输出 1536 x 300 长条 X 光图像。'),
            ($id3, 'EJ-001', '剔除设备', 3, 'FW-EJ-1.0.0', 1, NULL, '尚未自检', 1, '负责执行气吹、推杆、下沉或停机等动作。'),
            ($id4, 'XS-001', 'X 光光源', 5, 'FW-XS-1.0.0', 1, NULL, '尚未自检', 1, '负责高压使能、光源就绪和辐射联锁状态。');
            """,
            [
                ("$id1", SeedData.ConveyorDeviceId.ToString()),
                ("$id2", SeedData.XrayDetectorDeviceId.ToString()),
                ("$id3", SeedData.EjectorDeviceId.ToString()),
                ("$id4", SeedData.XraySourceDeviceId.ToString())
            ], cancellationToken);
    }

    private static async Task EnsureXraySourceDeviceSeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM hardware_devices WHERE device_no = 'XS-001';";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (count > 0)
        {
            return;
        }

        await ExecuteAsync(connection, """
            INSERT INTO hardware_devices (
                id, device_no, name, type, firmware_version, state,
                last_self_check_at, last_self_check_result, is_enabled, notes
            ) VALUES
            ($id, 'XS-001', 'X 光光源', 5, 'FW-XS-1.0.0', 1, NULL, '尚未自检', 1, '负责高压使能、光源就绪和辐射联锁状态。');
            """,
            [
                ("$id", SeedData.XraySourceDeviceId.ToString())
            ], cancellationToken);
    }

    private static async Task MigrateModelVersionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columns = await GetColumnNamesAsync(connection, "model_versions", cancellationToken);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "seafood_category_id",
            "version",
            "source_weight_path",
            "notes",
            "status",
            "created_at",
            "training_image_size"
        };

        if (columns.SetEquals(expected))
        {
            return;
        }

        var statusProjection = columns.Contains("status")
            ? "CASE WHEN status = 9 THEN 9 ELSE 0 END"
            : "0";
        var trainingImageSizeProjection = columns.Contains("training_image_size")
            ? "training_image_size"
            : "NULL";
        var createdAtProjection = columns.Contains("created_at")
            ? "COALESCE(created_at, CURRENT_TIMESTAMP)"
            : columns.Contains("exported_at")
                ? "COALESCE(exported_at, CURRENT_TIMESTAMP)"
                : "CURRENT_TIMESTAMP";
        var command = connection.CreateCommand();
        command.CommandText = $$"""
            ALTER TABLE model_versions RENAME TO model_versions_legacy;

            CREATE TABLE model_versions (
                id TEXT PRIMARY KEY,
                seafood_category_id TEXT NOT NULL,
                version TEXT NOT NULL,
                source_weight_path TEXT NOT NULL,
                notes TEXT NOT NULL,
                status INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                training_image_size INTEGER NULL
            );

            INSERT INTO model_versions (id, seafood_category_id, version, source_weight_path, notes, status, created_at, training_image_size)
            SELECT
                id,
                seafood_category_id,
                version,
                source_weight_path,
                COALESCE(notes, ''),
                {{statusProjection}},
                {{createdAtProjection}},
                {{trainingImageSizeProjection}}
            FROM model_versions_legacy;

            DROP TABLE model_versions_legacy;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateChannelConfigsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columns = await GetColumnNamesAsync(connection, "channel_configs", cancellationToken);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "channel_no",
            "name",
            "seafood_product_id",
            "model_version_id",
            "model_path",
            "defect_handling_action",
            "conveyor_speed",
            "confidence_threshold",
            "is_enabled",
            "last_runtime_predict_config_path",
            "last_runtime_output_directory",
            "camera_to_eject_distance_mm",
            "mm_per_pixel_y",
            "software_latency_ms",
            "actuator_delay_ms",
            "horizontal_lane_mapping_json"
        };

        if (columns.SetEquals(expected))
        {
            return;
        }

        var command = connection.CreateCommand();
        command.CommandText = """
            ALTER TABLE channel_configs RENAME TO channel_configs_legacy;

            CREATE TABLE channel_configs (
                id TEXT PRIMARY KEY,
                channel_no INTEGER NOT NULL,
                name TEXT NOT NULL,
                seafood_product_id TEXT NOT NULL,
                model_version_id TEXT NULL,
                model_path TEXT NOT NULL DEFAULT '',
                defect_handling_action INTEGER NOT NULL,
                conveyor_speed REAL NOT NULL,
                confidence_threshold REAL NOT NULL,
                is_enabled INTEGER NOT NULL,
                last_runtime_predict_config_path TEXT NULL,
                last_runtime_output_directory TEXT NULL,
                camera_to_eject_distance_mm REAL NOT NULL DEFAULT 420,
                mm_per_pixel_y REAL NOT NULL DEFAULT 1,
                software_latency_ms INTEGER NOT NULL DEFAULT 40,
                actuator_delay_ms INTEGER NOT NULL DEFAULT 25,
                horizontal_lane_mapping_json TEXT NOT NULL DEFAULT '[]'
            );

            INSERT INTO channel_configs (
                id, channel_no, name, seafood_product_id, model_version_id, model_path, defect_handling_action,
                conveyor_speed, confidence_threshold, is_enabled, last_runtime_predict_config_path, last_runtime_output_directory,
                camera_to_eject_distance_mm, mm_per_pixel_y, software_latency_ms, actuator_delay_ms, horizontal_lane_mapping_json
            )
            SELECT
                id,
                channel_no,
                name,
                seafood_product_id,
                model_version_id,
                COALESCE(model_path, ''),
                defect_handling_action,
                conveyor_speed,
                confidence_threshold,
                is_enabled,
                last_runtime_predict_config_path,
                last_runtime_output_directory,
                COALESCE(camera_to_eject_distance_mm, 420),
                COALESCE(mm_per_pixel_y, 1),
                COALESCE(software_latency_ms, 40),
                COALESCE(actuator_delay_ms, 25),
                COALESCE(horizontal_lane_mapping_json, '[]')
            FROM channel_configs_legacy;

            DROP TABLE channel_configs_legacy;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
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

    private static async Task DropLegacyTableIfExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS {tableName};";
        await command.ExecuteNonQueryAsync(cancellationToken);
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

public sealed class SqliteModelRegistryRepository(SqliteConnectionFactory connectionFactory) : IModelRegistryRepository
{
    public Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        QueryAsync("SELECT * FROM model_versions WHERE seafood_category_id = $categoryId AND status <> 9 ORDER BY created_at DESC;", ("$categoryId", categoryId.ToString()), cancellationToken);

    public async Task<ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var results = await QueryAsync("SELECT * FROM model_versions WHERE id = $id AND status <> 9 LIMIT 1;", ("$id", id.ToString()), cancellationToken);
        return results.FirstOrDefault();
    }

    public Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
        QueryAsync("SELECT * FROM model_versions WHERE status <> 9 ORDER BY created_at DESC;", cancellationToken: cancellationToken);

    public async Task<ModelVersion> UpsertAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO model_versions (
                id, seafood_category_id, version, source_weight_path,
                notes, status, created_at, training_image_size
            ) VALUES (
                $id, $categoryId, $version, $sourcePath, $notes, $status, $createdAt, $trainingImageSize
            )
            ON CONFLICT(id) DO UPDATE SET
                seafood_category_id = excluded.seafood_category_id,
                version = excluded.version,
                source_weight_path = excluded.source_weight_path,
                notes = excluded.notes,
                status = excluded.status,
                created_at = excluded.created_at,
                training_image_size = excluded.training_image_size;
            """;
        BindModel(command, modelVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return modelVersion;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE model_versions SET status = 9 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                (ModelStatus)reader.GetInt32(5),
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }

        return results;
    }

    private static void BindModel(SqliteCommand command, ModelVersion modelVersion)
    {
        command.Parameters.AddWithValue("$id", modelVersion.Id.ToString());
        command.Parameters.AddWithValue("$categoryId", modelVersion.SeafoodCategoryId.ToString());
        command.Parameters.AddWithValue("$version", modelVersion.Version);
        command.Parameters.AddWithValue("$sourcePath", modelVersion.SourceWeightPath);
        command.Parameters.AddWithValue("$notes", modelVersion.Notes);
        command.Parameters.AddWithValue("$status", (int)modelVersion.Status);
        command.Parameters.AddWithValue("$createdAt", modelVersion.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$trainingImageSize", (object?)modelVersion.TrainingImageSize ?? DBNull.Value);
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
                id, recipe_id, model_version_id, detection_session_id, batch_code, image_path, is_rejected,
                is_timed_out, captured_at, detections_json, eject_command_json
            ) VALUES (
                $id, $recipeId, $modelVersionId, $sessionId, $batchCode, $imagePath, $rejected,
                $timedOut, $capturedAt, $detections, $ejectCommand
            );
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$recipeId", record.RecipeId.ToString());
        command.Parameters.AddWithValue("$modelVersionId", record.ModelVersionId.ToString());
        command.Parameters.AddWithValue("$sessionId", record.DetectionSessionId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$batchCode", record.BatchCode);
        command.Parameters.AddWithValue("$imagePath", record.ImagePath);
        command.Parameters.AddWithValue("$rejected", record.IsRejected ? 1 : 0);
        command.Parameters.AddWithValue("$timedOut", record.IsTimedOut ? 1 : 0);
        command.Parameters.AddWithValue("$capturedAt", record.CapturedAt.ToString("O"));
        command.Parameters.AddWithValue("$detections", JsonSerializer.Serialize(record.Detections));
        command.Parameters.AddWithValue("$ejectCommand", JsonSerializer.Serialize(record.EjectCommands));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InspectionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipe_id, model_version_id, detection_session_id, batch_code, image_path, is_rejected, is_timed_out, captured_at, detections_json, eject_command_json
            FROM inspection_records
            ORDER BY rowid DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", take);

        var results = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadInspectionRecord(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<InspectionRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipe_id, model_version_id, detection_session_id, batch_code, image_path, is_rejected, is_timed_out, captured_at, detections_json, eject_command_json
            FROM inspection_records
            WHERE detection_session_id = $sessionId
            ORDER BY captured_at ASC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString());

        var results = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadInspectionRecord(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<InspectionRecord>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recipe_id, model_version_id, detection_session_id, batch_code, image_path, is_rejected, is_timed_out, captured_at, detections_json, eject_command_json
            FROM inspection_records
            WHERE captured_at >= $since
            ORDER BY captured_at DESC;
            """;
        command.Parameters.AddWithValue("$since", since.ToString("O"));

        var results = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadInspectionRecord(reader));
        }

        return results;
    }

    private static InspectionRecord ReadInspectionRecord(SqliteDataReader reader)
    {
        var detections = JsonSerializer.Deserialize<List<DefectDetection>>(reader.GetString(9)) ?? [];
        var ejectCommands = reader.IsDBNull(10)
                ? []
                : JsonSerializer.Deserialize<List<EjectCommand>>(reader.GetString(10)) ?? [];

        return new InspectionRecord(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6) == 1,
                reader.GetInt32(7) == 1,
                DateTimeOffset.Parse(reader.GetString(8)),
                detections,
                ejectCommands);
    }
}

public sealed class SqliteManualReviewRepository(SqliteConnectionFactory connectionFactory) : IManualReviewRepository
{
    public async Task<IReadOnlyList<ManualReviewRecord>> GetBySessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, detection_session_id, inspection_record_id, detection_id, model_label, human_label, judgement, reviewer, notes, reviewed_at
            FROM manual_review_records
            WHERE detection_session_id = $sessionId
            ORDER BY reviewed_at DESC;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString());

        var results = new List<ManualReviewRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadManualReview(reader));
        }

        return results;
    }

    public async Task<ManualReviewRecord> UpsertAsync(ManualReviewRecord reviewRecord, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO manual_review_records (
                id, detection_session_id, inspection_record_id, detection_id, model_label, human_label, judgement, reviewer, notes, reviewed_at
            ) VALUES (
                $id, $sessionId, $recordId, $detectionId, $modelLabel, $humanLabel, $judgement, $reviewer, $notes, $reviewedAt
            )
            ON CONFLICT(detection_id) DO UPDATE SET
                detection_session_id = excluded.detection_session_id,
                inspection_record_id = excluded.inspection_record_id,
                model_label = excluded.model_label,
                human_label = excluded.human_label,
                judgement = excluded.judgement,
                reviewer = excluded.reviewer,
                notes = excluded.notes,
                reviewed_at = excluded.reviewed_at;
            """;
        command.Parameters.AddWithValue("$id", reviewRecord.Id.ToString());
        command.Parameters.AddWithValue("$sessionId", reviewRecord.DetectionSessionId.ToString());
        command.Parameters.AddWithValue("$recordId", reviewRecord.InspectionRecordId.ToString());
        command.Parameters.AddWithValue("$detectionId", reviewRecord.DetectionId.ToString());
        command.Parameters.AddWithValue("$modelLabel", reviewRecord.ModelLabel);
        command.Parameters.AddWithValue("$humanLabel", reviewRecord.HumanLabel);
        command.Parameters.AddWithValue("$judgement", (int)reviewRecord.Judgement);
        command.Parameters.AddWithValue("$reviewer", reviewRecord.Reviewer);
        command.Parameters.AddWithValue("$notes", reviewRecord.Notes);
        command.Parameters.AddWithValue("$reviewedAt", reviewRecord.ReviewedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return reviewRecord;
    }

    private static ManualReviewRecord ReadManualReview(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.GetString(5),
            (ManualReviewJudgement)reader.GetInt32(6),
            reader.GetString(7),
            reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)));
}

public sealed class SqliteDetectionSessionRepository(SqliteConnectionFactory connectionFactory) : IDetectionSessionRepository
{
    public async Task<DetectionSession> AddAsync(DetectionSession session, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO detection_sessions (
                id, session_code, channel_id, product_id, model_version_id, started_at, ended_at, status, data_source_mode, is_hardware_execution_enabled
            ) VALUES (
                $id, $code, $channelId, $productId, $modelVersionId, $startedAt, $endedAt, $status, $dataSourceMode, $isHardwareExecutionEnabled
            );
            """;
        AddSessionParameters(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return session;
    }

    public async Task<DetectionSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_code, channel_id, product_id, model_version_id, started_at, ended_at, status, data_source_mode, is_hardware_execution_enabled
            FROM detection_sessions
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        return await ReadSingleSessionAsync(command, cancellationToken);
    }

    public async Task<DetectionSession?> GetActiveAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_code, channel_id, product_id, model_version_id, started_at, ended_at, status, data_source_mode, is_hardware_execution_enabled
            FROM detection_sessions
            WHERE status = $status
            ORDER BY started_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$status", (int)DetectionSessionStatus.Running);
        return await ReadSingleSessionAsync(command, cancellationToken);
    }

    public async Task<DetectionSession?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_code, channel_id, product_id, model_version_id, started_at, ended_at, status, data_source_mode, is_hardware_execution_enabled
            FROM detection_sessions
            ORDER BY started_at DESC
            LIMIT 1;
            """;
        return await ReadSingleSessionAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<DetectionSession>> GetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_code, channel_id, product_id, model_version_id, started_at, ended_at, status, data_source_mode, is_hardware_execution_enabled
            FROM detection_sessions
            WHERE started_at >= $since
            ORDER BY started_at DESC;
            """;
        command.Parameters.AddWithValue("$since", since.ToString("O"));

        var results = new List<DetectionSession>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSession(reader));
        }

        return results;
    }

    public async Task<DetectionSession> UpdateAsync(DetectionSession session, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE detection_sessions SET
                session_code = $code,
                channel_id = $channelId,
                product_id = $productId,
                model_version_id = $modelVersionId,
                started_at = $startedAt,
                ended_at = $endedAt,
                status = $status,
                data_source_mode = $dataSourceMode,
                is_hardware_execution_enabled = $isHardwareExecutionEnabled
            WHERE id = $id;
            """;
        AddSessionParameters(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return session;
    }

    private static void AddSessionParameters(SqliteCommand command, DetectionSession session)
    {
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$code", session.SessionCode);
        command.Parameters.AddWithValue("$channelId", session.ChannelId.ToString());
        command.Parameters.AddWithValue("$productId", session.ProductId.ToString());
        command.Parameters.AddWithValue("$modelVersionId", session.ModelVersionId.ToString());
        command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$endedAt", session.EndedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$dataSourceMode", (int)session.DataSourceMode);
        command.Parameters.AddWithValue("$isHardwareExecutionEnabled", session.IsHardwareExecutionEnabled ? 1 : 0);
    }

    private static async Task<DetectionSession?> ReadSingleSessionAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    private static DetectionSession ReadSession(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            (DetectionSessionStatus)reader.GetInt32(7),
            (RuntimeDataSourceMode)reader.GetInt32(8),
            reader.GetInt32(9) == 1);
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

    public async Task AcknowledgeAsync(Guid alarmId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE alarm_events SET is_acknowledged = 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", alarmId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqliteHardwareDeviceRepository(SqliteConnectionFactory connectionFactory) : IHardwareDeviceRepository
{
    public async Task<IReadOnlyList<HardwareDevice>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, device_no, name, type, firmware_version, state,
                   last_self_check_at, last_self_check_result, is_enabled, notes
            FROM hardware_devices
            ORDER BY type, device_no;
            """;

        return await ReadDevicesAsync(command, cancellationToken);
    }

    public async Task<HardwareDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, device_no, name, type, firmware_version, state,
                   last_self_check_at, last_self_check_result, is_enabled, notes
            FROM hardware_devices
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        var devices = await ReadDevicesAsync(command, cancellationToken);
        return devices.FirstOrDefault();
    }

    public async Task<HardwareDevice> UpsertAsync(HardwareDevice device, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO hardware_devices (
                id, device_no, name, type, firmware_version, state,
                last_self_check_at, last_self_check_result, is_enabled, notes
            ) VALUES (
                $id, $deviceNo, $name, $type, $firmwareVersion, $state,
                $lastSelfCheckAt, $lastSelfCheckResult, $enabled, $notes
            )
            ON CONFLICT(id) DO UPDATE SET
                device_no = excluded.device_no,
                name = excluded.name,
                type = excluded.type,
                firmware_version = excluded.firmware_version,
                state = excluded.state,
                last_self_check_at = excluded.last_self_check_at,
                last_self_check_result = excluded.last_self_check_result,
                is_enabled = excluded.is_enabled,
                notes = excluded.notes;
            """;
        command.Parameters.AddWithValue("$id", device.Id.ToString());
        command.Parameters.AddWithValue("$deviceNo", device.DeviceNo);
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$type", (int)device.Type);
        command.Parameters.AddWithValue("$firmwareVersion", device.FirmwareVersion);
        command.Parameters.AddWithValue("$state", (int)device.State);
        command.Parameters.AddWithValue("$lastSelfCheckAt", device.LastSelfCheckAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastSelfCheckResult", device.LastSelfCheckResult);
        command.Parameters.AddWithValue("$enabled", device.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$notes", device.Notes);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return device;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM hardware_devices WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<HardwareDevice>> ReadDevicesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<HardwareDevice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new HardwareDevice(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                (DeviceType)reader.GetInt32(3),
                reader.GetString(4),
                (DeviceState)reader.GetInt32(5),
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                reader.GetString(7),
                reader.GetInt32(8) == 1,
                reader.GetString(9)));
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
        command.CommandText = "SELECT id, user_name, display_name, role, is_enabled, password_hash, last_login_at FROM user_accounts ORDER BY role DESC, user_name;";

        return await ReadUsersAsync(command, cancellationToken);
    }

    public async Task<UserAccount?> GetByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, user_name, display_name, role, is_enabled, password_hash, last_login_at
            FROM user_accounts
            WHERE user_name = $userName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$userName", userName);

        return (await ReadUsersAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<UserAccount> UpsertAsync(UserAccount userAccount, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_accounts (
                id, user_name, display_name, role, is_enabled, password_hash, last_login_at
            ) VALUES (
                $id, $userName, $displayName, $role, $isEnabled, $passwordHash, $lastLoginAt
            )
            ON CONFLICT(id) DO UPDATE SET
                user_name = excluded.user_name,
                display_name = excluded.display_name,
                role = excluded.role,
                is_enabled = excluded.is_enabled,
                password_hash = excluded.password_hash,
                last_login_at = excluded.last_login_at;
            """;
        command.Parameters.AddWithValue("$id", userAccount.Id.ToString());
        command.Parameters.AddWithValue("$userName", userAccount.UserName);
        command.Parameters.AddWithValue("$displayName", userAccount.DisplayName);
        command.Parameters.AddWithValue("$role", (int)userAccount.Role);
        command.Parameters.AddWithValue("$isEnabled", userAccount.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$passwordHash", userAccount.PasswordHash);
        command.Parameters.AddWithValue("$lastLoginAt", userAccount.LastLoginAt?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return userAccount;
    }

    private static async Task<IReadOnlyList<UserAccount>> ReadUsersAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<UserAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new UserAccount(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                (UserRole)reader.GetInt32(3),
                reader.GetInt32(4) == 1,
                reader.GetString(5),
                reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))));
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
        [],
        null);

    public RuntimeSnapshot GetSnapshot() => _snapshot;

    public void Update(RuntimeSnapshot snapshot) => _snapshot = snapshot;
}

internal static class OceanFreshPaths
{
    private const string DataRootEnvVar = "OCEANFRESH_DATA_ROOT";

    public static string DataRoot
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataRootEnvVar);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                Directory.CreateDirectory(overridden);
                return overridden;
            }

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OceanFreshSortingSystem");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string DataDirectory => Ensure("data");

    public static string PredictConfigDirectory => Ensure("predict-configs");

    public static string ProductPredictConfigDirectory => Ensure("product-predict-configs");

    public static string PredictInputDirectory => Ensure("predict-input");

    public static string PredictOutputDirectory => Ensure("predict-output");

    public static string PredictLogDirectory => Ensure("predict-logs");

    private static string Ensure(string child)
    {
        var path = Path.Combine(DataRoot, child);
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class SimulatedHardwareProtocolClient(IHardwareDeviceRepository hardwareDeviceRepository) : IHardwareProtocolClient
{
    public async Task<IReadOnlyList<HardwareSignalStatus>> ReadSignalsAsync(CancellationToken cancellationToken)
    {
        var devices = await hardwareDeviceRepository.GetAllAsync(cancellationToken);
        if (devices.Count == 0)
        {
            return [];
        }

        return devices
            .Where(x => x.IsEnabled)
            .Select(x => new HardwareSignalStatus(
                x.DeviceNo,
                x.Type,
                x.State,
                MapSeverity(x.State),
                BuildSignalCode(x.Type, x.State),
                $"{x.Name}: {MapStateText(x.State)}",
                null,
                string.Empty,
                DateTimeOffset.UtcNow))
            .ToList();
    }

    private static HardwareSignalSeverity MapSeverity(DeviceState state) => state switch
    {
        DeviceState.Faulted or DeviceState.Offline => HardwareSignalSeverity.Critical,
        DeviceState.Warning => HardwareSignalSeverity.Warning,
        _ => HardwareSignalSeverity.Normal
    };

    private static string BuildSignalCode(DeviceType type, DeviceState state)
    {
        var typeCode = type switch
        {
            DeviceType.Conveyor => "CONVEYOR",
            DeviceType.XrayDetector => "XRAY_DETECTOR",
            DeviceType.Ejector => "EJECTOR",
            DeviceType.XraySource => "XRAY_SOURCE",
            DeviceType.Controller => "CONTROLLER",
            _ => "DEVICE"
        };

        return $"{typeCode}_{state.ToString().ToUpperInvariant()}";
    }

    private static string MapStateText(DeviceState state) => state switch
    {
        DeviceState.Offline => "离线",
        DeviceState.Idle => "待机",
        DeviceState.Running => "运行中",
        DeviceState.Warning => "预警",
        DeviceState.Faulted => "故障",
        _ => state.ToString()
    };
}

public sealed class SimulatedDeviceHealthProvider(IHardwareProtocolClient hardwareProtocolClient) : IDeviceHealthProvider
{
    public async Task<IReadOnlyList<DeviceStatus>> GetStatusesAsync(CancellationToken cancellationToken)
    {
        var signals = await hardwareProtocolClient.ReadSignalsAsync(cancellationToken);
        return signals
            .Select(signal => new DeviceStatus(
                signal.DeviceNo,
                signal.State,
                signal.Message,
                signal.UpdatedAt))
            .ToList();
    }
}

public sealed class SimulatedEjectorController : IEjectorController
{
    public Task ExecuteAsync(EjectCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SimulatedImageSource : IImageSource
{
    private static readonly Guid DefaultRecipeId = Guid.Empty;
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
