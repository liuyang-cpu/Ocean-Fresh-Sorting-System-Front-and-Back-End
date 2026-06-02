using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.Infrastructure;

namespace OceanFresh.SortingSystem.Tests;

public sealed class ModelRegistryServiceTests
{
    [Fact]
    public async Task SeafoodProductRepository_RejectsDuplicateCode()
    {
        var connectionFactory = new SqliteConnectionFactory();
        var initializer = new SqliteDatabaseInitializer(connectionFactory);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new SqliteSeafoodProductRepository(connectionFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpsertAsync(new SeafoodProduct(Guid.NewGuid(), "SP-HG", "重复花蛤", true), CancellationToken.None));
    }

    [Fact]
    public async Task SeafoodProductRepository_DoesNotAllowChangingCodeAfterCreate()
    {
        var connectionFactory = new SqliteConnectionFactory();
        var initializer = new SqliteDatabaseInitializer(connectionFactory);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new SqliteSeafoodProductRepository(connectionFactory);

        var product = await repository.GetByIdAsync(Guid.Parse("e1a17fd7-c7a3-4cd2-921d-f05d05b00577"), CancellationToken.None);
        Assert.NotNull(product);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpsertAsync(product! with { Code = "SP-NEW" }, CancellationToken.None));
    }

    [Fact]
    public async Task ActivateAsync_SetsRequestedModelAsPrimary()
    {
        var connectionFactory = new SqliteConnectionFactory();
        var initializer = new SqliteDatabaseInitializer(connectionFactory);
        await initializer.InitializeAsync(CancellationToken.None);
        var modelRepository = new SqliteModelRegistryRepository(connectionFactory);
        var bindingRepository = new SqliteRecipeModelBindingRepository(connectionFactory);
        var service = new ModelRegistryService(modelRepository, bindingRepository);

        var recipeId = Guid.Parse("6f1503ef-f421-4248-b0b9-7a1915b4eb6a");
        var targetModelId = Guid.Parse("5db4c85a-8362-4b39-a95c-dddb14fbe4cf");

        await service.ActivateAsync(recipeId, targetModelId, CancellationToken.None);

        var bindings = await bindingRepository.GetByRecipeAsync(recipeId, CancellationToken.None);
        Assert.Contains(bindings, x => x.ModelVersionId == targetModelId && x.IsPrimary);
    }

    [Fact]
    public void BuildCommand_MapsDetectionToNozzleAndEncoderWindow()
    {
        var locator = new PixelToEjectLocator();
        var recipe = new ProductRecipe(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "花蛤标准线",
            1.5m,
            1536,
            300,
            40m,
            8m,
            DefectHandlingAction.Sink,
            "正常",
            128,
            50,
            57,
            1464,
            true);

        var command = locator.BuildCommand(
            recipe,
            new DefectDetection(Guid.NewGuid(), "砂石", 0.81m, 260, 100, 96, 96),
            DefectHandlingAction.Sink,
            DateTimeOffset.UtcNow);

        Assert.Equal(2, command.NozzleNumber);
        Assert.Equal(157, command.TriggerEncoderPosition);
        Assert.Equal(128, command.TriggerDelayMicroseconds);
    }

    [Fact]
    public void Resolve_ReturnsPassForNormalAndSinkForDefect()
    {
        var recipe = new ProductRecipe(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "油蛤标准线",
            1.6m,
            1536,
            300,
            40m,
            8m,
            DefectHandlingAction.Sink,
            "正常",
            120,
            40,
            50,
            1320,
            true);

        Assert.Equal(DefectHandlingAction.Pass, DefectActionPolicy.Resolve(recipe, "正常"));
        Assert.Equal(DefectHandlingAction.Sink, DefectActionPolicy.Resolve(recipe, "泥包"));
        Assert.Equal(DefectHandlingAction.Sink, DefectActionPolicy.Resolve(recipe, "碎壳"));
    }
}
