using OceanFresh.SortingSystem.HMI;
using OceanFresh.SortingSystem.HMI.Pages;

namespace OceanFresh.SortingSystem.Tests;

public sealed class HmiViewModelTests
{
    public HmiViewModelTests()
    {
        ChannelRuntimeSelectionService.ResetForTests();
    }

    [Fact]
    public void ProductEditor_NewProduct_PrefillsOnlySuggestedCode()
    {
        var viewModel = new ProductEditorPageViewModel(null, "SP-TEST-001");

        Assert.Equal("SP-TEST-001", viewModel.DraftProductCode);
        Assert.Equal(string.Empty, viewModel.DraftProductName);
        Assert.Contains("正常样本默认固定为", viewModel.StatusMessage);
    }

    [Fact]
    public void RecipesPage_HasSelectedProduct_FollowsSelection()
    {
        var viewModel = new RecipesPageViewModel();

        Assert.False(viewModel.HasSelectedProduct);

        viewModel.SelectedProduct = new ProductRowViewModel(
            Guid.NewGuid(),
            "花蛤",
            "SP-0001",
            "正常",
            "碎壳 / 泥包",
            3);

        Assert.True(viewModel.HasSelectedProduct);
    }

    [Fact]
    public void ChannelConfigsPage_HasSelectedChannel_FollowsSelection()
    {
        var viewModel = new ChannelConfigsPageViewModel();

        Assert.False(viewModel.HasSelectedChannel);

        viewModel.SelectedChannel = new ChannelRowViewModel(
            Guid.NewGuid(),
            1,
            "1号通道",
            "油蛤",
            "YG-MV-001",
            "下沉",
            "0.58",
            Guid.NewGuid(),
            null,
            OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            1.8m,
            50m,
            6m,
            false,
            "待机");

        Assert.True(viewModel.HasSelectedChannel);
    }

    [Fact]
    public void ModelManagement_GeneratesUniqueVersionCodesPerCategory()
    {
        var viewModel = new ModelManagementPageViewModel();

        var code = viewModel.BuildNextVersionCode("花蛤");

        Assert.Equal("HG-MV-003", code);
    }

    [Fact]
    public void ModelManagement_FilterModelsBySelectedCategory()
    {
        var viewModel = new ModelManagementPageViewModel();

        viewModel.SelectedCategory = viewModel.CategoryOptions.First(x => x.Name == "油蛤");

        Assert.Single(viewModel.Models);
        Assert.All(viewModel.Models, x => Assert.Equal("油蛤", x.CategoryName));
    }

    [Fact]
    public void ModelImport_UsesSuggestedVersionCode()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003");

