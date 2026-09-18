using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;

namespace OceanFresh.SortingSystem.LocalApi;

internal static class ManagementAuditChanges
{
    public static IReadOnlyList<OperationAuditChangeDto> Product(
        SeafoodProductProfileDto? before,
        SeafoodProductProfileDto after)
    {
        var changes = new List<OperationAuditChangeDto>();
        Add(changes, "Code", "产品编码", before?.Product.Code, after.Product.Code, before is null);
        Add(changes, "Name", "海鲜名称", before?.Product.Name, after.Product.Name, before is null);
        Add(changes, "IsEnabled", "启用状态", Bool(before?.Product.IsEnabled), Bool(after.Product.IsEnabled), before is null);
        Add(changes, "ClassesFile", "标签文件", FileName(before?.Product.ClassesFilePath), FileName(after.Product.ClassesFilePath), before is null);
        Add(changes, "PredictConfig", "推理配置", FileName(before?.Product.PredictConfigPath), FileName(after.Product.PredictConfigPath), before is null);
        Add(changes, "Traits", "缺陷类型", Traits(before?.Traits), Traits(after.Traits), before is null);
        return changes;
    }

    public static IReadOnlyList<OperationAuditChangeDto> ProductSnapshot(SeafoodProductProfileDto product) =>
        Product(null, product);

    public static IReadOnlyList<OperationAuditChangeDto> AsDeletionSnapshot(
        IReadOnlyList<OperationAuditChangeDto> snapshot) =>
        snapshot.Select(x => x with { OldValue = x.NewValue, NewValue = "已删除" }).ToArray();

    public static IReadOnlyList<OperationAuditChangeDto> Model(
        ModelVersion? before,
        ModelVersion after,
        string? beforeCategory,
        string afterCategory)
    {
        var changes = new List<OperationAuditChangeDto>();
        Add(changes, "Category", "海鲜类别", beforeCategory, afterCategory, before is null);
        Add(changes, "Version", "模型版本", before?.Version, after.Version, before is null);
        Add(changes, "WeightFile", "模型文件", FileName(before?.SourceWeightPath), FileName(after.SourceWeightPath), before is null);
        Add(changes, "TrainingImageSize", "训练分辨率 imgsz", before?.TrainingImageSize?.ToString(), after.TrainingImageSize?.ToString(), before is null);
        Add(changes, "Notes", "模型描述", before?.Notes, after.Notes, before is null);
        return changes;
    }

    public static IReadOnlyList<OperationAuditChangeDto> Channel(
        ChannelConfig? before,
        ChannelConfig after,
        string? beforeProduct,
        string afterProduct,
        string? beforeModel,
        string afterModel)
    {
        var changes = new List<OperationAuditChangeDto>();
        Add(changes, "ChannelNo", "通道编号", before?.ChannelNo.ToString(), after.ChannelNo.ToString(), before is null);
        Add(changes, "Name", "通道名称", before?.Name, after.Name, before is null);
        Add(changes, "Product", "绑定产品", beforeProduct, afterProduct, before is null);
        Add(changes, "Model", "绑定模型", beforeModel, afterModel, before is null);
        Add(changes, "DefectHandlingAction", "缺陷处理", Handling(before?.DefectHandlingAction), Handling(after.DefectHandlingAction), before is null);
        Add(changes, "ConfidenceThreshold", "置信度阈值", Decimal(before?.ConfidenceThreshold), Decimal(after.ConfidenceThreshold), before is null);
        Add(changes, "IsEnabled", "启用状态", Bool(before?.IsEnabled), Bool(after.IsEnabled), before is null);
        Add(changes, "CameraToEjectDistance", "相机至剔除距离", Millimeters(before?.CameraToEjectDistanceMillimeters), Millimeters(after.CameraToEjectDistanceMillimeters), before is null);
        Add(changes, "MillimetersPerPixelY", "纵向像素比例", Ratio(before?.MillimetersPerPixelY), Ratio(after.MillimetersPerPixelY), before is null);
        Add(changes, "SoftwareLatency", "软件延迟", Milliseconds(before?.SoftwareLatencyMilliseconds), Milliseconds(after.SoftwareLatencyMilliseconds), before is null);
        Add(changes, "ActuatorDelay", "执行器延迟", Milliseconds(before?.ActuatorDelayMilliseconds), Milliseconds(after.ActuatorDelayMilliseconds), before is null);
        if (before is null || !string.Equals(before.HorizontalLaneMappingJson, after.HorizontalLaneMappingJson, StringComparison.Ordinal))
        {
            changes.Add(new OperationAuditChangeDto(
                "HorizontalLaneMapping",
                "阀位映射",
                before is null ? "-" : "原配置",
                "已配置"));
        }

        return changes;
    }

    public static string ProductName(Guid id, IReadOnlyList<SeafoodProduct> products) =>
        products.FirstOrDefault(x => x.Id == id)?.Name ?? id.ToString();

    public static string ModelName(Guid? id, IReadOnlyList<ModelVersion> models) =>
        id is null ? "未绑定" : models.FirstOrDefault(x => x.Id == id)?.Version ?? id.Value.ToString();

    private static void Add(
        ICollection<OperationAuditChangeDto> changes,
        string field,
        string displayName,
        string? oldValue,
        string? newValue,
        bool includeUnchanged = false)
    {
        var normalizedOld = string.IsNullOrWhiteSpace(oldValue) ? "-" : oldValue.Trim();
        var normalizedNew = string.IsNullOrWhiteSpace(newValue) ? "-" : newValue.Trim();
        if (includeUnchanged || !string.Equals(normalizedOld, normalizedNew, StringComparison.Ordinal))
        {
            changes.Add(new OperationAuditChangeDto(field, displayName, normalizedOld, normalizedNew));
        }
    }

    private static string Traits(IReadOnlyList<SeafoodTrait>? traits) => traits is null
        ? "-"
        : string.Join("、", traits.Where(x => !x.IsNormal).OrderBy(x => x.SortOrder).Select(x => x.Name));

    private static string Bool(bool? value) => value switch { true => "启用", false => "停用", null => "-" };
    private static string FileName(string? path) => string.IsNullOrWhiteSpace(path) ? "-" : Path.GetFileName(path);
    private static string Decimal(decimal? value) => value?.ToString("0.###") ?? "-";
    private static string Millimeters(decimal? value) => value is null ? "-" : $"{value:0.###} mm";
    private static string Ratio(decimal? value) => value is null ? "-" : $"{value:0.###} mm/px";
    private static string Milliseconds(int? value) => value is null ? "-" : $"{value} ms";
    private static string Handling(DefectHandlingAction? action) => action switch
    {
        DefectHandlingAction.Pass => "放行",
        DefectHandlingAction.Sink => "下沉",
        DefectHandlingAction.AirJet => "气吹",
        DefectHandlingAction.Pusher => "推杆",
        DefectHandlingAction.StopLine => "停线",
        _ => "-"
    };
}
