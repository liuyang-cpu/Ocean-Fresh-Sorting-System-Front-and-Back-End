using OceanFresh.SortingSystem.HMI;
using OceanFresh.SortingSystem.HMI.Pages;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using System.Reflection;

namespace OceanFresh.SortingSystem.Tests;

public sealed class HmiViewModelTests
{
    public HmiViewModelTests()
    {
        ChannelRuntimeSelectionService.ResetForTests();
        ModelCatalogState.ResetForTests();
    }

    [Fact]
    public void ProductEditor_NewProduct_DoesNotExposeGeneratedCode()
    {
        var viewModel = new ProductEditorPageViewModel(null);

        Assert.Equal(string.Empty, viewModel.DraftProductCode);
        Assert.Equal(string.Empty, viewModel.DraftProductName);
        Assert.Equal(string.Empty, viewModel.StatusMessage);
    }

    [Fact]
    public void ProductEditor_RejectsManualNormalTraitEntry()
    {
        var viewModel = new ProductEditorPageViewModel(null)
        {
            PendingDefectName = "正常"
        };

        viewModel.AddDefectCommand.Execute(null);

        Assert.Empty(viewModel.DefectItems);
        Assert.Equal("正常性状默认固定为“正常”，不需要手动添加。", viewModel.StatusMessage);
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
            3,
            "");

