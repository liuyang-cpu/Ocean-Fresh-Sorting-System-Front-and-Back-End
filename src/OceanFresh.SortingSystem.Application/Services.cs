using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public sealed class RecipeService(IProductRecipeRepository recipeRepository)
{
    public Task<IReadOnlyList<ProductRecipe>> GetAllAsync(CancellationToken cancellationToken) =>
        recipeRepository.GetAllAsync(cancellationToken);

    public Task<ProductRecipe?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        recipeRepository.GetByIdAsync(id, cancellationToken);

    public async Task<ProductRecipe> UpsertAsync(UpsertRecipeRequest request, CancellationToken cancellationToken)
    {
        var recipe = new ProductRecipe(
            request.Id ?? Guid.NewGuid(),
            request.SeafoodCategoryId,
            request.Name,
            request.ConveyorSpeedMetersPerSecond,
            request.ImageWidth,
            request.ImageHeight,
            request.XrayVoltageKv,
            request.XrayCurrentMa,
            request.DefectHandlingAction,
            request.NormalLabel,
            request.EjectDelayMicroseconds,
            request.EjectPulseWidthMicroseconds,
            request.EncoderWindowStart,
            request.EncoderWindowEnd,
            request.IsEnabled);

        return await recipeRepository.UpsertAsync(recipe, cancellationToken);
    }
}

public sealed class SeafoodProductService(
    ISeafoodProductRepository productRepository,
    ISeafoodTraitRepository traitRepository,
    IChannelConfigRepository channelRepository)
{
    public async Task<IReadOnlyList<SeafoodProductProfileDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var products = await productRepository.GetAllAsync(cancellationToken);
        var results = new List<SeafoodProductProfileDto>(products.Count);

        foreach (var product in products)
        {
            var traits = await traitRepository.GetByProductAsync(product.Id, cancellationToken);
            results.Add(new SeafoodProductProfileDto(product, traits));
        }

        return results;
    }

    public async Task<SeafoodProductProfileDto> UpsertAsync(UpsertSeafoodProductRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new InvalidOperationException("产品编码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("海鲜名称不能为空。");
        }

        var existingProducts = await productRepository.GetAllAsync(cancellationToken);
        if (existingProducts.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.Code, request.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("产品编码不能与现有产品重复。");
        }

        if (existingProducts.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("海鲜名称不能与现有产品重复。");
        }

        if (request.Traits is null || request.Traits.Count == 0)
        {
            throw new InvalidOperationException("海鲜产品至少需要包含正常项和一个缺陷项。");
        }

        var normalCount = request.Traits.Count(x => x.IsNormal && !string.IsNullOrWhiteSpace(x.Name));
        var defectCount = request.Traits.Count(x => !x.IsNormal && !string.IsNullOrWhiteSpace(x.Name));
        if (normalCount != 1)
        {
            throw new InvalidOperationException("海鲜产品必须且只能保留一个正常项。");
        }

        if (defectCount < 1)
        {
            throw new InvalidOperationException("海鲜产品至少需要配置一个缺陷项。");
        }

        var product = new SeafoodProduct(
            request.Id ?? Guid.NewGuid(),
            request.Code.Trim(),
            request.Name.Trim(),
            request.IsEnabled);

        var savedProduct = await productRepository.UpsertAsync(product, cancellationToken);
        var traits = request.Traits
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select((trait, index) => new SeafoodTrait(
                Guid.NewGuid(),
                savedProduct.Id,
                trait.Name.Trim(),
                trait.IsNormal,
                index + 1,
                true))
            .ToList();

        await traitRepository.ReplaceForProductAsync(savedProduct.Id, traits, cancellationToken);
        return new SeafoodProductProfileDto(savedProduct, traits);
    }

    public async Task DeleteAsync(Guid productId, CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAllAsync(cancellationToken);
        if (channels.Any(x => x.SeafoodProductId == productId))
        {
            throw new InvalidOperationException("该海鲜产品仍被通道使用，不能删除。请先解除通道绑定。");
        }

        await productRepository.DeleteAsync(productId, cancellationToken);
    }
}

