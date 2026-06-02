using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.Application;

public sealed record DashboardDto(
    RuntimeSnapshot Snapshot,
    IReadOnlyList<ProductRecipe> Recipes,
    IReadOnlyList<ModelVersion> ActiveModels);

public sealed record SeafoodProductProfileDto(
    SeafoodProduct Product,
    IReadOnlyList<SeafoodTrait> Traits);

public sealed record ChannelConfigDetailDto(
    ChannelConfig Channel,
    SeafoodProduct? Product,
    ModelVersion? ModelVersion);

public sealed record ImportModelRequest(
    Guid SeafoodCategoryId,
    string Version,
    string SourceWeightPath,
    string DeploymentModelPath,
    string InputTensorShape,
    string LabelMapJson,
    decimal ConfidenceThreshold,
    decimal NmsThreshold,
    string Notes);

public sealed record ModelImportResult(
    OceanFresh.SortingSystem.Domain.ModelVersion ModelVersion,
    OceanFresh.SortingSystem.Domain.ModelValidationResult Validation);

public sealed record UpsertRecipeRequest(
    Guid? Id,
    Guid SeafoodCategoryId,
    string Name,
    decimal ConveyorSpeedMetersPerSecond,
    int ImageWidth,
    int ImageHeight,
    decimal XrayVoltageKv,
    decimal XrayCurrentMa,
    DefectHandlingAction DefectHandlingAction,
    string NormalLabel,
    int EjectDelayMicroseconds,
    int EjectPulseWidthMicroseconds,
    int EncoderWindowStart,
    int EncoderWindowEnd,
    bool IsEnabled);

public sealed record UpsertSeafoodTraitRequest(
    string Name,
    bool IsNormal);

public sealed record UpsertSeafoodProductRequest(
    Guid? Id,
    string Code,
    string Name,
    bool IsEnabled,
    IReadOnlyList<UpsertSeafoodTraitRequest> Traits);

public sealed record UpsertChannelConfigRequest(
    Guid? Id,
    int ChannelNo,
    string Name,
    Guid SeafoodProductId,
    Guid? ModelVersionId,
    DefectHandlingAction DefectHandlingAction,
    int ImageWidth,
    int ImageHeight,
    decimal ConveyorSpeedMetersPerSecond,
    decimal XrayVoltageKv,
    decimal XrayCurrentMa,
    decimal ConfidenceThreshold,
    bool IsEnabled);
