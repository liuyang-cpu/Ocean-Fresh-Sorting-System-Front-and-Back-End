using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.Infrastructure;

namespace OceanFresh.SortingSystem.Tests;

public sealed class ModelRegistryServiceTests
{
    [Fact]
    public async Task DatabaseInitializer_MigratesModelTableWithoutExportedAtColumn()
    {
        await WithIsolatedDataRootAsync(async () =>
        {
            var connectionFactory = new SqliteConnectionFactory();
            await using (var connection = connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE model_versions (
                        id TEXT PRIMARY KEY,
                        seafood_category_id TEXT NOT NULL,
                        version TEXT NOT NULL,
                        source_weight_path TEXT NOT NULL,
                        notes TEXT NOT NULL,
                        status INTEGER NOT NULL DEFAULT 0,
                        created_at TEXT NOT NULL
                    );
                    INSERT INTO model_versions VALUES (
                        '10000000-0000-0000-0000-000000000001',
                        '20000000-0000-0000-0000-000000000002',
                        'legacy-v1', 'legacy.pt', 'legacy', 0, '2026-01-01T00:00:00+00:00'
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var initializer = new SqliteDatabaseInitializer(connectionFactory);
            await initializer.InitializeAsync(CancellationToken.None);
            var repository = new SqliteModelRegistryRepository(connectionFactory);
            var model = await repository.GetByIdAsync(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                CancellationToken.None);

            Assert.NotNull(model);
            Assert.Null(model!.TrainingImageSize);
        });
    }

    [Fact]
    public async Task SeafoodProductRepository_RejectsDuplicateCode()
    {
        await WithIsolatedDataRootAsync(async () =>
        {
            var connectionFactory = new SqliteConnectionFactory();
            var initializer = new SqliteDatabaseInitializer(connectionFactory);
            await initializer.InitializeAsync(CancellationToken.None);
            var repository = new SqliteSeafoodProductRepository(connectionFactory);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpsertAsync(new SeafoodProduct(Guid.NewGuid(), "SP-HG", "重复花蛤", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", ""), CancellationToken.None));
        });
    }

    [Fact]
    public async Task SeafoodProductRepository_DoesNotAllowChangingCodeAfterCreate()
    {
        await WithIsolatedDataRootAsync(async () =>
        {
            var connectionFactory = new SqliteConnectionFactory();
            var initializer = new SqliteDatabaseInitializer(connectionFactory);
            await initializer.InitializeAsync(CancellationToken.None);
            var repository = new SqliteSeafoodProductRepository(connectionFactory);

            var product = await repository.GetByIdAsync(Guid.Parse("e1a17fd7-c7a3-4cd2-921d-f05d05b00577"), CancellationToken.None);
            Assert.NotNull(product);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.UpsertAsync(product! with { Code = "SP-NEW" }, CancellationToken.None));
        });
    }

    [Fact]
    public void BuildCommand_MapsDetectionToNozzleAndTiming()
    {
        var locator = new PixelToEjectLocator();
        var channel = new ChannelConfig(
            Guid.NewGuid(),
            1,
            "1号通道",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "model.pt",
            DefectHandlingAction.Sink,
            0.6m,
            true,
            null,
            null,
            420m,
            1m,
            40,
            25,
            """[{"nozzleNumber":1,"startX":0,"endX":383},{"nozzleNumber":2,"startX":384,"endX":767},{"nozzleNumber":3,"startX":768,"endX":1151},{"nozzleNumber":4,"startX":1152,"endX":1536}]""");

        var command = locator.BuildCommand(
            channel,
            new DefectDetection(Guid.NewGuid(), "砂石", 0.81m, 260, 100, 96, 96),
            DefectHandlingAction.Sink,
            DateTimeOffset.UtcNow);

        Assert.Equal(1, command.NozzleNumber);
        Assert.Equal(148, command.TriggerEncoderPosition);
        Assert.True(command.TriggerDelayMicroseconds > 0);
    }

    private static async Task WithIsolatedDataRootAsync(Func<Task> action)
    {
        var original = Environment.GetEnvironmentVariable("OCEANFRESH_DATA_ROOT");
        var tempRoot = Path.Combine(Path.GetTempPath(), "OceanFreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("OCEANFRESH_DATA_ROOT", tempRoot);
        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OCEANFRESH_DATA_ROOT", original);
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }
}