public sealed class ChannelConfigService(
    IChannelConfigRepository channelRepository,
    ISeafoodProductRepository productRepository,
    IModelRegistryRepository modelRepository)
{
    public async Task<IReadOnlyList<ChannelConfigDetailDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var channels = await channelRepository.GetAllAsync(cancellationToken);
        var products = await productRepository.GetAllAsync(cancellationToken);
        var models = await modelRepository.GetAllAsync(cancellationToken);

        return channels
            .Select(channel => new ChannelConfigDetailDto(
                channel,
                products.FirstOrDefault(product => product.Id == channel.SeafoodProductId),
                channel.ModelVersionId is null ? null : models.FirstOrDefault(model => model.Id == channel.ModelVersionId.Value)))
            .OrderBy(x => x.Channel.ChannelNo)
            .ToList();
    }

    public Task<ChannelConfig?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        channelRepository.GetByIdAsync(id, cancellationToken);

    public async Task<ChannelConfig> UpsertAsync(UpsertChannelConfigRequest request, CancellationToken cancellationToken)
    {
        if (request.ChannelNo <= 0)
        {
            throw new InvalidOperationException("通道编号必须是大于 0 的整数。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("通道名称不能为空。");
        }

        if (request.SeafoodProductId == Guid.Empty)
        {
            throw new InvalidOperationException("通道必须绑定一个海鲜产品。");
        }

        if (request.ModelVersionId is null || request.ModelVersionId == Guid.Empty)
        {
            throw new InvalidOperationException("通道必须选择一个模型。");
        }

        if (request.ConveyorSpeedMetersPerSecond <= 0)
        {
            throw new InvalidOperationException("皮带速度必须是大于 0 的数字。");
        }

        if (request.XrayVoltageKv <= 0)
        {
            throw new InvalidOperationException("X 光电压必须是大于 0 的数字。");
        }

        if (request.XrayCurrentMa <= 0)
        {
            throw new InvalidOperationException("X 光电流必须是大于 0 的数字。");
        }

        if (request.ConfidenceThreshold <= 0 || request.ConfidenceThreshold > 1)
        {
            throw new InvalidOperationException("置信度阈值必须是大于 0 且不超过 1.0 的数字。");
        }

        var existingChannels = await channelRepository.GetAllAsync(cancellationToken);
        if (existingChannels.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("通道名称必须唯一，不能与现有通道重复。");
        }

        if (request.Id is Guid channelId)
        {
            var existing = existingChannels.FirstOrDefault(x => x.Id == channelId);
            if (existing is not null)
            {
                if (existing.ChannelNo != request.ChannelNo)
                {
                    throw new InvalidOperationException("通道编号创建后不可修改。");
                }

                if (existing.ConveyorSpeedMetersPerSecond != request.ConveyorSpeedMetersPerSecond)
                {
                    throw new InvalidOperationException("皮带速度创建后不可修改。");
                }

                if (existing.XrayVoltageKv != request.XrayVoltageKv)
                {
                    throw new InvalidOperationException("X 光电压创建后不可修改。");
                }
            }
        }

        var channelConfig = new ChannelConfig(
            request.Id ?? Guid.NewGuid(),
            request.ChannelNo,
            request.Name.Trim(),
            request.SeafoodProductId,
            request.ModelVersionId,
            request.DefectHandlingAction,
            request.ImageWidth,
            request.ImageHeight,
            request.ConveyorSpeedMetersPerSecond,
            request.XrayVoltageKv,
            request.XrayCurrentMa,
            request.ConfidenceThreshold,
            request.IsEnabled);

        return await channelRepository.UpsertAsync(channelConfig, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        channelRepository.DeleteAsync(id, cancellationToken);
}

public sealed class ModelManagementService(
    IModelRegistryRepository modelRepository,
    IRecipeModelBindingRepository bindingRepository,
    IModelRegistry modelRegistry,
    IModelValidator modelValidator)
{
    public Task<IReadOnlyList<ModelVersion>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        modelRepository.GetByCategoryAsync(categoryId, cancellationToken);

    public Task<IReadOnlyList<ModelVersion>> GetAllAsync(CancellationToken cancellationToken) =>
        modelRepository.GetAllAsync(cancellationToken);

    public async Task<ModelImportResult> ImportAsync(ImportModelRequest request, CancellationToken cancellationToken)
    {
        if (request.SeafoodCategoryId == Guid.Empty)
        {
            throw new InvalidOperationException("模型导入必须选择海鲜类别。");
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            throw new InvalidOperationException("模型版本号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("模型描述不能为空。");
        }

        if (request.ConfidenceThreshold <= 0 || request.ConfidenceThreshold > 1)
        {
            throw new InvalidOperationException("模型置信度阈值必须是大于 0 且不超过 1.0 的数字。");
        }

        if (request.NmsThreshold <= 0 || request.NmsThreshold > 1)
        {
            throw new InvalidOperationException("模型 NMS 阈值必须是大于 0 且不超过 1.0 的数字。");
        }

        var version = new ModelVersion(
            Guid.NewGuid(),
            request.SeafoodCategoryId,
            request.Version,
            request.SourceWeightPath,
            request.DeploymentModelPath,
            request.InputTensorShape,
            request.LabelMapJson,
            request.ConfidenceThreshold,
            request.NmsThreshold,
            request.Notes,
            ModelStatus.Draft,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var validation = await modelValidator.ValidateAsync(version, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" | ", validation.Messages));
        }

        var imported = await modelRegistry.ImportAsync(version, cancellationToken);
        return new ModelImportResult(imported, validation);
    }

    public Task ActivateAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken) =>
        modelRegistry.ActivateAsync(recipeId, modelVersionId, cancellationToken);

    public Task RollbackAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken) =>
        modelRegistry.RollbackAsync(recipeId, modelVersionId, cancellationToken);

    public Task<IReadOnlyList<RecipeModelBinding>> GetBindingsAsync(Guid recipeId, CancellationToken cancellationToken) =>
        bindingRepository.GetByRecipeAsync(recipeId, cancellationToken);
}