        Assert.False(viewModel.HasGeneratedVersionCode);
        Assert.Equal(string.Empty, viewModel.DraftVersionCode);
    }

    [Fact]
    public void ModelImport_GeneratesVersionCodeAfterSave()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003");

        viewModel.DraftModelDescription = "花蛤缺陷模型";
        viewModel.SaveImportCommand.Execute(null);

        Assert.True(viewModel.HasGeneratedVersionCode);
        Assert.Equal("HG-MV-003", viewModel.DraftVersionCode);
    }

    [Fact]
    public void ModelImport_RejectsTooShortDescription()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003");

        viewModel.DraftModelDescription = "短描";
        viewModel.SaveImportCommand.Execute(null);

        Assert.False(viewModel.HasGeneratedVersionCode);
        Assert.Equal("模型描述至少填写 4 个字符，便于后续区分版本。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SeafoodProductService_RejectsDuplicateProductName()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepositoryForNameValidation([
            new OceanFresh.SortingSystem.Domain.SeafoodProduct(productId, "SP-1001", "花蛤", true)
        ]);
        var traitRepository = new FakeSeafoodTraitRepositoryForNameValidation();
        var channelRepository = new FakeChannelConfigRepositoryForNameValidation();
        var service = new OceanFresh.SortingSystem.Application.SeafoodProductService(productRepository, traitRepository, channelRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertSeafoodProductRequest(
                    null,
                    "SP-1002",
                    "花蛤",
                    true,
                    [
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("正常", true),
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("碎壳", false)
                    ]),
                CancellationToken.None));

        Assert.Equal("海鲜名称不能与现有产品重复。", exception.Message);
    }

    [Fact]
    public async Task SeafoodProductService_RejectsDuplicateProductCode()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepositoryForNameValidation([
            new OceanFresh.SortingSystem.Domain.SeafoodProduct(productId, "SP-1001", "花蛤", true)
        ]);
        var traitRepository = new FakeSeafoodTraitRepositoryForNameValidation();
        var channelRepository = new FakeChannelConfigRepositoryForNameValidation();
        var service = new OceanFresh.SortingSystem.Application.SeafoodProductService(productRepository, traitRepository, channelRepository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertSeafoodProductRequest(
                    null,
                    "SP-1001",
                    "油蛤",
                    true,
                    [
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("正常", true),
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("碎壳", false)
                    ]),
                CancellationToken.None));

        Assert.Equal("产品编码不能与现有产品重复。", exception.Message);
    }

    [Fact]
    public void ChannelEditor_FiltersModelsBySelectedProduct()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        var oilProductId = Guid.NewGuid();
        var clamProductId = Guid.NewGuid();
        viewModel.ProductOptions.Add(new ProductOptionViewModel(oilProductId, "油蛤", "正常", "碎壳 / 泥包"));
        viewModel.ProductOptions.Add(new ProductOptionViewModel(clamProductId, "花蛤", "正常", "砂石 / 空壳"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "YG-MV-001", "{}", "油蛤"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "HG-MV-001", "{}", "花蛤"));

        viewModel.DraftProductId = oilProductId;

        Assert.NotEmpty(viewModel.AvailableModelOptions);
        Assert.All(viewModel.AvailableModelOptions, x => Assert.Equal("油蛤", x.CategoryName));
        Assert.True(viewModel.CanSelectModel);
    }

    [Fact]
    public void ChannelEditor_DisablesModelSelection_WhenNoProductSelected()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "YG-MV-001", "{}", "油蛤"));

        viewModel.DraftProductId = null;

        Assert.Empty(viewModel.AvailableModelOptions);
        Assert.False(viewModel.CanSelectModel);
    }

    [Fact]
    public void ChannelEditor_RejectsInvalidConfidenceThreshold()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        viewModel.ProductOptions.Add(new ProductOptionViewModel(productId, "油蛤", "正常", "碎壳 / 泥包"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(modelId, "YG-MV-001", "{}", "油蛤"));
        viewModel.DraftProductId = productId;
        viewModel.DraftModelVersionId = modelId;
        viewModel.DraftChannelName = "1号通道";
        viewModel.DraftConfidenceThreshold = "1.2";

        viewModel.SaveChannelCommand.Execute(null);

        Assert.Equal("置信度阈值必须是大于 0 且不超过 1.0 的数字。", viewModel.StatusMessage);
    }

    [Fact]
    public void ChannelConfigs_FilterByProduct_OnlyKeepsMatchingChannels()
    {
        var oilChannelId = Guid.NewGuid();
        var clamChannelId = Guid.NewGuid();
        var viewModel = new ChannelConfigsPageViewModel();
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            oilChannelId, 1, "1号通道", "油蛤", "YG-MV-001", "下沉", "0.58",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            1.8m, 50m, 6m, false, "待机"));
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            clamChannelId, 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            1.8m, 50m, 6m, false, "待机"));
        viewModel.ProductFilterOptions.Add(new ProductFilterOptionViewModel(string.Empty, "全部海鲜产品"));
        viewModel.ProductFilterOptions.Add(new ProductFilterOptionViewModel("油蛤", "油蛤"));
        ChannelRuntimeSelectionService.SetActive(oilChannelId, "1号通道");
        viewModel.SelectedProductFilter = viewModel.ProductFilterOptions.Last();

        Assert.Single(viewModel.Channels);
        Assert.Equal("油蛤", viewModel.Channels[0].ProductName);
    }

    [Fact]
    public void ChannelConfigs_SearchByChannelName_OnlyKeepsMatchingChannels()
    {
        var oilChannelId = Guid.NewGuid();
        var clamChannelId = Guid.NewGuid();
        var viewModel = new ChannelConfigsPageViewModel();
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            oilChannelId, 1, "1号通道", "油蛤", "YG-MV-001", "下沉", "0.58",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            1.8m, 50m, 6m, false, "待机"));
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            clamChannelId, 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            1.8m, 50m, 6m, false, "待机"));
        viewModel.ProductFilterOptions.Add(new ProductFilterOptionViewModel(string.Empty, "全部海鲜产品"));
        viewModel.SelectedProductFilter = viewModel.ProductFilterOptions.First();
        ChannelRuntimeSelectionService.SetActive(oilChannelId, "1号通道");
        viewModel.ChannelSearchText = "2号";

        Assert.Single(viewModel.Channels);
        Assert.Equal("2号通道", viewModel.Channels[0].Name);
    }

    [Fact]
    public void ChannelDetail_EnablingNewChannelReportsPreviousChannel()
    {
        var previousId = Guid.NewGuid();
        ChannelRuntimeSelectionService.SetActive(previousId, "1号通道");
        var detail = new ChannelDetailPageViewModel(
            new ChannelRowViewModel(
                Guid.NewGuid(), 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
                Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                1.8m, 50m, 6m, false, "待机"),
            _ => true);

        detail.ToggleChannelEnabledCommand.Execute(null);

        Assert.Equal("当前运行", detail.RunningStatus);
        Assert.Contains("同时停用了 1号通道", detail.StatusMessage);
    }

    [Fact]
    public void SeafoodCategoryOption_ToString_ReturnsChineseName()
    {
        var option = new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤");

        Assert.Equal("花蛤", option.ToString());
    }

    [Fact]
    public void ModelOption_ToString_ReturnsVersionOnly()
    {
        var option = new ModelOptionViewModel(Guid.NewGuid(), "HG-MV-001", "{}", "花蛤");

        Assert.Equal("HG-MV-001", option.ToString());
    }

    [Fact]
    public void ChannelEditor_ExistingChannel_LocksImmutableFields()
    {
        var viewModel = new ChannelEditorPageViewModel(
            new ChannelRowViewModel(
                Guid.NewGuid(), 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
                Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                1.8m, 50m, 6m, false, "待机"));

        Assert.True(viewModel.IsChannelNoReadOnly);
        Assert.True(viewModel.IsConveyorSpeedReadOnly);
        Assert.True(viewModel.IsVoltageReadOnly);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsDuplicateChannelName()
    {
        var existingId = Guid.NewGuid();
        var repo = new FakeChannelRepositoryForValidation([
            new OceanFresh.SortingSystem.Domain.ChannelConfig(
                existingId, 1, "1号通道", Guid.NewGuid(), null,
                OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                1536, 300, 1.8m, 50m, 6m, 0.58m, true)
        ]);
        var service = new OceanFresh.SortingSystem.Application.ChannelConfigService(
            repo,
            new FakeSeafoodProductRepositoryForChannelValidation(),
            new FakeModelRegistryRepositoryForChannelValidation());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertChannelConfigRequest(
                    null, 2, "1号通道", Guid.NewGuid(), Guid.NewGuid(),
                    OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                    1536, 300, 1.8m, 50m, 6m, 0.60m, true),
                CancellationToken.None));

        Assert.Equal("通道名称必须唯一，不能与现有通道重复。", ex.Message);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsChangingImmutableFields()
    {
        var existingId = Guid.NewGuid();
        var repo = new FakeChannelRepositoryForValidation([
            new OceanFresh.SortingSystem.Domain.ChannelConfig(
                existingId, 1, "1号通道", Guid.NewGuid(), null,
                OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                1536, 300, 1.8m, 50m, 6m, 0.58m, true)
        ]);
        var service = new OceanFresh.SortingSystem.Application.ChannelConfigService(
            repo,
            new FakeSeafoodProductRepositoryForChannelValidation(),
            new FakeModelRegistryRepositoryForChannelValidation());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertChannelConfigRequest(
                    existingId, 2, "1号通道-改名", Guid.NewGuid(), Guid.NewGuid(),
                    OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                    1536, 300, 2.2m, 55m, 6m, 0.60m, true),
                CancellationToken.None));

        Assert.Equal("通道编号创建后不可修改。", ex.Message);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsInvalidConfidenceThreshold()
    {
        var service = new OceanFresh.SortingSystem.Application.ChannelConfigService(
            new FakeChannelRepositoryForValidation([]),
            new FakeSeafoodProductRepositoryForChannelValidation(),
            new FakeModelRegistryRepositoryForChannelValidation());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertChannelConfigRequest(
                    null, 1, "1号通道", Guid.NewGuid(), Guid.NewGuid(),
                    OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                    1536, 300, 1.8m, 50m, 6m, 1.2m, true),
                CancellationToken.None));

        Assert.Equal("置信度阈值必须是大于 0 且不超过 1.0 的数字。", ex.Message);
    }

    private sealed class FakeSeafoodProductRepositoryForNameValidation(
        IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodProduct> seedItems)
        : OceanFresh.SortingSystem.Domain.ISeafoodProductRepository
    {
        private readonly List<OceanFresh.SortingSystem.Domain.SeafoodProduct> _items = seedItems.ToList();

        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodProduct>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodProduct>>(_items);

        public Task<OceanFresh.SortingSystem.Domain.SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.SeafoodProduct?>(_items.FirstOrDefault(x => x.Id == id));

        public Task<OceanFresh.SortingSystem.Domain.SeafoodProduct> UpsertAsync(OceanFresh.SortingSystem.Domain.SeafoodProduct product, CancellationToken cancellationToken) =>
            Task.FromResult(product);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSeafoodTraitRepositoryForNameValidation : OceanFresh.SortingSystem.Domain.ISeafoodTraitRepository
    {
        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodTrait>> GetByProductAsync(Guid productId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodTrait>>([]);

        public Task ReplaceForProductAsync(Guid productId, IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodTrait> traits, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeChannelConfigRepositoryForNameValidation : OceanFresh.SortingSystem.Domain.IChannelConfigRepository
    {
        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ChannelConfig>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.ChannelConfig>>([]);

        public Task<OceanFresh.SortingSystem.Domain.ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.ChannelConfig?>(null);

        public Task<OceanFresh.SortingSystem.Domain.ChannelConfig> UpsertAsync(OceanFresh.SortingSystem.Domain.ChannelConfig channelConfig, CancellationToken cancellationToken) =>
            Task.FromResult(channelConfig);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeChannelRepositoryForValidation(
        IReadOnlyList<OceanFresh.SortingSystem.Domain.ChannelConfig> seedItems)
        : OceanFresh.SortingSystem.Domain.IChannelConfigRepository
    {
        private readonly List<OceanFresh.SortingSystem.Domain.ChannelConfig> _items = seedItems.ToList();

        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ChannelConfig>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.ChannelConfig>>(_items);

        public Task<OceanFresh.SortingSystem.Domain.ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.ChannelConfig?>(_items.FirstOrDefault(x => x.Id == id));

        public Task<OceanFresh.SortingSystem.Domain.ChannelConfig> UpsertAsync(OceanFresh.SortingSystem.Domain.ChannelConfig channelConfig, CancellationToken cancellationToken) =>
            Task.FromResult(channelConfig);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSeafoodProductRepositoryForChannelValidation : OceanFresh.SortingSystem.Domain.ISeafoodProductRepository
    {
        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodProduct>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodProduct>>([]);

        public Task<OceanFresh.SortingSystem.Domain.SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.SeafoodProduct?>(null);

        public Task<OceanFresh.SortingSystem.Domain.SeafoodProduct> UpsertAsync(OceanFresh.SortingSystem.Domain.SeafoodProduct product, CancellationToken cancellationToken) =>
            Task.FromResult(product);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeModelRegistryRepositoryForChannelValidation : OceanFresh.SortingSystem.Domain.IModelRegistryRepository
    {
        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>> GetByCategoryAsync(Guid seafoodCategoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>>([]);

        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>>([]);

        public Task<OceanFresh.SortingSystem.Domain.ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.ModelVersion?>(null);

        public Task<OceanFresh.SortingSystem.Domain.ModelVersion> UpsertAsync(OceanFresh.SortingSystem.Domain.ModelVersion modelVersion, CancellationToken cancellationToken) =>
            Task.FromResult(modelVersion);
    }
}
