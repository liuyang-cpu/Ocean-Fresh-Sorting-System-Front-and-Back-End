using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.HMI;

namespace OceanFresh.SortingSystem.HMI.Pages;

public sealed class DashboardPageViewModel
{
    public int TotalCount { get; } = 1280;
    public int RejectCount { get; } = 74;
    public string ModelVersion { get; } = "花蛤-v2";
    public string YieldRate { get; } = "94.22%";

    public ObservableCollection<DeviceSummary> DeviceSummaries { get; } =
    [
        new("X 光源", "运行正常"),
        new("探测器", "同步稳定"),
        new("编码器", "脉冲稳定"),
        new("气吹阵列", "可执行")
    ];
}

public sealed class MonitoringPageViewModel
{
    public string CurrentChannel { get; } = "当前通道: 2号通道";
    public string LastDetection { get; } = "异常类别: 泥包";
    public string LastConfidence { get; } = "置信度: 0.72";
    public string LastNozzle { get; } = "处理动作: 下沉剔除，目标通道: #2";
}

public sealed class RecipesPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private string _statusMessage = "正在加载海鲜产品...";
    private ProductRowViewModel? _selectedProduct;

    public RecipesPageViewModel()
    {
        OpenCreateProductCommand = new RelayCommand(_ => OpenCreateEditor());
        OpenEditProductCommand = new RelayCommand(_ => OpenEditEditor());
        DeleteProductCommand = new AsyncRelayCommand(DeleteSelectedProductAsync);
        RefreshCommand = new AsyncRelayCommand(LoadProductsAsync);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ProductRowViewModel? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            SetProperty(ref _selectedProduct, value);
            RaisePropertyChanged(nameof(HasSelectedProduct));
        }
    }

    public ObservableCollection<ProductRowViewModel> Products { get; } = [];

    public ICommand OpenCreateProductCommand { get; }

    public ICommand OpenEditProductCommand { get; }

    public ICommand DeleteProductCommand { get; }

    public ICommand RefreshCommand { get; }

    public Task ActivateAsync() => LoadProductsAsync();

    public bool HasSelectedProduct => SelectedProduct is not null;

    private async Task LoadProductsAsync()
    {
        try
        {
            var products = await _apiClient.GetProductsAsync(CancellationToken.None);
            Products.Clear();

            foreach (var product in products)
            {
                var normalTrait = product.Traits.FirstOrDefault(x => x.IsNormal)?.Name ?? "正常";
                var defects = product.Traits
                    .Where(x => !x.IsNormal)
                    .OrderBy(x => x.SortOrder)
                    .Select(x => x.Name)
                    .ToArray();

                Products.Add(new ProductRowViewModel(
                    product.Product.Id,
                    product.Product.Name,
                    product.Product.Code,
                    normalTrait,
                    string.Join(" / ", defects),
                    product.Traits.Count));
            }

            StatusMessage = $"已加载 {Products.Count} 个海鲜产品。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取海鲜产品失败: {ex.Message}";
        }
    }

    private async Task DeleteSelectedProductAsync()
    {
        if (SelectedProduct is null)
        {
            StatusMessage = "请先选择要删除的海鲜产品。";
            return;
        }

        try
        {
            await _apiClient.DeleteProductAsync(SelectedProduct.ProductId, CancellationToken.None);
            StatusMessage = $"已删除海鲜产品: {SelectedProduct.Name}";
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void OpenCreateEditor() => OpenEditor(null);

    private void OpenEditEditor()
    {
        if (SelectedProduct is null)
        {
            StatusMessage = "请先在列表中选中一个海鲜产品，再进入编辑。";
            return;
        }

        OpenEditor(SelectedProduct);
    }

    private void OpenEditor(ProductRowViewModel? product)
    {

        try
        {
            ShellNavigationService.Navigate(
                new ProductEditorPageViewModel(product, BuildSuggestedProductCode()),
                product is null ? "新建海鲜产品" : $"编辑海鲜产品 {product.Name}",
                product is null
                    ? "新建时只预填产品编码，其他内容按需逐项填写"
                    : "产品编码已锁定，只能维护名称和缺陷项");
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开产品编辑页失败: {ex.Message}";
        }
    }

    private string BuildSuggestedProductCode()
    {
        var index = 1;
        while (index < 10000)
        {
            var candidate = $"SP-{index:0000}";
            if (!Products.Any(x => string.Equals(x.Code, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            index++;
        }

        return $"SP-{DateTime.Now:HHmmss}";
    }
}

public sealed class ProductEditorPageViewModel : ViewModelBase
{
    private const string FixedNormalLabel = "正常";
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly ProductRowViewModel? _sourceProduct;
    private Guid? _editingProductId;
    private string _draftProductName = string.Empty;
    private string _draftProductCode;
    private string _pendingDefectName = string.Empty;
    private string _statusMessage;

    public ProductEditorPageViewModel(ProductRowViewModel? sourceProduct, string suggestedCode)
    {
        _sourceProduct = sourceProduct;
        _draftProductCode = suggestedCode;
        _statusMessage = sourceProduct is null ? "请先填写海鲜名称，再逐个添加缺陷项。正常样本默认固定为“正常”。" : "产品编码已锁定，可继续维护缺陷项。";
        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync);
        BackToListCommand = new RelayCommand(_ => BackToList());
        DeleteProductCommand = new AsyncRelayCommand(DeleteProductAsync);
        AddDefectCommand = new RelayCommand(_ => AddDefectItem());
        RemoveDefectCommand = new RelayCommand(RemoveDefectItem);
        ApplySourceProduct();
    }

    public string DraftProductName
    {
        get => _draftProductName;
        set => SetProperty(ref _draftProductName, value);
    }

    public string DraftProductCode
    {
        get => _draftProductCode;
        set => SetProperty(ref _draftProductCode, value);
    }

    public string PendingDefectName
    {
        get => _pendingDefectName;
        set => SetProperty(ref _pendingDefectName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsCodeReadOnly => _sourceProduct is not null;

    public bool CanDelete => _sourceProduct is not null;

    public ObservableCollection<EditableDefectItemViewModel> DefectItems { get; } = [];

    public ICommand SaveProductCommand { get; }

    public ICommand BackToListCommand { get; }

    public ICommand DeleteProductCommand { get; }

    public ICommand AddDefectCommand { get; }

    public ICommand RemoveDefectCommand { get; }

    private void ApplySourceProduct()
    {
        if (_sourceProduct is null)
        {
            return;
        }

        _editingProductId = _sourceProduct.ProductId;
        DraftProductName = _sourceProduct.Name;
        DraftProductCode = _sourceProduct.Code;

        foreach (var item in _sourceProduct.DefectTraitsDisplay.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            DefectItems.Add(new EditableDefectItemViewModel(item));
        }
    }

    private void AddDefectItem()
    {
        if (string.IsNullOrWhiteSpace(PendingDefectName))
        {
            StatusMessage = "请先输入一个缺陷项名称。";
            return;
        }

        if (DefectItems.Any(x => string.Equals(x.Name, PendingDefectName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "缺陷项已存在，不需要重复添加。";
            return;
        }

        DefectItems.Add(new EditableDefectItemViewModel(PendingDefectName.Trim()));
        PendingDefectName = string.Empty;
        StatusMessage = "已添加缺陷项。";
    }

    private void RemoveDefectItem(object? parameter)
    {
        if (parameter is not EditableDefectItemViewModel item)
        {
            return;
        }

        DefectItems.Remove(item);
        StatusMessage = "已移除缺陷项。";
    }

    private async Task SaveProductAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(DraftProductCode))
            {
                StatusMessage = "产品编码不能为空。";
                return;
            }

            if (string.IsNullOrWhiteSpace(DraftProductName))
            {
                StatusMessage = "海鲜名称不能为空。";
                return;
            }

            var traits = BuildTraitRequests();
            if (traits.Count < 2)
            {
                StatusMessage = "请至少保留 1 个缺陷项；正常样本默认固定为“正常”。";
                return;
            }

            var request = new UpsertSeafoodProductRequest(
                _editingProductId,
                DraftProductCode.Trim(),
                DraftProductName.Trim(),
                true,
                traits);

            await _apiClient.SaveProductAsync(request, CancellationToken.None);
            BackToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    private async Task DeleteProductAsync()
    {
        if (_sourceProduct is null)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteProductAsync(_sourceProduct.ProductId, CancellationToken.None);
            BackToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void BackToList() =>
        ShellNavigationService.Navigate(
            new RecipesPageViewModel(),
            "海鲜产品",
            "海鲜产品列表页只预览，新增和编辑都在独立编辑页完成");

    private List<UpsertSeafoodTraitRequest> BuildTraitRequests()
    {
        var items = DefectItems
            .Select(x => x.Name.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !string.Equals(x, FixedNormalLabel, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new UpsertSeafoodTraitRequest(x, false))
            .ToList();

        items.Insert(0, new UpsertSeafoodTraitRequest(FixedNormalLabel, true));

        return items;
    }
}

public sealed class ChannelConfigsPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private string _statusMessage = "正在加载通道配置...";
    private ChannelRowViewModel? _selectedChannel;
    private ProductFilterOptionViewModel? _selectedProductFilter;
    private string _channelSearchText = string.Empty;
    private string _activeChannelSummary = "当前运行通道: 未启用";

    public ChannelConfigsPageViewModel()
    {
        OpenCreateChannelCommand = new RelayCommand(_ => OpenCreateEditor());
        OpenEditChannelCommand = new RelayCommand(_ => OpenEditEditor());
        OpenChannelDetailsCommand = new RelayCommand(OpenChannelDetails);
        DeleteChannelCommand = new AsyncRelayCommand(DeleteSelectedChannelAsync);
        RefreshCommand = new AsyncRelayCommand(LoadChannelsAsync);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ChannelRowViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            SetProperty(ref _selectedChannel, value);
            RaisePropertyChanged(nameof(HasSelectedChannel));
        }
    }

    public ProductFilterOptionViewModel? SelectedProductFilter
    {
        get => _selectedProductFilter;
        set
        {
            SetProperty(ref _selectedProductFilter, value);
            ApplyChannelView();
        }
    }

    public string ChannelSearchText
    {
        get => _channelSearchText;
        set
        {
            SetProperty(ref _channelSearchText, value);
            ApplyChannelView();
        }
    }

    public string ActiveChannelSummary
    {
        get => _activeChannelSummary;
        set => SetProperty(ref _activeChannelSummary, value);
    }

    public ObservableCollection<ChannelRowViewModel> AllChannels { get; } = [];

    public ObservableCollection<ChannelRowViewModel> Channels { get; } = [];

    public ObservableCollection<ProductFilterOptionViewModel> ProductFilterOptions { get; } = [];

    public ICommand OpenCreateChannelCommand { get; }

    public ICommand OpenEditChannelCommand { get; }

    public ICommand OpenChannelDetailsCommand { get; }

    public ICommand DeleteChannelCommand { get; }

    public ICommand RefreshCommand { get; }

    public Task ActivateAsync() => LoadChannelsAsync();

    public bool HasSelectedChannel => SelectedChannel is not null;

    private async Task LoadChannelsAsync()
    {
        try
        {
            var channels = await _apiClient.GetChannelsAsync(CancellationToken.None);
            AllChannels.Clear();

            foreach (var channel in channels)
            {
            AllChannels.Add(new ChannelRowViewModel(
                    channel.Channel.Id,
                    channel.Channel.ChannelNo,
                    channel.Channel.Name,
                    channel.Product?.Name ?? "未绑定",
                    channel.ModelVersion?.Version ?? "未选择",
                    MapActionToChinese(channel.Channel.DefectHandlingAction),
                    channel.Channel.ConfidenceThreshold.ToString("0.##"),
                    channel.Channel.SeafoodProductId,
                    channel.Channel.ModelVersionId,
                    channel.Channel.DefectHandlingAction,
                    channel.Channel.ConveyorSpeedMetersPerSecond,
                    channel.Channel.XrayVoltageKv,
                    channel.Channel.XrayCurrentMa,
                    false,
                    "待机"));
            }

            BuildFilters();
            if (AllChannels.Count > 0)
            {
                var defaultChannel = AllChannels.OrderBy(x => x.ChannelNo).First();
                ChannelRuntimeSelectionService.EnsureDefault(defaultChannel.ChannelId, defaultChannel.Name);
            }
            ApplyChannelView();
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取通道配置失败: {ex.Message}";
        }
    }

    private async Task DeleteSelectedChannelAsync()
    {
        if (SelectedChannel is null)
        {
            StatusMessage = "请先选择要删除的通道。";
            return;
        }

        try
        {
            await _apiClient.DeleteChannelAsync(SelectedChannel.ChannelId, CancellationToken.None);
            StatusMessage = $"已删除通道: {SelectedChannel.Name}";
            await LoadChannelsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void OpenCreateEditor() => OpenEditor(null);

    private void OpenEditEditor()
    {
        if (SelectedChannel is null)
        {
            StatusMessage = "请先在列表中选中一个通道，再进入编辑。";
            return;
        }

        OpenEditor(SelectedChannel);
    }

    private void OpenChannelDetails(object? parameter)
    {
        var channel = parameter as ChannelRowViewModel ?? SelectedChannel;
        if (channel is null)
        {
            StatusMessage = "请先选中一个通道，再查看详情。";
            return;
        }

        ShellNavigationService.Navigate(
            new ChannelDetailPageViewModel(channel),
            $"通道详情 {channel.Name}",
            "在这里启用或停用通道，并进入通道编辑页");
    }

    private void OpenEditor(ChannelRowViewModel? channel)
    {

        try
        {
            ShellNavigationService.Navigate(
                new ChannelEditorPageViewModel(channel),
                channel is null ? "新建通道" : $"编辑通道 {channel.Name}",
                channel is null
                    ? "设置通道名称、海鲜产品、模型和阈值"
                    : "调整选中通道的海鲜、模型和运行参数");
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开通道编辑页失败: {ex.Message}";
        }
    }

    private static string MapActionToChinese(DefectHandlingAction action)
    {
        return action switch
        {
            DefectHandlingAction.Pass => "放行",
            DefectHandlingAction.Sink => "下沉",
            DefectHandlingAction.AirJet => "气吹",
            DefectHandlingAction.Pusher => "推杆",
            DefectHandlingAction.StopLine => "停机",
            _ => "下沉"
        };
    }

    private void BuildFilters()
    {
        ProductFilterOptions.Clear();
        ProductFilterOptions.Add(new ProductFilterOptionViewModel(string.Empty, "全部海鲜产品"));
        foreach (var item in AllChannels
                     .Select(x => x.ProductName)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x))
        {
            ProductFilterOptions.Add(new ProductFilterOptionViewModel(item, item));
        }

        SelectedProductFilter ??= ProductFilterOptions.FirstOrDefault();
    }

    private void ApplyChannelView()
    {
        Channels.Clear();

        var productName = SelectedProductFilter?.ProductName;
        var searchText = ChannelSearchText.Trim();
        var activeChannelId = ChannelRuntimeSelectionService.ActiveChannelId;

        var items = AllChannels
            .Where(x => string.IsNullOrWhiteSpace(productName) || x.ProductName == productName)
            .Where(x => string.IsNullOrWhiteSpace(searchText) || x.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ChannelNo)
            .Select(x => x with
            {
                IsActive = activeChannelId == x.ChannelId,
                RunningStatus = activeChannelId == x.ChannelId ? "当前运行" : "待机"
            })
            .ToList();

        foreach (var item in items)
        {
            Channels.Add(item);
        }

        if (SelectedChannel is not null && Channels.All(x => x.ChannelId != SelectedChannel.ChannelId))
        {
            SelectedChannel = null;
        }

        ActiveChannelSummary = $"当前运行通道: {ChannelRuntimeSelectionService.ActiveChannelName}";
        StatusMessage = $"当前运行通道: {ChannelRuntimeSelectionService.ActiveChannelName}；列表显示 {Channels.Count} 条配置。";
    }
}

public sealed class ChannelDetailPageViewModel : ViewModelBase
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly ChannelRowViewModel _channel;
    private readonly Func<string, bool> _confirmSwitch;
    private readonly Func<string, bool> _confirmDelete;
    private string _statusMessage;

    public ChannelDetailPageViewModel(
        ChannelRowViewModel channel,
        Func<string, bool>? confirmSwitch = null,
        Func<string, bool>? confirmDelete = null)
    {
        _channel = channel;
        _confirmSwitch = confirmSwitch ?? ConfirmSwitch;
        _confirmDelete = confirmDelete ?? ConfirmDelete;
        _statusMessage = channel.IsActive
            ? $"{channel.Name} 当前正在运行。"
            : $"{channel.Name} 当前处于待机状态。";
        ToggleChannelEnabledCommand = new RelayCommand(_ => ToggleChannelEnabled());
        OpenEditCommand = new RelayCommand(_ => OpenEdit());
        BackToListCommand = new RelayCommand(_ => BackToList());
        DeleteChannelCommand = new AsyncRelayCommand(DeleteChannelAsync);
    }

    public string ChannelName => _channel.Name;

    public string ProductName => _channel.ProductName;

    public string ChannelNoLabel => $"设备编号: CH-{_channel.ChannelNo:00}";

    public string ModelVersion => _channel.ModelVersion;

    public string DefectAction => _channel.DefectAction;

    public string ConfidenceThreshold => _channel.ConfidenceThreshold;

    public string RunningStatus => ChannelRuntimeSelectionService.ActiveChannelId == _channel.ChannelId ? "当前运行" : "待机";

    public string ToggleActionText => ChannelRuntimeSelectionService.ActiveChannelId == _channel.ChannelId ? "停用通道" : "启用通道";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand ToggleChannelEnabledCommand { get; }

    public ICommand OpenEditCommand { get; }

    public ICommand BackToListCommand { get; }

    public ICommand DeleteChannelCommand { get; }

    private void ToggleChannelEnabled()
    {
        if (ChannelRuntimeSelectionService.ActiveChannelId == _channel.ChannelId)
        {
            ChannelRuntimeSelectionService.DisableIfActive(_channel.ChannelId);
            StatusMessage = $"已停用 {_channel.Name}。";
            RaisePropertyChanged(nameof(RunningStatus));
            RaisePropertyChanged(nameof(ToggleActionText));
            return;
        }

        var previousName = ChannelRuntimeSelectionService.ActiveChannelName;
        var message = previousName == "未启用"
            ? $"准备启用 {_channel.Name}。"
            : $"启用 {_channel.Name} 会同时停用 {previousName}，是否继续？";

        if (!_confirmSwitch(message))
        {
            StatusMessage = "已取消启用操作。";
            return;
        }

        ChannelRuntimeSelectionService.SetActive(_channel.ChannelId, _channel.Name);
        StatusMessage = previousName == "未启用"
            ? $"已启用 {_channel.Name}。"
            : $"已启用 {_channel.Name}，同时停用了 {previousName}。";
        RaisePropertyChanged(nameof(RunningStatus));
        RaisePropertyChanged(nameof(ToggleActionText));
    }

    private void OpenEdit() =>
        ShellNavigationService.Navigate(
            new ChannelEditorPageViewModel(_channel),
            $"编辑通道 {_channel.Name}",
            "修改通道名称、海鲜产品、模型和运行参数");

    private async Task DeleteChannelAsync()
    {
        if (!_confirmDelete($"确认删除 {_channel.Name} 吗？删除后需要重新配置该通道。"))
        {
            StatusMessage = "已取消删除通道。";
            return;
        }

        try
        {
            await _apiClient.DeleteChannelAsync(_channel.ChannelId, CancellationToken.None);
            if (ChannelRuntimeSelectionService.ActiveChannelId == _channel.ChannelId)
            {
                ChannelRuntimeSelectionService.DisableIfActive(_channel.ChannelId);
            }

            ShellNavigationService.Navigate(
                new ChannelConfigsPageViewModel(),
                "通道配置",
                "通道配置列表页只预览，新增和编辑都在独立编辑页完成");
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void BackToList() =>
        ShellNavigationService.Navigate(
            new ChannelConfigsPageViewModel(),
            "通道配置",
            "通过海鲜产品筛选与通道名称搜索查看通道，再进入详情页");

    private static bool ConfirmSwitch(string message)
    {
        var result = MessageBox.Show(
            message,
            "海洋生鲜分拣系统",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static bool ConfirmDelete(string message)
    {
        var result = MessageBox.Show(
            message,
            "海洋生鲜分拣系统",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
}

public sealed class ChannelEditorPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly ChannelRowViewModel? _sourceChannel;
    private Guid? _editingChannelId;
    private string _draftChannelNo = "1";
    private string _draftChannelName = string.Empty;
    private Guid? _draftProductId;
    private Guid? _draftModelVersionId;
    private string _draftDefectAction = "Sink";
    private string _draftConveyorSpeed = "1.8";
    private string _draftVoltage = "50";
    private string _draftCurrent = "6";
    private string _draftConfidenceThreshold = "0.58";
    private string _statusMessage = "正在加载海鲜产品和模型...";

    public ChannelEditorPageViewModel(ChannelRowViewModel? sourceChannel)
    {
        _sourceChannel = sourceChannel;
        SaveChannelCommand = new AsyncRelayCommand(SaveChannelAsync);
        BackToListCommand = new RelayCommand(_ => BackToList());
        DeleteChannelCommand = new AsyncRelayCommand(DeleteChannelAsync);
    }

    public string DraftChannelNo
    {
        get => _draftChannelNo;
        set => SetProperty(ref _draftChannelNo, value);
    }

    public string DraftChannelName
    {
        get => _draftChannelName;
        set => SetProperty(ref _draftChannelName, value);
    }

    public Guid? DraftProductId
    {
        get => _draftProductId;
        set
        {
            SetProperty(ref _draftProductId, value);
            RefreshAvailableModels();
            RaisePropertyChanged(nameof(CanSelectModel));
        }
    }

    public Guid? DraftModelVersionId
    {
        get => _draftModelVersionId;
        set => SetProperty(ref _draftModelVersionId, value);
    }

    public string DraftDefectAction
    {
        get => _draftDefectAction;
        set => SetProperty(ref _draftDefectAction, value);
    }

    public string DraftConveyorSpeed
    {
        get => _draftConveyorSpeed;
        set => SetProperty(ref _draftConveyorSpeed, value);
    }

    public string DraftVoltage
    {
        get => _draftVoltage;
        set => SetProperty(ref _draftVoltage, value);
    }

    public string DraftCurrent
    {
        get => _draftCurrent;
        set => SetProperty(ref _draftCurrent, value);
    }

    public string DraftConfidenceThreshold
    {
        get => _draftConfidenceThreshold;
        set => SetProperty(ref _draftConfidenceThreshold, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanDelete => _sourceChannel is not null;

    public bool IsChannelNoReadOnly => _sourceChannel is not null;

    public bool IsConveyorSpeedReadOnly => _sourceChannel is not null;

    public bool IsVoltageReadOnly => _sourceChannel is not null;

    public ObservableCollection<ProductOptionViewModel> ProductOptions { get; } = [];

    public ObservableCollection<ModelOptionViewModel> ModelOptions { get; } = [];

    public ObservableCollection<ModelOptionViewModel> AvailableModelOptions { get; } = [];

    public ICommand SaveChannelCommand { get; }

    public ICommand BackToListCommand { get; }

    public ICommand DeleteChannelCommand { get; }

    public Task ActivateAsync() => LoadReferenceDataAsync();

    public bool CanSelectModel => DraftProductId is not null && AvailableModelOptions.Count > 0;

    private async Task LoadReferenceDataAsync()
    {
        try
        {
            var products = await _apiClient.GetProductsAsync(CancellationToken.None);
            var models = await _apiClient.GetModelsAsync(CancellationToken.None);

            ProductOptions.Clear();
            foreach (var product in products)
            {
                ProductOptions.Add(new ProductOptionViewModel(
                    product.Product.Id,
                    product.Product.Name,
                    product.Traits.FirstOrDefault(x => x.IsNormal)?.Name ?? "正常",
                    string.Join(" / ", product.Traits.Where(x => !x.IsNormal).Select(x => x.Name))));
            }

            ModelOptions.Clear();
            foreach (var model in models)
            {
                ModelOptions.Add(new ModelOptionViewModel(
                    model.Id,
                    model.Version,
                    model.LabelMapJson,
                    ResolveCategoryName(model.Version)));
            }

            ApplySourceChannel();
            RefreshAvailableModels();
            StatusMessage = _sourceChannel is null
                ? "请为新通道选择海鲜产品和模型。"
                : $"已载入 {_sourceChannel.Name} 的配置。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取基础数据失败: {ex.Message}";
        }
    }

    private void ApplySourceChannel()
    {
        if (_sourceChannel is null)
        {
            _editingChannelId = null;
            DraftProductId ??= ProductOptions.FirstOrDefault()?.Id;
            DraftModelVersionId ??= AvailableModelOptions.FirstOrDefault()?.Id;
            return;
        }

        _editingChannelId = _sourceChannel.ChannelId;
        DraftChannelNo = _sourceChannel.ChannelNo.ToString();
        DraftChannelName = _sourceChannel.Name;
        DraftProductId = _sourceChannel.ProductId;
        DraftModelVersionId = _sourceChannel.ModelVersionId ?? AvailableModelOptions.FirstOrDefault()?.Id;
        DraftDefectAction = _sourceChannel.DefectActionCode.ToString();
        DraftConveyorSpeed = _sourceChannel.ConveyorSpeedRaw.ToString("0.##");
        DraftVoltage = _sourceChannel.VoltageRaw.ToString("0.##");
        DraftCurrent = _sourceChannel.CurrentRaw.ToString("0.##");
        DraftConfidenceThreshold = _sourceChannel.ConfidenceThreshold;
    }

    private async Task SaveChannelAsync()
    {
        try
        {
            if (!int.TryParse(DraftChannelNo, out var channelNo) || channelNo <= 0)
            {
                StatusMessage = "通道编号必须是大于 0 的整数。";
                return;
            }

            if (string.IsNullOrWhiteSpace(DraftChannelName))
            {
                StatusMessage = "通道名称不能为空。";
                return;
            }

            if (DraftProductId is null)
            {
                StatusMessage = "请先为通道选择一个海鲜产品。";
                return;
            }

            if (DraftModelVersionId is null)
            {
                StatusMessage = "请先为通道选择一个模型。";
                return;
            }

            if (!TryParsePositiveDecimal(DraftConveyorSpeed, out var conveyorSpeed))
            {
                StatusMessage = "皮带速度必须是大于 0 的数字。";
                return;
            }

            if (!TryParsePositiveDecimal(DraftVoltage, out var voltage))
            {
                StatusMessage = "X 光电压必须是大于 0 的数字。";
                return;
            }

            if (!TryParsePositiveDecimal(DraftCurrent, out var current))
            {
                StatusMessage = "X 光电流必须是大于 0 的数字。";
                return;
            }

            if (!TryParseProbability(DraftConfidenceThreshold, out var confidenceThreshold))
            {
                StatusMessage = "置信度阈值必须是大于 0 且不超过 1.0 的数字。";
                return;
            }

            var request = new UpsertChannelConfigRequest(
                _editingChannelId,
                channelNo,
                DraftChannelName.Trim(),
                DraftProductId.Value,
                DraftModelVersionId,
                ParseAction(DraftDefectAction),
                1536,
                300,
                conveyorSpeed,
                voltage,
                current,
                confidenceThreshold,
                true);

            await _apiClient.SaveChannelAsync(request, CancellationToken.None);
            BackToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    private async Task DeleteChannelAsync()
    {
        if (_sourceChannel is null)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteChannelAsync(_sourceChannel.ChannelId, CancellationToken.None);
            BackToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void BackToList() =>
        ShellNavigationService.Navigate(
            new ChannelConfigsPageViewModel(),
            "通道配置",
            "通道配置列表页只预览，新增和编辑都在独立编辑页完成");

    private void RefreshAvailableModels()
    {
        AvailableModelOptions.Clear();

        if (DraftProductId is null)
        {
            DraftModelVersionId = null;
            StatusMessage = "请先选择海鲜产品，再选择对应模型。";
            RaisePropertyChanged(nameof(CanSelectModel));
            return;
        }

        var selectedProduct = ProductOptions.FirstOrDefault(x => x.Id == DraftProductId.Value);
        var selectedCategoryName = selectedProduct?.Name ?? string.Empty;
        var filtered = ModelOptions.Where(x => x.CategoryName == selectedCategoryName).ToList();

        foreach (var item in filtered)
        {
            AvailableModelOptions.Add(item);
        }

        if (AvailableModelOptions.All(x => x.Id != DraftModelVersionId))
        {
            DraftModelVersionId = AvailableModelOptions.FirstOrDefault()?.Id;
        }

        if (AvailableModelOptions.Count == 0)
        {
            StatusMessage = $"当前海鲜产品 {selectedCategoryName} 还没有可选模型。";
        }

        RaisePropertyChanged(nameof(CanSelectModel));
    }

    private static string ResolveCategoryName(string version)
    {
        if (version.StartsWith("花蛤", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("HG-", StringComparison.OrdinalIgnoreCase))
        {
            return "花蛤";
        }

        if (version.StartsWith("油蛤", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("YG-", StringComparison.OrdinalIgnoreCase))
        {
            return "油蛤";
        }

        if (version.StartsWith("美贝", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("MB-", StringComparison.OrdinalIgnoreCase))
        {
            return "美贝";
        }

        return string.Empty;
    }

    private static DefectHandlingAction ParseAction(string value)
    {
        return Enum.TryParse<DefectHandlingAction>(value, true, out var action)
            ? action
            : value switch
            {
                "下沉" => DefectHandlingAction.Sink,
                "气吹" => DefectHandlingAction.AirJet,
                "推杆" => DefectHandlingAction.Pusher,
                "停机" => DefectHandlingAction.StopLine,
                _ => DefectHandlingAction.Sink
            };
    }

    private static bool TryParsePositiveDecimal(string value, out decimal result)
    {
        var normalized = value.Trim();
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return result > 0;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out result))
        {
            return result > 0;
        }

        result = 0;
        return false;
    }

    private static bool TryParseProbability(string value, out decimal result)
    {
        var normalized = value.Trim();
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return result > 0 && result <= 1;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out result))
        {
            return result > 0 && result <= 1;
        }

        result = 0;
        return false;
    }
}

public sealed class ModelManagementPageViewModel : ViewModelBase
{
    private SeafoodCategoryOptionViewModel? _selectedCategory;
    private string _draftVersionCode = string.Empty;
    private string _statusMessage = "先按海鲜类别筛选，再决定导入哪个模型。";

    public ModelManagementPageViewModel()
    {
        CategoryOptions =
        [
            new(Guid.Empty, "全部海鲜"),
            new(Guid.Parse("b33199af-10e8-411a-b5e6-9821d467b4df"), "花蛤"),
            new(Guid.Parse("8eeac4e9-0646-4a5d-a1ee-0d09a1776e7a"), "油蛤"),
            new(Guid.Parse("7fc3d726-c789-4d97-8c39-1275521f9c8c"), "美贝")
        ];

        AllModels =
        [
            new("花蛤", "HG-MV-001", "当前启用", "good/empty/sand/broken", true),
            new("花蛤", "HG-MV-002", "未绑定", "good/empty/sand/broken", false),
            new("油蛤", "YG-MV-001", "当前启用", "正常/碎壳/泥包/空壳", true),
            new("美贝", "MB-MV-001", "未绑定", "正常/碎壳/泥包/空壳", false)
        ];

        OpenImportPageCommand = new RelayCommand(_ => OpenImportPage());
        SelectedCategory = CategoryOptions.First();
    }

    public ObservableCollection<SeafoodCategoryOptionViewModel> CategoryOptions { get; }

    public ObservableCollection<ModelRowViewModel> AllModels { get; }

    public ObservableCollection<ModelRowViewModel> Models { get; } = [];

    public ICommand OpenImportPageCommand { get; }

    public SeafoodCategoryOptionViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            SetProperty(ref _selectedCategory, value);
            DraftVersionCode = BuildNextVersionCode(value?.Name);
            RefreshModels();
        }
    }

    public string DraftVersionCode
    {
        get => _draftVersionCode;
        set => SetProperty(ref _draftVersionCode, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public void RefreshModels()
    {
        Models.Clear();
        var selectedName = SelectedCategory?.Name;
        var items = selectedName is null || selectedName == "全部海鲜"
            ? AllModels
            : new ObservableCollection<ModelRowViewModel>(AllModels.Where(x => x.CategoryName == selectedName));

        foreach (var model in items)
        {
            Models.Add(model);
        }

        StatusMessage = selectedName is null || selectedName == "全部海鲜"
            ? $"当前显示全部模型，共 {Models.Count} 个版本。"
            : $"当前显示 {selectedName} 模型，共 {Models.Count} 个版本。";
    }

    public string BuildNextVersionCode(string? categoryName)
    {
        var prefix = string.IsNullOrWhiteSpace(categoryName) || categoryName == "全部海鲜"
            ? "MODEL"
            : BuildCategoryCode(categoryName.Trim());
        var existing = AllModels
            .Where(x => x.CategoryName == categoryName)
            .Select(x => x.Version)
            .ToList();

        var index = 1;
        while (true)
        {
            var code = $"{prefix}-MV-{index:000}";
            if (!existing.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                return code;
            }

            index++;
        }
    }

    private void OpenImportPage() =>
        ShellNavigationService.Navigate(
            new ModelImportPageViewModel(SelectedCategory, DraftVersionCode),
            "导入新模型",
            "选择海鲜类别并填写模型描述，导入成功后生成版本号");

    private static string BuildCategoryCode(string categoryName) => categoryName switch
    {
        "花蛤" => "HG",
        "油蛤" => "YG",
        "美贝" => "MB",
        _ => categoryName.ToUpperInvariant()
    };
}

public sealed class ModelImportPageViewModel : ViewModelBase
{
    private readonly string _suggestedVersionCode;
    private SeafoodCategoryOptionViewModel? _selectedCategory;
    private string _draftVersionCode;
    private string _draftModelDescription = string.Empty;
    private string _statusMessage = "导入页会在保存成功后，根据海鲜类别生成不重复的版本号。";

    public ModelImportPageViewModel(SeafoodCategoryOptionViewModel? selectedCategory, string suggestedVersionCode)
    {
        CategoryOptions =
        [
            new(Guid.Parse("b33199af-10e8-411a-b5e6-9821d467b4df"), "花蛤"),
            new(Guid.Parse("8eeac4e9-0646-4a5d-a1ee-0d09a1776e7a"), "油蛤"),
            new(Guid.Parse("7fc3d726-c789-4d97-8c39-1275521f9c8c"), "美贝")
        ];
        _selectedCategory = CategoryOptions.FirstOrDefault(x => x.Name == selectedCategory?.Name) ?? CategoryOptions.First();
        _suggestedVersionCode = suggestedVersionCode;
        _draftVersionCode = string.Empty;
        BackToListCommand = new RelayCommand(_ => BackToList());
        SaveImportCommand = new RelayCommand(_ => SaveImport());
    }

    public ObservableCollection<SeafoodCategoryOptionViewModel> CategoryOptions { get; }

    public ICommand BackToListCommand { get; }

    public ICommand SaveImportCommand { get; }

    public SeafoodCategoryOptionViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public string DraftVersionCode
    {
        get => _draftVersionCode;
        set
        {
            SetProperty(ref _draftVersionCode, value);
            RaisePropertyChanged(nameof(HasGeneratedVersionCode));
        }
    }

    public string DraftModelDescription
    {
        get => _draftModelDescription;
        set => SetProperty(ref _draftModelDescription, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasGeneratedVersionCode => !string.IsNullOrWhiteSpace(DraftVersionCode);

    private void SaveImport()
    {
        if (SelectedCategory is null)
        {
            StatusMessage = "请先选择海鲜类别。";
            return;
        }

        if (string.IsNullOrWhiteSpace(DraftModelDescription))
        {
            StatusMessage = "请先填写模型描述。";
            return;
        }

        if (DraftModelDescription.Trim().Length < 4)
        {
            StatusMessage = "模型描述至少填写 4 个字符，便于后续区分版本。";
            return;
        }

        DraftVersionCode = _suggestedVersionCode;
        StatusMessage = $"已准备导入 {SelectedCategory.Name} 模型，生成版本号 {DraftVersionCode}。";
    }

    private void BackToList() =>
        ShellNavigationService.Navigate(
            new ModelManagementPageViewModel(),
            "模型管理",
            "按海鲜类别管理多版本模型、启用与回滚");
}

public sealed class SettingsPageViewModel
{
}

public sealed class EditableDefectItemViewModel(string name) : ViewModelBase
{
    private string _name = name;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
}

public sealed record DeviceSummary(string Name, string Status);

public sealed record ProductRowViewModel(
    Guid ProductId,
    string Name,
    string Code,
    string NormalLabel,
    string DefectTraitsDisplay,
    int TraitCount);

public sealed record ProductOptionViewModel(
    Guid Id,
    string Name,
    string NormalLabel,
    string DefectTraitsSummary)
{
    public override string ToString() => Name;
}

public sealed record ModelOptionViewModel(
    Guid Id,
    string Version,
    string LabelMapJson,
    string CategoryName)
{
    public override string ToString() => Version;
}

public sealed record ChannelRowViewModel(
    Guid ChannelId,
    int ChannelNo,
    string Name,
    string ProductName,
    string ModelVersion,
    string DefectAction,
    string ConfidenceThreshold,
    Guid ProductId,
    Guid? ModelVersionId,
    DefectHandlingAction DefectActionCode,
    decimal ConveyorSpeedRaw,
    decimal VoltageRaw,
    decimal CurrentRaw,
    bool IsActive,
    string RunningStatus);

public sealed record ProductFilterOptionViewModel(string ProductName, string Label)
{
    public override string ToString() => Label;
}

public sealed record ActiveChannelOptionViewModel(Guid ChannelId, string Name)
{
    public override string ToString() => Name;
}

public sealed record SeafoodCategoryOptionViewModel(Guid Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record ModelRowViewModel(string CategoryName, string Version, string Status, string LabelMap, bool IsBound);