public sealed class RuntimeDashboardService(
    IRuntimeStateStore runtimeStateStore,
    IProductRecipeRepository recipeRepository,
    IRecipeModelBindingRepository bindingRepository,
    IModelRegistryRepository modelRepository)
{
    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var recipes = await recipeRepository.GetAllAsync(cancellationToken);
        var activeModels = new List<ModelVersion>();

        foreach (var recipe in recipes)
        {
            var bindings = await bindingRepository.GetByRecipeAsync(recipe.Id, cancellationToken);
            var primary = bindings.FirstOrDefault(x => x.IsPrimary);
            if (primary is null)
            {
                continue;
            }

            var model = await modelRepository.GetByIdAsync(primary.ModelVersionId, cancellationToken);
            if (model is not null)
            {
                activeModels.Add(model);
            }
        }

        return new DashboardDto(runtimeStateStore.GetSnapshot(), recipes, activeModels);
    }
}

public sealed class PixelToEjectLocator : ILocator
{
    public EjectCommand BuildCommand(ProductRecipe recipe, DefectDetection detection, DefectHandlingAction action, DateTimeOffset createdAt)
    {
        var nozzleNumber = Math.Max(1, detection.X / 128);
        var triggerPosition = recipe.EncoderWindowStart + detection.Y;

        return new EjectCommand(
            Guid.NewGuid(),
            recipe.Id,
            detection.Label,
            action,
            nozzleNumber,
            triggerPosition,
            recipe.EjectDelayMicroseconds,
            recipe.EjectPulseWidthMicroseconds,
            createdAt);
    }
}

public static class DefectActionPolicy
{
    public static DefectHandlingAction Resolve(ProductRecipe recipe, string defectLabel)
    {
        if (string.IsNullOrWhiteSpace(recipe.NormalLabel))
        {
            return recipe.DefectHandlingAction;
        }

        return string.Equals(recipe.NormalLabel, defectLabel, StringComparison.OrdinalIgnoreCase)
            ? DefectHandlingAction.Pass
            : recipe.DefectHandlingAction;
    }
}

