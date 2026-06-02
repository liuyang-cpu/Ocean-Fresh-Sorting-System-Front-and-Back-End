using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Tests;

public sealed class ProductAndChannelWorkflowTests
{
    [Fact]
    public async Task DeleteProductAsync_ThrowsReadableMessage_WhenProductIsStillBoundToChannel()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9001", "测试油蛤", true)
        ]);
        var traitRepository = new FakeSeafoodTraitRepository();
        var channelRepository = new FakeChannelConfigRepository([
            new ChannelConfig(
                Guid.NewGuid(),
                1,
                "1号通道",
                productId,
                null,
                DefectHandlingAction.Sink,
                1536,
                300,
                1.8m,
                50m,
                6m,
                0.58m,
                true)
        ]);
        var service = new SeafoodProductService(productRepository, traitRepository, channelRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(productId, CancellationToken.None));

        Assert.Equal("该海鲜产品仍被通道使用，不能删除。请先解除通道绑定。", exception.Message);
    }

    [Fact]
    public async Task DeleteProductAsync_DeletesProduct_WhenNoChannelUsesIt()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9002", "可删产品", true)
        ]);
        var traitRepository = new FakeSeafoodTraitRepository();
        var channelRepository = new FakeChannelConfigRepository([]);
        var service = new SeafoodProductService(productRepository, traitRepository, channelRepository);

        await service.DeleteAsync(productId, CancellationToken.None);

        Assert.DoesNotContain(productRepository.Items, x => x.Id == productId);
    }

    [Fact]
    public async Task UpsertProductAsync_SavesNormalAndDefectTraits()
    {
        var productRepository = new FakeSeafoodProductRepository([]);
        var traitRepository = new FakeSeafoodTraitRepository();
        var channelRepository = new FakeChannelConfigRepository([]);
        var service = new SeafoodProductService(productRepository, traitRepository, channelRepository);

        var result = await service.UpsertAsync(
            new UpsertSeafoodProductRequest(
                null,
                "SP-9003",
                "花蛤测试",
                true,
                [
                    new UpsertSeafoodTraitRequest("正常", true),
                    new UpsertSeafoodTraitRequest("碎壳", false),
                    new UpsertSeafoodTraitRequest("泥包", false)
                ]),
            CancellationToken.None);

        Assert.Equal("SP-9003", result.Product.Code);
        Assert.Equal(3, result.Traits.Count);
        Assert.Contains(result.Traits, x => x.Name == "正常" && x.IsNormal);
        Assert.Contains(result.Traits, x => x.Name == "碎壳" && !x.IsNormal);
        Assert.Contains(result.Traits, x => x.Name == "泥包" && !x.IsNormal);
    }

    [Fact]
    public async Task UpsertAndDeleteChannelAsync_CompletesRoundTrip()
    {
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var channelRepository = new FakeChannelConfigRepository([]);
        var productRepository = new FakeSeafoodProductRepository([
            new SeafoodProduct(productId, "SP-9004", "美贝测试", true)
        ]);
        var modelRepository = new FakeModelRegistryRepository([
            new ModelVersion(
                modelId,
                Guid.NewGuid(),
                "MB-MV-001",
                "mb.pt",
                "mb.onnx",
                "1x1x1536x300",
                "{}",
                0.6m,
                0.5m,
                "美贝模型",
                ModelStatus.Draft,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)
        ]);
        var service = new ChannelConfigService(channelRepository, productRepository, modelRepository);

        var saved = await service.UpsertAsync(
            new UpsertChannelConfigRequest(
                null,
                3,
                "3号通道",
                productId,
                modelId,
                DefectHandlingAction.Sink,
                1536,
                300,
                1.6m,
                45m,
                7m,
                0.61m,
                true),
            CancellationToken.None);

        Assert.Single(channelRepository.Items);
        Assert.Equal("3号通道", saved.Name);

        await service.DeleteAsync(saved.Id, CancellationToken.None);

        Assert.Empty(channelRepository.Items);
    }

    private sealed class FakeSeafoodProductRepository(IReadOnlyList<SeafoodProduct> seedItems) : ISeafoodProductRepository
    {
        public List<SeafoodProduct> Items { get; } = seedItems.ToList();

        public Task<IReadOnlyList<SeafoodProduct>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SeafoodProduct>>(Items.OrderBy(x => x.Name).ToList());

        public Task<SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<SeafoodProduct?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<SeafoodProduct> UpsertAsync(SeafoodProduct product, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == product.Id);
            if (existing >= 0)
            {
                Items[existing] = product;
            }
            else
            {
                Items.Add(product);
            }

            return Task.FromResult(product);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSeafoodTraitRepository : ISeafoodTraitRepository
    {
        private readonly Dictionary<Guid, List<SeafoodTrait>> _traitsByProduct = [];

        public Task<IReadOnlyList<SeafoodTrait>> GetByProductAsync(Guid productId, CancellationToken cancellationToken)
        {
            _traitsByProduct.TryGetValue(productId, out var traits);
            return Task.FromResult<IReadOnlyList<SeafoodTrait>>(traits?.ToList() ?? []);
        }

        public Task ReplaceForProductAsync(Guid productId, IReadOnlyList<SeafoodTrait> traits, CancellationToken cancellationToken)
        {
            _traitsByProduct[productId] = traits.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChannelConfigRepository(IReadOnlyList<ChannelConfig> seedItems) : IChannelConfigRepository
    {
        public List<ChannelConfig> Items { get; } = seedItems.ToList();

        public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChannelConfig>>(Items.OrderBy(x => x.ChannelNo).ToList());

        public Task<ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ChannelConfig?>(Items.FirstOrDefault(x => x.Id == id));

        public Task<ChannelConfig> UpsertAsync(ChannelConfig channelConfig, CancellationToken cancellationToken)
        {
            var existing = Items.FindIndex(x => x.Id == channelConfig.Id);
            if (existing >= 0)
            {
                Items[existing] = channelConfig;
            }
            else
            {
                Items.Add(channelConfig);
            }

            return Task.FromResult(channelConfig);
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModelRegistryRepository(IReadOnlyList<ModelVersion> seedItems) : IModelRegistryRepository
    {
        private readonly List<ModelVersion> _items = seedItems.ToList();

        public Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelVersion>>(_items.Where(x => x.SeafoodCategoryId == categoryId).ToList());

        public Task<ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ModelVersion?>(_items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelVersion>>(_items.ToList());

        public Task<ModelVersion> UpsertAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
        {
            var existing = _items.FindIndex(x => x.Id == modelVersion.Id);
            if (existing >= 0)
            {
                _items[existing] = modelVersion;
            }
            else
            {
                _items.Add(modelVersion);
            }

            return Task.FromResult(modelVersion);
        }
    }
}