        Assert.True(viewModel.HasSelectedProduct);
    }

    [Fact]
    public void RecipesPage_SearchProducts_MatchesNameCodeAndDefectTraits()
    {
        var viewModel = new RecipesPageViewModel();
        viewModel.Products.Add(new ProductRowViewModel(
            Guid.NewGuid(), "花蛤", "SP-0001", "正常", "碎壳 / 泥包", 3, ""));
        viewModel.Products.Add(new ProductRowViewModel(
            Guid.NewGuid(), "油蛤", "SP-0002", "正常", "空壳", 2, ""));

        viewModel.ProductSearchText = "泥包";

        Assert.Single(viewModel.VisibleProducts);
        Assert.Equal("花蛤", viewModel.VisibleProducts[0].Name);
        Assert.Equal(1, viewModel.FilteredProductCount);

        viewModel.ProductSearchText = "SP-0002";

        Assert.Single(viewModel.VisibleProducts);
        Assert.Equal("油蛤", viewModel.VisibleProducts[0].Name);
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
            false,
            "待机",
            null,
            null);

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
    public void ModelManagement_SearchModels_MatchesBoundChannel()
    {
        var viewModel = new ModelManagementPageViewModel();

        viewModel.ModelSearchText = "2号通道";

        Assert.Single(viewModel.Models);
        Assert.Equal("HG-MV-001", viewModel.Models[0].Version);
    }

    [Fact]
    public void ModelImport_UsesSuggestedVersionCode()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003",
            loadProductsAsync: _ => Task.FromResult(BuildModelImportProducts()),
            loadCategoriesAsync: _ => Task.FromResult(BuildModelImportCategories()));

        Assert.False(viewModel.HasGeneratedVersionCode);
        Assert.Equal(string.Empty, viewModel.DraftVersionCode);
    }

    [Fact]
    public async Task ModelImport_RejectsMissingWeightFile()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003",
            (_, _) => Task.FromResult(new OceanFresh.SortingSystem.Domain.ModelVersion(
                Guid.NewGuid(), Guid.NewGuid(), "HG-MV-003", "", "花蛤缺陷模型",
                OceanFresh.SortingSystem.Domain.ModelStatus.Normal, DateTimeOffset.UtcNow)),
            loadProductsAsync: _ => Task.FromResult(BuildModelImportProducts()),
            loadCategoriesAsync: _ => Task.FromResult(BuildModelImportCategories()));

        await viewModel.ActivateAsync();

        viewModel.DraftModelDescription = "花蛤缺陷模型";
        viewModel.SaveImportCommand.Execute(null);

        Assert.False(viewModel.HasGeneratedVersionCode);
        Assert.Equal("请先从文件管理器中选择一个 YOLO 权重文件。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ModelImport_GeneratesVersionCodeAfterSave()
    {
        UpsertModelRequest? captured = null;
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003",
            (request, _) =>
            {
                captured = request;
                return Task.FromResult(new OceanFresh.SortingSystem.Domain.ModelVersion(
                Guid.NewGuid(), Guid.NewGuid(), "HG-MV-003", @"E:\Models\HG\HG-MV-003.pt", "花蛤缺陷模型",
                OceanFresh.SortingSystem.Domain.ModelStatus.Normal, DateTimeOffset.UtcNow));
            },
            loadProductsAsync: _ => Task.FromResult(BuildModelImportProducts()),
            loadCategoriesAsync: _ => Task.FromResult(BuildModelImportCategories()));

        await viewModel.ActivateAsync();

        viewModel.SelectedWeightFilePath = @"E:\Models\HG\HG-MV-003.pt";
        viewModel.DraftModelDescription = "花蛤缺陷模型";
        viewModel.DraftTrainingImageSize = 640;
        viewModel.SaveImportCommand.Execute(null);

        Assert.True(viewModel.HasGeneratedVersionCode);
        Assert.Equal("HG-MV-003", viewModel.DraftVersionCode);
        Assert.NotNull(captured);
        Assert.Equal(@"E:\Models\HG\HG-MV-003.pt", captured!.SourceWeightPath);
    }

    [Fact]
    public async Task ModelImport_EditMode_PrefillsExistingModelData()
    {
        var existing = new ModelRowViewModel(
            Guid.NewGuid(),
            "花蛤",
            "HG-MV-001",
            "2号通道",
            "花蛤标准缺陷模型",
            @"E:\Models\HG\HG-MV-001.pt",
            "HG-MV-001.pt");

        var viewModel = new ModelImportPageViewModel(existing, (_, _) => Task.FromResult(new OceanFresh.SortingSystem.Domain.ModelVersion(
            existing.ModelId, Guid.NewGuid(), existing.Version, existing.WeightFilePath, existing.Description,
            OceanFresh.SortingSystem.Domain.ModelStatus.Normal, DateTimeOffset.UtcNow)),
            loadProductsAsync: _ => Task.FromResult(BuildModelImportProducts()),
            loadCategoriesAsync: _ => Task.FromResult(BuildModelImportCategories()));

        await viewModel.ActivateAsync();

        Assert.True(viewModel.IsEditMode);
        Assert.Equal("编辑模型", viewModel.PageTitle);
        Assert.Equal("HG-MV-001", viewModel.DraftVersionCode);
        Assert.Equal("花蛤标准缺陷模型", viewModel.DraftModelDescription);
        Assert.Equal(@"E:\Models\HG\HG-MV-001.pt", viewModel.SelectedWeightFilePath);
        Assert.Equal("保存模型修改", viewModel.SaveButtonText);
        Assert.Equal("花蛤", viewModel.SelectedProduct?.Name);
    }

    [Fact]
    public async Task ModelImport_RejectsTooShortDescription()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "花蛤"),
            "HG-MV-003",
            (_, _) => Task.FromResult(new OceanFresh.SortingSystem.Domain.ModelVersion(
                Guid.NewGuid(), Guid.NewGuid(), "HG-MV-003", "", "花蛤缺陷模型",
                OceanFresh.SortingSystem.Domain.ModelStatus.Normal, DateTimeOffset.UtcNow)),
            loadProductsAsync: _ => Task.FromResult(BuildModelImportProducts()),
            loadCategoriesAsync: _ => Task.FromResult(BuildModelImportCategories()));

        await viewModel.ActivateAsync();

        viewModel.SelectedWeightFilePath = @"E:\Models\HG\HG-MV-003.pt";
        viewModel.DraftModelDescription = "短描";
        viewModel.SaveImportCommand.Execute(null);

        Assert.False(viewModel.HasGeneratedVersionCode);
        Assert.Equal("模型描述至少填写 4 个字符，便于后续区分版本。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ModelImport_RejectsDuplicateTraitMapping()
    {
        var viewModel = new ModelImportPageViewModel(
            new SeafoodCategoryOptionViewModel(Guid.NewGuid(), "油蛤"),
            "YG-MV-002",
            (_, _) => Task.FromResult(new OceanFresh.SortingSystem.Domain.ModelVersion(
                Guid.NewGuid(), Guid.NewGuid(), "YG-MV-002", "", "油蛤缺陷模型",
                OceanFresh.SortingSystem.Domain.ModelStatus.Normal, DateTimeOffset.UtcNow)),
            loadProductsAsync: _ => Task.FromResult(BuildModelImportProducts()),
            loadCategoriesAsync: _ => Task.FromResult(BuildModelImportCategories()));

        await viewModel.ActivateAsync();
        viewModel.SelectedWeightFilePath = @"E:\Models\YG\YG-MV-002.pt";
        viewModel.DraftModelDescription = "油蛤缺陷模型";
        viewModel.DraftTrainingImageSize = 640;
        viewModel.SelectedProduct = new ModelImportProductOptionViewModel(
            Guid.NewGuid(),
            "未维护映射产品",
            "油蛤",
            ["正常", "碎壳", "泥包", "空壳"],
            ["碎壳", "泥包", "空壳"],
            string.Empty,
            "{}");

        viewModel.SaveImportCommand.Execute(null);

        Assert.Equal("海鲜产品 未维护映射产品 还没有完成标准标签映射，请先到海鲜产品编辑页维护 classes.txt。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SeafoodProductService_RejectsDuplicateProductName()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepositoryForNameValidation([
            new OceanFresh.SortingSystem.Domain.SeafoodProduct(productId, "SP-1001", "花蛤", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
        ]);
        var traitRepository = new FakeSeafoodTraitRepositoryForNameValidation();
        var channelRepository = new FakeChannelConfigRepositoryForNameValidation();
        var service = new OceanFresh.SortingSystem.Application.SeafoodProductService(productRepository, traitRepository, channelRepository, new FakeModelRegistryRepositoryForProductService(), new FakeProductPredictConfigStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertSeafoodProductRequest(
                    null,
                    "SP-1002",
                    "花蛤",
                    true,
                    @"E:\classes\test.txt",
                    """{"0":"正常","1":"碎壳"}""",
                    null,
                    null,
                    [
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("正常", true),
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("碎壳", false)
                    ]),
                CancellationToken.None));

        Assert.Equal("海鲜名称不能与现有产品重复。", exception.Message);
    }

    [Fact]
    public async Task SeafoodProductService_GeneratesProductCodeForNewProduct()
    {
        var productId = Guid.NewGuid();
        var productRepository = new FakeSeafoodProductRepositoryForNameValidation([
            new OceanFresh.SortingSystem.Domain.SeafoodProduct(productId, "SP-1001", "花蛤", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
        ]);
        var traitRepository = new FakeSeafoodTraitRepositoryForNameValidation();
        var channelRepository = new FakeChannelConfigRepositoryForNameValidation();
        var service = new OceanFresh.SortingSystem.Application.SeafoodProductService(productRepository, traitRepository, channelRepository, new FakeModelRegistryRepositoryForProductService(), new FakeProductPredictConfigStore());

        var saved = await service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertSeafoodProductRequest(
                    null,
                    "SP-1001",
                    "油蛤",
                    true,
                    @"E:\classes\test.txt",
                    """{"0":"正常","1":"碎壳"}""",
                    null,
                    null,
                    [
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("正常", true),
                        new OceanFresh.SortingSystem.Application.UpsertSeafoodTraitRequest("碎壳", false)
                    ]),
                CancellationToken.None);

        Assert.Equal("SP-0001", saved.Product.Code);
    }

    [Fact]
    public void ChannelEditor_FiltersModelsBySelectedProduct()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        var oilProductId = Guid.NewGuid();
        var clamProductId = Guid.NewGuid();
        viewModel.ProductOptions.Add(new ProductOptionViewModel(oilProductId, "油蛤", "油蛤", "正常", "碎壳 / 泥包"));
        viewModel.ProductOptions.Add(new ProductOptionViewModel(clamProductId, "花蛤", "花蛤", "正常", "砂石 / 空壳"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "YG-MV-001", "油蛤"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "HG-MV-001", "花蛤"));

        viewModel.DraftProductId = oilProductId;

        Assert.NotEmpty(viewModel.AvailableModelOptions);
        Assert.All(viewModel.AvailableModelOptions, x => Assert.Equal("油蛤", x.CategoryName));
        Assert.True(viewModel.CanSelectModel);
    }

    [Fact]
    public void ChannelEditor_DisablesModelSelection_WhenNoProductSelected()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "YG-MV-001", "油蛤"));

        viewModel.DraftProductId = null;

        Assert.Empty(viewModel.AvailableModelOptions);
        Assert.False(viewModel.CanSelectModel);
    }

    [Fact]
    public void ChannelEditor_FiltersModelsByDerivedSeafoodCategory_ForLineStyleProductNames()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        var productId = Guid.NewGuid();
        viewModel.ProductOptions.Add(new ProductOptionViewModel(productId, "花蛤标准线", "花蛤", "正常", "碎壳 / 泥包"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "HG-MV-001", "花蛤"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(Guid.NewGuid(), "YG-MV-001", "油蛤"));

        viewModel.DraftProductId = productId;

        Assert.Single(viewModel.AvailableModelOptions);
        Assert.Equal("HG-MV-001", viewModel.AvailableModelOptions[0].Version);
    }

    [Fact]
    public void ChannelEditor_RejectsInvalidConfidenceThreshold()
    {
        var viewModel = new ChannelEditorPageViewModel(null);
        var productId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        viewModel.ProductOptions.Add(new ProductOptionViewModel(productId, "油蛤", "油蛤", "正常", "碎壳 / 泥包"));
        viewModel.ModelOptions.Add(new ModelOptionViewModel(modelId, "YG-MV-001", "油蛤"));
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
            false, "待机", null, null));
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            clamChannelId, 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            false, "待机", null, null));
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
            false, "待机", null, null));
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            clamChannelId, 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            false, "待机", null, null));
        viewModel.ProductFilterOptions.Add(new ProductFilterOptionViewModel(string.Empty, "全部海鲜产品"));
        viewModel.SelectedProductFilter = viewModel.ProductFilterOptions.First();
        ChannelRuntimeSelectionService.SetActive(oilChannelId, "1号通道");
        viewModel.ChannelSearchText = "2号";

        Assert.Single(viewModel.Channels);
        Assert.Equal("2号通道", viewModel.Channels[0].Name);
    }

    [Fact]
    public void ChannelConfigs_SearchByModelVersion_OnlyKeepsMatchingChannels()
    {
        var viewModel = new ChannelConfigsPageViewModel();
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            Guid.NewGuid(), 1, "1号通道", "油蛤", "YG-MV-001", "下沉", "0.58",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            false, "待机", null, null));
        viewModel.AllChannels.Add(new ChannelRowViewModel(
            Guid.NewGuid(), 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
            Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
            false, "待机", null, null));
        viewModel.ProductFilterOptions.Add(new ProductFilterOptionViewModel(string.Empty, "全部海鲜产品"));
        viewModel.SelectedProductFilter = viewModel.ProductFilterOptions.First();

        viewModel.ChannelSearchText = "YG-MV";

        Assert.Single(viewModel.Channels);
        Assert.Equal("1号通道", viewModel.Channels[0].Name);
    }

    [Fact]
    public void MonitoringPage_ReplaceCountSummary_UpdatesLatestCounts()
    {
        var viewModel = new MonitoringPageViewModel();

        InvokePrivate(viewModel, "ReplaceCountSummary", (object)new[] { "normal", "broken", "broken", "empty" });

        Assert.Equal("海鲜总数: 4", viewModel.TotalDetectedCountText);
        Assert.Collection(
            viewModel.TraitCountItems,
            item =>
            {
                Assert.Equal("正常", item.Label);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal("碎壳", item.Label);
                Assert.Equal(2, item.Count);
            },
            item =>
            {
                Assert.Equal("泥包", item.Label);
                Assert.Equal(0, item.Count);
            },
            item =>
            {
                Assert.Equal("空壳", item.Label);
                Assert.Equal(1, item.Count);
            });
    }

    [Fact]
    public void MonitoringPage_AccumulateCountSummary_AddsAcrossFinalizedFrames()
    {
        var viewModel = new MonitoringPageViewModel();

        InvokePrivate(viewModel, "ResetCountSummary");
        InvokePrivate(viewModel, "AccumulateCountSummary", (object)new[] { "broken", "normal" });
        InvokePrivate(viewModel, "AccumulateCountSummary", (object)new[] { "broken", "muddy", "broken" });

        Assert.Equal("海鲜总数: 5", viewModel.TotalDetectedCountText);
        Assert.Collection(
            viewModel.TraitCountItems,
            item =>
            {
                Assert.Equal("正常", item.Label);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal("碎壳", item.Label);
                Assert.Equal(3, item.Count);
            },
            item =>
            {
                Assert.Equal("泥包", item.Label);
                Assert.Equal(1, item.Count);
            },
            item =>
            {
                Assert.Equal("空壳", item.Label);
                Assert.Equal(0, item.Count);
            });
    }

    [Fact]
    public void MonitoringPage_ResolveLabelMap_SupportsNumericKeyJson()
    {
        var result = (Dictionary<int, string>)InvokePrivateStatic(
            typeof(MonitoringPageViewModel),
            "ResolveLabelMap",
            """{"0":"normal","1":"broken","2":"muddy","3":"empty"}""");

        Assert.Equal("normal", result[0]);
        Assert.Equal("broken", result[1]);
        Assert.Equal("muddy", result[2]);
        Assert.Equal("empty", result[3]);
    }

    [Fact]
    public void ChannelDetail_ActiveChannelIsReadOnlyAndCannotBeDeleted()
    {
        var detail = new ChannelDetailPageViewModel(
            new ChannelRowViewModel(
                Guid.NewGuid(), 1, "1号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
                Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                true, "当前使用", null, null));

        Assert.Equal("当前使用", detail.RunningStatus);
        Assert.False(detail.CanDelete);
        Assert.False(detail.HasStatusMessage);
    }

    [Fact]
    public void ProductDetail_BackToList_NavigatesToSettingsProductModule()
    {
        var shell = new MainWindowViewModel();
        var detail = new ProductDetailPageViewModel(
            new ProductRowViewModel(
                Guid.NewGuid(), "油蛤", "SP-YG", "正常", "碎壳 / 泥包 / 空壳", 4, string.Empty));

        detail.BackToListCommand.Execute(null);

        var settings = Assert.IsType<SettingsPageViewModel>(shell.CurrentPage);
        Assert.Equal("海鲜产品", settings.SelectedModule?.Title);
        Assert.IsType<RecipesPageViewModel>(settings.CurrentModulePage);
    }

    [Fact]
    public void ChannelDetail_BackToList_NavigatesToSettingsChannelModule()
    {
        var shell = new MainWindowViewModel();
        var detail = new ChannelDetailPageViewModel(
            new ChannelRowViewModel(
                Guid.NewGuid(), 1, "1号通道", "油蛤", "YG-MV-001", "下沉", "0.60",
                Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                false, "待机", null, null));

        detail.BackToListCommand.Execute(null);

        var settings = Assert.IsType<SettingsPageViewModel>(shell.CurrentPage);
        Assert.Equal("通道配置", settings.SelectedModule?.Title);
        Assert.IsType<ChannelConfigsPageViewModel>(settings.CurrentModulePage);
    }

    [Fact]
    public void SettingsModules_AreGroupedByTheirOperationalArea()
    {
        var hardware = new SettingsPageViewModel("硬件设置");
        Assert.Collection(
            hardware.ModuleShortcuts,
            module => Assert.Equal("设备管理", module.Title));

        var ejection = new SettingsPageViewModel("剔除设置");
        Assert.Collection(
            ejection.ModuleShortcuts,
            module => Assert.Equal("剔除控制", module.Title));

        var system = new SettingsPageViewModel("系统设置");
        Assert.Collection(
            system.ModuleShortcuts,
            module => Assert.Equal("安全预警", module.Title),
            module => Assert.Equal("软件维护", module.Title));
    }

    [Fact]
    public void ModelDetail_BackToList_NavigatesToSettingsModelModule()
    {
        var shell = new MainWindowViewModel();
        var detail = new ModelDetailPageViewModel(
            new ModelRowViewModel(
                Guid.NewGuid(), "油蛤", "YG-MV-001", string.Empty, "油蛤模型",
                @"E:\Models\YG\YG-MV-001.pt", "YG-MV-001.pt"),
            (_, _) => Task.CompletedTask);

        detail.BackToListCommand.Execute(null);

        var settings = Assert.IsType<SettingsPageViewModel>(shell.CurrentPage);
        Assert.Equal("模型管理", settings.SelectedModule?.Title);
        Assert.IsType<ModelManagementPageViewModel>(settings.CurrentModulePage);
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
        var option = new ModelOptionViewModel(Guid.NewGuid(), "HG-MV-001", "花蛤");

        Assert.Equal("HG-MV-001", option.ToString());
    }

    [Fact]
    public void ChannelEditor_DoesNotExposeChannelConveyorSpeed()
    {
        var viewModel = new ChannelEditorPageViewModel(
            new ChannelRowViewModel(
                Guid.NewGuid(), 2, "2号通道", "花蛤", "HG-MV-001", "下沉", "0.60",
                Guid.NewGuid(), null, OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                false, "待机", null, null));

        Assert.DoesNotContain(
            viewModel.GetType().GetProperties().Select(x => x.Name),
            x => string.Equals(x, "DraftConveyorSpeed", StringComparison.Ordinal));
    }

    [Fact]
    public void ModelDetail_ExposesWeightFileAndDescription()
    {
        var model = new ModelRowViewModel(
            Guid.NewGuid(),
            "花蛤",
            "HG-MV-001",
            "2号通道",
            "花蛤标准缺陷模型",
            @"E:\Models\HG\HG-MV-001.pt",
            "HG-MV-001.pt");
        var viewModel = new ModelDetailPageViewModel(model, (_, _) => Task.CompletedTask);

        Assert.Equal("HG-MV-001.pt", viewModel.WeightFileName);
        Assert.Equal(@"E:\Models\HG\HG-MV-001.pt", viewModel.WeightFilePath);
        Assert.Equal("花蛤标准缺陷模型", viewModel.Description);
        Assert.Equal("已绑定通道", viewModel.BoundStatus);
        Assert.Equal("2号通道", viewModel.BoundChannelNames);
    }

    [Fact]
    public void ModelDetail_HasEditCommand()
    {
        var model = new ModelRowViewModel(
            Guid.NewGuid(),
            "花蛤",
            "HG-MV-001",
            "2号通道",
            "花蛤标准缺陷模型",
            @"E:\Models\HG\HG-MV-001.pt",
            "HG-MV-001.pt");

        var viewModel = new ModelDetailPageViewModel(model, (_, _) => Task.CompletedTask);

        Assert.NotNull(viewModel.OpenEditCommand);
    }

    [Fact]
    public void ModelDetail_DeleteCommand_RemovesModelFromCatalog()
    {
        ModelCatalogState.ResetForTests();
        var shell = new MainWindowViewModel();
        var model = ModelCatalogState.GetByVersion("HG-MV-002")!;
        var viewModel = new ModelDetailPageViewModel(model, (_, _) => Task.CompletedTask);

        viewModel.DeleteModelCommand.Execute(null);

        Assert.Null(ModelCatalogState.GetByVersion("HG-MV-002"));
        var settings = Assert.IsType<SettingsPageViewModel>(shell.CurrentPage);
        Assert.Equal("模型管理", settings.SelectedModule?.Title);
        var page = Assert.IsType<ModelManagementPageViewModel>(settings.CurrentModulePage);
        Assert.Equal("全部海鲜", page.SelectedCategory?.Name);
    }

    [Fact]
    public void ModelDetail_DeleteCommand_BlocksRemovalWhenModelIsStillBoundToChannel()
    {
        ModelCatalogState.ResetForTests();
        var model = ModelCatalogState.GetByVersion("HG-MV-001")!;
        string? dialogMessage = null;
        var viewModel = new ModelDetailPageViewModel(
            model,
            (_, _) => Task.FromException(new InvalidOperationException("当前模型仍绑定到 2号通道，请先解除通道绑定后再删除。")),
            message => dialogMessage = message);

        viewModel.DeleteModelCommand.Execute(null);

        Assert.NotNull(ModelCatalogState.GetByVersion("HG-MV-001"));
        Assert.Equal("当前模型仍绑定到 2号通道，请先解除通道绑定后再删除。", viewModel.StatusMessage);
        Assert.Equal("当前模型仍绑定到 2号通道，请先解除通道绑定后再删除。", dialogMessage);
    }

    [Fact]
    public void ModelCatalogState_ApplyChannelBindings_ClearsModelBindingAfterChannelRemoval()
    {
        ModelCatalogState.ResetForTests();

        ModelCatalogState.ApplyChannelBindings(
        [
            new ChannelRowViewModel(
                Guid.NewGuid(),
                2,
                "2号通道",
                "花蛤",
                "HG-MV-001",
                "下沉",
                "0.6",
                Guid.NewGuid(),
                Guid.NewGuid(),
                OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                true,
                "当前运行",
                null,
                null)
        ]);

        var boundModel = ModelCatalogState.GetByVersion("HG-MV-001")!;
        Assert.Equal("2号通道", boundModel.BoundChannelNames);
        Assert.Equal("已绑定 · 1 个通道", boundModel.UsageSummary);
        Assert.Equal("已绑定", boundModel.BindingStatusText);
        Assert.Equal("1 个通道", boundModel.BoundChannelCountText);

        ModelCatalogState.ApplyChannelBindings([]);

        var releasedModel = ModelCatalogState.GetByVersion("HG-MV-001")!;
        Assert.Equal(string.Empty, releasedModel.BoundChannelNames);
        Assert.Equal("未绑定", releasedModel.UsageSummary);
        Assert.Equal("未绑定", releasedModel.BindingStatusText);
        Assert.Equal(string.Empty, releasedModel.BoundChannelCountText);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsDuplicateChannelName()
    {
        var existingId = Guid.NewGuid();
        var repo = new FakeChannelRepositoryForValidation([
            new OceanFresh.SortingSystem.Domain.ChannelConfig(
                existingId, 1, "1号通道", Guid.NewGuid(), null, "model.pt",
                OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                0.58m, true, null, null, 420m, 1m, 40, 25,
                """[{"nozzleNumber":1,"startX":0,"endX":383}]""")
        ]);
        var service = new OceanFresh.SortingSystem.Application.ChannelConfigService(
            repo,
            new FakeSeafoodProductRepositoryForChannelValidation(),
            new FakeModelRegistryRepositoryForChannelValidation(),
            new FakeRuntimeStateStoreForChannelValidation());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertChannelConfigRequest(
                    null, 2, "1号通道", Guid.NewGuid(), Guid.NewGuid(),
                    OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                    0.60m, true),
                CancellationToken.None));

        Assert.Equal("通道名称必须唯一，不能与现有通道重复。", ex.Message);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsChangingImmutableFields()
    {
        var existingId = Guid.NewGuid();
        var repo = new FakeChannelRepositoryForValidation([
            new OceanFresh.SortingSystem.Domain.ChannelConfig(
                existingId, 1, "1号通道", Guid.NewGuid(), null, "model.pt",
                OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                0.58m, true, null, null, 420m, 1m, 40, 25,
                """[{"nozzleNumber":1,"startX":0,"endX":383}]""")
        ]);
        var service = new OceanFresh.SortingSystem.Application.ChannelConfigService(
            repo,
            new FakeSeafoodProductRepositoryForChannelValidation(),
            new FakeModelRegistryRepositoryForChannelValidation(),
            new FakeRuntimeStateStoreForChannelValidation());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertChannelConfigRequest(
                    existingId, 2, "1号通道-改名", Guid.NewGuid(), Guid.NewGuid(),
                    OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                    0.60m, true),
                CancellationToken.None));

        Assert.Equal("通道编号创建后不可修改。", ex.Message);
    }

    [Fact]
    public async Task ChannelConfigService_RejectsInvalidConfidenceThreshold()
    {
        var service = new OceanFresh.SortingSystem.Application.ChannelConfigService(
            new FakeChannelRepositoryForValidation([]),
            new FakeSeafoodProductRepositoryForChannelValidation(),
            new FakeModelRegistryRepositoryForChannelValidation(),
            new FakeRuntimeStateStoreForChannelValidation());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertAsync(
                new OceanFresh.SortingSystem.Application.UpsertChannelConfigRequest(
                    null, 1, "1号通道", Guid.NewGuid(), Guid.NewGuid(),
                    OceanFresh.SortingSystem.Domain.DefectHandlingAction.Sink,
                    1.2m, true),
                CancellationToken.None));

        Assert.Equal("置信度阈值必须是大于 0 且不超过 1.0 的数字。", ex.Message);
    }

    private static IReadOnlyList<SeafoodProductProfileDto> BuildModelImportProducts() =>
    [
        new SeafoodProductProfileDto(
            new OceanFresh.SortingSystem.Domain.SeafoodProduct(Guid.Parse("8eeac4e9-0646-4a5d-a1ee-0d09a1776e7a"), "SP-YG", "油蛤", true, @"E:\classes\youge.txt", """{"0":"碎壳","1":"正常","2":"泥包","3":"空壳"}""", ""),
            [
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "正常", true, 0, true),
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "碎壳", false, 1, true),
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "泥包", false, 2, true),
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "空壳", false, 3, true)
            ]),
        new SeafoodProductProfileDto(
            new OceanFresh.SortingSystem.Domain.SeafoodProduct(Guid.Parse("b33199af-10e8-411a-b5e6-9821d467b4df"), "SP-HG", "花蛤", true, @"E:\classes\huage.txt", """{"0":"正常","1":"碎壳","2":"泥包","3":"空壳"}""", ""),
            [
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "正常", true, 0, true),
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "碎壳", false, 1, true),
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "泥包", false, 2, true),
                new OceanFresh.SortingSystem.Domain.SeafoodTrait(Guid.NewGuid(), Guid.Empty, "空壳", false, 3, true)
            ])
    ];

    private static IReadOnlyList<SeafoodCategory> BuildModelImportCategories() =>
    [
        new(Guid.Parse("b33199af-10e8-411a-b5e6-9821d467b4df"), "HG", "花蛤", "花蛤"),
        new(Guid.Parse("8eeac4e9-0646-4a5d-a1ee-0d09a1776e7a"), "YG", "油蛤", "油蛤"),
        new(Guid.Parse("7fc3d726-c789-4d97-8c39-1275521f9c8c"), "MB", "美贝", "美贝")
    ];

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
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.SeafoodProduct>>(
            [
                new OceanFresh.SortingSystem.Domain.SeafoodProduct(Guid.Parse("11111111-1111-1111-1111-111111111111"), "SP-TEST", "测试产品", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", "")
            ]);

        public Task<OceanFresh.SortingSystem.Domain.SeafoodProduct?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.SeafoodProduct?>(
                new OceanFresh.SortingSystem.Domain.SeafoodProduct(id, "SP-TEST", "测试产品", true, @"E:\classes\test.txt", """{"0":"正常","1":"碎壳"}""", ""));

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
            Task.FromResult<OceanFresh.SortingSystem.Domain.ModelVersion?>(new OceanFresh.SortingSystem.Domain.ModelVersion(
                id,
                Guid.NewGuid(),
                "HG-MV-001",
                "model.pt",
                "test model",
                OceanFresh.SortingSystem.Domain.ModelStatus.Normal,
                DateTimeOffset.UtcNow));

        public Task<OceanFresh.SortingSystem.Domain.ModelVersion> UpsertAsync(OceanFresh.SortingSystem.Domain.ModelVersion modelVersion, CancellationToken cancellationToken) =>
            Task.FromResult(modelVersion);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRuntimeStateStoreForChannelValidation : OceanFresh.SortingSystem.Domain.IRuntimeStateStore
    {
        private OceanFresh.SortingSystem.Domain.RuntimeSnapshot _snapshot = new(
            OceanFresh.SortingSystem.Domain.RuntimeMode.Stopped,
            OceanFresh.SortingSystem.Domain.DeviceState.Idle,
            "未启用通道",
            "未选择模型",
            "TEST",
            0,
            0,
            100m,
            DateTimeOffset.UtcNow,
            [],
            [],
            null);

        public OceanFresh.SortingSystem.Domain.RuntimeSnapshot GetSnapshot() => _snapshot;

        public void Update(OceanFresh.SortingSystem.Domain.RuntimeSnapshot snapshot) => _snapshot = snapshot;
    }

    private sealed class FakeModelRegistryRepositoryForProductService : OceanFresh.SortingSystem.Domain.IModelRegistryRepository
    {
        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>> GetByCategoryAsync(Guid seafoodCategoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>>([]);

        public Task<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OceanFresh.SortingSystem.Domain.ModelVersion>>([]);

        public Task<OceanFresh.SortingSystem.Domain.ModelVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OceanFresh.SortingSystem.Domain.ModelVersion?>(null);

        public Task<OceanFresh.SortingSystem.Domain.ModelVersion> UpsertAsync(OceanFresh.SortingSystem.Domain.ModelVersion modelVersion, CancellationToken cancellationToken) =>
            Task.FromResult(modelVersion);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePredictWorkspace : OceanFresh.SortingSystem.Domain.IPredictWorkspace
    {
        public Task<OceanFresh.SortingSystem.Domain.PreparedPredictRun> PrepareRunAsync(
            OceanFresh.SortingSystem.Domain.ChannelConfig channelConfig,
            string sourcePath,
            string runName,
            string predictConfigPath,
            string modelPath,
            int trainingImageSize,
            decimal confidenceThreshold,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProductPredictConfigStore : OceanFresh.SortingSystem.Domain.IProductPredictConfigStore
    {
        public Task<string> GetDefaultTemplateContentAsync(CancellationToken cancellationToken) =>
            Task.FromResult("""{"conf":0.25,"iou":0.7}""");

        public Task<string?> SaveManagedConfigAsync(string productCode, string? requestedPredictConfigPath, string? requestedPredictConfigJson, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(requestedPredictConfigPath ?? $@"product-predict-configs\{productCode}-predict.json");
    }

    private static void InvokePrivate(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, [.. args]);
    }

    private static object InvokePrivateStatic(Type targetType, string methodName, params object[] args)
    {
        var method = targetType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(null, [.. args])!;
    }
}