public sealed class ModelRegistryService(
    IModelRegistryRepository modelRepository,
    IRecipeModelBindingRepository bindingRepository) : IModelRegistry
{
    public async Task<ModelVersion?> GetActiveForRecipeAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var bindings = await bindingRepository.GetByRecipeAsync(recipeId, cancellationToken);
        var primary = bindings.FirstOrDefault(x => x.IsPrimary);
        if (primary is null)
        {
            return null;
        }

        return await modelRepository.GetByIdAsync(primary.ModelVersionId, cancellationToken);
    }

    public Task<ModelVersion> ImportAsync(ModelVersion modelVersion, CancellationToken cancellationToken) =>
        modelRepository.UpsertAsync(modelVersion, cancellationToken);

    public async Task ActivateAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken)
    {
        await bindingRepository.SetPrimaryAsync(recipeId, modelVersionId, cancellationToken);

        var model = await modelRepository.GetByIdAsync(modelVersionId, cancellationToken);
        if (model is null)
        {
            return;
        }

        await modelRepository.UpsertAsync(model with { Status = ModelStatus.Active }, cancellationToken);
    }

    public async Task RollbackAsync(Guid recipeId, Guid modelVersionId, CancellationToken cancellationToken)
    {
        await bindingRepository.SetPrimaryAsync(recipeId, modelVersionId, cancellationToken);

        var model = await modelRepository.GetByIdAsync(modelVersionId, cancellationToken);
        if (model is null)
        {
            return;
        }

        await modelRepository.UpsertAsync(model with { Status = ModelStatus.RolledBack }, cancellationToken);
    }
}

public sealed class FileSystemModelValidator : IModelValidator
{
    public Task<ModelValidationResult> ValidateAsync(ModelVersion modelVersion, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(modelVersion.Version))
        {
            messages.Add("模型版本不能为空。");
        }

        if (!File.Exists(modelVersion.SourceWeightPath))
        {
            messages.Add($"未找到 YOLO 源权重文件: {modelVersion.SourceWeightPath}");
        }

        if (!File.Exists(modelVersion.DeploymentModelPath))
        {
            messages.Add($"未找到 ONNX 部署文件: {modelVersion.DeploymentModelPath}");
        }
        else if (!string.Equals(Path.GetExtension(modelVersion.DeploymentModelPath), ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add("部署模型必须是 .onnx 文件。");
        }

        if (string.IsNullOrWhiteSpace(modelVersion.InputTensorShape) || !modelVersion.InputTensorShape.StartsWith("[", StringComparison.Ordinal))
        {
            messages.Add("输入张量尺寸格式无效，应类似 [1,1,640,640]。");
        }

        try
        {
            System.Text.Json.JsonDocument.Parse(modelVersion.LabelMapJson);
        }
        catch
        {
            messages.Add("标签映射不是有效 JSON。");
        }

        if (modelVersion.ConfidenceThreshold <= 0 || modelVersion.ConfidenceThreshold > 1)
        {
            messages.Add("置信度阈值必须在 0 到 1 之间。");
        }

        if (modelVersion.NmsThreshold <= 0 || modelVersion.NmsThreshold > 1)
        {
            messages.Add("NMS 阈值必须在 0 到 1 之间。");
        }

        return Task.FromResult(new ModelValidationResult(messages.Count == 0, messages));
    }
}

public sealed class RuntimeCoordinator(
    IRuntimeStateStore runtimeStateStore,
    IDeviceHealthProvider deviceHealthProvider) : IRuntimeCoordinator
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var devices = await deviceHealthProvider.GetStatusesAsync(cancellationToken);
        runtimeStateStore.Update(new RuntimeSnapshot(
            RuntimeMode.Running,
            DeviceState.Running,
            "花蛤标准线",
            "花蛤-v2",
            "BATCH-20260601-01",
            0,
            0,
            100m,
            DateTimeOffset.UtcNow,
            devices,
            []));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var current = runtimeStateStore.GetSnapshot();
        runtimeStateStore.Update(current with
        {
            RuntimeMode = RuntimeMode.SafeStop,
            DeviceState = DeviceState.Idle,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return Task.CompletedTask;
    }

    public RuntimeSnapshot GetSnapshot() => runtimeStateStore.GetSnapshot();
}
