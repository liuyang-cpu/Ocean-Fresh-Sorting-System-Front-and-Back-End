using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OceanFresh.SortingSystem.Application;
using OceanFresh.SortingSystem.Domain;
using OceanFresh.SortingSystem.HMI;

namespace OceanFresh.SortingSystem.HMI.Pages;

public sealed class DashboardPageViewModel : ViewModelBase, IActivatablePageViewModel, IDisposable
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly DispatcherTimer _refreshTimer;
    private string _dashboardStatus = "正在读取首页数据...";
    private string _latestAbnormalImageName = "最近异常帧: 暂无";
    private string _latestAbnormalSummary = "暂无异常记录";

    public DashboardPageViewModel()
    {
        OpenHistoryStatisticsCommand = new RelayCommand(_ => OpenHistoryStatistics());
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await LoadDashboardAsync();
    }

    public ObservableCollection<KpiCardViewModel> KpiCards { get; } = [];

    public ObservableCollection<DeviceSummary> DeviceSummaries { get; } = [];

    public ObservableCollection<AlarmPolicyRowViewModel> AlarmPolicies { get; } = [];

    public ObservableCollection<DefectOverviewRowViewModel> DefectOverviewRows { get; } = [];

    public ObservableCollection<DashboardChannelCardViewModel> ChannelCards { get; } = [];

    public ICommand OpenHistoryStatisticsCommand { get; }

    public string DashboardStatus
    {
        get => _dashboardStatus;
        private set => SetProperty(ref _dashboardStatus, value);
    }

    public string LatestAbnormalImageName
    {
        get => _latestAbnormalImageName;
        private set => SetProperty(ref _latestAbnormalImageName, value);
    }

    public string LatestAbnormalSummary
    {
        get => _latestAbnormalSummary;
        private set => SetProperty(ref _latestAbnormalSummary, value);
    }

    public async Task ActivateAsync()
    {
        await LoadDashboardAsync();
        _refreshTimer.Start();
    }

    public void Dispose() => _refreshTimer.Stop();

    private async Task LoadDashboardAsync()
    {
        try
        {
            var dashboard = await _apiClient.GetDashboardAsync(CancellationToken.None);
            RefreshKpis(dashboard);
            RefreshDevices(dashboard);
            RefreshAlarms(dashboard);
            RefreshDefects(dashboard);
            RefreshChannels(dashboard);
            RefreshLatestAbnormal(dashboard);
            DashboardStatus = dashboard.CurrentSession is null
                ? "当前没有检测任务，首页显示机器状态与通道待机信息。"
                : $"当前检测任务: {dashboard.CurrentSession.SessionCode} · {MapSessionStatus(dashboard.CurrentSession.Status)}";
        }
        catch (Exception ex)
        {
            DashboardStatus = $"首页数据读取失败: {ex.Message}";
        }
    }

    private void RefreshKpis(DashboardDto dashboard)
    {
        KpiCards.Clear();
        KpiCards.Add(new KpiCardViewModel("当前通道", dashboard.Summary.CurrentChannel, "\uE7C1", "#F1F7FC", "#4CC3FF"));
        KpiCards.Add(new KpiCardViewModel("本轮海鲜总数", dashboard.Summary.TotalCount.ToString(CultureInfo.InvariantCulture), "\uE9D2", "#F1F7FC", "#7FAFD2"));
        KpiCards.Add(new KpiCardViewModel("正常品数", dashboard.Summary.NormalCount.ToString(CultureInfo.InvariantCulture), "\uE930", "#F1F7FC", "#20D58C"));
        KpiCards.Add(new KpiCardViewModel("工作时长", dashboard.Summary.MachineWorkDuration, "\uE916", "#F1F7FC", "#C9D8E6"));
        KpiCards.Add(new KpiCardViewModel("异常总数", dashboard.Summary.RejectCount.ToString(CultureInfo.InvariantCulture), "\uE7BA", "#F1F7FC", "#FFB15A"));
        KpiCards.Add(new KpiCardViewModel("良率", $"{dashboard.Summary.YieldRate:0.##}%", "\uE9F9", "#20D58C", "#20D58C"));
    }

    private void RefreshDevices(DashboardDto dashboard)
    {
        DeviceSummaries.Clear();
        if (dashboard.Snapshot.Devices.Count == 0)
        {
            DeviceSummaries.Add(new DeviceSummary("机器状态", MapRuntimeMode(dashboard.Snapshot.RuntimeMode), dashboard.Snapshot.RuntimeMode == RuntimeMode.Running ? "#20F090" : "#FFD166"));
            DeviceSummaries.Add(new DeviceSummary("当前产品", dashboard.Summary.CurrentProduct, "#7FAFD2"));
            DeviceSummaries.Add(new DeviceSummary("当前模型", dashboard.Summary.CurrentModel, "#7FAFD2"));
            return;
        }

        foreach (var device in dashboard.Snapshot.Devices)
        {
            DeviceSummaries.Add(new DeviceSummary(device.DeviceKey, device.Message, MapDeviceStateColor(device.State)));
        }
    }

    private void RefreshAlarms(DashboardDto dashboard)
    {
        AlarmPolicies.Clear();
        if (dashboard.Snapshot.ActiveAlarms.Count == 0)
        {
            AlarmPolicies.Add(new AlarmPolicyRowViewModel("安全预警", "暂无未确认预警", "#20F090"));
            AlarmPolicies.Add(new AlarmPolicyRowViewModel("检测状态", dashboard.CurrentSession is null ? "未开始检测" : MapSessionStatus(dashboard.CurrentSession.Status), "#7FAFD2"));
            return;
        }

        foreach (var alarm in dashboard.Snapshot.ActiveAlarms.Take(3))
        {
            AlarmPolicies.Add(new AlarmPolicyRowViewModel(alarm.Code, alarm.Message, alarm.Severity == AlarmSeverity.Critical ? "#FF9A9A" : "#FFD166"));
        }
    }

    private void RefreshDefects(DashboardDto dashboard)
    {
        DefectOverviewRows.Clear();
        var max = dashboard.DefectStats.Count == 0 ? 1 : dashboard.DefectStats.Max(x => x.Count);
        foreach (var item in dashboard.DefectStats)
        {
            var width = Math.Max(12, item.Count * 300.0 / max);
            DefectOverviewRows.Add(new DefectOverviewRowViewModel(item.Label, item.Count.ToString(CultureInfo.InvariantCulture), $"{item.Percent:0.##}%", width, ResolveDefectColor(item.Label)));
        }

        if (DefectOverviewRows.Count == 0)
        {
            DefectOverviewRows.Add(new DefectOverviewRowViewModel("暂无异常", "0", "0%", 12, "#7FAFD2"));
        }
    }

    private void RefreshChannels(DashboardDto dashboard)
    {
        ChannelCards.Clear();
        foreach (var channel in dashboard.ChannelKpis)
        {
            ChannelCards.Add(new DashboardChannelCardViewModel(
                channel.ChannelName,
                channel.Status,
                channel.ProductName,
                channel.ModelVersion,
                channel.TotalCount.ToString(CultureInfo.InvariantCulture),
                channel.NormalCount.ToString(CultureInfo.InvariantCulture),
                channel.RejectCount.ToString(CultureInfo.InvariantCulture),
                $"{channel.YieldRate:0.##}%",
                channel.TopDefectLabel,
                channel.Status == "运行中" ? "#20F090" : channel.Status == "禁用" ? "#778899" : "#FFD166"));
        }
    }

    private void RefreshLatestAbnormal(DashboardDto dashboard)
    {
        LatestAbnormalImageName = dashboard.LatestAbnormal is null
            ? "最近异常帧: 暂无"
            : $"最近异常帧: {Path.GetFileName(dashboard.LatestAbnormal.ImagePath)}";
        LatestAbnormalSummary = dashboard.LatestAbnormal?.Summary ?? "暂无异常记录";
    }

    private static void OpenHistoryStatistics() =>
        ShellNavigationService.Navigate(
            new ProductionStatisticsPageViewModel(),
            "数据中心",
            "按 1 天、7 天、30 天查看各海鲜产品的检测量、剔除量、良率和缺陷分布");

    private static string MapRuntimeMode(RuntimeMode mode) => mode switch
    {
        RuntimeMode.Running => "机器运行中",
        RuntimeMode.SafeStop => "安全停机",
        RuntimeMode.Faulted => "故障",
        RuntimeMode.Starting => "启动中",
        _ => "机器停止"
    };

    private static string MapSessionStatus(DetectionSessionStatus status) => status switch
    {
        DetectionSessionStatus.Running => "检测中",
        DetectionSessionStatus.Stopped => "已停止",
        _ => "已创建"
    };

    private static string MapDeviceStateColor(DeviceState state) => state switch
    {
        DeviceState.Running => "#20F090",
        DeviceState.Warning => "#FFD166",
        DeviceState.Faulted => "#FF9A9A",
        DeviceState.Offline => "#778899",
        _ => "#7FAFD2"
    };

    private static string ResolveDefectColor(string label)
    {
        if (label.Contains("破", StringComparison.OrdinalIgnoreCase) || label.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            return "#FFB15A";
        }

        if (label.Contains("空", StringComparison.OrdinalIgnoreCase) || label.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return "#7FAFD2";
        }

        if (label.Contains("泥", StringComparison.OrdinalIgnoreCase) || label.Contains("muddy", StringComparison.OrdinalIgnoreCase))
        {
            return "#FFD166";
        }

        return "#4CC3FF";
    }
}

public sealed class MonitoringPageViewModel : ViewModelBase, IActivatablePageViewModel, IDisposable
{
    private const double WarningYieldRateThreshold = 50;
    private const double TargetYieldRateThreshold = 80;
    private static readonly TimeSpan ActiveMonitorPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan IdleMonitorPollInterval = TimeSpan.FromSeconds(1);

    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly Dictionary<string, string> _streamSourceImageByFrameName = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _batchDetectionCts;
    private Task? _batchDetectionTask;
    private CancellationTokenSource? _liveMonitorCts;
    private readonly object _streamRecordSync = new();
    private Task _streamRecordWriteTail = Task.CompletedTask;
    private Exception? _streamRecordWriteError;
    private Guid? _lastLiveRecordId;
    private RuntimeDataSourceMode _dataSourceMode = RuntimeDataSourceMode.XrayCamera;
    private string _dataSourceModeText = "X 光相机";
    private string _dataSourceStatusText = "默认使用 X 光相机采集";
    private string _dataSourceDirectoryText = "采集目录: 默认 X 光采集目录";
    private string _dataSourceProgressText = "硬件剔除: 启用";
    private string _dataSourceBatchTitleText = "生产采集模式";
    private string _dataSourceProgressCompactText = "硬件剔除启用";
    private bool _isHardwareExecutionEnabled = true;
    private string _selectedFolderPath = string.Empty;
    private string _currentChannel = "当前通道: 未启用";
    private string _currentModel = "当前模型: 未选择";
    private string _currentProduct = "当前食材: 未选择";
    private string _conveyorSpeedText = "传送带速度: 暂无";
    private string _workDurationText = "工作时长: 00:00:00";
    private string _hudDetectionStatusText = "待机";
    private string _hudDetectionStatusColor = "#F59E0B";
    private string _selectedImagePath = string.Empty;
    private string _selectedImageDimensions = "1536 × 300";
    private string _statusMessage = "机器启动后会自动从 PLC/X 光采集目录读取新图片，并按当前启用通道调用 YOLO。";
    private string _lastDetection = "最近结果: 暂无";
    private string _lastNozzle = "剔除信息: 暂无";
    private string _totalDetectedCountText = "海鲜总数: 暂无";
    private string _normalDetectedCountText = "正常总数: 暂无";
    private string _rejectedDetectedCountText = "异常总数: 暂无";
    private string _abnormalSummaryText = "0";
    private string _abnormalPercentText = "异常占比 暂无";
    private string _yieldRateText = "良率: 暂无";
    private string _yieldRateColor = "#9DB8CE";
    private double _yieldNormalBarWidth;
    private double _yieldRatePercent;
    private string _yieldStatusText = "等待检测";
    private string _normalCountLegendText = "■  正常  0";
    private string _rejectedCountLegendText = "■  异常  0";
    private string _totalTraitKindText = "共 0 种";
    private string _currentFrameTargetSummary = "当前帧   0 个目标";
    private string _latestModelInference = "模型推理: 暂无";
    private string _latestPostprocess = "后处理: 暂无";
    private string _latestResponse = "软件响应: 暂无";
    private string _latestServiceOverhead = "服务开销: 暂无";
    private string _averageModelInference = "平均推理: 暂无";
    private string _averagePostprocess = "平均后处理: 暂无";
    private string _averageResponse = "平均响应: 暂无";
    private string _averageServiceOverhead = "平均服务开销: 暂无";
    private string _currentFrame = "当前帧: 暂无";
    private string _bufferedFrame = "缓冲帧: 暂无";
    private string _finalizedFrame = "定稿帧: 暂无";
    private double _previewDesignWidth = 1536;
    private double _previewDesignHeight = 300;
    private bool _isMachineRunning;
    private Guid? _activeChannelId;
    private RuntimeChannelOptionViewModel? _selectedChannelOption;
    private bool _isChannelDetailsOpen;
    private bool _isStreaming;
    private int _streamFrameCount;
    private double _totalModelInferenceMs;
    private double _totalPostprocessMs;
    private double _totalResponseMs;
    private double _totalServiceOverheadMs;
    private readonly Dictionary<string, int> _cumulativeTraitCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProductTraitDisplayOption> _activeProductTraits = [];

    public MonitoringPageViewModel()
    {
        SelectImageCommand = new RelayCommand(_ => SelectImage());
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        UseXrayCameraDataSourceCommand = new AsyncRelayCommand(UseXrayCameraDataSourceAsync);
        RunInferenceCommand = new AsyncRelayCommand(RunInferenceAsync);
        ToggleMachineCommand = new AsyncRelayCommand(ToggleMachineAsync);
        ResetMachineCommand = new AsyncRelayCommand(ResetMachineAsync);
        ToggleBatchDetectionCommand = new AsyncRelayCommand(ToggleBatchDetectionAsync);
        StartMachineCommand = new AsyncRelayCommand(StartMachineAsync);
        StopMachineCommand = new AsyncRelayCommand(StopMachineAsync);
        StartDetectionCommand = new AsyncRelayCommand(StartDetectionAsync);
        StopDetectionCommand = new AsyncRelayCommand(StopDetectionAsync);
        RefreshActiveChannelCommand = new AsyncRelayCommand(LoadActiveChannelAsync);
        ApplySelectedChannelCommand = new AsyncRelayCommand(ApplySelectedChannelAsync);
        OpenChannelDetailsCommand = new RelayCommand(_ => OpenChannelDetails());
        CloseChannelDetailsCommand = new RelayCommand(_ => CloseChannelDetails());
    }

    public string CurrentChannel
    {
        get => _currentChannel;
        set
        {
            if (!string.Equals(_currentChannel, value, StringComparison.Ordinal))
            {
                SetProperty(ref _currentChannel, value);
                RaisePropertyChanged(nameof(CurrentChannelValue));
            }
        }
    }

    public string CurrentChannelValue => StripTelemetryPrefix(CurrentChannel);

    public RuntimeChannelOptionViewModel? SelectedChannelOption
    {
        get => _selectedChannelOption;
        set
        {
            if (Equals(_selectedChannelOption, value))
            {
                return;
            }

            SetProperty(ref _selectedChannelOption, value);
            RaiseChannelSelectionStateChanged();
        }
    }

    public bool CanChangeChannel => !IsMachineRunning && ChannelOptions.Count > 0;

    public bool HasPendingChannelSelection =>
        SelectedChannelOption is not null && SelectedChannelOption.ChannelId != _activeChannelId;

    public bool CanApplyChannelSelection => CanChangeChannel && HasPendingChannelSelection;

    public bool HasActiveChannel => _activeChannelId is not null;

    public bool CanOpenChannelDetails => ChannelOptions.Count > 0;

    public bool IsChannelDetailsOpen
    {
        get => _isChannelDetailsOpen;
        set => SetProperty(ref _isChannelDetailsOpen, value);
    }

    public string ChannelDialogStatusText => IsMachineRunning
        ? "设备运行中，请先停止设备"
        : HasPendingChannelSelection
            ? "设备已停止，可以切换"
            : "请选择其他通道后切换";

    public string ChannelDialogStatusColor => IsMachineRunning || !HasPendingChannelSelection
        ? "#8FA8C2"
        : "#20D98B";

    public string ChannelSelectionActionText => HasPendingChannelSelection ? "切换" : "当前使用";

    public string ChannelSelectionStateText => _activeChannelId is null ? "未选择" : "当前使用";

    public string ChannelSelectionStateColor => _activeChannelId is null ? "#8FA8C2" : "#20D98B";

    public string ChannelSelectionToolTip => IsMachineRunning
        ? "设备运行中不可切换通道"
        : HasPendingChannelSelection
            ? "应用所选通道"
            : "当前检测使用的通道";

    public bool CanToggleMachine => IsMachineRunning ||
        (_activeChannelId is not null && !HasPendingChannelSelection);

    public bool CanResetMachine => !IsMachineRunning && !IsStreaming;

    public string CurrentModel
    {
        get => _currentModel;
        set
        {
            if (!string.Equals(_currentModel, value, StringComparison.Ordinal))
            {
                SetProperty(ref _currentModel, value);
                RaisePropertyChanged(nameof(CurrentModelValue));
            }
        }
    }

    public string CurrentModelValue => StripTelemetryPrefix(CurrentModel);

    public string CurrentProduct
    {
        get => _currentProduct;
        set
        {
            if (!string.Equals(_currentProduct, value, StringComparison.Ordinal))
            {
                SetProperty(ref _currentProduct, value);
                RaisePropertyChanged(nameof(CurrentProductValue));
            }
        }
    }

    public string CurrentProductValue => StripTelemetryPrefix(CurrentProduct);

    public string ConveyorSpeedText
    {
        get => _conveyorSpeedText;
        set
        {
            if (!string.Equals(_conveyorSpeedText, value, StringComparison.Ordinal))
            {
                SetProperty(ref _conveyorSpeedText, value);
                RaisePropertyChanged(nameof(ConveyorSpeedValue));
            }
        }
    }

    public string ConveyorSpeedValue => StripTelemetryPrefix(ConveyorSpeedText);

    public string WorkDurationText
    {
        get => _workDurationText;
        set
        {
            if (!string.Equals(_workDurationText, value, StringComparison.Ordinal))
            {
                SetProperty(ref _workDurationText, value);
                RaisePropertyChanged(nameof(WorkDurationValue));
            }
        }
    }

    public string WorkDurationValue => StripTelemetryPrefix(WorkDurationText);

    public string HudDetectionStatusText
    {
        get => _hudDetectionStatusText;
        set => SetProperty(ref _hudDetectionStatusText, value);
    }

    public string HudDetectionStatusColor
    {
        get => _hudDetectionStatusColor;
        set => SetProperty(ref _hudDetectionStatusColor, value);
    }

    public string SelectedImagePath
    {
        get => _selectedImagePath;
        set
        {
            SetProperty(ref _selectedImagePath, value);
            UpdateSelectedImageMetadata(value);
            RaisePropertyChanged(nameof(SelectedImageName));
            RaisePropertyChanged(nameof(HasSelectedImage));
        }
    }

    public string SelectedImageName => string.IsNullOrWhiteSpace(SelectedImagePath)
        ? "尚未导入 X 光图片"
        : Path.GetFileName(SelectedImagePath);

    public bool HasSelectedImage => !string.IsNullOrWhiteSpace(SelectedImagePath);

    public string SelectedImageDimensions
    {
        get => _selectedImageDimensions;
        set => SetProperty(ref _selectedImageDimensions, value);
    }

    public string PlcXrayInputDirectory { get; } = ResolvePlcXrayInputDirectory();

    public double PreviewDesignWidth
    {
        get => _previewDesignWidth;
        set => SetProperty(ref _previewDesignWidth, value);
    }

    public double PreviewDesignHeight
    {
        get => _previewDesignHeight;
        set => SetProperty(ref _previewDesignHeight, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LastDetection
    {
        get => _lastDetection;
        set => SetProperty(ref _lastDetection, value);
    }

    public string LastNozzle
    {
        get => _lastNozzle;
        set => SetProperty(ref _lastNozzle, value);
    }

    public string TotalDetectedCountText
    {
        get => _totalDetectedCountText;
        set
        {
            if (!string.Equals(_totalDetectedCountText, value, StringComparison.Ordinal))
            {
                SetProperty(ref _totalDetectedCountText, value);
                RaisePropertyChanged(nameof(TotalDetectedCountValue));
            }
        }
    }

    public string TotalDetectedCountValue => StripTelemetryPrefix(TotalDetectedCountText);

    public string NormalDetectedCountText
    {
        get => _normalDetectedCountText;
        set
        {
            if (!string.Equals(_normalDetectedCountText, value, StringComparison.Ordinal))
            {
                SetProperty(ref _normalDetectedCountText, value);
                RaisePropertyChanged(nameof(NormalDetectedCountValue));
            }
        }
    }

    public string NormalDetectedCountValue => StripTelemetryPrefix(NormalDetectedCountText);

    public string RejectedDetectedCountText
    {
        get => _rejectedDetectedCountText;
        set
        {
            if (!string.Equals(_rejectedDetectedCountText, value, StringComparison.Ordinal))
            {
                SetProperty(ref _rejectedDetectedCountText, value);
                RaisePropertyChanged(nameof(RejectedDetectedCountValue));
            }
        }
    }

    public string RejectedDetectedCountValue => StripTelemetryPrefix(RejectedDetectedCountText);

    public string AbnormalSummaryText
    {
        get => _abnormalSummaryText;
        set => SetProperty(ref _abnormalSummaryText, value);
    }

    public string AbnormalPercentText
    {
        get => _abnormalPercentText;
        set => SetProperty(ref _abnormalPercentText, value);
    }

    public string YieldRateText
    {
        get => _yieldRateText;
        set
        {
            if (!string.Equals(_yieldRateText, value, StringComparison.Ordinal))
            {
                SetProperty(ref _yieldRateText, value);
                RaisePropertyChanged(nameof(YieldRateValue));
            }
        }
    }

    public string YieldRateValue => StripTelemetryPrefix(YieldRateText);

    public string YieldRateColor
    {
        get => _yieldRateColor;
        set => SetProperty(ref _yieldRateColor, value);
    }

    public double YieldNormalBarWidth
    {
        get => _yieldNormalBarWidth;
        set => SetProperty(ref _yieldNormalBarWidth, value);
    }

    public double YieldRatePercent
    {
        get => _yieldRatePercent;
        set => SetProperty(ref _yieldRatePercent, value);
    }

    public string YieldStatusText
    {
        get => _yieldStatusText;
        set => SetProperty(ref _yieldStatusText, value);
    }

    public string YieldTargetText => $"达标线 {TargetYieldRateThreshold:0}%";

    public GridLength YieldTargetLeftWidth => new(TargetYieldRateThreshold, GridUnitType.Star);

    public GridLength YieldTargetRightWidth => new(100 - TargetYieldRateThreshold, GridUnitType.Star);

    public GridLength YieldWarningZoneWidth => new(WarningYieldRateThreshold, GridUnitType.Star);

    public GridLength YieldApproachZoneWidth => new(TargetYieldRateThreshold - WarningYieldRateThreshold, GridUnitType.Star);

    public GridLength YieldPassZoneWidth => new(100 - TargetYieldRateThreshold, GridUnitType.Star);

    public string NormalCountLegendText
    {
        get => _normalCountLegendText;
        set => SetProperty(ref _normalCountLegendText, value);
    }

    public string RejectedCountLegendText
    {
        get => _rejectedCountLegendText;
        set => SetProperty(ref _rejectedCountLegendText, value);
    }

    public string TotalTraitKindText
    {
        get => _totalTraitKindText;
        set => SetProperty(ref _totalTraitKindText, value);
    }

    public string CurrentFrameTargetSummary
    {
        get => _currentFrameTargetSummary;
        set => SetProperty(ref _currentFrameTargetSummary, value);
    }

    public RuntimeDataSourceMode DataSourceMode
    {
        get => _dataSourceMode;
        set
        {
            if (_dataSourceMode == value)
            {
                return;
            }

            SetProperty(ref _dataSourceMode, value);
            RaisePropertyChanged(nameof(IsLocalImageDirectorySource));
            RaisePropertyChanged(nameof(IsXrayCameraSource));
            RaisePropertyChanged(nameof(XrayCameraSourceBackground));
            RaisePropertyChanged(nameof(LocalDirectorySourceBackground));
            RaisePropertyChanged(nameof(XrayCameraSourceBorder));
            RaisePropertyChanged(nameof(LocalDirectorySourceBorder));
        }
    }

    public bool IsXrayCameraSource => DataSourceMode == RuntimeDataSourceMode.XrayCamera;

    public bool IsLocalImageDirectorySource => DataSourceMode == RuntimeDataSourceMode.LocalImageDirectory;

    public string DataSourceModeText
    {
        get => _dataSourceModeText;
        set => SetProperty(ref _dataSourceModeText, value);
    }

    public string DataSourceStatusText
    {
        get => _dataSourceStatusText;
        set => SetProperty(ref _dataSourceStatusText, value);
    }

    public string DataSourceDirectoryText
    {
        get => _dataSourceDirectoryText;
        set => SetProperty(ref _dataSourceDirectoryText, value);
    }

    public string DataSourceProgressText
    {
        get => _dataSourceProgressText;
        set => SetProperty(ref _dataSourceProgressText, value);
    }

    public string DataSourceBatchTitleText
    {
        get => _dataSourceBatchTitleText;
        set => SetProperty(ref _dataSourceBatchTitleText, value);
    }

    public string DataSourceProgressCompactText
    {
        get => _dataSourceProgressCompactText;
        set => SetProperty(ref _dataSourceProgressCompactText, value);
    }

    public bool IsHardwareExecutionEnabled
    {
        get => _isHardwareExecutionEnabled;
        set => SetProperty(ref _isHardwareExecutionEnabled, value);
    }

    public string XrayCameraSourceBackground => IsXrayCameraSource ? "#1D4F7A" : "#101B2A";

    public string LocalDirectorySourceBackground => IsLocalImageDirectorySource ? "#1D4F7A" : "#101B2A";

    public string XrayCameraSourceBorder => IsXrayCameraSource ? "#5FA8E8" : "#3B5870";

    public string LocalDirectorySourceBorder => IsLocalImageDirectorySource ? "#5FA8E8" : "#3B5870";

    public string SelectedFolderPath
    {
        get => _selectedFolderPath;
        set
        {
            SetProperty(ref _selectedFolderPath, value);
            RaisePropertyChanged(nameof(SelectedFolderName));
            RaisePropertyChanged(nameof(HasSelectedFolder));
            RaisePropertyChanged(nameof(CanStartStreaming));
        }
    }

    public string SelectedFolderName => string.IsNullOrWhiteSpace(SelectedFolderPath)
        ? "尚未选择图片目录"
        : Path.GetFileName(SelectedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public bool HasSelectedFolder => !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public bool IsMachineRunning
    {
        get => _isMachineRunning;
        set
        {
            if (_isMachineRunning == value)
            {
                return;
            }

            SetProperty(ref _isMachineRunning, value);
            RaisePropertyChanged(nameof(MachineControlText));
            RaisePropertyChanged(nameof(MachineControlBackground));
            RaisePropertyChanged(nameof(MachineControlForeground));
            RaisePropertyChanged(nameof(MachineControlBorder));
            RaisePropertyChanged(nameof(CanResetMachine));
            RaiseChannelSelectionStateChanged();
        }
    }

    public string MachineControlText => IsMachineRunning ? "停止设备" : "启动设备";

    public string MachineControlBackground => IsMachineRunning ? "#B01A24" : "#0F8D5A";

    public string MachineControlForeground => "#FFFFFF";

    public string MachineControlBorder => IsMachineRunning ? "#F04A4F" : "#25D89B";

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming == value)
            {
                return;
            }

            SetProperty(ref _isStreaming, value);
            RaisePropertyChanged(nameof(CanStartStreaming));
            RaisePropertyChanged(nameof(BatchDetectionControlText));
            RaisePropertyChanged(nameof(BatchDetectionBackground));
            RaisePropertyChanged(nameof(BatchDetectionForeground));
            RaisePropertyChanged(nameof(BatchDetectionBorder));
            RaisePropertyChanged(nameof(CanResetMachine));
        }
    }

    public bool CanStartStreaming => HasSelectedFolder && !IsStreaming;

    public string BatchDetectionControlText => IsStreaming ? "停止目录检测" : "开始目录检测";

    public string BatchDetectionBackground => IsStreaming ? "#301F28" : "#101B2A";

    public string BatchDetectionForeground => IsStreaming ? "#FFB4B4" : "#CFE2FF";

    public string BatchDetectionBorder => IsStreaming ? "#7F333A" : "#3B5870";

    public string LatestModelInference
    {
        get => _latestModelInference;
        set => SetProperty(ref _latestModelInference, value);
    }

    public string LatestPostprocess
    {
        get => _latestPostprocess;
        set => SetProperty(ref _latestPostprocess, value);
    }

    public string LatestResponse
    {
        get => _latestResponse;
        set => SetProperty(ref _latestResponse, value);
    }

    public string AverageModelInference
    {
        get => _averageModelInference;
        set => SetProperty(ref _averageModelInference, value);
    }

    public string AveragePostprocess
    {
        get => _averagePostprocess;
        set => SetProperty(ref _averagePostprocess, value);
    }

    public string AverageResponse
    {
        get => _averageResponse;
        set => SetProperty(ref _averageResponse, value);
    }

    public string LatestServiceOverhead
    {
        get => _latestServiceOverhead;
        set => SetProperty(ref _latestServiceOverhead, value);
    }

    public string AverageServiceOverhead
    {
        get => _averageServiceOverhead;
        set => SetProperty(ref _averageServiceOverhead, value);
    }

    public string CurrentFrame
    {
        get => _currentFrame;
        set => SetProperty(ref _currentFrame, value);
    }

    public string BufferedFrame
    {
        get => _bufferedFrame;
        set => SetProperty(ref _bufferedFrame, value);
    }

    public string FinalizedFrame
    {
        get => _finalizedFrame;
        set => SetProperty(ref _finalizedFrame, value);
    }

    public ObservableCollection<ManualDetectionItemViewModel> DetectionItems { get; } = [];

    public ObservableCollection<ManualEjectCommandItemViewModel> EjectCommandItems { get; } = [];

    public ObservableCollection<TraitCountItemViewModel> FrameTraitItems { get; } = [];

    public ObservableCollection<TraitCountItemViewModel> TraitCountItems { get; } = [];

    public ObservableCollection<TraitCountItemViewModel> DefectCountItems { get; } = [];

    public ObservableCollection<TraitCountItemViewModel> AbnormalTraitItems { get; } = [];

    public ObservableCollection<LedSegmentViewModel> AbnormalCompositionSegments { get; } = [];

    public ObservableCollection<RuntimeChannelOptionViewModel> ChannelOptions { get; } = [];

    public ICommand SelectImageCommand { get; }

    public ICommand SelectFolderCommand { get; }

    public ICommand UseXrayCameraDataSourceCommand { get; }

    public ICommand RunInferenceCommand { get; }

    public ICommand ToggleMachineCommand { get; }

    public ICommand ResetMachineCommand { get; }

    public ICommand ToggleBatchDetectionCommand { get; }

    public ICommand StartMachineCommand { get; }

    public ICommand StopMachineCommand { get; }

    public ICommand StartDetectionCommand { get; }

    public ICommand StopDetectionCommand { get; }

    public ICommand RefreshActiveChannelCommand { get; }

    public ICommand ApplySelectedChannelCommand { get; }

    public ICommand OpenChannelDetailsCommand { get; }

    public ICommand CloseChannelDetailsCommand { get; }

    public async Task ActivateAsync()
    {
        await LoadActiveChannelAsync();
        await RefreshRuntimeStatusAsync();
        if (!IsMachineRunning)
        {
            await PreloadActiveChannelModelAsync();
        }
        StartLiveRecordPolling();
    }

    public void Dispose()
    {
        _batchDetectionCts?.Cancel();
        _batchDetectionCts?.Dispose();
        _liveMonitorCts?.Cancel();
        _liveMonitorCts?.Dispose();
    }

    private async Task LoadActiveChannelAsync()
    {
        try
        {
            var channelsTask = _apiClient.GetChannelsAsync(CancellationToken.None);
            var productsTask = _apiClient.GetProductsAsync(CancellationToken.None);
            await Task.WhenAll(channelsTask, productsTask);
            var channels = await channelsTask;
            var products = await productsTask;
            var traitsByProduct = products.ToDictionary(
                item => item.Product.Id,
                item => string.Join(" / ", item.Traits
                    .Where(trait => trait.IsEnabled && !trait.IsNormal)
                    .OrderBy(trait => trait.SortOrder)
                    .Select(trait => trait.Name)));
            var active = channels.FirstOrDefault(x => x.Channel.IsEnabled);
            SelectedChannelOption = null;
            ChannelOptions.Clear();
            foreach (var channel in channels.OrderBy(x => x.Channel.ChannelNo))
            {
                ChannelOptions.Add(new RuntimeChannelOptionViewModel(
                    channel.Channel.Id,
                    channel.Channel.ChannelNo,
                    channel.Channel.Name,
                    channel.Product?.Name ?? "未绑定产品",
                    channel.ModelVersion?.Version ?? "未绑定模型",
                    traitsByProduct.GetValueOrDefault(channel.Channel.SeafoodProductId, "暂无缺陷类型"),
                    channel.Channel.IsEnabled));
            }

            _activeChannelId = active?.Channel.Id;
            SelectedChannelOption = active is null
                ? ChannelOptions.FirstOrDefault()
                : ChannelOptions.FirstOrDefault(x => x.ChannelId == active.Channel.Id);
            RaiseChannelSelectionStateChanged();
            if (active is null)
            {
                CurrentChannel = "当前通道: 未启用";
                CurrentModel = "当前模型: 未选择";
                CurrentProduct = "当前食材: 未选择";
                ConveyorSpeedText = "传送带速度: 暂无";
                StatusMessage = "当前没有启用中的通道，请先在通道配置页启用一条通道。";
                return;
            }

            CurrentChannel = $"当前通道: {active.Channel.Name}";
            CurrentModel = $"当前模型: {active.ModelVersion?.Version ?? "未选择"}";
            CurrentProduct = $"当前食材: {active.Product?.Name ?? "未选择"}";
            ConveyorSpeedText = $"传送带速度: {MachineRuntimeDefaults.ConveyorSpeedMetersPerSecond:0.##} m/s";
            await UpdateActiveProductTraitsAsync(active.Channel.SeafoodProductId);
            if (!HasSelectedImage)
            {
                StatusMessage = $"机器启动后会自动读取 PLC/X 光采集目录的新图片: {PlcXrayInputDirectory}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取当前启用通道失败: {ex.Message}";
        }
    }

    private async Task ApplySelectedChannelAsync()
    {
        if (IsMachineRunning)
        {
            StatusMessage = "设备运行中不可切换通道，请先停止设备。";
            return;
        }

        var selected = SelectedChannelOption;
        if (selected is null || selected.ChannelId == _activeChannelId)
        {
            return;
        }

        var prompt = _activeChannelId is null
            ? $"将 {selected.Name} 设为当前检测通道？"
            : $"将当前检测通道切换为 {selected.Name}？";
        if (MessageBox.Show(
                prompt,
                "切换通道",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            SelectedChannelOption = ChannelOptions.FirstOrDefault(x => x.ChannelId == _activeChannelId);
            return;
        }

        try
        {
            StatusMessage = $"正在切换到 {selected.Name}...";
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _apiClient.SelectRuntimeChannelAsync(selected.ChannelId, timeoutCts.Token);
            ChannelRuntimeSelectionService.SetActive(selected.ChannelId, selected.Name);
            ApplyActiveChannelSelection(selected);
            await Task.Yield();
            await LoadActiveChannelAsync();
            await PreloadActiveChannelModelAsync();
            StatusMessage = $"当前检测通道已切换为 {selected.Name}。";
        }
        catch (Exception ex)
        {
            SelectedChannelOption = ChannelOptions.FirstOrDefault(x => x.ChannelId == _activeChannelId);
            StatusMessage = $"切换通道失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "切换通道", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyActiveChannelSelection(RuntimeChannelOptionViewModel selected)
    {
        _activeChannelId = selected.ChannelId;
        SelectedChannelOption = selected;
        CurrentChannel = $"当前通道: {selected.Name}";
        CurrentModel = $"当前模型: {selected.ModelVersion}";
        CurrentProduct = $"当前食材: {selected.ProductName}";
        ConveyorSpeedText = $"传送带速度: {MachineRuntimeDefaults.ConveyorSpeedMetersPerSecond:0.##} m/s";
        IsChannelDetailsOpen = false;
        RaiseChannelSelectionStateChanged();
    }

    private void RaiseChannelSelectionStateChanged()
    {
        RaisePropertyChanged(nameof(CanChangeChannel));
        RaisePropertyChanged(nameof(HasPendingChannelSelection));
        RaisePropertyChanged(nameof(CanApplyChannelSelection));
        RaisePropertyChanged(nameof(ChannelSelectionActionText));
        RaisePropertyChanged(nameof(ChannelSelectionStateText));
        RaisePropertyChanged(nameof(ChannelSelectionStateColor));
        RaisePropertyChanged(nameof(ChannelSelectionToolTip));
        RaisePropertyChanged(nameof(ChannelDialogStatusText));
        RaisePropertyChanged(nameof(ChannelDialogStatusColor));
        RaisePropertyChanged(nameof(HasActiveChannel));
        RaisePropertyChanged(nameof(CanOpenChannelDetails));
        RaisePropertyChanged(nameof(CanToggleMachine));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OpenChannelDetails()
    {
        SelectedChannelOption = ChannelOptions.FirstOrDefault(x => x.ChannelId == _activeChannelId)
            ?? ChannelOptions.FirstOrDefault();
        IsChannelDetailsOpen = true;
    }

    private void CloseChannelDetails()
    {
        SelectedChannelOption = ChannelOptions.FirstOrDefault(x => x.ChannelId == _activeChannelId)
            ?? ChannelOptions.FirstOrDefault();
        IsChannelDetailsOpen = false;
    }

    private async Task RefreshRuntimeStatusAsync()
    {
        try
        {
            var dashboard = await _apiClient.GetDashboardAsync(CancellationToken.None);
            WorkDurationText = $"工作时长: {dashboard.Summary.MachineWorkDuration}";
            IsMachineRunning = dashboard.Snapshot.RuntimeMode == RuntimeMode.Running;
            ApplyDataSource(dashboard.DataSource);
            if (dashboard.Snapshot.RuntimeMode == RuntimeMode.Running)
            {
                HudDetectionStatusText = dashboard.CurrentSession?.Status == DetectionSessionStatus.Running ? "正在检测" : "机器运行";
                HudDetectionStatusColor = "#10B981";
            }
            else
            {
                HudDetectionStatusText = dashboard.Snapshot.RuntimeMode == RuntimeMode.Faulted ? "故障" : "待机";
                HudDetectionStatusColor = dashboard.Snapshot.RuntimeMode == RuntimeMode.Faulted ? "#EF4444" : "#F59E0B";
            }

            if (!string.IsNullOrWhiteSpace(dashboard.Summary.CurrentProduct) && dashboard.Summary.CurrentProduct != "未选择产品")
            {
                CurrentProduct = $"当前食材: {dashboard.Summary.CurrentProduct}";
            }

            if (!string.IsNullOrWhiteSpace(dashboard.Summary.CurrentChannel) && dashboard.Summary.CurrentChannel != "未启用通道")
            {
                CurrentChannel = $"当前通道: {dashboard.Summary.CurrentChannel}";
            }

            if (!string.IsNullOrWhiteSpace(dashboard.Summary.CurrentModel) && dashboard.Summary.CurrentModel != "未选择模型")
            {
                CurrentModel = $"当前模型: {dashboard.Summary.CurrentModel}";
            }

            // 监控页计数使用当前采集或批量检测会话的本地累计，避免被历史检测任务聚合值覆盖。
        }
        catch
        {
            // Runtime status is auxiliary for the monitor; keep image inspection available if the API restarts.
        }
    }

    private async Task ToggleMachineAsync()
    {
        if (IsMachineRunning)
        {
            await StopMachineAsync();
            return;
        }

        await StartMachineAsync();
    }

    private async Task StartMachineAsync()
    {
        if (_activeChannelId is null)
        {
            StatusMessage = "请先选择并应用当前检测通道。";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (HasPendingChannelSelection)
        {
            StatusMessage = "通道选择尚未应用，请先完成通道切换。";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            StatusMessage = "正在启动设备与检测服务...";
            await Task.Run(() =>
            {
                CompanionProcessManager.EnsureLocalApiStarted();
                CompanionProcessManager.EnsureYoloServiceStarted();
            });

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _apiClient.StartMachineAsync(timeoutCts.Token, IsLocalImageDirectorySource);
            await RefreshRuntimeStatusAsync();
            await MarkLatestInspectionRecordAsSeenAsync();
            ResetStreamMetrics();
            DetectionItems.Clear();
            EjectCommandItems.Clear();
            LastDetection = "最近结果: 等待 PLC/X 光图片";
            LastNozzle = "剔除信息: 暂无";
            HudDetectionStatusText = "正在检测";
            HudDetectionStatusColor = "#10B981";
            IsMachineRunning = true;
            if (IsLocalImageDirectorySource)
            {
                if (!HasSelectedFolder)
                {
                    StatusMessage = "机器已启动；当前为离线图片模式，请先选择或更新图片源。";
                    return;
                }

                StatusMessage = $"机器已启动，正在开始离线图片检测: {SelectedFolderName}";
                _batchDetectionTask = StartBatchDetectionAsync();
                return;
            }

            StatusMessage = $"机器已启动，检测任务已自动创建；正在监听 PLC/X 光采集目录并允许硬件剔除: {PlcXrayInputDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "机器启动超时，请检查本地 API、YOLO 服务或通道模型配置。";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"机器启动失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StopMachineAsync()
    {
        try
        {
            StatusMessage = "正在停止设备与检测任务...";
            await Task.Run(CompanionProcessManager.EnsureLocalApiStarted);
            StopBatchDetection();
            var activeBatchTask = _batchDetectionTask;
            if (activeBatchTask is not null)
            {
                await activeBatchTask;
            }
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _apiClient.StopMachineAsync(timeoutCts.Token);
            await RefreshRuntimeStatusAsync();
            HudDetectionStatusText = "已停止";
            HudDetectionStatusColor = "#EF4444";
            IsMachineRunning = false;
            StatusMessage = "机器已停止，当前检测任务已同步停止。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "机器停止超时，请检查本地 API 是否仍在响应。";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"机器停止失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ResetMachineAsync()
    {
        if (!CanResetMachine)
        {
            StatusMessage = "请先停止设备，再重置监控状态。";
            return;
        }

        try
        {
            StatusMessage = "正在重置监控状态...";
            await Task.Run(CompanionProcessManager.EnsureLocalApiStarted);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _apiClient.ResetMachineAsync(timeoutCts.Token);

            SelectedImagePath = string.Empty;
            DetectionItems.Clear();
            EjectCommandItems.Clear();
            ResetTimingMetrics();
            ResetStreamMetrics();
            WorkDurationText = "工作时长: 00:00:00";
            LastDetection = "最近结果: 等待第一帧";
            LastNozzle = "剔除信息: 暂无";
            HudDetectionStatusText = "待机";
            HudDetectionStatusColor = "#F59E0B";
            IsMachineRunning = false;
            await RefreshRuntimeStatusAsync();
            StatusMessage = "监控状态已重置，可以开始新的检测任务。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "重置超时，请检查本地 API 是否正常运行。";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"重置失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StartDetectionAsync()
    {
        try
        {
            CompanionProcessManager.EnsureLocalApiStarted();
            var session = await _apiClient.StartDetectionAsync(CancellationToken.None);
            await RefreshRuntimeStatusAsync();
            ResetStreamMetrics();
            HudDetectionStatusText = "正在检测";
            HudDetectionStatusColor = "#10B981";
            StatusMessage = $"检测已开始: {session.SessionCode}。本轮统计已清零。";
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("当前已有检测任务运行中", StringComparison.Ordinal))
            {
                StatusMessage = "当前检测任务已在运行中，PLC/X 光采集目录的新图片会继续进入 YOLO 推理。";
                return;
            }

            StatusMessage = $"开始检测失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StopDetectionAsync()
    {
        try
        {
            CompanionProcessManager.EnsureLocalApiStarted();
            StopBatchDetection();
            await _apiClient.StopDetectionAsync(CancellationToken.None);
            await RefreshRuntimeStatusAsync();
            HudDetectionStatusText = "待机";
            HudDetectionStatusColor = "#F59E0B";
            StatusMessage = "检测已停止，本轮数据已保留到历史统计。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"停止检测失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "运行控制", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyDataSource(RuntimeDataSourceConfigDto config)
    {
        DataSourceMode = config.Mode;
        DataSourceModeText = config.ModeText;
        IsHardwareExecutionEnabled = config.IsHardwareExecutionEnabled;
        SelectedFolderPath = config.Mode == RuntimeDataSourceMode.LocalImageDirectory
            ? config.LocalDirectoryPath ?? string.Empty
            : string.Empty;
        DataSourceStatusText = config.Status;
        DataSourceDirectoryText = config.Mode == RuntimeDataSourceMode.LocalImageDirectory
            ? $"本地目录: {config.LocalDirectoryPath}"
            : $"采集目录: {PlcXrayInputDirectory}";
        DataSourceProgressText = config.Mode == RuntimeDataSourceMode.LocalImageDirectory
            ? $"进度: {config.CurrentIndex}/{config.ImageCount} · 硬件剔除: 禁用"
            : "硬件剔除: 启用";
        DataSourceBatchTitleText = config.Mode == RuntimeDataSourceMode.LocalImageDirectory
            ? $"已加载 {config.ImageCount} 张"
            : string.Empty;
        DataSourceProgressCompactText = config.Mode == RuntimeDataSourceMode.LocalImageDirectory
            ? "硬件剔除禁用"
            : "硬件剔除启用";
    }

    private void SelectImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 X 光图片",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedImagePath = dialog.FileName;
            StatusMessage = $"已导入 X 光图片: {Path.GetFileName(dialog.FileName)}，将按 {SelectedImageDimensions} 的图像比例展示。";
        }
    }

    private async Task UseXrayCameraDataSourceAsync()
    {
        try
        {
            CompanionProcessManager.EnsureLocalApiStarted();
            var config = await _apiClient.UpdateRuntimeDataSourceAsync(
                new UpdateRuntimeDataSourceRequest(RuntimeDataSourceMode.XrayCamera, null, RuntimeDataSourceConfig.DefaultFrameIntervalMilliseconds),
                CancellationToken.None);
            ApplyDataSource(config);
            SelectedFolderPath = string.Empty;
            StatusMessage = "数据源已切换为 X 光相机。机器启动后将监听真实 X 光采集链路并允许硬件剔除。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换数据源失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "数据源配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SelectFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择本地图片目录数据源"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                CompanionProcessManager.EnsureLocalApiStarted();
                var config = await _apiClient.UpdateRuntimeDataSourceAsync(
                    new UpdateRuntimeDataSourceRequest(RuntimeDataSourceMode.LocalImageDirectory, dialog.FolderName, RuntimeDataSourceConfig.DefaultFrameIntervalMilliseconds),
                    CancellationToken.None);
                ApplyDataSource(config);
                SelectedFolderPath = config.LocalDirectoryPath ?? dialog.FolderName;
                StatusMessage = $"数据源已切换为本地图片目录: {SelectedFolderName}。该模式只做检测分析，不输出剔除动作。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"配置本地图片目录失败: {ex.Message}";
                MessageBox.Show(StatusMessage, "数据源配置", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async Task RunInferenceAsync()
    {
        if (!HasSelectedImage)
        {
            var fallbackImage = TryResolveFirstSelectedFolderImage();
            if (fallbackImage is null)
            {
                StatusMessage = "请先导入一张 X 光图片，或选择批量检测图片目录后再调用推理。";
                MessageBox.Show(StatusMessage, "单张检测", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedImagePath = fallbackImage;
        }

        try
        {
            CompanionProcessManager.EnsureLocalApiStarted();
            CompanionProcessManager.EnsureYoloServiceStarted();
            StatusMessage = "正在调用当前启用通道的模型执行单张检测...";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var result = await _apiClient.RunManualInferenceAsync(SelectedImagePath, timeout.Token);
            CurrentChannel = $"当前通道: {result.ChannelName}";
            CurrentModel = $"当前模型: {result.ModelVersion}";
            DetectionItems.Clear();
            RefreshFrameTraitItems([]);
            var frameLabels = new List<string>();
            foreach (var detection in result.Detections)
            {
                frameLabels.Add(detection.Label);
                DetectionItems.Add(new ManualDetectionItemViewModel(
                    detection.Label,
                    detection.Confidence.ToString("0.000", CultureInfo.InvariantCulture),
                    $"{detection.X},{detection.Y},{detection.Width},{detection.Height}"));
            }
            RefreshFrameTraitItems(frameLabels);
            ReplaceCountSummary(frameLabels);

            EjectCommandItems.Clear();
            foreach (var command in result.EjectCommands)
            {
                EjectCommandItems.Add(new ManualEjectCommandItemViewModel(
                    command.DefectLabel,
                    command.Action.ToString(),
                    $"#{command.NozzleNumber}",
                    $"{command.TriggerDelayMicroseconds / 1000.0:0.##} ms"));
            }

            var firstDetection = result.Detections.FirstOrDefault();
            if (firstDetection is null)
            {
                LastDetection = "最近结果: 未检出目标";
            }
            else
            {
                LastDetection = $"最近结果: {firstDetection.Label}";
            }

            LastNozzle = result.EjectCommands.Count == 0
                ? "剔除信息: 当前图片无需触发剔除"
                : $"剔除信息: 已生成 {result.EjectCommands.Count} 条命令，首条目标通道 {result.EjectCommands[0].NozzleNumber}";

            StatusMessage = $"单张检测完成: 通道 {result.ChannelName} 使用模型 {result.ModelVersion}，共返回 {result.Detections.Count} 个框、{result.EjectCommands.Count} 条剔除命令。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"单张检测失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "单张检测", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? TryResolveFirstSelectedFolderImage()
    {
        if (!HasSelectedFolder || !Directory.Exists(SelectedFolderPath))
        {
            return null;
        }

        return Directory.GetFiles(SelectedFolderPath)
            .Where(IsSupportedImageFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async Task ToggleBatchDetectionAsync()
    {
        if (IsStreaming)
        {
            StopBatchDetection();
            return;
        }

        await StartBatchDetectionAsync();
    }

    private async Task StartBatchDetectionAsync()
    {
        if (!HasSelectedFolder)
        {
            StatusMessage = "请先选择本地图片目录。";
            return;
        }

        StatusMessage = "正在检查 YOLO 检测服务...";
        await Task.Run(CompanionProcessManager.EnsureYoloServiceStarted);

        var activeContext = await LoadActiveChannelContextAsync();
        if (activeContext is null)
        {
            return;
        }

        if (!await EnsureMachineAndDetectionRunningAsync())
        {
            return;
        }

        var files = Directory.GetFiles(SelectedFolderPath)
            .Where(IsSupportedImageFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            StatusMessage = "所选文件夹中没有可用的图片文件。";
            return;
        }

        ResetTimingMetrics();
        ResetCountSummary();
        _streamSourceImageByFrameName.Clear();
        DetectionItems.Clear();
        RefreshFrameTraitItems([]);
        EjectCommandItems.Clear();
        LastDetection = "最近结果: 等待第一帧";
        LastNozzle = "剔除信息: 暂无";

        _batchDetectionCts?.Cancel();
        _batchDetectionCts = new CancellationTokenSource();
        var cancellationToken = _batchDetectionCts.Token;
        IsStreaming = true;

        string? sessionId = null;

        try
        {
            StatusMessage = $"正在启动本地目录检测任务并预热模型，共 {files.Length} 张图片。";
            var session = await _apiClient.StartYoloStreamSessionAsync(
                activeContext.ModelPath,
                activeContext.PredictConfigPath,
                68,
                $"monitor-{DateTimeOffset.Now:yyyyMMddHHmmss}",
                cancellationToken);
            sessionId = session.SessionId;
            StatusMessage = $"已启动本地目录检测，共 {files.Length} 张图片，按 68ms/张送入当前通道。模型预加载耗时 {session.ModelPreloadMilliseconds:0.##} ms。";

            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frameCycle = Stopwatch.StartNew();
                var file = files[index];
                var frameName = Path.GetFileName(file);
                _streamSourceImageByFrameName[frameName] = file;
                CurrentFrame = $"当前输入: {Path.GetFileName(file)}";
                BufferedFrame = index == 0
                    ? "当前缓冲: 首帧准备发送"
                    : $"当前缓冲: 正在等待第 {index + 1} 帧返回";
                if (index == 0)
                {
                    FinalizedFrame = "最新定稿: 首帧发送后将先进入缓冲，需等待第二帧完成定稿";
                }

                StatusMessage = $"正在发送第 {index + 1}/{files.Length} 帧: {Path.GetFileName(file)}";
                var stopwatch = Stopwatch.StartNew();
                var frame = await _apiClient.PushYoloStreamFrameAsync(sessionId, file, frameName, cancellationToken);
                stopwatch.Stop();
                UpdateTimingMetrics(
                    frame.ModelInferenceMilliseconds,
                    frame.PostprocessMilliseconds,
                    frame.ServiceOverheadMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds);
                BufferedFrame = $"当前缓冲: {frame.BufferedFrameName ?? "暂无"}";
                FinalizedFrame = $"最新定稿: {frame.FinalizedFrameName ?? "等待下一帧"}";

                if (frame.HasFinalizedOutput)
                {
                    var persistenceMs = await UpdateStreamResultViewAsync(
                        activeContext,
                        frame.FinalizedImagePath,
                        ResolveSourceImagePath(frame.FinalizedFrameName, frame.FinalizedImagePath),
                        frame.Detections,
                        cancellationToken);
                    WriteStreamTimingLog(frame, stopwatch.Elapsed.TotalMilliseconds, frameCycle.Elapsed.TotalMilliseconds, persistenceMs);
                }
                else
                {
                    LastDetection = "最近结果: 首帧已缓冲，等待第二帧";
                    LastNozzle = "剔除信息: 首帧尚未定稿";
                }

                frameCycle.Stop();
                var remainingFrameDelay = 68 - frameCycle.Elapsed.TotalMilliseconds;
                if (remainingFrameDelay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingFrameDelay), cancellationToken);
                }
            }

            if (sessionId is not null)
            {
                var stopwatch = Stopwatch.StartNew();
                var finish = await _apiClient.FinishYoloStreamSessionAsync(sessionId, cancellationToken);
                stopwatch.Stop();
                UpdateTimingMetrics(
                    0,
                    finish.PostprocessMilliseconds,
                    finish.ServiceOverheadMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds);
                CurrentFrame = "当前输入: 已完成";
                BufferedFrame = "当前缓冲: 无";
                FinalizedFrame = $"最新定稿: {finish.FinalizedFrameName ?? "无"}";
                await UpdateStreamResultViewAsync(
                    activeContext,
                    finish.FinalizedImagePath,
                    ResolveSourceImagePath(finish.FinalizedFrameName, finish.FinalizedImagePath),
                    finish.Detections,
                    cancellationToken);
                StatusMessage = $"本地目录检测完成，共处理 {files.Length} 张图片。";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "本地目录检测已停止。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"本地目录检测失败: {ex.Message}";
        }
        finally
        {
            if (sessionId is not null)
            {
                try
                {
                    await _apiClient.FinishYoloStreamSessionAsync(sessionId, CancellationToken.None);
                }
                catch
                {
                    // Ignore cleanup failures.
                }
            }

            await AwaitPendingStreamRecordsAsync();
            IsStreaming = false;
            _batchDetectionCts?.Dispose();
            _batchDetectionCts = null;
            _batchDetectionTask = null;
        }
    }

    private void StopBatchDetection() => _batchDetectionCts?.Cancel();

    private void StartLiveRecordPolling()
    {
        _liveMonitorCts?.Cancel();
        _liveMonitorCts?.Dispose();
        _liveMonitorCts = new CancellationTokenSource();
        _ = PollLiveInspectionRecordsAsync(_liveMonitorCts.Token);
    }

    private async Task MarkLatestInspectionRecordAsSeenAsync()
    {
        try
        {
            var records = await _apiClient.GetRecentInspectionRecordsAsync(1, CancellationToken.None);
            _lastLiveRecordId = records.FirstOrDefault()?.Id;
        }
        catch
        {
            _lastLiveRecordId = null;
        }
    }

    private async Task PollLiveInspectionRecordsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var records = await _apiClient.GetRecentInspectionRecordsAsync(1, cancellationToken);
                var latest = records.FirstOrDefault();
                if (latest is not null && latest.Id != _lastLiveRecordId)
                {
                    _lastLiveRecordId = latest.Id;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                        () => ApplyInspectionRecordToMonitor(latest),
                        DispatcherPriority.Background,
                        cancellationToken);
                }

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    async () => await RefreshRuntimeStatusAsync(),
                    DispatcherPriority.Background,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep the monitoring page alive if the Local API is restarting.
            }

            try
            {
                var interval = IsMachineRunning || IsStreaming
                    ? ActiveMonitorPollInterval
                    : IdleMonitorPollInterval;
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplyInspectionRecordToMonitor(InspectionRecord record)
    {
        if (IsStreaming)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.ImagePath) && File.Exists(record.ImagePath))
        {
            SelectedImagePath = record.ImagePath;
        }

        DetectionItems.Clear();
        var renderFrontendOverlay = ShouldRenderFrontendDetectionOverlay(record.ImagePath);
        var frameLabels = new List<string>();
        foreach (var detection in record.Detections)
        {
            frameLabels.Add(detection.Label);
            if (renderFrontendOverlay)
            {
                DetectionItems.Add(new ManualDetectionItemViewModel(
                    detection.Label,
                    detection.Confidence.ToString("0.000", CultureInfo.InvariantCulture),
                    $"{detection.X},{detection.Y},{detection.Width},{detection.Height}"));
            }
        }
        RefreshFrameTraitItems(frameLabels);

        EjectCommandItems.Clear();
        foreach (var command in record.EjectCommands)
        {
            EjectCommandItems.Add(new ManualEjectCommandItemViewModel(
                command.DefectLabel,
                command.Action.ToString(),
                $"#{command.NozzleNumber}",
                $"{command.TriggerDelayMicroseconds / 1000.0:0.##} ms"));
        }

        AccumulateCountSummary(record.Detections.Select(x => x.Label));

        var firstLabel = frameLabels.FirstOrDefault();
        LastDetection = firstLabel is null ? "最近结果: 未检出目标" : $"最近结果: {firstLabel}";
        LastNozzle = record.EjectCommands.Count == 0
            ? IsLocalImageDirectorySource
                ? "剔除信息: 离线检测模式，未启用硬件剔除"
                : "剔除信息: 当前 PLC/X 光图片无需触发剔除"
            : $"剔除信息: 当前 PLC/X 光图片生成 {record.EjectCommands.Count} 条命令";
        CurrentFrame = $"当前输入: {Path.GetFileName(record.ImagePath)}";
        BufferedFrame = IsLocalImageDirectorySource ? "本地目录: 按序读取" : "PLC采集: 实时读取";
        FinalizedFrame = $"最新记录: {Path.GetFileName(record.ImagePath)}";
        StatusMessage = IsLocalImageDirectorySource
            ? $"已检测本地图片: {Path.GetFileName(record.ImagePath)}，检测框 {record.Detections.Count} 个，离线模式不输出剔除动作。"
            : $"已接收 PLC/X 光图片: {Path.GetFileName(record.ImagePath)}，检测框 {record.Detections.Count} 个，剔除命令 {record.EjectCommands.Count} 条。";
    }

    private async Task<bool> EnsureMachineAndDetectionRunningAsync()
    {
        try
        {
            await Task.Run(CompanionProcessManager.EnsureLocalApiStarted);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _apiClient.StartMachineAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "机器启动超时，无法开始本地目录检测。";
            MessageBox.Show(StatusMessage, "本地目录检测", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"机器启动失败，无法开始本地目录检测: {ex.Message}";
            MessageBox.Show(StatusMessage, "本地目录检测", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var session = await _apiClient.StartDetectionAsync(timeoutCts.Token);
            StatusMessage = $"机器已启动，检测任务 {session.SessionCode} 已开始，本地目录图片将按当前通道进入统计。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "开始检测超时，无法开始本地目录检测。";
            MessageBox.Show(StatusMessage, "本地目录检测", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("当前已有检测任务运行中", StringComparison.Ordinal))
        {
            StatusMessage = "机器已启动，继续使用当前运行中的检测任务统计本地目录图片。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"开始检测失败，无法开始本地目录检测: {ex.Message}";
            MessageBox.Show(StatusMessage, "本地目录检测", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void UpdateSelectedImageMetadata(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            SelectedImageDimensions = "1536 × 300";
            PreviewDesignWidth = 1536;
            PreviewDesignHeight = 300;
            return;
        }

        try
        {
            using var stream = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null || frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            {
                return;
            }

            PreviewDesignWidth = frame.PixelWidth;
            PreviewDesignHeight = frame.PixelHeight;
            SelectedImageDimensions = $"{frame.PixelWidth} × {frame.PixelHeight}";
        }
        catch
        {
            SelectedImageDimensions = "1536 × 300";
            PreviewDesignWidth = 1536;
            PreviewDesignHeight = 300;
        }
    }

    private async Task<ActiveMonitoringContext?> LoadActiveChannelContextAsync()
    {
        try
        {
            var channels = await _apiClient.GetChannelsAsync(CancellationToken.None);
            var active = channels.FirstOrDefault(x => x.Channel.IsEnabled);
            if (active is null)
            {
                StatusMessage = "当前没有启用中的通道，请先启用一条通道。";
                return null;
            }

            if (active.ModelVersion is null)
            {
                StatusMessage = $"当前通道 {active.Channel.Name} 尚未绑定模型。";
                return null;
            }

            var products = await _apiClient.GetProductsAsync(CancellationToken.None);
            var product = products.FirstOrDefault(x => x.Product.Id == active.Channel.SeafoodProductId);
            var normalLabel = product?.Traits.FirstOrDefault(x => x.IsNormal)?.Name ?? "正常";
            ApplyActiveProductTraits(product?.Traits);
            var modelPath = ResolveUsableModelPath(active.Channel, active.ModelVersion);
            var predictConfigPath = ResolvePredictConfigPath(product);

            if (!File.Exists(modelPath))
            {
                StatusMessage = $"当前启用通道绑定的模型文件不存在: {modelPath}";
                return null;
            }

            if (string.IsNullOrWhiteSpace(predictConfigPath) || !File.Exists(predictConfigPath))
            {
                var productName = product?.Product.Name ?? "未知海鲜产品";
                StatusMessage = $"当前启用通道绑定的海鲜产品 {productName} 未找到 predict 配置，请检查产品配置或项目内置模板: {predictConfigPath}";
                return null;
            }

            CurrentChannel = $"当前通道: {active.Channel.Name}";
            CurrentModel = $"当前模型: {active.ModelVersion.Version}";

            var effectiveLabelMapJson = !string.IsNullOrWhiteSpace(product?.Product.LabelMapJson) &&
                                        product.Product.LabelMapJson != "{}"
                ? product.Product.LabelMapJson
                : "{}";

            return new ActiveMonitoringContext(
                active.Channel,
                active.ModelVersion,
                normalLabel,
                modelPath,
                predictConfigPath,
                ResolveLabelMap(effectiveLabelMapJson));
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取启用通道失败: {ex.Message}";
            return null;
        }
    }

    private Task<double> UpdateStreamResultViewAsync(
        ActiveMonitoringContext context,
        string? finalizedImagePath,
        string? sourceImagePath,
        IReadOnlyList<YoloStreamDetectionItemDto> detections,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(finalizedImagePath) && File.Exists(finalizedImagePath))
        {
            SelectedImagePath = finalizedImagePath;
        }

        DetectionItems.Clear();
        var renderFrontendOverlay = ShouldRenderFrontendDetectionOverlay(finalizedImagePath);
        var frameLabels = new List<string>();
        var defectDetections = new List<DefectDetection>();
        foreach (var detection in detections)
        {
            var label = context.LabelMap.TryGetValue(detection.ClassId, out var mapped)
                ? mapped
                : $"class_{detection.ClassId}";
            frameLabels.Add(label);
            var defectDetection = ToDefectDetection(label, detection);
            defectDetections.Add(defectDetection);
            if (renderFrontendOverlay)
            {
                DetectionItems.Add(new ManualDetectionItemViewModel(
                    label,
                    detection.FinalConf.ToString("0.000", CultureInfo.InvariantCulture),
                    BuildBoundingBoxText(detection.Xyxy)));
            }
        }
        RefreshFrameTraitItems(frameLabels);

        var previewCommands = detections
            .Select(x => TryBuildPreviewCommand(context, x))
            .Where(x => x is not null)
            .Cast<EjectCommand>()
            .ToArray();

        EjectCommandItems.Clear();
        foreach (var command in previewCommands)
        {
            EjectCommandItems.Add(new ManualEjectCommandItemViewModel(
                command.DefectLabel,
                command.Action.ToString(),
                $"#{command.NozzleNumber}",
                $"{command.TriggerDelayMicroseconds / 1000.0:0.##} ms"));
        }

        var firstLabel = frameLabels.FirstOrDefault();
        LastDetection = firstLabel is null ? "最近结果: 未检出目标" : $"最近结果: {firstLabel}";
        LastNozzle = previewCommands.Length == 0
            ? "剔除信息: 当前定稿帧无需触发剔除"
            : $"剔除信息: 当前定稿帧生成 {previewCommands.Length} 条命令";
        AccumulateCountSummary(frameLabels);

        var persistenceMs = 0.0;
        if (!string.IsNullOrWhiteSpace(finalizedImagePath))
        {
            var persistenceStopwatch = Stopwatch.StartNew();
            EnqueueStreamInspectionRecord(new StreamInspectionRecordRequest(
                context.Channel.Id,
                context.Model.Id,
                finalizedImagePath,
                false,
                defectDetections,
                previewCommands));
            persistenceStopwatch.Stop();
            persistenceMs = persistenceStopwatch.Elapsed.TotalMilliseconds;
        }

        return Task.FromResult(persistenceMs);
    }

    private void EnqueueStreamInspectionRecord(StreamInspectionRecordRequest request)
    {
        lock (_streamRecordSync)
        {
            var previous = _streamRecordWriteTail;
            _streamRecordWriteTail = PersistStreamInspectionRecordAsync(previous, request);
        }
    }

    private async Task PersistStreamInspectionRecordAsync(
        Task previous,
        StreamInspectionRecordRequest request)
    {
        try
        {
            await previous;
        }
        catch
        {
            // The previous failure is retained separately; keep later records ordered.
        }

        try
        {
            await _apiClient.AddStreamInspectionRecordAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            lock (_streamRecordSync)
            {
                _streamRecordWriteError ??= ex;
            }
        }
    }

    private async Task AwaitPendingStreamRecordsAsync()
    {
        Task pending;
        lock (_streamRecordSync)
        {
            pending = _streamRecordWriteTail;
        }

        await pending;
        lock (_streamRecordSync)
        {
            if (_streamRecordWriteError is not null)
            {
                StatusMessage = $"部分检测记录保存失败: {_streamRecordWriteError.Message}";
                _streamRecordWriteError = null;
            }
        }
    }

    private static void WriteStreamTimingLog(
        YoloStreamFrameResultDto frame,
        double clientResponseMs,
        double elapsedBeforePersistenceCompletedMs,
        double persistenceMs)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OceanFreshSortingSystem",
                "logs");
            Directory.CreateDirectory(logDirectory);
            var line = string.Join(
                ',',
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                frame.ReceivedFrameName,
                frame.ModelInferenceMilliseconds.ToString("0.##", CultureInfo.InvariantCulture),
                frame.PostprocessMilliseconds.ToString("0.##", CultureInfo.InvariantCulture),
                frame.ServiceOverheadMilliseconds.ToString("0.##", CultureInfo.InvariantCulture),
                frame.RequestTotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture),
                clientResponseMs.ToString("0.##", CultureInfo.InvariantCulture),
                persistenceMs.ToString("0.##", CultureInfo.InvariantCulture),
                elapsedBeforePersistenceCompletedMs.ToString("0.##", CultureInfo.InvariantCulture));
            File.AppendAllText(Path.Combine(logDirectory, "stream-timing.csv"), line + Environment.NewLine);
        }
        catch
        {
            // Diagnostic logging must never interrupt the detection stream.
        }
    }

    private async Task PreloadActiveChannelModelAsync()
    {
        try
        {
            await Task.Run(CompanionProcessManager.EnsureYoloServiceStarted);
            var context = await LoadActiveChannelContextAsync();
            if (context is null)
            {
                return;
            }

            StatusMessage = $"正在预加载当前通道模型: {context.Model.Version}";
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _apiClient.PreloadYoloModelAsync(
                context.ModelPath,
                context.PredictConfigPath,
                timeoutCts.Token);
            StatusMessage = result.AlreadyWarmed
                ? $"当前通道模型已就绪: {context.Model.Version}"
                : $"当前通道模型已加载并预热: {context.Model.Version} ({result.PreloadMilliseconds:0} ms)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"当前通道模型预加载失败，启动时将重试: {ex.Message}";
        }
    }

    private string? ResolveSourceImagePath(string? finalizedFrameName, string? finalizedImagePath)
    {
        if (!string.IsNullOrWhiteSpace(finalizedFrameName) &&
            _streamSourceImageByFrameName.TryGetValue(finalizedFrameName, out var sourcePath) &&
            File.Exists(sourcePath))
        {
            return sourcePath;
        }

        return finalizedImagePath;
    }

    private static bool ShouldRenderFrontendDetectionOverlay(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return true;
        }

        return !imagePath.Contains("predict_run", StringComparison.OrdinalIgnoreCase);
    }

    private static DefectDetection ToDefectDetection(string label, YoloStreamDetectionItemDto detection)
    {
        var xyxy = detection.Xyxy;
        if (xyxy.Count < 4)
        {
            return new DefectDetection(Guid.NewGuid(), label, (decimal)detection.FinalConf, 0, 0, 0, 0);
        }

        var x = (int)Math.Round(xyxy[0], MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(xyxy[1], MidpointRounding.AwayFromZero);
        var width = Math.Max(0, (int)Math.Round(xyxy[2] - xyxy[0], MidpointRounding.AwayFromZero));
        var height = Math.Max(0, (int)Math.Round(xyxy[3] - xyxy[1], MidpointRounding.AwayFromZero));
        return new DefectDetection(Guid.NewGuid(), label, (decimal)detection.FinalConf, x, y, width, height);
    }

    private static string BuildBoundingBoxText(IReadOnlyList<double> xyxy)
    {
        if (xyxy.Count < 4)
        {
            return "0,0,0,0";
        }

        var x = (int)Math.Round(xyxy[0], MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(xyxy[1], MidpointRounding.AwayFromZero);
        var width = Math.Max(0, (int)Math.Round(xyxy[2] - xyxy[0], MidpointRounding.AwayFromZero));
        var height = Math.Max(0, (int)Math.Round(xyxy[3] - xyxy[1], MidpointRounding.AwayFromZero));
        return $"{x},{y},{width},{height}";
    }

    private EjectCommand? TryBuildPreviewCommand(ActiveMonitoringContext context, YoloStreamDetectionItemDto detection)
    {
        var label = context.LabelMap.TryGetValue(detection.ClassId, out var mapped)
            ? mapped
            : $"class_{detection.ClassId}";
        if (string.Equals(label, context.NormalLabel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nozzleNumber = ResolveNozzleNumber(context.Channel.HorizontalLaneMappingJson, detection.XCenterPx, ChannelRuntimeDefaults.ImageWidth);
        var yCenter = (decimal)detection.YCenterPx;
        var remainingPixels = Math.Max(0m, ChannelRuntimeDefaults.ImageHeight - yCenter);
        var remainingMillimeters = (remainingPixels * context.Channel.MillimetersPerPixelY) + context.Channel.CameraToEjectDistanceMillimeters;
        var conveyorSpeedMmPerSecond = MachineRuntimeDefaults.ConveyorSpeedMetersPerSecond * 1000m;
        var travelMilliseconds = conveyorSpeedMmPerSecond <= 0
            ? 0m
            : (remainingMillimeters / conveyorSpeedMmPerSecond) * 1000m;
        var triggerDelayMs = Math.Max(0m, travelMilliseconds - context.Channel.SoftwareLatencyMilliseconds - context.Channel.ActuatorDelayMilliseconds);
        var triggerDelayMicroseconds = (int)Math.Max(0m, decimal.Round(triggerDelayMs * 1000m, MidpointRounding.AwayFromZero));

        return new EjectCommand(
            Guid.NewGuid(),
            context.Channel.Id,
            label,
            context.Channel.DefectHandlingAction,
            nozzleNumber,
            (int)Math.Round(yCenter, MidpointRounding.AwayFromZero),
            triggerDelayMicroseconds,
            50_000,
            DateTimeOffset.UtcNow);
    }

    private void ResetTimingMetrics()
    {
        _streamFrameCount = 0;
        _totalModelInferenceMs = 0;
        _totalPostprocessMs = 0;
        _totalResponseMs = 0;
        _totalServiceOverheadMs = 0;
        ResetCountSummary();
        LatestModelInference = "模型推理: 暂无";
        LatestPostprocess = "后处理: 暂无";
        LatestResponse = "软件响应: 暂无";
        LatestServiceOverhead = "服务开销: 暂无";
        AverageModelInference = "平均推理: 暂无";
        AveragePostprocess = "平均后处理: 暂无";
        AverageResponse = "平均响应: 暂无";
        AverageServiceOverhead = "平均服务开销: 暂无";
        CurrentFrame = "当前帧: 暂无";
        BufferedFrame = "缓冲帧: 暂无";
        FinalizedFrame = "定稿帧: 暂无";
    }

    private void ResetCountSummary()
    {
        _cumulativeTraitCounts.Clear();
        RefreshFrameTraitItems([]);
        RefreshCountItems();
        TotalDetectedCountText = "海鲜总数: 暂无";
        NormalDetectedCountText = "正常总数: 暂无";
        RejectedDetectedCountText = "异常总数: 暂无";
        YieldRateText = "良率: 暂无";
        AbnormalSummaryText = "0";
        AbnormalPercentText = "异常占比 暂无";
        YieldRateColor = "#9DB8CE";
        YieldNormalBarWidth = 0;
        YieldRatePercent = 0;
        YieldStatusText = "等待检测";
        NormalCountLegendText = "■  正常  0";
        RejectedCountLegendText = "■  异常  0";
        TotalTraitKindText = "共 0 种";
        CurrentFrameTargetSummary = "当前帧   0 个目标";
    }

    private void ResetStreamMetrics()
    {
        _streamFrameCount = 0;
        _totalModelInferenceMs = 0;
        _totalPostprocessMs = 0;
        _totalResponseMs = 0;
        _totalServiceOverheadMs = 0;
        LatestModelInference = "模型推理: 暂无";
        LatestPostprocess = "后处理: 暂无";
        LatestServiceOverhead = "服务开销: 暂无";
        LatestResponse = "软件响应: 暂无";
        AverageModelInference = "平均推理: 暂无";
        AveragePostprocess = "平均后处理: 暂无";
        AverageServiceOverhead = "平均服务开销: 暂无";
        AverageResponse = "平均响应: 暂无";
        ResetCountSummary();
    }

    private void ReplaceCountSummary(IEnumerable<string> labels)
    {
        _cumulativeTraitCounts.Clear();
        foreach (var label in labels.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            _cumulativeTraitCounts[label] = _cumulativeTraitCounts.TryGetValue(label, out var current)
                ? current + 1
                : 1;
        }

        RefreshCountItems();
    }

    private void AccumulateCountSummary(IEnumerable<string> labels)
    {
        foreach (var label in labels.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            _cumulativeTraitCounts[label] = _cumulativeTraitCounts.TryGetValue(label, out var current)
                ? current + 1
                : 1;
        }

        RefreshCountItems();
    }

    private void RefreshCountItems()
    {
        TraitCountItems.Clear();
        DefectCountItems.Clear();
        var displayItems = BuildOrderedTraitCounts();
        var total = Math.Max(1, displayItems.Sum(x => x.Value));
        foreach (var pair in displayItems)
        {
            var color = ResolveTraitColor(pair.Key);
            TraitCountItems.Add(new TraitCountItemViewModel(
                pair.Key,
                pair.Value,
                color,
                $"{(pair.Value * 100.0 / total):0.#}%",
                Math.Max(6, pair.Value * 160.0 / total),
                BuildLedSegments(pair.Value, total, color)));
        }

        var inspectedTotal = displayItems.Sum(x => x.Value);
        var normalCount = CountNormalDetections(displayItems);
        var rejectedCount = Math.Max(0, inspectedTotal - normalCount);
        var abnormalItems = displayItems
            .Where(x => !IsNormalTraitLabel(x.Key))
            .ToArray();
        foreach (var pair in abnormalItems)
        {
            var color = ResolveTraitColor(pair.Key);
            DefectCountItems.Add(new TraitCountItemViewModel(
                pair.Key,
                pair.Value,
                color,
                rejectedCount <= 0 ? "0%" : $"{(pair.Value * 100.0 / rejectedCount):0.##}%",
                pair.Value <= 0 ? 0 : Math.Max(6, pair.Value * 390.0 / Math.Max(1, rejectedCount)),
                BuildLedSegments(pair.Value, Math.Max(1, rejectedCount), color)));
        }

        TotalDetectedCountText = $"海鲜总数: {inspectedTotal}";
        NormalDetectedCountText = $"正常总数: {normalCount}";
        RejectedDetectedCountText = $"异常总数: {rejectedCount}";
        NormalCountLegendText = $"■  正常  {normalCount}";
        RejectedCountLegendText = $"■  异常  {rejectedCount}";
        TotalTraitKindText = $"共 {abnormalItems.Length} 种";
        RefreshAbnormalComposition(abnormalItems, inspectedTotal, rejectedCount);
        RefreshYieldRate(inspectedTotal, normalCount);
    }

    private void RefreshAbnormalComposition(
        IReadOnlyList<KeyValuePair<string, int>> abnormalItems,
        int inspectedTotal,
        int rejectedCount)
    {
        AbnormalTraitItems.Clear();
        foreach (var pair in abnormalItems)
        {
            var color = ResolveTraitColor(pair.Key);
            AbnormalTraitItems.Add(new TraitCountItemViewModel(
                pair.Key,
                pair.Value,
                color,
                rejectedCount <= 0 ? "0%" : $"{(pair.Value * 100.0 / rejectedCount):0.#}%",
                Math.Max(6, pair.Value * 160.0 / Math.Max(1, rejectedCount)),
                BuildLedSegments(pair.Value, Math.Max(1, rejectedCount), color)));
        }

        AbnormalCompositionSegments.Clear();
        foreach (var segment in BuildAbnormalCompositionSegments(abnormalItems, rejectedCount))
        {
            AbnormalCompositionSegments.Add(segment);
        }

        AbnormalSummaryText = rejectedCount.ToString(CultureInfo.InvariantCulture);
        AbnormalPercentText = inspectedTotal <= 0
            ? "异常占比 暂无"
            : $"异常占比 {(rejectedCount * 100.0 / inspectedTotal):0.##}%";
    }

    private void RefreshYieldRate(int inspectedTotal, int normalCount)
    {
        if (inspectedTotal <= 0)
        {
            YieldRateText = "良率: 暂无";
            YieldRateColor = "#9DB8CE";
            YieldNormalBarWidth = 0;
            YieldRatePercent = 0;
            YieldStatusText = "等待检测";
            return;
        }

        var yieldRate = normalCount * 100.0 / inspectedTotal;
        YieldRateText = $"良率: {yieldRate:0.##}%";
        YieldNormalBarWidth = Math.Clamp(yieldRate * 4.3, 0, 430);
        YieldRatePercent = Math.Clamp(yieldRate, 0, 100);
        YieldStatusText = yieldRate switch
        {
            < WarningYieldRateThreshold => "未达标",
            < TargetYieldRateThreshold => "接近达标",
            _ => "已达标"
        };
        YieldRateColor = yieldRate switch
        {
            < WarningYieldRateThreshold => "#EF4444",
            < TargetYieldRateThreshold => "#F59E0B",
            _ => "#10B981"
        };
    }

    private int CountNormalDetections(IReadOnlyList<KeyValuePair<string, int>> displayItems)
    {
        return displayItems
            .Where(x => IsNormalTraitLabel(x.Key))
            .Sum(x => x.Value);
    }

    private bool IsNormalTraitLabel(string label)
    {
        return _activeProductTraits.Any(x => x.IsNormal && IsSameTraitKind(label, x.Label)) ||
               IsSameTraitKind(label, "正常");
    }

    private void RefreshFrameTraitItems(IEnumerable<string> labels)
    {
        var frameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            frameCounts[label] = frameCounts.TryGetValue(label, out var current)
                ? current + 1
                : 1;
        }

        var displayTraits = _activeProductTraits.Count == 0
            ? BuildFallbackTraitOptions()
            : _activeProductTraits;
        var displayItems = displayTraits
            .Select(trait => new KeyValuePair<string, int>(
                trait.Label,
                frameCounts.Where(x => IsSameTraitKind(x.Key, trait.Label)).Sum(x => x.Value)))
            .Concat(frameCounts
                .Where(x => !displayTraits.Any(trait => IsSameTraitKind(x.Key, trait.Label)))
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var total = Math.Max(1, displayItems.Sum(x => x.Value));
        CurrentFrameTargetSummary = $"当前帧   {displayItems.Sum(x => x.Value)} 个目标";
        FrameTraitItems.Clear();
        foreach (var pair in displayItems)
        {
            var color = ResolveTraitColor(pair.Key);
            FrameTraitItems.Add(new TraitCountItemViewModel(
                pair.Key,
                pair.Value,
                color,
                $"{(pair.Value * 100.0 / total):0.#}%",
                Math.Max(6, pair.Value * 150.0 / total),
                BuildLedSegments(pair.Value, total, color)));
        }
    }

    private IReadOnlyList<KeyValuePair<string, int>> BuildOrderedTraitCounts()
    {
        var ordered = new List<KeyValuePair<string, int>>();
        var displayTraits = _activeProductTraits.Count == 0
            ? BuildFallbackTraitOptions()
            : _activeProductTraits;

        foreach (var trait in displayTraits)
        {
            AddDisplayTrait(ordered, trait.Label);
        }

        foreach (var pair in _cumulativeTraitCounts
                     .Where(x => !displayTraits.Any(trait => IsSameTraitKind(x.Key, trait.Label)))
                     .OrderByDescending(x => x.Value)
                     .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            ordered.Add(pair);
        }

        return ordered;
    }

    private void AddDisplayTrait(List<KeyValuePair<string, int>> target, string displayLabel)
    {
        var count = _cumulativeTraitCounts
            .Where(x => IsSameTraitKind(x.Key, displayLabel))
            .Sum(x => x.Value);
        target.Add(new KeyValuePair<string, int>(displayLabel, count));
    }

    private async Task UpdateActiveProductTraitsAsync(Guid productId)
    {
        try
        {
            var products = await _apiClient.GetProductsAsync(CancellationToken.None);
            ApplyActiveProductTraits(products.FirstOrDefault(x => x.Product.Id == productId)?.Traits);
        }
        catch
        {
            if (_activeProductTraits.Count == 0)
            {
                _activeProductTraits.AddRange(BuildFallbackTraitOptions());
            }
        }
    }

    private void ApplyActiveProductTraits(IReadOnlyList<SeafoodTrait>? traits)
    {
        _activeProductTraits.Clear();
        if (traits is not null && traits.Count > 0)
        {
            _activeProductTraits.AddRange(
                traits
                    .OrderByDescending(x => x.IsNormal)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new ProductTraitDisplayOption(x.Name, x.IsNormal, ResolveTraitColor(x.Name))));
        }

        if (_activeProductTraits.Count == 0)
        {
            _activeProductTraits.AddRange(BuildFallbackTraitOptions());
        }

        RefreshCountItems();
    }

    private static IReadOnlyList<ProductTraitDisplayOption> BuildFallbackTraitOptions() =>
    [
        new("正常", true, "#10B981"),
        new("碎壳", false, "#3B82F6"),
        new("泥包", false, "#F59E0B"),
        new("空壳", false, "#94A3B8")
    ];

    private static bool IsSameTraitKind(string label, string canonicalLabel)
    {
        if (string.Equals(label, canonicalLabel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var labelKind = ResolveTraitKind(label);
        var canonicalKind = ResolveTraitKind(canonicalLabel);
        return labelKind is not null &&
               canonicalKind is not null &&
               string.Equals(labelKind, canonicalKind, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveTraitKind(string label)
    {
        if (label.Contains("正常", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("良", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("合格", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("normal", StringComparison.OrdinalIgnoreCase))
        {
            return "normal";
        }

        if (label.Contains("碎", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("破", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            return "broken";
        }

        if (label.Contains("泥", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("muddy", StringComparison.OrdinalIgnoreCase))
        {
            return "muddy";
        }

        if (label.Contains("空", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return "empty";
        }

        return null;
    }

    private static string ResolveTraitColor(string label)
    {
        if (IsSameTraitKind(label, "正常"))
        {
            return "#10B981";
        }

        if (IsSameTraitKind(label, "碎壳"))
        {
            return "#3B82F6";
        }

        if (IsSameTraitKind(label, "泥包"))
        {
            return "#F59E0B";
        }

        if (IsSameTraitKind(label, "空壳"))
        {
            return "#94A3B8";
        }

        return "#60A5FA";
    }

    private static IReadOnlyList<LedSegmentViewModel> BuildLedSegments(int count, int total, string color)
    {
        const int segmentCount = 18;
        var activeCount = total <= 0 ? 0 : (int)Math.Ceiling(count * segmentCount / (double)total);
        return Enumerable.Range(1, segmentCount)
            .Select(index => new LedSegmentViewModel(index <= activeCount, color))
            .ToArray();
    }

    private static IReadOnlyList<LedSegmentViewModel> BuildAbnormalCompositionSegments(
        IReadOnlyList<KeyValuePair<string, int>> abnormalItems,
        int rejectedCount)
    {
        const int segmentCount = 30;
        if (rejectedCount <= 0)
        {
            return Enumerable.Range(0, segmentCount)
                .Select(_ => new LedSegmentViewModel(false, "#20384F"))
                .ToArray();
        }

        var weighted = abnormalItems
            .Where(x => x.Value > 0)
            .Select(x => new
            {
                Label = x.Key,
                x.Value,
                ExactSegments = x.Value * segmentCount / (double)rejectedCount
            })
            .OrderByDescending(x => x.Value)
            .ToArray();

        var allocations = weighted
            .Select(x => new
            {
                x.Label,
                Count = Math.Max(1, (int)Math.Floor(x.ExactSegments)),
                Remainder = x.ExactSegments - Math.Floor(x.ExactSegments)
            })
            .ToList();

        var allocated = allocations.Sum(x => x.Count);
        while (allocated < segmentCount && allocations.Count > 0)
        {
            var index = allocations
                .Select((x, i) => new { Item = x, Index = i })
                .OrderByDescending(x => x.Item.Remainder)
                .ThenBy(x => x.Index)
                .First()
                .Index;
            allocations[index] = new
            {
                allocations[index].Label,
                Count = allocations[index].Count + 1,
                allocations[index].Remainder
            };
            allocated++;
        }

        while (allocated > segmentCount && allocations.Count > 0)
        {
            var index = allocations
                .Select((x, i) => new { Item = x, Index = i })
                .Where(x => x.Item.Count > 1)
                .OrderBy(x => x.Item.Remainder)
                .ThenByDescending(x => x.Index)
                .FirstOrDefault()
                ?.Index ?? -1;
            if (index < 0)
            {
                break;
            }

            allocations[index] = new
            {
                allocations[index].Label,
                Count = allocations[index].Count - 1,
                allocations[index].Remainder
            };
            allocated--;
        }

        var segments = new List<LedSegmentViewModel>(segmentCount);
        foreach (var item in allocations)
        {
            segments.AddRange(Enumerable.Range(0, item.Count)
                .Select(_ => new LedSegmentViewModel(true, ResolveTraitColor(item.Label))));
        }

        while (segments.Count < segmentCount)
        {
            segments.Add(new LedSegmentViewModel(false, "#20384F"));
        }

        return segments.Take(segmentCount).ToArray();
    }

    private static string StripTelemetryPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "暂无";
        }

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex < 0)
        {
            separatorIndex = value.IndexOf('：');
        }

        return separatorIndex >= 0 && separatorIndex + 1 < value.Length
            ? value[(separatorIndex + 1)..].Trim()
            : value.Trim();
    }

    private void UpdateTimingMetrics(double modelInferenceMs, double postprocessMs, double serviceOverheadMs, double responseMs)
    {
        _streamFrameCount++;
        _totalModelInferenceMs += modelInferenceMs;
        _totalPostprocessMs += postprocessMs;
        _totalResponseMs += responseMs;
        _totalServiceOverheadMs += serviceOverheadMs;
        LatestModelInference = $"模型推理: {modelInferenceMs:0.##} ms";
        LatestPostprocess = $"后处理: {postprocessMs:0.##} ms";
        LatestServiceOverhead = $"服务开销: {serviceOverheadMs:0.##} ms";
        LatestResponse = $"软件响应: {responseMs:0.##} ms";
        AverageModelInference = $"平均推理: {_totalModelInferenceMs / _streamFrameCount:0.##} ms";
        AveragePostprocess = $"平均后处理: {_totalPostprocessMs / _streamFrameCount:0.##} ms";
        AverageServiceOverhead = $"平均服务开销: {_totalServiceOverheadMs / _streamFrameCount:0.##} ms";
        AverageResponse = $"平均响应: {_totalResponseMs / _streamFrameCount:0.##} ms";
    }

    private static bool IsSupportedImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveUsableModelPath(ChannelConfig channel, ModelVersion model)
    {
        var candidates = new[]
        {
            channel.ModelPath,
            model.SourceWeightPath
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var resolved = ResolveSharedPath(candidate);
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return ResolveSharedPath(candidates.FirstOrDefault() ?? string.Empty);
    }

    private static string ResolvePredictConfigPath(SeafoodProductProfileDto? product)
    {
        var candidates = new[]
        {
            product?.Product.PredictConfigPath,
            ResolveBundledPredictTemplatePath()
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var resolved = Path.IsPathRooted(candidate!) ? candidate! : ResolveSharedPath(candidate!);
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return ResolveSharedPath(product?.Product.PredictConfigPath ?? string.Empty);
    }

    private static string ResolveBundledPredictTemplatePath()
    {
        const string relativeTemplatePath = @"predict\youge\predict_youge.json";
        var candidate = Path.Combine(AppContext.BaseDirectory, relativeTemplatePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            candidate = Path.Combine(directory.FullName, relativeTemplatePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, relativeTemplatePath);
    }

    private static string ResolveSharedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var overridden = Environment.GetEnvironmentVariable("OCEANFRESH_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            Directory.CreateDirectory(overridden);
            return Path.GetFullPath(Path.Combine(overridden, path));
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OceanFreshSortingSystem");
        Directory.CreateDirectory(root);
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static string ResolvePlcXrayInputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("OCEANFRESH_PLC_XRAY_INPUT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var overridden = Environment.GetEnvironmentVariable("OCEANFRESH_DATA_ROOT");
        var root = !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OceanFreshSortingSystem");
        return Path.GetFullPath(Path.Combine(root, "plc-xray-input"));
    }

    private static Dictionary<int, string> ResolveLabelMap(string labelMapJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(labelMapJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return [];
            }

            var result = new Dictionary<int, string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var classIdFromValue))
                {
                    result[classIdFromValue] = property.Name;
                    continue;
                }

                if (int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var classIdFromKey) &&
                    property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var labelName = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(labelName))
                    {
                        result[classIdFromKey] = labelName;
                    }
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static int ResolveNozzleNumber(string horizontalLaneMappingJson, double xCenterPx, int imageWidth)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(horizontalLaneMappingJson))
            {
                var mappings = System.Text.Json.JsonSerializer.Deserialize<List<HorizontalLaneMapping>>(horizontalLaneMappingJson);
                if (mappings is not null)
                {
                    var match = mappings
                        .Where(x => x.EndX >= x.StartX)
                        .FirstOrDefault(x => xCenterPx >= x.StartX && xCenterPx <= x.EndX);
                    if (match is not null)
                    {
                        return match.NozzleNumber;
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed mapping and use fallback segmentation.
        }

        var segmentWidth = imageWidth / 4.0;
        return Math.Clamp((int)(xCenterPx / segmentWidth) + 1, 1, 4);
    }

    private sealed record ActiveMonitoringContext(
        ChannelConfig Channel,
        ModelVersion Model,
        string NormalLabel,
        string ModelPath,
        string PredictConfigPath,
        IReadOnlyDictionary<int, string> LabelMap);

    private sealed record ProductTraitDisplayOption(string Label, bool IsNormal, string Color);

    private sealed record HorizontalLaneMapping(int NozzleNumber, double StartX, double EndX);
}

public sealed class RecipesPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly List<ProductRowViewModel> _filteredProducts = [];
    private string _statusMessage = "正在加载海鲜产品...";
    private string _productSearchText = string.Empty;
    private ProductRowViewModel? _selectedProduct;
    private int _currentPage = 1;
    private int _selectedPageSize = 10;

    public RecipesPageViewModel()
    {
        OpenCreateProductCommand = new RelayCommand(_ => OpenCreateEditor());
        OpenEditProductCommand = new RelayCommand(OpenEditProduct);
        OpenProductDetailsCommand = new RelayCommand(OpenProductDetails);
        DeleteProductCommand = new RelayCommand(parameter => _ = DeleteProductAsync(parameter as ProductRowViewModel ?? SelectedProduct));
        RefreshCommand = new AsyncRelayCommand(LoadProductsAsync);
        FirstPageCommand = new RelayCommand(_ => GoToPage(1));
        PreviousPageCommand = new RelayCommand(_ => GoToPage(CurrentPage - 1));
        NextPageCommand = new RelayCommand(_ => GoToPage(CurrentPage + 1));
        LastPageCommand = new RelayCommand(_ => GoToPage(TotalPages));
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

    public string ProductSearchText
    {
        get => _productSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_productSearchText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _productSearchText, normalized);
            ApplyProductView();
        }
    }

    public ObservableCollection<ProductRowViewModel> Products { get; } = [];

    public ObservableCollection<ProductRowViewModel> VisibleProducts { get; } = [];

    public ObservableCollection<SettingsListPageSizeOptionViewModel> PageSizeOptions { get; } =
    [
        new(10),
        new(20),
        new(50)
    ];

    public ICommand OpenCreateProductCommand { get; }

    public ICommand OpenEditProductCommand { get; }

    public ICommand OpenProductDetailsCommand { get; }

    public ICommand DeleteProductCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand FirstPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand LastPageCommand { get; }

    public Task ActivateAsync() => LoadProductsAsync();

    public bool HasSelectedProduct => SelectedProduct is not null;

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            SetProperty(ref _currentPage, value);
            RaisePropertyChanged(nameof(PageSummary));
            RaisePropertyChanged(nameof(CanGoToPreviousPage));
            RaisePropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public int SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            var normalized = value <= 0 ? 10 : value;
            if (_selectedPageSize == normalized)
            {
                return;
            }

            SetProperty(ref _selectedPageSize, normalized);
            CurrentPage = 1;
            ApplyPagination();
        }
    }

    public int FilteredProductCount => _filteredProducts.Count;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(FilteredProductCount / (double)SelectedPageSize));

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public string PageSummary => $"{CurrentPage} / {TotalPages}";

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
                    product.Traits.Count,
                    product.Product.PredictConfigPath,
                    product.Product.ClassesFilePath));
            }

            ApplyProductView();
            StatusMessage = $"已加载 {Products.Count} 个海鲜产品。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取海鲜产品失败: {ex.Message}";
        }
    }

    private async Task DeleteProductAsync(ProductRowViewModel? product)
    {
        if (product is null)
        {
            StatusMessage = "请先选择要删除的海鲜产品。";
            return;
        }

        if (MessageBox.Show(
                $"确认删除海鲜产品 {product.Name} 吗？删除后可能影响已绑定模型和通道。",
                "确认删除产品",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            StatusMessage = "已取消删除产品。";
            return;
        }

        try
        {
            await _apiClient.DeleteProductAsync(product.ProductId, CancellationToken.None);
            StatusMessage = $"已删除海鲜产品: {product.Name}";
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void OpenCreateEditor() => OpenEditor(null);

    private void OpenEditEditor() => OpenEditProduct(SelectedProduct);

    private void OpenEditProduct(object? parameter)
    {
        var product = parameter as ProductRowViewModel;
        if (product is null)
        {
            StatusMessage = "请先在列表中选中一个海鲜产品，再进入编辑。";
            return;
        }

        OpenEditor(product);
    }

    public void OpenSelectedProductDetails() => OpenProductDetails(SelectedProduct);

    private void OpenProductDetails(object? parameter)
    {
        var product = parameter as ProductRowViewModel;
        if (product is null)
        {
            StatusMessage = "请先在列表中选中一个海鲜产品，再查看详情。";
            return;
        }

        ShellNavigationService.Navigate(
            new ProductDetailPageViewModel(product),
            $"产品详情 {product.Name}",
            "在详情页中编辑或删除海鲜产品，保存后返回列表页");
    }

    private void OpenEditor(ProductRowViewModel? product)
    {

        try
        {
            ShellNavigationService.Navigate(
                new ProductEditorPageViewModel(product),
                "产品设置",
                string.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开产品编辑页失败: {ex.Message}";
        }
    }

    private void GoToPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        ApplyPagination();
    }

    private void ApplyProductView()
    {
        var searchText = ProductSearchText.Trim();
        _filteredProducts.Clear();
        _filteredProducts.AddRange(Products
            .Where(product => string.IsNullOrWhiteSpace(searchText)
                || product.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || product.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || product.DefectTraitsDisplay.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(product => product.Name));

        if (SelectedProduct is not null && _filteredProducts.All(product => product.ProductId != SelectedProduct.ProductId))
        {
            SelectedProduct = null;
        }

        CurrentPage = 1;
        RaisePropertyChanged(nameof(FilteredProductCount));
        ApplyPagination();
    }

    private void ApplyPagination()
    {
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        VisibleProducts.Clear();
        foreach (var item in _filteredProducts.Skip((CurrentPage - 1) * SelectedPageSize).Take(SelectedPageSize))
        {
            VisibleProducts.Add(item);
        }

        RaisePropertyChanged(nameof(TotalPages));
        RaisePropertyChanged(nameof(PageSummary));
        RaisePropertyChanged(nameof(CanGoToPreviousPage));
        RaisePropertyChanged(nameof(CanGoToNextPage));
    }
}

public sealed class ProductEditorPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private const string FixedNormalLabel = "正常";
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly ProductRowViewModel? _sourceProduct;
    private Guid? _editingProductId;
    private string _draftProductName = string.Empty;
    private string _draftProductCode;
    private string _pendingDefectName = string.Empty;
    private string _selectedClassesFilePath = string.Empty;
    private string _selectedPredictConfigPath = string.Empty;
    private string _predictConfigJsonText = string.Empty;
    private string _statusMessage;

    public ProductEditorPageViewModel(ProductRowViewModel? sourceProduct)
    {
        _sourceProduct = sourceProduct;
        _draftProductCode = sourceProduct?.Code ?? string.Empty;
        _statusMessage = string.Empty;
        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync);
        BackToListCommand = new RelayCommand(_ => BackToList());
        DeleteProductCommand = new AsyncRelayCommand(DeleteProductAsync);
        AddDefectCommand = new RelayCommand(_ => AddDefectItem());
        RemoveDefectCommand = new RelayCommand(RemoveDefectItem);
        BrowseClassesFileCommand = new RelayCommand(_ => BrowseClassesFile());
        BrowsePredictConfigCommand = new RelayCommand(_ => BrowsePredictConfigFile());
        LoadDefaultPredictConfigCommand = new AsyncRelayCommand(LoadDefaultPredictConfigAsync);
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

    public string SelectedClassesFilePath
    {
        get => _selectedClassesFilePath;
        set
        {
            SetProperty(ref _selectedClassesFilePath, value);
            RebuildTraitMappings();
        }
    }

    public string SelectedPredictConfigPath
    {
        get => _selectedPredictConfigPath;
        set
        {
            if (string.Equals(_selectedPredictConfigPath, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _selectedPredictConfigPath, value);
            RaisePropertyChanged(nameof(PredictConfigSourceDisplay));
            RaisePropertyChanged(nameof(HasPredictConfigSource));
        }
    }

    public string PredictConfigJsonText
    {
        get => _predictConfigJsonText;
        set => SetProperty(ref _predictConfigJsonText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _statusMessage, value);
            RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool IsEditing => _sourceProduct is not null;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasPredictConfigSource => !string.IsNullOrWhiteSpace(SelectedPredictConfigPath);

    public string PredictConfigSourceDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedPredictConfigPath))
            {
                return string.Empty;
            }

            return SelectedPredictConfigPath == "将保存为产品专属配置"
                ? "系统默认模板"
                : Path.GetFileName(SelectedPredictConfigPath);
        }
    }

    public bool CanDelete => _sourceProduct is not null;

    public ObservableCollection<EditableDefectItemViewModel> DefectItems { get; } = [];

    public ObservableCollection<ModelTraitMappingRowViewModel> TraitMappings { get; } = [];

    public ICommand SaveProductCommand { get; }

    public ICommand BackToListCommand { get; }

    public ICommand DeleteProductCommand { get; }

    public ICommand AddDefectCommand { get; }

    public ICommand RemoveDefectCommand { get; }

    public ICommand BrowseClassesFileCommand { get; }

    public ICommand BrowsePredictConfigCommand { get; }

    public ICommand LoadDefaultPredictConfigCommand { get; }

    public string MappingCompletionText
    {
        get
        {
            var mappedCount = TraitMappings.Count(x => !string.IsNullOrWhiteSpace(x.SelectedTraitName));
            return TraitMappings.Count == 0
                ? string.Empty
                : $"{mappedCount} / {TraitMappings.Count} 已完成映射";
        }
    }

    public Task ActivateAsync() => LoadSourceProductAsync();

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
            var viewModel = new EditableDefectItemViewModel(item);
            HookDefectItem(viewModel);
            DefectItems.Add(viewModel);
        }
    }

    private async Task LoadSourceProductAsync()
    {
        if (_sourceProduct is null)
        {
            return;
        }

        try
        {
            var products = await _apiClient.GetProductsAsync(CancellationToken.None);
            var profile = products.FirstOrDefault(x => x.Product.Id == _sourceProduct.ProductId);
            if (profile is null)
            {
                return;
            }

            SelectedClassesFilePath = profile.Product.ClassesFilePath;
            SelectedPredictConfigPath = profile.Product.PredictConfigPath;
            ApplySavedProductMappings(profile.Product.LabelMapJson);
            await LoadPredictConfigJsonFromPathAsync(SelectedPredictConfigPath);
        }
        catch
        {
            // Ignore edit-page hydration failures and keep existing summary data editable.
        }
    }

    private void AddDefectItem()
    {
        if (string.IsNullOrWhiteSpace(PendingDefectName))
        {
            StatusMessage = "请先输入一个缺陷项名称。";
            return;
        }

        if (string.Equals(PendingDefectName.Trim(), FixedNormalLabel, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "正常性状默认固定为“正常”，不需要手动添加。";
            return;
        }

        if (DefectItems.Any(x => string.Equals(x.Name, PendingDefectName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "缺陷项已存在，不需要重复添加。";
            return;
        }

        DefectItems.Add(new EditableDefectItemViewModel(PendingDefectName.Trim()));
        HookDefectItem(DefectItems[^1]);
        PendingDefectName = string.Empty;
        RebuildTraitMappings();
        StatusMessage = "已添加缺陷项。";
    }

    private void RemoveDefectItem(object? parameter)
    {
        if (parameter is not EditableDefectItemViewModel item)
        {
            return;
        }

        DefectItems.Remove(item);
        RebuildTraitMappings();
        StatusMessage = "已移除缺陷项。";
    }

    private void BrowseClassesFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择产品标准 classes.txt",
            Filter = "YOLO 类别文件 (classes.txt)|classes.txt|文本文件 (*.txt)|*.txt",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.Combine(AppContext.BaseDirectory, "predict", "youge")
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedClassesFilePath = dialog.FileName;
            StatusMessage = $"已选择标签文件: {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void BrowsePredictConfigFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择产品后处理配置",
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedPredictConfigPath = dialog.FileName;
            try
            {
                PredictConfigJsonText = File.ReadAllText(dialog.FileName);
                StatusMessage = $"已载入产品后处理配置: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"读取产品后处理配置失败: {ex.Message}";
            }
        }
    }

    private async Task LoadDefaultPredictConfigAsync()
    {
        try
        {
            PredictConfigJsonText = await _apiClient.GetDefaultProductPredictConfigTemplateAsync(CancellationToken.None);
            SelectedPredictConfigPath = "将保存为产品专属配置";
            StatusMessage = "已载入产品默认后处理参数，仅需调整目标高度最小值和最大值。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"载入默认模板失败: {ex.Message}";
        }
    }

    private async Task SaveProductAsync()
    {
        try
        {
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

            if (string.IsNullOrWhiteSpace(SelectedClassesFilePath))
            {
                StatusMessage = "请先选择当前海鲜产品的标准 classes.txt。";
                return;
            }

            if (TraitMappings.Count == 0)
            {
                StatusMessage = "请先读取 classes.txt 并完成产品标签映射。";
                return;
            }

            var unmapped = TraitMappings.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.SelectedTraitName));
            if (unmapped is not null)
            {
                StatusMessage = $"模型标签 {unmapped.ModelLabel} 尚未映射到海鲜产品性状。";
                return;
            }

            var selectedTraitNames = TraitMappings.Select(x => x.SelectedTraitName!).ToList();
            var productTraitNames = traits.Select(x => x.Name).ToList();
            if (selectedTraitNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedTraitNames.Count)
            {
                StatusMessage = "classes.txt 标签与海鲜产品性状必须一一对应，不能重复占用同一个性状。";
                return;
            }

            if (selectedTraitNames.Count != productTraitNames.Count ||
                selectedTraitNames.Except(productTraitNames, StringComparer.OrdinalIgnoreCase).Any() ||
                productTraitNames.Except(selectedTraitNames, StringComparer.OrdinalIgnoreCase).Any())
            {
                StatusMessage = "classes.txt 标签数量必须与当前海鲜产品性状数量一致，并完成一一对应映射。";
                return;
            }

            var labelMapJson = System.Text.Json.JsonSerializer.Serialize(
                TraitMappings.ToDictionary(
                    x => x.ClassIndex.ToString(CultureInfo.InvariantCulture),
                    x => x.SelectedTraitName!,
                    StringComparer.OrdinalIgnoreCase));

            var request = new UpsertSeafoodProductRequest(
                _editingProductId,
                DraftProductCode.Trim(),
                DraftProductName.Trim(),
                true,
                SelectedClassesFilePath.Trim(),
                labelMapJson,
                string.IsNullOrWhiteSpace(SelectedPredictConfigPath) ? null : SelectedPredictConfigPath.Trim(),
                string.IsNullOrWhiteSpace(PredictConfigJsonText) ? null : PredictConfigJsonText,
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
            new SettingsPageViewModel(
                initialModuleTitle: "海鲜产品",
                initialModulePage: new RecipesPageViewModel()),
            "设置",
            string.Empty);

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

    private void RebuildTraitMappings()
    {
        TraitMappings.Clear();
        var traitOptions = BuildTraitRequests().Select(x => x.Name).ToArray();
        if (traitOptions.Length == 0 || string.IsNullOrWhiteSpace(SelectedClassesFilePath) || !File.Exists(SelectedClassesFilePath))
        {
            RaisePropertyChanged(nameof(MappingCompletionText));
            return;
        }

        var labels = File.ReadAllLines(SelectedClassesFilePath)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        foreach (var entry in labels.Select((label, index) => new { label, index }))
        {
            var row = new ModelTraitMappingRowViewModel(entry.index, entry.label, traitOptions);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ModelTraitMappingRowViewModel.SelectedTraitName))
                {
                    RaisePropertyChanged(nameof(MappingCompletionText));
                }
            };
            TraitMappings.Add(row);
        }

        RaisePropertyChanged(nameof(MappingCompletionText));
    }

    private void ApplySavedProductMappings(string labelMapJson)
    {
        if (string.IsNullOrWhiteSpace(labelMapJson))
        {
            return;
        }

        Dictionary<int, string> savedMappings;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(labelMapJson);
            savedMappings = document.RootElement.EnumerateObject()
                .Where(x => int.TryParse(x.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .ToDictionary(
                    x => int.Parse(x.Name, CultureInfo.InvariantCulture),
                    x => x.Value.GetString() ?? string.Empty);
        }
        catch
        {
            return;
        }

        if (TraitMappings.Count == 0)
        {
            RebuildTraitMappings();
        }

        foreach (var row in TraitMappings)
        {
            if (savedMappings.TryGetValue(row.ClassIndex, out var traitName))
            {
                row.SelectedTraitName = traitName;
            }
        }

        RaisePropertyChanged(nameof(MappingCompletionText));
    }

    private void HookDefectItem(EditableDefectItemViewModel item)
    {
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditableDefectItemViewModel.Name))
            {
                RebuildTraitMappings();
            }
        };
    }

    private async Task LoadPredictConfigJsonFromPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            PredictConfigJsonText = string.Empty;
            return;
        }

        try
        {
            var resolved = ResolveSharedPath(path);
            if (File.Exists(resolved))
            {
                PredictConfigJsonText = await File.ReadAllTextAsync(resolved);
            }
        }
        catch
        {
            PredictConfigJsonText = string.Empty;
        }
    }

    private static string ResolveSharedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var overridden = Environment.GetEnvironmentVariable("OCEANFRESH_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            Directory.CreateDirectory(overridden);
            return Path.GetFullPath(Path.Combine(overridden, path));
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OceanFreshSortingSystem");
        Directory.CreateDirectory(root);
        return Path.GetFullPath(Path.Combine(root, path));
    }
}

public sealed class ProductDetailPageViewModel : ViewModelBase
{
    private readonly ProductRowViewModel _product;
    private string _statusMessage;

    public ProductDetailPageViewModel(ProductRowViewModel product)
    {
        _product = product;
        _statusMessage = $"{product.Name} 的产品信息已展开，可在这里继续编辑或删除。";
        OpenEditCommand = new RelayCommand(_ => OpenEdit());
        DeleteProductCommand = new AsyncRelayCommand(DeleteProductAsync);
        BackToListCommand = new RelayCommand(_ => BackToList());
        OpenClassesFileCommand = new RelayCommand(_ => OpenClassesFile());
    }

    public string Name => _product.Name;

    public string Code => _product.Code;

    public string DefectTraitsDisplay => _product.DefectTraitsDisplay;

    public IReadOnlyList<string> DefectTraits => _product.DefectTraits;

    public string ClassesFilePath => string.IsNullOrWhiteSpace(_product.ClassesFilePath)
        ? "未配置"
        : _product.ClassesFilePath;

    public bool HasClassesFile => !string.IsNullOrWhiteSpace(_product.ClassesFilePath);

    public IReadOnlyList<ProductLabelMappingRowViewModel> LabelMappings
    {
        get
        {
            var traits = new List<string> { _product.NormalLabel };
            traits.AddRange(_product.DefectTraits);
            return traits
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((trait, index) => new ProductLabelMappingRowViewModel(
                    index,
                    ResolveModelLabel(trait),
                    trait))
                .ToList();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand OpenEditCommand { get; }

    public ICommand DeleteProductCommand { get; }

    public ICommand BackToListCommand { get; }

    public ICommand OpenClassesFileCommand { get; }

    private void OpenEdit() =>
        ShellNavigationService.Navigate(
            new ProductEditorPageViewModel(_product),
            "产品设置",
            string.Empty);

    private async Task DeleteProductAsync()
    {
        var client = new OceanFreshLocalApiClient();
        try
        {
            await client.DeleteProductAsync(_product.ProductId, CancellationToken.None);
            NavigateToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void BackToList() => NavigateToList();

    private void OpenClassesFile()
    {
        if (string.IsNullOrWhiteSpace(_product.ClassesFilePath))
        {
            StatusMessage = "当前产品没有配置标签文件。";
            return;
        }

        if (!File.Exists(_product.ClassesFilePath))
        {
            StatusMessage = $"标签文件不存在: {_product.ClassesFilePath}";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_product.ClassesFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开标签文件失败: {ex.Message}";
        }
    }

    private static string ResolveModelLabel(string trait)
    {
        if (trait.Contains("正常", StringComparison.OrdinalIgnoreCase))
        {
            return "normal";
        }

        if (trait.Contains("碎", StringComparison.OrdinalIgnoreCase) || trait.Contains("破", StringComparison.OrdinalIgnoreCase))
        {
            return "broken";
        }

        if (trait.Contains("泥", StringComparison.OrdinalIgnoreCase))
        {
            return "muddy";
        }

        if (trait.Contains("空", StringComparison.OrdinalIgnoreCase))
        {
            return "empty";
        }

        return $"class_{trait}";
    }

    private static void NavigateToList() =>
        ShellNavigationService.Navigate(
            new SettingsPageViewModel(
                initialModuleTitle: "海鲜产品",
                initialModulePage: new RecipesPageViewModel()),
            "设置",
            string.Empty);
}

public sealed class ChannelConfigsPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private string _statusMessage = "正在加载通道配置...";
    private ChannelRowViewModel? _selectedChannel;
    private ProductFilterOptionViewModel? _selectedProductFilter;
    private string _channelSearchText = string.Empty;
    private string _activeChannelSummary = "当前运行通道: 未启用";
    private int _currentPage = 1;
    private int _selectedPageSize = 10;

    public ChannelConfigsPageViewModel()
    {
        OpenCreateChannelCommand = new RelayCommand(_ => OpenCreateEditor());
        OpenEditChannelCommand = new RelayCommand(OpenEditChannel);
        OpenChannelDetailsCommand = new RelayCommand(OpenChannelDetails);
        DeleteChannelCommand = new RelayCommand(parameter => _ = DeleteChannelAsync(parameter as ChannelRowViewModel ?? SelectedChannel));
        RefreshCommand = new AsyncRelayCommand(LoadChannelsAsync);
        FirstPageCommand = new RelayCommand(_ => GoToPage(1));
        PreviousPageCommand = new RelayCommand(_ => GoToPage(CurrentPage - 1));
        NextPageCommand = new RelayCommand(_ => GoToPage(CurrentPage + 1));
        LastPageCommand = new RelayCommand(_ => GoToPage(TotalPages));
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
            SetProperty(ref _channelSearchText, value ?? string.Empty);
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

    public ObservableCollection<ChannelRowViewModel> VisibleChannels { get; } = [];

    public ObservableCollection<ProductFilterOptionViewModel> ProductFilterOptions { get; } = [];

    public ObservableCollection<SettingsListPageSizeOptionViewModel> PageSizeOptions { get; } =
    [
        new(10),
        new(20),
        new(50)
    ];

    public ICommand OpenCreateChannelCommand { get; }

    public ICommand OpenEditChannelCommand { get; }

    public ICommand OpenChannelDetailsCommand { get; }

    public ICommand DeleteChannelCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand FirstPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand LastPageCommand { get; }

    public Task ActivateAsync() => LoadChannelsAsync();

    public bool HasSelectedChannel => SelectedChannel is not null;

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            SetProperty(ref _currentPage, value);
            RaisePropertyChanged(nameof(PageSummary));
            RaisePropertyChanged(nameof(CanGoToPreviousPage));
            RaisePropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public int SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            var normalized = value <= 0 ? 10 : value;
            if (_selectedPageSize == normalized)
            {
                return;
            }

            SetProperty(ref _selectedPageSize, normalized);
            CurrentPage = 1;
            ApplyPagination();
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Channels.Count / (double)SelectedPageSize));

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public string PageSummary => $"{CurrentPage} / {TotalPages}";

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
                channel.Channel.IsEnabled,
                channel.Channel.IsEnabled ? "当前使用" : "未选用",
                channel.Channel.LastRuntimePredictConfigPath,
                channel.Channel.LastRuntimeOutputDirectory));
        }

        ModelCatalogState.ApplyChannelBindings(AllChannels);

        BuildFilters();
            var activeChannel = AllChannels.FirstOrDefault(x => x.IsActive);
            if (activeChannel is not null)
            {
                ChannelRuntimeSelectionService.SetActive(activeChannel.ChannelId, activeChannel.Name);
            }
            else if (AllChannels.Count > 0)
            {
                ChannelRuntimeSelectionService.DisableIfActive(ChannelRuntimeSelectionService.ActiveChannelId ?? Guid.Empty);
            }
            ApplyChannelView();
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取通道配置失败: {ex.Message}";
        }
    }

    private async Task DeleteChannelAsync(ChannelRowViewModel? channel)
    {
        if (channel is null)
        {
            StatusMessage = "请先选择要删除的通道。";
            return;
        }

        if (channel.IsActive)
        {
            StatusMessage = "当前使用的通道不能删除，请先在首页切换通道。";
            MessageBox.Show(StatusMessage, "通道配置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                $"确认删除通道 {channel.Name} 吗？删除后需要重新配置该通道。",
                "确认删除通道",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            StatusMessage = "已取消删除通道。";
            return;
        }

        try
        {
            await _apiClient.DeleteChannelAsync(channel.ChannelId, CancellationToken.None);
            StatusMessage = $"已删除通道: {channel.Name}";
            await LoadChannelsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void OpenCreateEditor() => OpenEditor(null);

    private void OpenEditEditor() => OpenEditChannel(SelectedChannel);

    private void OpenEditChannel(object? parameter)
    {
        var channel = parameter as ChannelRowViewModel ?? SelectedChannel;
        if (channel is null)
        {
            StatusMessage = "请先在列表中选中一个通道，再进入编辑。";
            return;
        }

        OpenEditor(channel);
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
            string.Empty);
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
                    : "调整选中通道的海鲜、模型和阈值");
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开通道编辑页失败: {ex.Message}";
        }
    }

    public void OpenSelectedChannelDetails()
    {
        if (SelectedChannel is null)
        {
            StatusMessage = "请先选中一个通道，再查看详情。";
            return;
        }

        OpenChannelDetails(SelectedChannel);
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
        var items = AllChannels
            .Where(x => string.IsNullOrWhiteSpace(productName) || x.ProductName == productName)
            .Where(x => string.IsNullOrWhiteSpace(searchText)
                || x.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || x.ProductName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || x.ModelVersion.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ChannelNo)
            .ToList();

        foreach (var item in items)
        {
            Channels.Add(item);
        }

        if (SelectedChannel is not null && Channels.All(x => x.ChannelId != SelectedChannel.ChannelId))
        {
            SelectedChannel = null;
        }

        ActiveChannelSummary = $"当前运行通道: {(items.FirstOrDefault(x => x.IsActive)?.Name ?? "未启用")}";
        StatusMessage = $"当前运行通道: {(items.FirstOrDefault(x => x.IsActive)?.Name ?? "未启用")}；列表显示 {Channels.Count} 条配置。";
        CurrentPage = 1;
        ApplyPagination();
    }

    private void GoToPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        ApplyPagination();
    }

    private void ApplyPagination()
    {
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        VisibleChannels.Clear();
        foreach (var item in Channels.Skip((CurrentPage - 1) * SelectedPageSize).Take(SelectedPageSize))
        {
            VisibleChannels.Add(item);
        }

        RaisePropertyChanged(nameof(TotalPages));
        RaisePropertyChanged(nameof(PageSummary));
        RaisePropertyChanged(nameof(CanGoToPreviousPage));
        RaisePropertyChanged(nameof(CanGoToNextPage));
    }
}

public sealed class ChannelDetailPageViewModel : ViewModelBase
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly ChannelRowViewModel _channel;
    private readonly Func<string, bool> _confirmDelete;
    private string _statusMessage;

    public ChannelDetailPageViewModel(
        ChannelRowViewModel channel,
        Func<string, bool>? confirmDelete = null)
    {
        _channel = channel;
        _confirmDelete = confirmDelete ?? ConfirmDelete;
        _statusMessage = string.Empty;
        OpenEditCommand = new RelayCommand(_ => OpenEdit());
        BackToListCommand = new RelayCommand(_ => BackToList());
        DeleteChannelCommand = new AsyncRelayCommand(DeleteChannelAsync);
    }

    public string ChannelName => _channel.Name;

    public string ProductName => _channel.ProductName;

    public string ChannelNoLabel => $"CH-{_channel.ChannelNo:00}";

    public string ModelVersion => _channel.ModelVersion;

    public string RunningStatus => _channel.IsActive ? "当前使用" : "未选用";

    public string RunningStatusColor => _channel.IsActive ? "#20D98B" : "#8FA8C2";

    public bool CanDelete => !_channel.IsActive;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            SetProperty(ref _statusMessage, value);
            RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public ICommand OpenEditCommand { get; }

    public ICommand BackToListCommand { get; }

    public ICommand DeleteChannelCommand { get; }

    private void OpenEdit() =>
        ShellNavigationService.Navigate(
            new ChannelEditorPageViewModel(_channel),
            $"编辑通道 {_channel.Name}",
            string.Empty);

    private async Task DeleteChannelAsync()
    {
        if (_channel.IsActive)
        {
            StatusMessage = "当前使用的通道不能删除，请先在首页切换通道。";
            return;
        }

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

            NavigateToList();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
        }
    }

    private void BackToList() => NavigateToList();

    private static void NavigateToList() =>
        ShellNavigationService.Navigate(
            new SettingsPageViewModel(
                initialModuleTitle: "通道配置",
                initialModulePage: new ChannelConfigsPageViewModel()),
            "设置",
            string.Empty);

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
    private int _draftChannelNo = 1;
    private string _draftChannelName = string.Empty;
    private Guid? _draftProductId;
    private Guid? _draftModelVersionId;
    private string _draftDefectAction = "Sink";
    private string _draftConfidenceThreshold = "0.58";
    private string _statusMessage = string.Empty;

    public ChannelEditorPageViewModel(ChannelRowViewModel? sourceChannel)
    {
        _sourceChannel = sourceChannel;
        SaveChannelCommand = new AsyncRelayCommand(SaveChannelAsync);
        BackToListCommand = new RelayCommand(_ => BackToList());
        DeleteChannelCommand = new AsyncRelayCommand(DeleteChannelAsync);
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

    public string DraftConfidenceThreshold
    {
        get => _draftConfidenceThreshold;
        set => SetProperty(ref _draftConfidenceThreshold, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _statusMessage, value);
            RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool CanDelete => _sourceChannel is not null;

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
            var categories = await _apiClient.GetCategoriesAsync(CancellationToken.None);
            var products = await _apiClient.GetProductsAsync(CancellationToken.None);
            var models = await _apiClient.GetModelsAsync(CancellationToken.None);
            var channels = await _apiClient.GetChannelsAsync(CancellationToken.None);
            var categoryNameById = categories.ToDictionary(x => x.Id, x => x.Name);

            ProductOptions.Clear();
            foreach (var product in products)
            {
                ProductOptions.Add(new ProductOptionViewModel(
                    product.Product.Id,
                    product.Product.Name,
                    ResolveProductCategoryName(product.Product.Name),
                    product.Traits.FirstOrDefault(x => x.IsNormal)?.Name ?? "正常",
                    string.Join(" / ", product.Traits.Where(x => !x.IsNormal).Select(x => x.Name))));
            }

            ModelOptions.Clear();
            foreach (var model in models)
            {
                ModelOptions.Add(new ModelOptionViewModel(
                    model.Id,
                    model.Version,
                    categoryNameById.TryGetValue(model.SeafoodCategoryId, out var categoryName)
                        ? categoryName
                        : ResolveCategoryName(model.Version)));
            }

            ApplySourceChannel(channels);
            RefreshAvailableModels();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取基础数据失败: {ex.Message}";
        }
    }

    private void ApplySourceChannel(IReadOnlyList<ChannelConfigDetailDto> channels)
    {
        if (_sourceChannel is null)
        {
            _editingChannelId = null;
            _draftChannelNo = channels.Count == 0
                ? 1
                : channels.Max(x => x.Channel.ChannelNo) + 1;
            DraftProductId ??= ProductOptions.FirstOrDefault()?.Id;
            DraftModelVersionId ??= AvailableModelOptions.FirstOrDefault()?.Id;
            return;
        }

        _editingChannelId = _sourceChannel.ChannelId;
        _draftChannelNo = _sourceChannel.ChannelNo;
        DraftChannelName = _sourceChannel.Name;
        DraftProductId = _sourceChannel.ProductId;
        DraftModelVersionId = _sourceChannel.ModelVersionId ?? AvailableModelOptions.FirstOrDefault()?.Id;
        DraftDefectAction = _sourceChannel.DefectActionCode.ToString();
        DraftConfidenceThreshold = _sourceChannel.ConfidenceThreshold;
    }

    private async Task SaveChannelAsync()
    {
        try
        {
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

            if (!TryParseProbability(DraftConfidenceThreshold, out var confidenceThreshold))
            {
                StatusMessage = "置信度阈值必须是大于 0 且不超过 1.0 的数字。";
                return;
            }

            var request = new UpsertChannelConfigRequest(
                _editingChannelId,
                _draftChannelNo,
                DraftChannelName.Trim(),
                DraftProductId.Value,
                DraftModelVersionId,
                ParseAction(DraftDefectAction),
                confidenceThreshold,
                _sourceChannel?.IsActive ?? true);

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
            new SettingsPageViewModel(
                initialModuleTitle: "通道配置",
                initialModulePage: new ChannelConfigsPageViewModel()),
            "设置",
            string.Empty);

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
        var selectedCategoryName = selectedProduct?.CategoryName ?? string.Empty;
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

    private static string ResolveProductCategoryName(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return string.Empty;
        }

        if (productName.Contains("花蛤", StringComparison.OrdinalIgnoreCase))
        {
            return "花蛤";
        }

        if (productName.Contains("油蛤", StringComparison.OrdinalIgnoreCase))
        {
            return "油蛤";
        }

        if (productName.Contains("美贝", StringComparison.OrdinalIgnoreCase))
        {
            return "美贝";
        }

        return productName;
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

public sealed class ModelManagementPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<ModelVersion>>> _loadModelsAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ChannelConfigDetailDto>>> _loadChannelsAsync;
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private SeafoodCategoryOptionViewModel? _selectedCategory;
    private ModelRowViewModel? _selectedModel;
    private string _modelSearchText = string.Empty;
    private string _draftVersionCode = string.Empty;
    private string _statusMessage = "先按海鲜类别筛选，再决定导入哪个模型。";
    private int _currentPage = 1;
    private int _selectedPageSize = 10;

    public ModelManagementPageViewModel(
        string? preselectedCategoryName = null,
        string? statusMessage = null,
        Func<CancellationToken, Task<IReadOnlyList<ModelVersion>>>? loadModelsAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<ChannelConfigDetailDto>>>? loadChannelsAsync = null)
    {
        var apiClient = new OceanFreshLocalApiClient();
        _loadModelsAsync = loadModelsAsync ?? apiClient.GetModelsAsync;
        _loadChannelsAsync = loadChannelsAsync ?? apiClient.GetChannelsAsync;
        CategoryOptions =
        [
            new(Guid.Empty, "全部海鲜"),
            new(Guid.Parse("b33199af-10e8-411a-b5e6-9821d467b4df"), "花蛤"),
            new(Guid.Parse("8eeac4e9-0646-4a5d-a1ee-0d09a1776e7a"), "油蛤"),
            new(Guid.Parse("7fc3d726-c789-4d97-8c39-1275521f9c8c"), "美贝")
        ];

        OpenImportPageCommand = new RelayCommand(_ => OpenImportPage());
        OpenEditModelCommand = new RelayCommand(OpenEditModel);
        OpenModelDetailsCommand = new RelayCommand(OpenModelDetails);
        DeleteModelCommand = new RelayCommand(parameter => _ = DeleteModelAsync(parameter as ModelRowViewModel ?? SelectedModel));
        FirstPageCommand = new RelayCommand(_ => GoToPage(1));
        PreviousPageCommand = new RelayCommand(_ => GoToPage(CurrentPage - 1));
        NextPageCommand = new RelayCommand(_ => GoToPage(CurrentPage + 1));
        LastPageCommand = new RelayCommand(_ => GoToPage(TotalPages));
        StatusMessage = statusMessage ?? StatusMessage;
        SelectedCategory = CategoryOptions.FirstOrDefault(x => x.Name == preselectedCategoryName) ?? CategoryOptions.First();
    }

    public ObservableCollection<SeafoodCategoryOptionViewModel> CategoryOptions { get; }

    public ObservableCollection<ModelRowViewModel> Models { get; } = [];

    public ObservableCollection<ModelRowViewModel> VisibleModels { get; } = [];

    public ObservableCollection<SettingsListPageSizeOptionViewModel> PageSizeOptions { get; } =
    [
        new(10),
        new(20),
        new(50)
    ];

    public ICommand OpenImportPageCommand { get; }

    public ICommand OpenEditModelCommand { get; }

    public ICommand OpenModelDetailsCommand { get; }

    public ICommand DeleteModelCommand { get; }

    public ICommand FirstPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand LastPageCommand { get; }

    public ModelRowViewModel? SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value);
    }

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

    public string ModelSearchText
    {
        get => _modelSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_modelSearchText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _modelSearchText, normalized);
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

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            SetProperty(ref _currentPage, value);
            RaisePropertyChanged(nameof(PageSummary));
            RaisePropertyChanged(nameof(CanGoToPreviousPage));
            RaisePropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public int SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            var normalized = value <= 0 ? 10 : value;
            if (_selectedPageSize == normalized)
            {
                return;
            }

            SetProperty(ref _selectedPageSize, normalized);
            CurrentPage = 1;
            ApplyPagination();
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Models.Count / (double)SelectedPageSize));

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public string PageSummary => $"{CurrentPage} / {TotalPages}";

    public Task ActivateAsync() => LoadModelsAsync();

    public void RefreshModels()
    {
        Models.Clear();
        var selectedName = SelectedCategory?.Name;
        var searchText = ModelSearchText.Trim();
        var allModels = ModelCatalogState.GetAll();
        var items = selectedName is null || selectedName == "全部海鲜"
            ? allModels
            : allModels.Where(x => x.CategoryName == selectedName).ToList();

        items = items
            .Where(model => string.IsNullOrWhiteSpace(searchText)
                || model.Version.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || model.CategoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || model.WeightFileName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || model.BindingStatusText.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || model.BoundChannelNames.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var model in items)
        {
            Models.Add(model);
        }

        CurrentPage = 1;
        ApplyPagination();

        StatusMessage = selectedName is null || selectedName == "全部海鲜"
            ? $"当前显示全部模型，共 {Models.Count} 个版本。"
            : $"当前显示 {selectedName} 模型，共 {Models.Count} 个版本。";
    }

    public string BuildNextVersionCode(string? categoryName)
    {
        var prefix = string.IsNullOrWhiteSpace(categoryName) || categoryName == "全部海鲜"
            ? "MODEL"
            : BuildCategoryCode(categoryName.Trim());
        var existing = ModelCatalogState.GetAll()
            .Select(x => x.Version)
            .Where(x => x.StartsWith($"{prefix}-MV-", StringComparison.OrdinalIgnoreCase))
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

    private void OpenModelDetails(object? parameter)
    {
        if (parameter is not ModelRowViewModel model)
        {
            return;
        }

        ShellNavigationService.Navigate(
            new ModelDetailPageViewModel(model),
            $"模型详情 {model.Version}",
            "查看模型权重文件、描述信息和绑定状态");
    }

    private void OpenEditModel(object? parameter)
    {
        if (parameter is not ModelRowViewModel model)
        {
            StatusMessage = "请先选择要编辑的模型。";
            return;
        }

        ShellNavigationService.Navigate(
            new ModelImportPageViewModel(model),
            $"编辑模型 {model.Version}",
            "支持更新模型描述、海鲜类别和权重文件");
    }

    public void OpenSelectedModelDetails()
    {
        if (SelectedModel is null)
        {
            StatusMessage = "请先选中一个模型，再查看详情。";
            return;
        }

        OpenModelDetails(SelectedModel);
    }

    private async Task DeleteModelAsync(ModelRowViewModel? model)
    {
        if (model is null)
        {
            StatusMessage = "请先选择要删除的模型。";
            return;
        }

        if (MessageBox.Show(
                $"确认删除模型 {model.Version} 吗？如果模型已绑定通道，系统会阻止删除并显示原因。",
                "确认删除模型",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            StatusMessage = "已取消删除模型。";
            return;
        }

        try
        {
            await _apiClient.DeleteModelAsync(model.ModelId, CancellationToken.None);
            ModelCatalogState.RemoveByVersion(model.Version);
            RefreshModels();
            StatusMessage = $"已删除模型 {model.Version}。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除模型失败: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "删除模型失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void GoToPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        ApplyPagination();
    }

    private void ApplyPagination()
    {
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        VisibleModels.Clear();
        foreach (var item in Models.Skip((CurrentPage - 1) * SelectedPageSize).Take(SelectedPageSize))
        {
            VisibleModels.Add(item);
        }

        RaisePropertyChanged(nameof(TotalPages));
        RaisePropertyChanged(nameof(PageSummary));
        RaisePropertyChanged(nameof(CanGoToPreviousPage));
        RaisePropertyChanged(nameof(CanGoToNextPage));
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await _loadModelsAsync(CancellationToken.None);
            var channels = await _loadChannelsAsync(CancellationToken.None);
            ModelCatalogState.ReplaceAll(MapModelRows(models, CategoryOptions));
            ModelCatalogState.ApplyChannelBindings(MapChannelRows(channels));
            DraftVersionCode = BuildNextVersionCode(SelectedCategory?.Name);
            RefreshModels();
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取模型列表失败: {ex.Message}";
        }
    }

    private static string BuildCategoryCode(string categoryName) => categoryName switch
    {
        "花蛤" => "HG",
        "油蛤" => "YG",
        "美贝" => "MB",
        _ => categoryName.ToUpperInvariant()
    };

    private static string MapActionToChinese(DefectHandlingAction action) => action switch
    {
        DefectHandlingAction.Sink => "下沉",
        DefectHandlingAction.AirJet => "气吹",
        DefectHandlingAction.Pusher => "推杆",
        DefectHandlingAction.StopLine => "停机",
        _ => "放行"
    };

    private static IReadOnlyList<ModelRowViewModel> MapModelRows(
        IReadOnlyList<ModelVersion> models,
        IEnumerable<SeafoodCategoryOptionViewModel> categories) =>
        models.Select(model =>
        {
            var categoryName = categories.FirstOrDefault(x => x.Id == model.SeafoodCategoryId)?.Name ?? model.SeafoodCategoryId.ToString();
            var weightPath = model.SourceWeightPath;
            return new ModelRowViewModel(
                model.Id,
                categoryName,
                model.Version,
                string.Empty,
                model.Notes,
                weightPath,
                Path.GetFileName(weightPath),
                model.TrainingImageSize ?? 0);
        }).ToList();

    private static IReadOnlyList<ChannelRowViewModel> MapChannelRows(IReadOnlyList<ChannelConfigDetailDto> channels) =>
        channels.Select(channel => new ChannelRowViewModel(
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
            channel.Channel.IsEnabled,
            channel.Channel.IsEnabled ? "当前使用" : "未选用",
            channel.Channel.LastRuntimePredictConfigPath,
            channel.Channel.LastRuntimeOutputDirectory)).ToList();

}

public sealed class ModelImportPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly Func<UpsertModelRequest, CancellationToken, Task<ModelVersion>> _saveModelAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SeafoodProductProfileDto>>> _loadProductsAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SeafoodCategory>>> _loadCategoriesAsync;
    private readonly string _suggestedVersionCode;
    private readonly Guid? _originalModelId;
    private readonly string? _originalVersionCode;
    private readonly Dictionary<Guid, SeafoodProductProfileDto> _productProfilesById = [];
    private readonly Dictionary<Guid, SeafoodCategoryOptionViewModel> _categoriesById = [];
    private ModelImportProductOptionViewModel? _selectedProduct;
    private string _draftVersionCode;
    private string _draftModelDescription = string.Empty;
    private string _selectedWeightFilePath = string.Empty;
    private int _draftTrainingImageSize;
    private string _statusMessage = string.Empty;
    private string? _prefillProductName;

    public ModelImportPageViewModel(
        SeafoodCategoryOptionViewModel? selectedCategory,
        string suggestedVersionCode,
        Func<UpsertModelRequest, CancellationToken, Task<ModelVersion>>? saveModelAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SeafoodProductProfileDto>>>? loadProductsAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SeafoodCategory>>>? loadCategoriesAsync = null)
    {
        _saveModelAsync = saveModelAsync ?? new OceanFreshLocalApiClient().SaveModelAsync;
        _loadProductsAsync = loadProductsAsync ?? _apiClient.GetProductsAsync;
        _loadCategoriesAsync = loadCategoriesAsync ?? _apiClient.GetCategoriesAsync;
        _prefillProductName = selectedCategory?.Name;
        _suggestedVersionCode = suggestedVersionCode;
        _draftVersionCode = string.Empty;
        BackToListCommand = new RelayCommand(_ => BackToList());
        BrowseWeightFileCommand = new RelayCommand(_ => BrowseWeightFile());
        SaveImportCommand = new AsyncRelayCommand(SaveImportAsync);
    }

    public ModelImportPageViewModel(
        ModelRowViewModel existingModel,
        Func<UpsertModelRequest, CancellationToken, Task<ModelVersion>>? saveModelAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SeafoodProductProfileDto>>>? loadProductsAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SeafoodCategory>>>? loadCategoriesAsync = null)
        : this(new SeafoodCategoryOptionViewModel(Guid.Empty, existingModel.CategoryName), existingModel.Version, saveModelAsync, loadProductsAsync, loadCategoriesAsync)
    {
        _originalModelId = existingModel.ModelId;
        _originalVersionCode = existingModel.Version;
        DraftVersionCode = existingModel.Version;
        DraftModelDescription = existingModel.Description;
        SelectedWeightFilePath = existingModel.WeightFilePath;
        DraftTrainingImageSize = existingModel.TrainingImageSize;
        _prefillProductName = existingModel.CategoryName;
        StatusMessage = string.Empty;
    }

    public ObservableCollection<ModelImportProductOptionViewModel> ProductOptions { get; } = [];

    public ICommand BackToListCommand { get; }

    public ICommand BrowseWeightFileCommand { get; }

    public ICommand SaveImportCommand { get; }

    public ModelImportProductOptionViewModel? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            SetProperty(ref _selectedProduct, value);
            RaisePropertyChanged(nameof(SelectedProductSummary));
            RaisePropertyChanged(nameof(ProductLabelMapSummary));
        }
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
        set
        {
            if (string.Equals(_draftModelDescription, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _draftModelDescription, value);
            RaisePropertyChanged(nameof(DescriptionLengthText));
        }
    }

    public string SelectedWeightFilePath
    {
        get => _selectedWeightFilePath;
        set => SetProperty(ref _selectedWeightFilePath, value);
    }

    public int DraftTrainingImageSize
    {
        get => _draftTrainingImageSize;
        set => SetProperty(ref _draftTrainingImageSize, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _statusMessage, value);
            RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasGeneratedVersionCode => !string.IsNullOrWhiteSpace(DraftVersionCode);

    public bool IsEditMode => !string.IsNullOrWhiteSpace(_originalVersionCode);

    public string SelectedProductSummary => SelectedProduct is null
        ? "请先选择海鲜产品。模型导入后会继承该产品已经定义好的标准性状映射。"
        : $"所属类别: {SelectedProduct.CategoryName} | 业务性状: {SelectedProduct.TraitSummary}";

    public string ProductLabelMapSummary => SelectedProduct is null
        ? "未选择海鲜产品。"
        : string.IsNullOrWhiteSpace(SelectedProduct.LabelMapJson) || SelectedProduct.LabelMapJson == "{}"
            ? "当前海鲜产品还没有配置标准标签映射，请先到海鲜产品编辑页绑定 classes.txt 并完成一一对应。"
            : $"当前模型会继承海鲜产品 {SelectedProduct.Name} 的标准标签映射。";

    public string PageTitle => IsEditMode ? "编辑模型" : "添加模型";

    public string DescriptionLengthText => $"{DraftModelDescription.Length} / 200";

    public string PageSubtitle => IsEditMode
        ? "在这里更新模型描述、海鲜产品和模型权重文件，标签映射继承自海鲜产品。"
        : "先选海鲜产品，再导入模型权重文件。标签映射直接继承海鲜产品标准。";

    public string SaveButtonText => IsEditMode ? "保存模型修改" : "保存模型";

    public Task ActivateAsync() => LoadReferenceDataAsync();

    private void BrowseWeightFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 YOLO 权重文件",
            Filter = "YOLO 权重文件 (*.pt;*.onnx)|*.pt;*.onnx|PyTorch 权重 (*.pt)|*.pt|ONNX 模型 (*.onnx)|*.onnx",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.Combine(AppContext.BaseDirectory, "models")
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedWeightFilePath = dialog.FileName;
            StatusMessage = $"已选择权重文件: {Path.GetFileName(dialog.FileName)}";
        }
    }

    private async Task LoadReferenceDataAsync()
    {
        try
        {
            var categories = await _loadCategoriesAsync(CancellationToken.None);
            var products = await _loadProductsAsync(CancellationToken.None);

            _categoriesById.Clear();
            foreach (var category in categories)
            {
                _categoriesById[category.Id] = new SeafoodCategoryOptionViewModel(category.Id, category.Name);
            }

            ProductOptions.Clear();
            _productProfilesById.Clear();
            foreach (var product in products)
            {
                _productProfilesById[product.Product.Id] = product;
                ProductOptions.Add(new ModelImportProductOptionViewModel(
                    product.Product.Id,
                    product.Product.Name,
                    ResolveProductCategoryName(product.Product.Name),
                    product.Traits.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArray(),
                    product.Traits.OrderBy(x => x.SortOrder).Where(x => !x.IsNormal).Select(x => x.Name).ToArray(),
                    product.Product.ClassesFilePath,
                    product.Product.LabelMapJson));
            }

            SelectedProduct = ProductOptions.FirstOrDefault(x => x.Name == _prefillProductName)
                ?? ProductOptions.FirstOrDefault(x => x.CategoryName == _prefillProductName)
                ?? ProductOptions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取模型导入基础数据失败: {ex.Message}";
        }
    }

    private async Task SaveImportAsync()
    {
        if (SelectedProduct is null)
        {
            StatusMessage = "请先选择海鲜产品。";
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

        if (string.IsNullOrWhiteSpace(SelectedWeightFilePath))
        {
            StatusMessage = "请先从文件管理器中选择一个 YOLO 权重文件。";
            return;
        }

        if (DraftTrainingImageSize <= 0)
        {
            StatusMessage = "模型训练分辨率 imgsz 必须为正整数。";
            return;
        }

        var extension = Path.GetExtension(SelectedWeightFilePath);
        if (!string.Equals(extension, ".pt", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "当前只支持导入 .pt 或 .onnx 文件。";
            return;
        }

        if (!IsEditMode)
        {
            DraftVersionCode = BuildNextVersionCode(SelectedProduct.CategoryName);
        }

        try
        {
            var selectedProduct = SelectedProduct;
            if (selectedProduct is null)
            {
                StatusMessage = "请先选择海鲜产品。";
                return;
            }

            var category = ResolveCategoryForProduct(selectedProduct);
            if (category is null)
            {
                StatusMessage = $"未找到海鲜产品 {selectedProduct.Name} 对应的海鲜类别。";
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedProduct.ClassesFilePath) ||
                string.IsNullOrWhiteSpace(selectedProduct.LabelMapJson) ||
                selectedProduct.LabelMapJson == "{}")
            {
                StatusMessage = $"海鲜产品 {selectedProduct.Name} 还没有完成标准标签映射，请先到海鲜产品编辑页维护 classes.txt。";
                return;
            }

            var request = new UpsertModelRequest(
                _originalModelId,
                category.Id,
                DraftVersionCode,
                SelectedWeightFilePath,
                DraftModelDescription.Trim(),
                DraftTrainingImageSize);
            var saved = await _saveModelAsync(request, CancellationToken.None);
            var existingRow = ModelCatalogState.GetByVersion(DraftVersionCode);
            var weightPath = saved.SourceWeightPath;
            ModelCatalogState.AddOrReplace(new ModelRowViewModel(
                saved.Id,
                selectedProduct.CategoryName,
                saved.Version,
                existingRow?.BoundChannelNames ?? string.Empty,
                saved.Notes,
                weightPath,
                Path.GetFileName(weightPath),
                saved.TrainingImageSize ?? DraftTrainingImageSize));
            BackToList(IsEditMode
                ? $"已更新模型 {DraftVersionCode}。"
                : $"已导入 {selectedProduct.Name} 模型，生成版本号 {DraftVersionCode}。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    private void BackToList(string? statusMessage = null) =>
        ShellNavigationService.Navigate(
            new SettingsPageViewModel(
                initialModuleTitle: "模型管理",
                initialModulePage: new ModelManagementPageViewModel(SelectedProduct?.CategoryName, statusMessage)),
            "设置",
            string.Empty);

    private SeafoodCategoryOptionViewModel? ResolveCategoryForProduct(ModelImportProductOptionViewModel product)
    {
        return _categoriesById.Values.FirstOrDefault(x =>
            string.Equals(x.Name, product.CategoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildNextVersionCode(string? categoryName)
    {
        var prefix = string.IsNullOrWhiteSpace(categoryName)
            ? "MODEL"
            : BuildCategoryCode(categoryName.Trim());
        var existing = ModelCatalogState.GetAll()
            .Select(x => x.Version)
            .Where(x => x.StartsWith($"{prefix}-MV-", StringComparison.OrdinalIgnoreCase))
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

    private static string BuildCategoryCode(string categoryName) => categoryName switch
    {
        "花蛤" => "HG",
        "油蛤" => "YG",
        "美贝" => "MB",
        _ => categoryName.ToUpperInvariant()
    };

    private static string ResolveProductCategoryName(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return string.Empty;
        }

        if (productName.Contains("花蛤", StringComparison.OrdinalIgnoreCase))
        {
            return "花蛤";
        }

        if (productName.Contains("油蛤", StringComparison.OrdinalIgnoreCase))
        {
            return "油蛤";
        }

        if (productName.Contains("美贝", StringComparison.OrdinalIgnoreCase))
        {
            return "美贝";
        }

        return productName;
    }
}

public sealed class ModelDetailPageViewModel : ViewModelBase
{
    private readonly Func<Guid, CancellationToken, Task> _deleteModelAsync;
    private readonly Action<string> _showErrorDialog;
    private readonly ModelRowViewModel _model;
    private string _statusMessage;

    public ModelDetailPageViewModel(
        ModelRowViewModel model,
        Func<Guid, CancellationToken, Task>? deleteModelAsync = null,
        Action<string>? showErrorDialog = null)
    {
        _deleteModelAsync = deleteModelAsync ?? new OceanFreshLocalApiClient().DeleteModelAsync;
        _showErrorDialog = showErrorDialog ?? ShowErrorDialog;
        _model = model;
        _statusMessage = $"{model.Version} 的模型信息已展开，可在这里编辑或删除。";
        OpenEditCommand = new RelayCommand(_ => OpenEdit());
        DeleteModelCommand = new AsyncRelayCommand(DeleteModelAsync);
        BackToListCommand = new RelayCommand(_ => BackToList());
    }

    public string CategoryName => _model.CategoryName;

    public string Version => _model.Version;

    public string BoundStatus => string.IsNullOrWhiteSpace(_model.BoundChannelNames) ? "未绑定通道" : "已绑定通道";

    public string BoundChannelNames => string.IsNullOrWhiteSpace(_model.BoundChannelNames) ? "暂无" : _model.BoundChannelNames;

    public string BoundChannelSummary => _model.BoundChannelCount == 0
        ? "未绑定通道"
        : $"已绑定 {_model.BoundChannelCount} 个通道";

    public IReadOnlyList<ModelBoundChannelRowViewModel> BoundChannels =>
        string.IsNullOrWhiteSpace(_model.BoundChannelNames)
            ? []
            : _model.BoundChannelNames
                .Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => new ModelBoundChannelRowViewModel(name, "已绑定"))
                .ToList();

    public string Description => _model.Description;

    public string WeightFileName => _model.WeightFileName;

    public string WeightFilePath => _model.WeightFilePath;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand OpenEditCommand { get; }

    public ICommand DeleteModelCommand { get; }

    public ICommand BackToListCommand { get; }

    private void OpenEdit() =>
        ShellNavigationService.Navigate(
            new ModelImportPageViewModel(_model),
            $"编辑模型 {_model.Version}",
            "支持更新模型描述、海鲜类别和权重文件");

    private async Task DeleteModelAsync()
    {
        try
        {
            await _deleteModelAsync(_model.ModelId, CancellationToken.None);
            ModelCatalogState.RemoveByVersion(_model.Version);
            NavigateToList(null, $"已删除模型 {_model.Version}。");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _showErrorDialog(ex.Message);
        }
    }

    private void BackToList() => NavigateToList(_model.CategoryName);

    private static void NavigateToList(string? categoryName = null, string? statusMessage = null) =>
        ShellNavigationService.Navigate(
            new SettingsPageViewModel(
                initialModuleTitle: "模型管理",
                initialModulePage: new ModelManagementPageViewModel(categoryName, statusMessage)),
            "设置",
            string.Empty);

    private static void ShowErrorDialog(string message) =>
        MessageBox.Show(
            message,
            "删除模型失败",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}

public sealed class DeviceManagementPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private DeviceRowViewModel? _selectedDevice;
    private Guid? _editingDeviceId;
    private string _deviceNo = string.Empty;
    private string _deviceName = string.Empty;
    private DeviceType _selectedDeviceType = DeviceType.Conveyor;
    private string _firmwareVersion = string.Empty;
    private DeviceState _selectedState = DeviceState.Idle;
    private bool _isEnabled = true;
    private string _notes = string.Empty;
    private string _statusMessage = "正在读取设备档案...";

    public DeviceManagementPageViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        NewDeviceCommand = new RelayCommand(_ => ClearEditor());
        SaveDeviceCommand = new AsyncRelayCommand(SaveDeviceAsync);
        RunSelfCheckCommand = new AsyncRelayCommand(RunSelfCheckAsync);
        DeleteDeviceCommand = new AsyncRelayCommand(DeleteDeviceAsync);
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];

    public IReadOnlyList<DeviceType> DeviceTypeOptions { get; } =
    [
        DeviceType.Conveyor,
        DeviceType.XraySource,
        DeviceType.XrayDetector,
        DeviceType.Ejector,
        DeviceType.Controller
    ];

    public IReadOnlyList<DeviceState> DeviceStateOptions { get; } =
    [
        DeviceState.Offline,
        DeviceState.Idle,
        DeviceState.Running,
        DeviceState.Warning,
        DeviceState.Faulted
    ];

    public DeviceRowViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            SetProperty(ref _selectedDevice, value);
            if (value is not null)
            {
                LoadEditor(value);
            }
        }
    }

    public string DeviceNo
    {
        get => _deviceNo;
        set => SetProperty(ref _deviceNo, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        set => SetProperty(ref _deviceName, value);
    }

    public DeviceType SelectedDeviceType
    {
        get => _selectedDeviceType;
        set => SetProperty(ref _selectedDeviceType, value);
    }

    public string FirmwareVersion
    {
        get => _firmwareVersion;
        set => SetProperty(ref _firmwareVersion, value);
    }

    public DeviceState SelectedState
    {
        get => _selectedState;
        set => SetProperty(ref _selectedState, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand NewDeviceCommand { get; }

    public ICommand SaveDeviceCommand { get; }

    public ICommand RunSelfCheckCommand { get; }

    public ICommand DeleteDeviceCommand { get; }

    public Task ActivateAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var devices = await _apiClient.GetDevicesAsync(CancellationToken.None);
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(DeviceRowViewModel.FromDevice(device));
            }

            SelectedDevice = Devices.FirstOrDefault();
            StatusMessage = $"已加载 {Devices.Count} 台设备。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取设备失败: {ex.Message}";
        }
    }

    private async Task SaveDeviceAsync()
    {
        try
        {
            var saved = await _apiClient.SaveDeviceAsync(
                new UpsertHardwareDeviceRequest(
                    _editingDeviceId,
                    DeviceNo,
                    DeviceName,
                    SelectedDeviceType,
                    FirmwareVersion,
                    SelectedState,
                    IsEnabled,
                    Notes),
                CancellationToken.None);

            StatusMessage = $"已保存设备 {saved.DeviceNo}。";
            await LoadAsync();
            SelectedDevice = Devices.FirstOrDefault(x => x.DeviceId == saved.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存设备失败: {ex.Message}";
        }
    }

    private async Task RunSelfCheckAsync()
    {
        if (_editingDeviceId is null)
        {
            StatusMessage = "请先从左侧选择一台设备，再执行自检。";
            return;
        }

        try
        {
            var result = await _apiClient.RunDeviceSelfCheckAsync(_editingDeviceId.Value, CancellationToken.None);
            StatusMessage = result.Message;
            await LoadAsync();
            SelectedDevice = Devices.FirstOrDefault(x => x.DeviceId == result.Device.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"设备自检失败: {ex.Message}";
        }
    }

    private async Task DeleteDeviceAsync()
    {
        if (_editingDeviceId is null)
        {
            StatusMessage = "请先选择要删除的设备。";
            return;
        }

        try
        {
            await _apiClient.DeleteDeviceAsync(_editingDeviceId.Value, CancellationToken.None);
            StatusMessage = "已删除设备档案。";
            ClearEditor();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除设备失败: {ex.Message}";
        }
    }

    private void LoadEditor(DeviceRowViewModel device)
    {
        _editingDeviceId = device.DeviceId;
        DeviceNo = device.DeviceNo;
        DeviceName = device.Name;
        SelectedDeviceType = device.Type;
        FirmwareVersion = device.FirmwareVersion;
        SelectedState = device.State;
        IsEnabled = device.IsEnabled;
        Notes = device.Notes;
    }

    private void ClearEditor()
    {
        _editingDeviceId = null;
        DeviceNo = string.Empty;
        DeviceName = string.Empty;
        SelectedDeviceType = DeviceType.Conveyor;
        FirmwareVersion = "FW-1.0.0";
        SelectedState = DeviceState.Idle;
        IsEnabled = true;
        Notes = string.Empty;
        SelectedDevice = null;
        StatusMessage = "正在新增设备档案。";
    }
}

public sealed class SafetyAlarmsPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private AlarmRowViewModel? _selectedAlarm;
    private string _statusMessage = "正在读取安全预警...";
    private string _interlockTitle = "硬件联锁状态";
    private string _interlockSummary = "正在读取硬件联锁状态...";
    private string _interlockColor = "#F59E0B";

    public SafetyAlarmsPageViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AcknowledgeCommand = new AsyncRelayCommand(AcknowledgeAsync);
    }

    public ObservableCollection<AlarmRowViewModel> ActiveAlarms { get; } = [];

    public ObservableCollection<DeviceStatusRowViewModel> InterlockDevices { get; } = [];

    public AlarmRowViewModel? SelectedAlarm
    {
        get => _selectedAlarm;
        set => SetProperty(ref _selectedAlarm, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string InterlockTitle
    {
        get => _interlockTitle;
        set => SetProperty(ref _interlockTitle, value);
    }

    public string InterlockSummary
    {
        get => _interlockSummary;
        set => SetProperty(ref _interlockSummary, value);
    }

    public string InterlockColor
    {
        get => _interlockColor;
        set => SetProperty(ref _interlockColor, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand AcknowledgeCommand { get; }

    public Task ActivateAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var alarms = await _apiClient.GetActiveAlarmsAsync(CancellationToken.None);
            ActiveAlarms.Clear();
            foreach (var alarm in alarms)
            {
                ActiveAlarms.Add(AlarmRowViewModel.FromAlarm(alarm));
            }

            var interlock = await _apiClient.GetHardwareInterlockAsync(CancellationToken.None);
            InterlockDevices.Clear();
            foreach (var device in interlock.Devices)
            {
                InterlockDevices.Add(DeviceStatusRowViewModel.FromStatus(device));
            }

            InterlockTitle = interlock.CanRun ? "硬件联锁正常" : "硬件联锁阻断";
            InterlockSummary = interlock.Summary;
            InterlockColor = interlock.CanRun ? "#10B981" : "#EF4444";
            SelectedAlarm = ActiveAlarms.FirstOrDefault();
            StatusMessage = ActiveAlarms.Count == 0
                ? "当前没有未确认安全预警。"
                : $"当前有 {ActiveAlarms.Count} 条未确认安全预警。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取安全预警失败: {ex.Message}";
        }
    }

    private async Task AcknowledgeAsync()
    {
        if (SelectedAlarm is null)
        {
            StatusMessage = "请先选择一条预警。";
            return;
        }

        try
        {
            await _apiClient.AcknowledgeAlarmAsync(SelectedAlarm.AlarmId, CancellationToken.None);
            StatusMessage = $"已确认预警 {SelectedAlarm.Code}。";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"确认预警失败: {ex.Message}";
        }
    }
}

public sealed class SoftwareMaintenancePageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private string _productName = "Ocean Fresh Sorting System";
    private string _currentVersion = "读取中";
    private string _buildTime = "读取中";
    private string _databaseVersion = "读取中";
    private string _yoloServiceVersion = "读取中";
    private string _runtimeEnvironment = "读取中";
    private string _updatePolicy = "机器运行中禁止更新。";
    private string _networkStatus = "检测中";
    private string _networkAdapter = "未连接";
    private string _latestVersion = "待检查";
    private string _releaseDate = "--";
    private string _packageSize = "--";
    private string _releaseNotes = "尚未检查更新";
    private bool _isMachineRunning;
    private bool _hasAvailableUpdate;
    private string _statusMessage = "正在读取软件版本信息...";

    public SoftwareMaintenancePageViewModel()
    {
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        StartUpdateCommand = new AsyncRelayCommand(StartUpdateAsync);
    }

    public ObservableCollection<SoftwareUpdateRecordRowViewModel> RecentUpdates { get; } = [];

    public string ProductName
    {
        get => _productName;
        set => SetProperty(ref _productName, value);
    }

    public string CurrentVersion
    {
        get => _currentVersion;
        set => SetProperty(ref _currentVersion, value);
    }

    public string BuildTime
    {
        get => _buildTime;
        set => SetProperty(ref _buildTime, value);
    }

    public string DatabaseVersion
    {
        get => _databaseVersion;
        set => SetProperty(ref _databaseVersion, value);
    }

    public string YoloServiceVersion
    {
        get => _yoloServiceVersion;
        set => SetProperty(ref _yoloServiceVersion, value);
    }

    public string RuntimeEnvironment
    {
        get => _runtimeEnvironment;
        set => SetProperty(ref _runtimeEnvironment, value);
    }

    public string UpdatePolicy
    {
        get => _updatePolicy;
        set => SetProperty(ref _updatePolicy, value);
    }

    public string NetworkStatus
    {
        get => _networkStatus;
        set => SetProperty(ref _networkStatus, value);
    }

    public string NetworkAdapter
    {
        get => _networkAdapter;
        set => SetProperty(ref _networkAdapter, value);
    }

    public string LatestVersion
    {
        get => _latestVersion;
        set
        {
            if (string.Equals(_latestVersion, value, StringComparison.Ordinal)) return;
            SetProperty(ref _latestVersion, value);
            RaisePropertyChanged(nameof(UpdateButtonText));
        }
    }

    public string ReleaseDate
    {
        get => _releaseDate;
        set => SetProperty(ref _releaseDate, value);
    }

    public string PackageSize
    {
        get => _packageSize;
        set => SetProperty(ref _packageSize, value);
    }

    public string ReleaseNotes
    {
        get => _releaseNotes;
        set => SetProperty(ref _releaseNotes, value);
    }

    public bool IsMachineRunning
    {
        get => _isMachineRunning;
        set
        {
            if (_isMachineRunning == value) return;
            SetProperty(ref _isMachineRunning, value);
            RaisePropertyChanged(nameof(CanStartUpdate));
            RaisePropertyChanged(nameof(UpdateBlockedText));
        }
    }

    public bool HasAvailableUpdate
    {
        get => _hasAvailableUpdate;
        set
        {
            if (_hasAvailableUpdate == value) return;
            SetProperty(ref _hasAvailableUpdate, value);
            RaisePropertyChanged(nameof(CanStartUpdate));
            RaisePropertyChanged(nameof(UpdateButtonText));
            RaisePropertyChanged(nameof(UpdateBlockedText));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string UpdateButtonText => HasAvailableUpdate ? $"更新到 {LatestVersion}" : "暂无可用更新";

    public bool CanStartUpdate => HasAvailableUpdate && !IsMachineRunning;

    public string UpdateBlockedText => IsMachineRunning
        ? "请先停止设备"
        : HasAvailableUpdate
            ? "已满足更新条件"
            : "检查更新后可执行";

    public ICommand CheckForUpdatesCommand { get; }

    public ICommand StartUpdateCommand { get; }

    public Task ActivateAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var info = await _apiClient.GetSoftwareVersionAsync(CancellationToken.None);
            ProductName = info.ProductName;
            CurrentVersion = info.CurrentVersion;
            BuildTime = info.BuildTime;
            DatabaseVersion = info.DatabaseVersion;
            YoloServiceVersion = info.YoloServiceVersion;
            RuntimeEnvironment = info.RuntimeEnvironment;
            UpdatePolicy = info.UpdatePolicy;
            RefreshNetworkStatus();

            try
            {
                var dashboard = await _apiClient.GetDashboardAsync(CancellationToken.None);
                IsMachineRunning = dashboard.Snapshot.RuntimeMode is RuntimeMode.Running or RuntimeMode.Starting;
            }
            catch
            {
                IsMachineRunning = false;
            }

            RecentUpdates.Clear();
            foreach (var update in info.RecentUpdates)
            {
                RecentUpdates.Add(SoftwareUpdateRecordRowViewModel.FromRecord(update));
            }

            StatusMessage = "软件版本信息已加载。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取软件版本失败: {ex.Message}";
        }
    }

    private Task CheckForUpdatesAsync()
    {
        RefreshNetworkStatus();
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            LatestVersion = "待检查";
            ReleaseDate = "--";
            PackageSize = "--";
            ReleaseNotes = "网络不可用";
            HasAvailableUpdate = false;
            StatusMessage = "未检测到可用网卡连接。";
            return Task.CompletedTask;
        }

        LatestVersion = CurrentVersion;
        ReleaseDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        PackageSize = "--";
        ReleaseNotes = "当前已是最新版本";
        HasAvailableUpdate = false;
        StatusMessage = "网络更新服务接口尚未接入，当前仅完成界面与网卡状态检测。";
        return Task.CompletedTask;
    }

    private Task StartUpdateAsync()
    {
        if (!CanStartUpdate)
        {
            StatusMessage = UpdateBlockedText;
            return Task.CompletedTask;
        }

        StatusMessage = "网络更新执行接口尚未接入。";
        return Task.CompletedTask;
    }

    private void RefreshNetworkStatus()
    {
        var networkAvailable = NetworkInterface.GetIsNetworkAvailable();
        NetworkStatus = networkAvailable ? "已连接" : "未连接";
        NetworkAdapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                        x.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .Select(x => x.Name)
            .FirstOrDefault() ?? "未连接";
    }
}

public sealed class UserCenterPageViewModel : ViewModelBase, IDisposable
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly DispatcherTimer _durationTimer;
    private readonly DispatcherTimer _searchDebounceTimer;
    private CancellationTokenSource? _auditLoadCancellation;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _passwordStatusMessage = "管理员可在这里修改登录密码。";
    private string _selectedTimeRange = "今日";
    private string _selectedOperationType = "全部类型";
    private string _searchText = string.Empty;
    private AuditPageSizeOptionViewModel _selectedPageSize;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private string _totalRecordText = "共 0 条";
    private string _pageStatusText = "第 1 / 1 页";
    private string _auditLoadStatusText = "正在读取操作记录...";
    private UserAuditRecordRowViewModel? _selectedAuditRecord;
    private bool _isAuditDetailOpen;

    public UserCenterPageViewModel(string currentUserDisplay, UserRole role, DateTimeOffset loginAt)
    {
        CurrentUserDisplay = currentUserDisplay;
        Role = role;
        LoginAt = loginAt;
        UserName = ParseUserName(currentUserDisplay);

        TimeRangeOptions = ["今日", "近 7 天", "近 30 天", "全部"];
        OperationTypeOptions = ["全部类型", "登录", "执行检测", "复核", "管理"];
        PageSizeOptions =
        [
            new AuditPageSizeOptionViewModel(10),
            new AuditPageSizeOptionViewModel(20),
            new AuditPageSizeOptionViewModel(50)
        ];
        _selectedPageSize = PageSizeOptions[0];

        foreach (var row in BuildPermissionRows(role))
        {
            PermissionRows.Add(row);
        }

        ChangeAdminPasswordCommand = new AsyncRelayCommand(ChangeAdminPasswordAsync);
        ResetFiltersCommand = new RelayCommand(_ => ResetFilters());
        ExportRecordsCommand = new AsyncRelayCommand(ExportRecordsAsync);
        PreviousPageCommand = new RelayCommand(_ => GoToPage(CurrentPage - 1));
        NextPageCommand = new RelayCommand(_ => GoToPage(CurrentPage + 1));
        GoToPageCommand = new RelayCommand(GoToPage);
        OpenAuditDetailCommand = new RelayCommand(OpenAuditDetail);
        CloseAuditDetailCommand = new RelayCommand(_ => IsAuditDetailOpen = false);

        _durationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += (_, _) => RaisePropertyChanged(nameof(LoginDurationText));
        _durationTimer.Start();

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _ = LoadAuditRecordsAsync();
        };

        _ = LoadAuditRecordsAsync();
    }

    public string CurrentUserDisplay { get; }

    public UserRole Role { get; }

    public DateTimeOffset LoginAt { get; }

    public string UserName { get; }

    public string RoleName => Role == UserRole.Administrator ? "管理员" : "操作员";

    public string LoginTimeDisplay => LoginAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string LoginTimeText => $"登录时间  {LoginTimeDisplay}";

    public string LoginDurationText
    {
        get
        {
            var duration = DateTimeOffset.Now - LoginAt;
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            return $"登录时长  {(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }

    public string LoginMethodText => Role == UserRole.Administrator
        ? "登录方式  本机管理员登录"
        : "登录方式  本机操作员登录";

    public bool CanChangeAdminPassword => Role == UserRole.Administrator;

    public string PermissionTitle => Role == UserRole.Administrator ? "管理员权限" : "操作员权限";

    public string PermissionSubtitle => Role == UserRole.Administrator ? "管理员权限范围" : "操作员权限范围";

    public string AuditScopeText => Role == UserRole.Administrator
        ? "当前显示全部重要操作；管理员可查看全部操作人"
        : $"当前仅显示 {UserName} 的重要操作；管理员可查看全部操作人";

    public string RoleSummary => Role == UserRole.Administrator
        ? "管理员可进入系统设置，维护产品、模型、通道、硬件、剔除设置和安全预警。"
        : "操作员可进入首页监控、用户中心、数据中心和人工复核，不可进入系统设置。";

    public ObservableCollection<UserPermissionRowViewModel> PermissionRows { get; } = [];

    public ObservableCollection<UserAuditRecordRowViewModel> VisibleAuditRecords { get; } = [];

    public ObservableCollection<AuditPageButtonViewModel> PageButtons { get; } = [];

    public ObservableCollection<string> TimeRangeOptions { get; }

    public ObservableCollection<string> OperationTypeOptions { get; }

    public ObservableCollection<AuditPageSizeOptionViewModel> PageSizeOptions { get; }

    public string SelectedTimeRange
    {
        get => _selectedTimeRange;
        set
        {
            if (string.Equals(_selectedTimeRange, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _selectedTimeRange, value);
            CurrentPage = 1;
            _ = LoadAuditRecordsAsync();
        }
    }

    public string SelectedOperationType
    {
        get => _selectedOperationType;
        set
        {
            if (string.Equals(_selectedOperationType, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _selectedOperationType, value);
            CurrentPage = 1;
            _ = LoadAuditRecordsAsync();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _searchText, value);
            CurrentPage = 1;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }

    public AuditPageSizeOptionViewModel SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            if (ReferenceEquals(_selectedPageSize, value) || value is null)
            {
                return;
            }

            SetProperty(ref _selectedPageSize, value);
            CurrentPage = 1;
            _ = LoadAuditRecordsAsync();
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
            {
                return;
            }

            SetProperty(ref _currentPage, value);
            RaisePropertyChanged(nameof(CanGoToPreviousAuditPage));
            RaisePropertyChanged(nameof(CanGoToNextAuditPage));
        }
    }

    public bool CanGoToPreviousAuditPage => CurrentPage > 1;

    public bool CanGoToNextAuditPage => CurrentPage < _totalPages;

    public string TotalRecordText
    {
        get => _totalRecordText;
        private set => SetProperty(ref _totalRecordText, value);
    }

    public string PageStatusText
    {
        get => _pageStatusText;
        private set => SetProperty(ref _pageStatusText, value);
    }

    public string AuditLoadStatusText
    {
        get => _auditLoadStatusText;
        private set => SetProperty(ref _auditLoadStatusText, value);
    }

    public UserAuditRecordRowViewModel? SelectedAuditRecord
    {
        get => _selectedAuditRecord;
        private set => SetProperty(ref _selectedAuditRecord, value);
    }

    public bool IsAuditDetailOpen
    {
        get => _isAuditDetailOpen;
        private set => SetProperty(ref _isAuditDetailOpen, value);
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetProperty(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public string PasswordStatusMessage
    {
        get => _passwordStatusMessage;
        set => SetProperty(ref _passwordStatusMessage, value);
    }

    public ICommand ChangeAdminPasswordCommand { get; }

    public ICommand ResetFiltersCommand { get; }

    public ICommand ExportRecordsCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand GoToPageCommand { get; }

    public ICommand OpenAuditDetailCommand { get; }

    public ICommand CloseAuditDetailCommand { get; }

    private async Task ChangeAdminPasswordAsync()
    {
        try
        {
            await _apiClient.ChangeAdminPasswordAsync(
                new ChangeAdminPasswordRequest(CurrentPassword, NewPassword, ConfirmPassword),
                CancellationToken.None);

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            PasswordStatusMessage = "管理员密码已修改，下次登录请使用新密码。";
        }
        catch (Exception ex)
        {
            PasswordStatusMessage = ex.Message;
        }
    }

    public string OperationHint => "左侧主导航保留现场高频入口，复杂配置集中在系统设置中。";

    public void Dispose()
    {
        _durationTimer.Stop();
        _searchDebounceTimer.Stop();
        _auditLoadCancellation?.Cancel();
        _auditLoadCancellation?.Dispose();
    }

    private void ResetFilters()
    {
        _selectedTimeRange = "今日";
        _selectedOperationType = "全部类型";
        _searchText = string.Empty;
        _selectedPageSize = PageSizeOptions[0];
        CurrentPage = 1;

        RaisePropertyChanged(nameof(SelectedTimeRange));
        RaisePropertyChanged(nameof(SelectedOperationType));
        RaisePropertyChanged(nameof(SearchText));
        RaisePropertyChanged(nameof(SelectedPageSize));
        _ = LoadAuditRecordsAsync();
    }

    private void GoToPage(object? parameter)
    {
        var targetPage = parameter switch
        {
            int page => page,
            string value when int.TryParse(value, out var page) => page,
            _ => CurrentPage
        };

        GoToPage(targetPage);
    }

    private void GoToPage(int targetPage)
    {
        var normalized = Math.Clamp(targetPage, 1, _totalPages);
        if (normalized == CurrentPage)
        {
            return;
        }

        CurrentPage = normalized;
        _ = LoadAuditRecordsAsync();
    }

    private async Task LoadAuditRecordsAsync()
    {
        _auditLoadCancellation?.Cancel();
        _auditLoadCancellation?.Dispose();
        _auditLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _auditLoadCancellation.Token;
        AuditLoadStatusText = "正在读取操作记录...";

        try
        {
            var result = await _apiClient.GetAuditLogsAsync(
                MapTimeRange(SelectedTimeRange),
                SelectedOperationType,
                SearchText,
                CurrentPage,
                SelectedPageSize.Value,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _totalPages = result.TotalPages;
            CurrentPage = Math.Clamp(result.Page, 1, _totalPages);
            RaisePropertyChanged(nameof(CanGoToPreviousAuditPage));
            RaisePropertyChanged(nameof(CanGoToNextAuditPage));
            VisibleAuditRecords.Clear();
            for (var index = 0; index < result.Items.Count; index++)
            {
                var row = new UserAuditRecordRowViewModel(result.Items[index])
                {
                    RowBackground = index % 2 == 0
                        ? UserCenterBrushes.AuditRowDark
                        : UserCenterBrushes.AuditRowLight
                };
                VisibleAuditRecords.Add(row);
            }

            RefreshPageButtons();
            TotalRecordText = $"共 {result.TotalCount} 条";
            PageStatusText = $"{CurrentPage} / {_totalPages}";
            AuditLoadStatusText = result.TotalCount == 0 ? "暂无符合条件的重要操作记录" : string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            VisibleAuditRecords.Clear();
            PageButtons.Clear();
            _totalPages = 1;
            CurrentPage = 1;
            RaisePropertyChanged(nameof(CanGoToPreviousAuditPage));
            RaisePropertyChanged(nameof(CanGoToNextAuditPage));
            TotalRecordText = "共 0 条";
            PageStatusText = "1 / 1";
            AuditLoadStatusText = $"操作记录读取失败：{ex.Message}";
        }
    }

    private void RefreshPageButtons()
    {
        PageButtons.Clear();
        var start = Math.Max(1, Math.Min(CurrentPage - 1, _totalPages - 2));
        var end = Math.Min(_totalPages, start + 2);
        for (var page = start; page <= end; page++)
        {
            PageButtons.Add(new AuditPageButtonViewModel(page, page == CurrentPage));
        }
    }

    private async Task ExportRecordsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出重要操作记录",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"重要操作记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var records = new List<OperationAuditRecordDto>();
        const int exportPageSize = 500;
        var firstPage = await _apiClient.GetAuditLogsAsync(
            MapTimeRange(SelectedTimeRange), SelectedOperationType, SearchText, 1, exportPageSize, CancellationToken.None);
        records.AddRange(firstPage.Items);
        for (var page = 2; page <= firstPage.TotalPages; page++)
        {
            var nextPage = await _apiClient.GetAuditLogsAsync(
                MapTimeRange(SelectedTimeRange), SelectedOperationType, SearchText, page, exportPageSize, CancellationToken.None);
            records.AddRange(nextPage.Items);
        }

        var csv = new StringBuilder();
        csv.AppendLine("时间,操作类型,操作内容,操作人");
        foreach (var row in records)
        {
            csv.AppendLine(string.Join(",",
                EscapeCsv(row.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(row.OperationType),
                EscapeCsv(row.Content),
                EscapeCsv(row.OperatorName)));
        }

        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
    }

    private static string EscapeCsv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private void OpenAuditDetail(object? parameter)
    {
        if (parameter is not UserAuditRecordRowViewModel row)
        {
            return;
        }

        SelectedAuditRecord = row;
        IsAuditDetailOpen = true;
    }

    private static string MapTimeRange(string value) => value switch
    {
        "今日" => "today",
        "近 7 天" => "7d",
        "近 30 天" => "30d",
        _ => "all"
    };

    private static string ParseUserName(string currentUserDisplay)
    {
        var parts = currentUserDisplay.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.LastOrDefault() ?? currentUserDisplay;
    }

    private static IReadOnlyList<UserPermissionRowViewModel> BuildPermissionRows(UserRole role)
    {
        var isAdministrator = role == UserRole.Administrator;
        return
        [
            new("首页监控", true, "实时图像与计数", true),
            new("用户中心", true, "查看身份与权限", false),
            new("数据中心", true, "历史统计与检测任务", true),
            new("人工复核", true, "确认异常、误检与类别修正", false),
            new("系统设置", isAdministrator, isAdministrator ? "产品、设备与系统配置" : "仅管理员", true)
        ];
    }

}

public sealed class UserPermissionRowViewModel
{
    public UserPermissionRowViewModel(string moduleName, bool isAllowed, string description, bool isAlternate)
    {
        ModuleName = moduleName;
        IsAllowed = isAllowed;
        Description = description;
        RowBackground = isAlternate ? UserCenterBrushes.PermissionAlternate : Brushes.Transparent;
    }

    public string ModuleName { get; }

    public bool IsAllowed { get; }

    public string Description { get; }

    public Brush RowBackground { get; }

    public Brush TextForeground => IsAllowed ? UserCenterBrushes.TextPrimary : UserCenterBrushes.TextDisabled;

    public Brush DescriptionForeground => IsAllowed ? UserCenterBrushes.TextSecondary : UserCenterBrushes.TextDisabled;

    public Brush IndicatorBackground => IsAllowed ? UserCenterBrushes.SuccessBackground : UserCenterBrushes.LockBackground;

    public Brush IndicatorStroke => IsAllowed ? UserCenterBrushes.Success : UserCenterBrushes.TextMuted;

    public string IndicatorGlyph => IsAllowed ? "✓" : "锁";

    public FontWeight ModuleFontWeight => IsAllowed ? FontWeights.Bold : FontWeights.Normal;
}

public sealed class UserAuditRecordRowViewModel
{
    public UserAuditRecordRowViewModel(OperationAuditRecordDto record)
        : this(record.OccurredAt, record.OperationType, record.Content, record.OperatorName)
    {
        Id = record.Id;
        ActionCode = record.ActionCode;
        OperatorDisplayName = record.OperatorDisplayName;
        TargetType = record.TargetType;
        TargetId = record.TargetId;
        TargetName = record.TargetName;
        Changes = record.Changes
            .Select(x => new UserAuditChangeRowViewModel(x.DisplayName, x.OldValue, x.NewValue))
            .ToArray();
        RelatedChanges = record.RelatedChanges;
        BusinessSummaryItems = BuildBusinessSummaryItems(record);
        ManagementRelatedChanges = record.OperationType == "管理" ? record.RelatedChanges : [];
    }

    public UserAuditRecordRowViewModel(
        DateTimeOffset timestamp,
        string operationType,
        string content,
        string operatorName,
        bool isRestricted = false)
    {
        Timestamp = timestamp;
        OperationType = operationType;
        Content = content;
        OperatorName = operatorName;
        IsRestricted = isRestricted;
        Changes = [];
        RelatedChanges = [];
        BusinessSummaryItems = [];
        ManagementRelatedChanges = [];
    }

    public Guid Id { get; }

    public string ActionCode { get; } = string.Empty;

    public DateTimeOffset Timestamp { get; }

    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

    public string OperationType { get; }

    public string Content { get; }

    public string OperatorName { get; }

    public string OperatorDisplayName { get; } = string.Empty;

    public string TargetType { get; } = string.Empty;

    public string TargetId { get; } = string.Empty;

    public string TargetName { get; } = string.Empty;

    public IReadOnlyList<UserAuditChangeRowViewModel> Changes { get; }

    public IReadOnlyList<string> RelatedChanges { get; }

    public IReadOnlyList<string> BusinessSummaryItems { get; }

    public IReadOnlyList<string> ManagementRelatedChanges { get; }

    public bool HasChanges => Changes.Count > 0;

    public bool HasRelatedChanges => RelatedChanges.Count > 0;

    public bool HasBusinessSummary => BusinessSummaryItems.Count > 0;

    public bool HasManagementChanges => OperationType == "管理" && Changes.Count > 0;

    public bool HasManagementRelatedChanges => ManagementRelatedChanges.Count > 0;

    public string BusinessSummaryTitle => OperationType switch
    {
        "执行检测" => "检测摘要",
        "复核" => "复核摘要",
        "登录" => "登录摘要",
        _ => "操作摘要"
    };

    public string DetailTitle => Content;

    public string OperatorDetail => string.IsNullOrWhiteSpace(OperatorDisplayName) || OperatorDisplayName == OperatorName
        ? OperatorName
        : $"{OperatorDisplayName} {OperatorName}";

    public bool IsRestricted { get; }

    public double TypeTagWidth => OperationType == "执行检测" ? 76 : 54;

    public Brush TypeBackground => OperationType switch
    {
        "登录" => UserCenterBrushes.LoginTag,
        "执行检测" => UserCenterBrushes.DetectionTag,
        "复核" => UserCenterBrushes.ReviewTag,
        _ => UserCenterBrushes.ManagementTag
    };

    public Brush RowBackground { get; set; } = UserCenterBrushes.AuditRowDark;

    public Brush TimestampForeground => IsRestricted ? UserCenterBrushes.TextDisabled : UserCenterBrushes.TextTableTime;

    public Brush OperatorForeground => IsRestricted ? UserCenterBrushes.TextDisabled : UserCenterBrushes.TextSecondary;

    public Brush PrimaryForeground => IsRestricted ? UserCenterBrushes.TextDisabled : UserCenterBrushes.TextPrimary;

    public FontWeight ContentFontWeight => IsRestricted ? FontWeights.Normal : FontWeights.Medium;

    private static IReadOnlyList<string> BuildBusinessSummaryItems(OperationAuditRecordDto record)
    {
        if (record.OperationType == "管理")
        {
            return [];
        }

        var items = new List<string>();
        foreach (var change in record.Changes)
        {
            if (record.OperationType == "执行检测" && change.Field == "SessionTime")
            {
                items.Add($"检测时段：{change.OldValue} 至 {change.NewValue}");
                continue;
            }

            var suffix = record.OperationType == "复核" && change.Field == "ReviewProgress"
                ? "（已完成）"
                : string.Empty;
            items.Add($"{change.DisplayName}：{change.NewValue}{suffix}");
        }

        items.AddRange(record.RelatedChanges);
        return items;
    }
}

public sealed record UserAuditChangeRowViewModel(
    string DisplayName,
    string OldValue,
    string NewValue);

public sealed class AuditPageButtonViewModel(int pageNumber, bool isSelected)
{
    public int PageNumber { get; } = pageNumber;

    public bool IsSelected { get; } = isSelected;
}

public sealed class SettingsListPageSizeOptionViewModel(int value)
{
    public int Value { get; } = value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed class AuditPageSizeOptionViewModel(int value)
{
    public int Value { get; } = value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

internal static class UserCenterBrushes
{
    public static readonly Brush TextPrimary = Create(0xE8, 0xF3, 0xFF);
    public static readonly Brush TextSecondary = Create(0xA9, 0xBE, 0xD2);
    public static readonly Brush TextTableTime = Create(0xD8, 0xE8, 0xF7);
    public static readonly Brush TextMuted = Create(0x8E, 0xA9, 0xC4);
    public static readonly Brush TextDisabled = Create(0x7F, 0x93, 0xA8);
    public static readonly Brush Success = Create(0x20, 0xD9, 0x8B);
    public static readonly Brush SuccessBackground = Create(0x0E, 0x4A, 0x3A);
    public static readonly Brush LockBackground = Create(0x13, 0x2A, 0x3F);
    public static readonly Brush PermissionAlternate = Create(0xBF, 0x08, 0x21, 0x37);
    public static readonly Brush AuditRowDark = Create(0x07, 0x1D, 0x31);
    public static readonly Brush AuditRowLight = Create(0x09, 0x20, 0x36);
    public static readonly Brush LoginTag = Create(0x12, 0x63, 0xAE);
    public static readonly Brush DetectionTag = Create(0x6D, 0x5B, 0xD0);
    public static readonly Brush ReviewTag = Create(0x0D, 0x6B, 0x4D);
    public static readonly Brush ManagementTag = Create(0x4A, 0x55, 0x68);

    private static Brush Create(byte red, byte green, byte blue) =>
        Create(0xFF, red, green, blue);

    private static Brush Create(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class EjectionSettingsPageViewModel : ViewModelBase
{
    private bool _isAirJetEnabled = true;
    private string _statusMessage = "参数尚未修改。";

    public EjectionSettingsPageViewModel()
    {
        MethodOptions =
        [
            new("推杆", false),
            new("翻板", false),
            new("下沉", false),
            new("散料气吹", true),
            new("放料翻板", false)
        ];

        TimingParameters =
        [
            new("喷跳发时间(1us)", "128", "控制气阀打开前的触发脉冲。"),
            new("一行时间(1us)", "200", "图像行到物理距离的换算基础。"),
            new("模拟处理行数", "128", "用于联调和模拟剔除延迟。"),
            new("阀打开位数", "1", "当前启用的气阀位。"),
            new("阀打开行数", "20 - 30", "目标进入剔除窗口的行范围。"),
            new("起始像素值", "57", "剔除窗口起始像素。"),
            new("截止像素值", "1464", "剔除窗口截止像素。")
        ];

        LaneParameters =
        [
            new("阀延时行数", "50"),
            new("阀信号控制输出", "开"),
            new("起始吹气阀标号", "6"),
            new("截止吹气阀标号", "124"),
            new("扩展阀的数目", "10"),
            new("延时释放气阀时间", "50"),
            new("吹气阀个数", "128")
        ];

        SendCommand = new RelayCommand(_ => StatusMessage = "剔除参数已发送到控制器。" );
        RestoreCommand = new RelayCommand(_ => StatusMessage = "已恢复上一次保存的参数。" );
        SaveCommand = new RelayCommand(_ => StatusMessage = "剔除参数已保存。" );
    }

    public ObservableCollection<EjectionMethodOptionViewModel> MethodOptions { get; }

    public ObservableCollection<EjectionParameterViewModel> TimingParameters { get; }

    public ObservableCollection<EjectionParameterViewModel> LaneParameters { get; }

    public bool IsAirJetEnabled
    {
        get => _isAirJetEnabled;
        set => SetProperty(ref _isAirJetEnabled, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SendCommand { get; }

    public ICommand RestoreCommand { get; }

    public ICommand SaveCommand { get; }
}

public sealed record EjectionMethodOptionViewModel(string Name, bool IsSelected);

public sealed class EjectionParameterViewModel(string name, string value, string hint = "") : ViewModelBase
{
    private string _value = value;

    public string Name { get; } = name;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string Hint { get; } = hint;
}

public sealed class SettingsPageViewModel : ViewModelBase
{
    private SettingsTopTabViewModel? _selectedTopTab;
    private SettingsModuleShortcutViewModel? _selectedModule;
    private object _currentModulePage = new RecipesPageViewModel();
    private string _moduleTitle = "产品设置";
    private string _moduleSubtitle = "维护海鲜产品、模型版本和通道绑定关系。";

    public SettingsPageViewModel(
        string initialTopTabTitle = "产品设置",
        string? initialModuleTitle = null,
        object? initialModulePage = null)
    {
        SelectTopTabCommand = new RelayCommand(SelectTopTab);
        SelectModuleCommand = new RelayCommand(SelectModule);

        TopTabs.Add(new SettingsTopTabViewModel("产品设置", "产品、模型、通道"));
        TopTabs.Add(new SettingsTopTabViewModel("硬件设置", "传送带、X 光、剔除设备"));
        TopTabs.Add(new SettingsTopTabViewModel("剔除设置", "动作方式与剔除位"));
        TopTabs.Add(new SettingsTopTabViewModel("系统设置", "预警、权限、日志"));

        var initialTopTab = TopTabs.FirstOrDefault(x => x.Title == initialTopTabTitle) ?? TopTabs[0];
        SelectTopTab(initialTopTab);

        if (!string.IsNullOrWhiteSpace(initialModuleTitle))
        {
            var initialModule = ModuleShortcuts.FirstOrDefault(x => x.Title == initialModuleTitle);
            if (initialModule is not null)
            {
                SelectModule(initialModule);
                if (initialModulePage is not null)
                {
                    CurrentModulePage = initialModulePage;
                    if (initialModulePage is IActivatablePageViewModel activatable)
                    {
                        _ = activatable.ActivateAsync();
                    }
                }
            }
        }
    }

    public ObservableCollection<SettingsTopTabViewModel> TopTabs { get; } = [];

    public ObservableCollection<SettingsModuleShortcutViewModel> ModuleShortcuts { get; } = [];

    public ICommand SelectTopTabCommand { get; }

    public ICommand SelectModuleCommand { get; }

    public SettingsTopTabViewModel? SelectedTopTab
    {
        get => _selectedTopTab;
        private set => SetProperty(ref _selectedTopTab, value);
    }

    public SettingsModuleShortcutViewModel? SelectedModule
    {
        get => _selectedModule;
        private set => SetProperty(ref _selectedModule, value);
    }

    public object CurrentModulePage
    {
        get => _currentModulePage;
        private set => SetProperty(ref _currentModulePage, value);
    }

    public string ModuleTitle
    {
        get => _moduleTitle;
        private set => SetProperty(ref _moduleTitle, value);
    }

    public string ModuleSubtitle
    {
        get => _moduleSubtitle;
        private set => SetProperty(ref _moduleSubtitle, value);
    }

    private void SelectTopTab(object? parameter)
    {
        if (parameter is not SettingsTopTabViewModel tab)
        {
            return;
        }

        foreach (var item in TopTabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SelectedTopTab = tab;
        ModuleShortcuts.Clear();
        foreach (var module in BuildModules(tab.Title))
        {
            ModuleShortcuts.Add(module);
        }

        SelectModule(ModuleShortcuts.FirstOrDefault());
    }

    private void SelectModule(object? parameter)
    {
        if (parameter is not SettingsModuleShortcutViewModel module)
        {
            return;
        }

        foreach (var item in ModuleShortcuts)
        {
            item.IsSelected = ReferenceEquals(item, module);
        }

        SelectedModule = module;
        ModuleTitle = module.Title;
        ModuleSubtitle = module.Description;
        CurrentModulePage = module.CreatePage();

        if (CurrentModulePage is IActivatablePageViewModel activatable)
        {
            _ = activatable.ActivateAsync();
        }
    }

    private static IReadOnlyList<SettingsModuleShortcutViewModel> BuildModules(string tabTitle) => tabTitle switch
    {
        "产品设置" =>
        [
            new("海鲜产品", "维护海鲜产品和性状标准。", () => new RecipesPageViewModel()),
            new("模型管理", "导入 YOLO 权重，维护模型版本和绑定状态。", () => new ModelManagementPageViewModel()),
            new("通道配置", "为通道选择产品、模型和置信度。", () => new ChannelConfigsPageViewModel())
        ],
        "硬件设置" =>
        [
            new("设备管理", "维护 X 光光源、X 光探测器、传送带、剔除设备。", () => new DeviceManagementPageViewModel())
        ],
        "剔除设置" =>
        [
            new("剔除控制", "配置剔除方式、剔除位、气吹/推杆等动作参数。", () => new EjectionSettingsPageViewModel())
        ],
        "系统设置" =>
        [
            new("安全预警", "集中查看和确认未处理安全预警。", () => new SafetyAlarmsPageViewModel()),
            new("软件维护", "查看软件版本、网络更新状态和更新历史。", () => new SoftwareMaintenancePageViewModel())
        ],
        _ => []
    };
}

public sealed class SettingsTopTabViewModel(string title, string description) : ViewModelBase
{
    private bool _isSelected;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SettingsModuleShortcutViewModel(
    string title,
    string description,
    Func<object> createPage) : ViewModelBase
{
    private bool _isSelected;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public object CreatePage() => createPage();
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

public sealed record KpiCardViewModel(string Title, string Value, string Icon, string ValueColor, string AccentColor);

public sealed record DeviceSummary(string Name, string Status, string StatusColor);

public sealed record AlarmPolicyRowViewModel(string Trigger, string Action, string ActionColor);

public sealed record DefectOverviewRowViewModel(string Label, string Value, string PercentText, double BarWidth, string Color);

public sealed record DashboardChannelCardViewModel(
    string ChannelName,
    string Status,
    string ProductName,
    string ModelVersion,
    string TotalCount,
    string NormalCount,
    string RejectCount,
    string YieldRate,
    string TopDefectLabel,
    string StatusColor)
{
    public string ProductAndModel => $"{ProductName} · {ModelVersion}";

    public string CountSummary => $"总数 {TotalCount} / 正常 {NormalCount} / 异常 {RejectCount} / 良率 {YieldRate}";
}

public sealed class ProductionStatisticsPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private const int DefaultTaskPageSize = 10;
    private const decimal YieldTargetThreshold = 80m;
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly ProductionStatisticsNavigationState? _navigationState;
    private string _selectedRange = "30d";
    private string _statusMessage = "请选择统计范围。";
    private ProductProductionStatRowViewModel? _selectedProduct;
    private DetectionSessionRowViewModel? _selectedSession;
    private string _selectedProductSummary = "请选择一条海鲜产品统计记录查看图表。";
    private string _selectedSessionSummary = "请选择一条检测任务查看检测情况。";
    private string _manualReviewSummary = "请选择一条检测任务加载异常剔除复核。";
    private string _manualReviewRecallStatus = "严格召回率暂不计算。";
    private string _manualReviewHumanLabel = "正常";
    private string _manualReviewNotes = string.Empty;
    private string _yieldTrendSummary = "暂无检测任务趋势数据。";
    private string _selectedProductHeader = "暂无海鲜产品";
    private ProductionStatisticsTabViewModel? _selectedTab;
    private ManualReviewCandidateRowViewModel? _selectedReviewCandidate;
    private QualityRingViewModel _productQualityRing = QualityRingViewModel.Empty("产品良率");
    private QualityRingViewModel _sessionQualityRing = QualityRingViewModel.Empty("任务良率");
    private PointCollection _yieldTrendLinePoints = [];
    private Geometry _yieldTrendLineGeometry = Geometry.Empty;
    private Geometry _yieldTrendQualifiedLineGeometry = Geometry.Empty;
    private Geometry _yieldTrendAlertLineGeometry = Geometry.Empty;
    private Geometry _yieldTrendAreaGeometry = Geometry.Empty;
    private IReadOnlyList<YieldTrendPointDto> _allYieldTrendPoints = [];
    private TaskTypeFilterOptionViewModel? _selectedTaskType;
    private int _taskCurrentPage = 1;
    private int _selectedTaskPageSize = DefaultTaskPageSize;
    private int _taskTotalCount;
    private int _taskReviewLoadVersion;

    public ProductionStatisticsPageViewModel(ProductionStatisticsNavigationState? navigationState = null)
    {
        _navigationState = navigationState;
        SelectRangeCommand = new RelayCommand(parameter => _ = SelectRangeAsync(parameter as ProductionRangeOptionViewModel));
        SelectTabCommand = new RelayCommand(parameter => SelectTab(parameter as ProductionStatisticsTabViewModel));
        SelectTrendPointCommand = new RelayCommand(parameter => SelectTrendPoint(parameter as YieldTrendPointViewModel));
        OpenManualReviewCommand = new RelayCommand(parameter => OpenManualReview(parameter as DetectionSessionRowViewModel));
        ConfirmReviewCandidateCommand = new RelayCommand(parameter => _ = ConfirmReviewCandidateAsync(parameter as ManualReviewCandidateRowViewModel));
        MarkFalsePositiveCommand = new RelayCommand(parameter => _ = MarkFalsePositiveAsync(parameter as ManualReviewCandidateRowViewModel));
        SaveManualReviewCommand = new RelayCommand(_ => _ = SaveManualReviewAsync());
        PreviousTaskPageCommand = new RelayCommand(_ => GoToTaskPage(TaskCurrentPage - 1));
        NextTaskPageCommand = new RelayCommand(_ => GoToTaskPage(TaskCurrentPage + 1));
        SelectedTaskType = TaskTypeOptions.First();

        if (navigationState is not null)
        {
            foreach (var range in RangeOptions)
            {
                range.IsSelected = string.Equals(range.Value, navigationState.RangeValue, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!RangeOptions.Any(x => x.IsSelected))
        {
            RangeOptions.First().IsSelected = true;
        }

        var initialTab = navigationState is null
            ? StatisticsTabs.First()
            : StatisticsTabs.FirstOrDefault(x => x.Code == navigationState.TabCode) ?? StatisticsTabs.First();
        foreach (var tab in StatisticsTabs)
        {
            tab.IsSelected = ReferenceEquals(tab, initialTab);
        }

        SelectedTab = initialTab;
    }

    public ObservableCollection<ProductionRangeOptionViewModel> RangeOptions { get; } =
    [
        new("过去 1 天", "1d", false),
        new("过去 7 天", "7d", false),
        new("过去 30 天", "30d", true)
    ];

    public ObservableCollection<ProductionStatisticsTabViewModel> StatisticsTabs { get; } =
    [
        new("R1", "历史统计", true),
        new("R2", "检测任务记录", false),
        new("R3", "报表与追溯", false)
    ];

    public ObservableCollection<ProductProductionStatRowViewModel> ProductRows { get; } = [];

    public ObservableCollection<DetectionSessionRowViewModel> RecentSessions { get; } = [];

    public ObservableCollection<DetectionSessionRowViewModel> ProductSessions { get; } = [];

    public ObservableCollection<DetectionSessionRowViewModel> TaskPageRows { get; } = [];

    public ObservableCollection<TaskTypeFilterOptionViewModel> TaskTypeOptions { get; } =
    [
        new("全部任务", "all"),
        new("生产检测", "生产检测"),
        new("离线检测", "离线检测")
    ];

    public ObservableCollection<int> TaskPageSizeOptions { get; } = [10, 20, 30];

    public ObservableCollection<StatisticChartItemViewModel> ProductChartItems { get; } = [];

    public ObservableCollection<StatisticChartItemViewModel> SessionChartItems { get; } = [];

    public ObservableCollection<ManualReviewCandidateRowViewModel> ManualReviewCandidates { get; } = [];

    public ObservableCollection<YieldTrendPointViewModel> YieldTrendItems { get; } = [];

    public ObservableCollection<YieldTrendAxisLabelViewModel> YieldTrendAxisLabels { get; } = [];

    public ICommand SelectRangeCommand { get; }

    public ICommand SelectTabCommand { get; }

    public ICommand SelectTrendPointCommand { get; }

    public ICommand OpenManualReviewCommand { get; }

    public ICommand ConfirmReviewCandidateCommand { get; }

    public ICommand MarkFalsePositiveCommand { get; }

    public ICommand SaveManualReviewCommand { get; }

    public ICommand PreviousTaskPageCommand { get; }

    public ICommand NextTaskPageCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public TaskTypeFilterOptionViewModel? SelectedTaskType
    {
        get => _selectedTaskType;
        set
        {
            SetProperty(ref _selectedTaskType, value);
            TaskCurrentPage = 1;
            RefreshTaskPage();
        }
    }

    public int TaskCurrentPage
    {
        get => _taskCurrentPage;
        private set
        {
            SetProperty(ref _taskCurrentPage, value);
            RaisePropertyChanged(nameof(TaskPageText));
            RaisePropertyChanged(nameof(CanGoToPreviousTaskPage));
            RaisePropertyChanged(nameof(CanGoToNextTaskPage));
        }
    }

    public int SelectedTaskPageSize
    {
        get => _selectedTaskPageSize;
        set
        {
            var normalized = value <= 0 ? DefaultTaskPageSize : value;
            SetProperty(ref _selectedTaskPageSize, normalized);
            TaskCurrentPage = 1;
            RaisePropertyChanged(nameof(TaskPageCount));
            RaisePropertyChanged(nameof(TaskPageText));
            RaisePropertyChanged(nameof(CanGoToNextTaskPage));
            RefreshTaskPage();
        }
    }

    public int TaskTotalCount
    {
        get => _taskTotalCount;
        private set
        {
            SetProperty(ref _taskTotalCount, value);
            RaisePropertyChanged(nameof(TaskTotalText));
            RaisePropertyChanged(nameof(TaskPageCount));
            RaisePropertyChanged(nameof(TaskPageText));
            RaisePropertyChanged(nameof(CanGoToNextTaskPage));
        }
    }

    public int TaskPageCount => Math.Max(1, (int)Math.Ceiling(TaskTotalCount / (double)Math.Max(1, SelectedTaskPageSize)));

    public string TaskTotalText => $"共 {TaskTotalCount} 条";

    public string TaskPageText => $"{TaskCurrentPage} / {TaskPageCount}";

    public bool CanGoToPreviousTaskPage => TaskCurrentPage > 1;

    public bool CanGoToNextTaskPage => TaskCurrentPage < TaskPageCount;

    public bool CanOpenSelectedManualReview => SelectedSession?.CanOpenManualReview == true;

    public ProductProductionStatRowViewModel? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            SetProperty(ref _selectedProduct, value);
            RefreshProductChart();
            FilterSessionsBySelectedProduct();
            RefreshYieldTrend();
        }
    }

    public DetectionSessionRowViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            SetProperty(ref _selectedSession, value);
            RefreshSessionChart();
            _ = LoadManualReviewAsync(value);
            RaisePropertyChanged(nameof(CanOpenSelectedManualReview));
        }
    }

    public ManualReviewCandidateRowViewModel? SelectedReviewCandidate
    {
        get => _selectedReviewCandidate;
        set
        {
            SetProperty(ref _selectedReviewCandidate, value);
            if (value is not null)
            {
                ManualReviewHumanLabel = string.IsNullOrWhiteSpace(value.HumanLabel) ? value.ModelLabel : value.HumanLabel;
                ManualReviewNotes = value.Notes;
            }
        }
    }

    public ProductionStatisticsTabViewModel? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            SetProperty(ref _selectedTab, value);
            RaisePropertyChanged(nameof(IsHistoryTabSelected));
            RaisePropertyChanged(nameof(IsTaskTabSelected));
            RaisePropertyChanged(nameof(IsReportTabSelected));
        }
    }

    public bool IsHistoryTabSelected => SelectedTab?.Code == "R1";

    public bool IsTaskTabSelected => SelectedTab?.Code == "R2";

    public bool IsReportTabSelected => SelectedTab?.Code == "R3";

    public string SelectedProductSummary
    {
        get => _selectedProductSummary;
        private set => SetProperty(ref _selectedProductSummary, value);
    }

    public string SelectedSessionSummary
    {
        get => _selectedSessionSummary;
        private set => SetProperty(ref _selectedSessionSummary, value);
    }

    public string ManualReviewSummary
    {
        get => _manualReviewSummary;
        private set => SetProperty(ref _manualReviewSummary, value);
    }

    public string ManualReviewRecallStatus
    {
        get => _manualReviewRecallStatus;
        private set => SetProperty(ref _manualReviewRecallStatus, value);
    }

    public string ManualReviewHumanLabel
    {
        get => _manualReviewHumanLabel;
        set => SetProperty(ref _manualReviewHumanLabel, value);
    }

    public string ManualReviewNotes
    {
        get => _manualReviewNotes;
        set => SetProperty(ref _manualReviewNotes, value);
    }

    public string SelectedProductHeader
    {
        get => _selectedProductHeader;
        private set => SetProperty(ref _selectedProductHeader, value);
    }

    public QualityRingViewModel ProductQualityRing
    {
        get => _productQualityRing;
        private set => SetProperty(ref _productQualityRing, value);
    }

    public QualityRingViewModel SessionQualityRing
    {
        get => _sessionQualityRing;
        private set => SetProperty(ref _sessionQualityRing, value);
    }

    public string YieldTrendSummary
    {
        get => _yieldTrendSummary;
        private set => SetProperty(ref _yieldTrendSummary, value);
    }

    public PointCollection YieldTrendLinePoints
    {
        get => _yieldTrendLinePoints;
        private set => SetProperty(ref _yieldTrendLinePoints, value);
    }

    public Geometry YieldTrendLineGeometry
    {
        get => _yieldTrendLineGeometry;
        private set => SetProperty(ref _yieldTrendLineGeometry, value);
    }

    public Geometry YieldTrendQualifiedLineGeometry
    {
        get => _yieldTrendQualifiedLineGeometry;
        private set => SetProperty(ref _yieldTrendQualifiedLineGeometry, value);
    }

    public Geometry YieldTrendAlertLineGeometry
    {
        get => _yieldTrendAlertLineGeometry;
        private set => SetProperty(ref _yieldTrendAlertLineGeometry, value);
    }

    public Geometry YieldTrendAreaGeometry
    {
        get => _yieldTrendAreaGeometry;
        private set => SetProperty(ref _yieldTrendAreaGeometry, value);
    }

    public Task ActivateAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var selected = RangeOptions.FirstOrDefault(x => x.IsSelected) ?? RangeOptions.First();
            var previousProductId = SelectedProduct?.ProductId ?? _navigationState?.ProductId;
            var previousSessionId = SelectedSession?.SessionId ?? _navigationState?.SessionId;
            _selectedRange = selected.Value;
            var statisticsTask = _apiClient.GetProductionStatisticsAsync(_selectedRange, CancellationToken.None);
            var productsTask = _apiClient.GetProductsAsync(CancellationToken.None);
            await Task.WhenAll(statisticsTask, productsTask);
            var statistics = await statisticsTask;
            var products = await productsTask;
            var statByProductId = statistics.Products.ToDictionary(x => x.ProductId);
            _allYieldTrendPoints = statistics.YieldTrend;
            ProductRows.Clear();
            foreach (var profile in products.OrderBy(x => x.Product.Name, StringComparer.CurrentCulture))
            {
                var product = profile.Product;
                statByProductId.TryGetValue(product.Id, out var stat);
                ProductRows.Add(new ProductProductionStatRowViewModel(
                    product.Id,
                    product.Name,
                    (stat?.TotalCount ?? 0).ToString(CultureInfo.InvariantCulture),
                    (stat?.NormalCount ?? 0).ToString(CultureInfo.InvariantCulture),
                    (stat?.RejectCount ?? 0).ToString(CultureInfo.InvariantCulture),
                    $"{stat?.YieldRate ?? 0m:0.##}%",
                    stat is null || stat.DefectStats.Count == 0
                        ? "暂无"
                        : string.Join(" / ", stat.DefectStats.Select(x => $"{x.Label}:{x.Count}")),
                    stat?.DefectStats ?? []));
            }

            RecentSessions.Clear();
            foreach (var session in statistics.RecentSessions)
            {
                RecentSessions.Add(new DetectionSessionRowViewModel(
                    session.SessionId,
                    session.ProductId,
                    session.SessionCode,
                    session.ProductName,
                    session.ModelVersion,
                    session.TaskType,
                    session.StartedAt,
                    session.EndedAt,
                    session.Status == DetectionSessionStatus.Running ? "检测中" : "已停止",
                    session.TotalCount.ToString(CultureInfo.InvariantCulture),
                    session.NormalCount.ToString(CultureInfo.InvariantCulture),
                    session.RejectCount.ToString(CultureInfo.InvariantCulture),
                    $"{session.YieldRate:0.##}%",
                    session.DefectStats));
            }

            SelectedProduct = ProductRows.FirstOrDefault(x => x.ProductId == previousProductId)
                ?? ProductRows.FirstOrDefault(x => ParseCount(x.TotalCount) > 0)
                ?? ProductRows.FirstOrDefault();
            if (previousSessionId is Guid sessionId)
            {
                ShowTaskSession(sessionId);
            }

            RefreshYieldTrend();
            var productsWithRecords = ProductRows.Count(x => ParseCount(x.TotalCount) > 0);
            StatusMessage = $"统计范围: {selected.Label}，共 {productsWithRecords} 个海鲜产品有检测记录。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取历史统计失败: {ex.Message}";
        }
    }

    private async Task SelectRangeAsync(ProductionRangeOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var item in RangeOptions)
        {
            item.IsSelected = ReferenceEquals(item, option);
        }

        await LoadAsync();
    }

    private void SelectTab(ProductionStatisticsTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        foreach (var item in StatisticsTabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SelectedTab = tab;
    }

    private void SelectTrendPoint(YieldTrendPointViewModel? point)
    {
        if (point is null)
        {
            return;
        }

        var session = ProductSessions.FirstOrDefault(x => x.SessionId == point.SessionId)
            ?? RecentSessions.FirstOrDefault(x => x.SessionId == point.SessionId);
        if (session is not null)
        {
            ShowTaskSession(session.SessionId);
        }

        SelectTab(StatisticsTabs.FirstOrDefault(x => x.Code == "R2"));
    }

    private void OpenManualReview(DetectionSessionRowViewModel? session)
    {
        session ??= SelectedSession;
        if (session is null)
        {
            StatusMessage = "请先选择一个检测任务。";
            return;
        }

        if (!session.CanOpenManualReview)
        {
            StatusMessage = session.Status == "检测中"
                ? "检测任务停止后才能进入人工复核。"
                : "当前任务没有需要复核的异常目标。";
            return;
        }

        ShellNavigationService.Navigate(
            new ManualReviewPageViewModel(
                session,
                new ProductionStatisticsNavigationState(
                    _selectedRange,
                    SelectedProduct?.ProductId,
                    SelectedTab?.Code ?? "R2",
                    session.SessionId)),
            "人工复核",
            $"{session.SessionCode} · {session.ProductName}");
    }

    private void RefreshProductChart()
    {
        ProductChartItems.Clear();
        if (SelectedProduct is null)
        {
            SelectedProductSummary = "请选择一条海鲜产品统计记录查看图表。";
            SelectedProductHeader = "暂无海鲜产品";
            ProductQualityRing = QualityRingViewModel.Empty("产品良率");
            return;
        }

        SelectedProductHeader = $"{SelectedProduct.ProductName} · {SelectedProduct.TotalCount} 个 / 良率 {SelectedProduct.YieldRate}";
        SelectedProductSummary = $"{SelectedProduct.ProductName}: 总量 {SelectedProduct.TotalCount}，正常 {SelectedProduct.NormalCount}，异常 {SelectedProduct.RejectCount}，良率 {SelectedProduct.YieldRate}";
        ProductQualityRing = BuildQualityRing("产品良率", SelectedProduct.NormalCount, SelectedProduct.RejectCount);
        AddDefectChartItems(ProductChartItems, SelectedProduct.DefectStats);
    }

    private void FilterSessionsBySelectedProduct()
    {
        if (SelectedProduct is null)
        {
            ProductSessions.Clear();
            TaskPageRows.Clear();
            TaskTotalCount = 0;
            SelectedSession = null;
            return;
        }

        ProductSessions.Clear();
        foreach (var session in RecentSessions.Where(x => x.ProductId == SelectedProduct.ProductId))
        {
            ProductSessions.Add(session);
        }

        TaskCurrentPage = 1;
        RefreshTaskPage();
    }

    private IReadOnlyList<DetectionSessionRowViewModel> GetFilteredTaskSessions()
    {
        var selectedType = SelectedTaskType?.Value ?? "all";
        return ProductSessions
            .Where(session =>
                string.Equals(selectedType, "all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(session.TaskType, selectedType, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private void RefreshTaskPage(Guid? preferredSessionId = null)
    {
        var filtered = GetFilteredTaskSessions();
        TaskTotalCount = filtered.Count;
        TaskCurrentPage = Math.Clamp(TaskCurrentPage, 1, TaskPageCount);
        RaisePropertyChanged(nameof(TaskPageCount));
        RaisePropertyChanged(nameof(TaskPageText));
        RaisePropertyChanged(nameof(CanGoToNextTaskPage));

        TaskPageRows.Clear();
        foreach (var session in filtered
                     .Skip((TaskCurrentPage - 1) * SelectedTaskPageSize)
                     .Take(SelectedTaskPageSize))
        {
            TaskPageRows.Add(session);
        }

        var selectedId = preferredSessionId ?? SelectedSession?.SessionId;
        var nextSelection = selectedId is Guid id
            ? TaskPageRows.FirstOrDefault(x => x.SessionId == id) ?? TaskPageRows.FirstOrDefault()
            : TaskPageRows.FirstOrDefault();
        if (!ReferenceEquals(SelectedSession, nextSelection))
        {
            SelectedSession = nextSelection;
        }

        _ = LoadTaskPageReviewSummariesAsync(TaskPageRows.ToList(), ++_taskReviewLoadVersion);
    }

    private void GoToTaskPage(int page)
    {
        if (page < 1 || page > TaskPageCount || page == TaskCurrentPage)
        {
            return;
        }

        TaskCurrentPage = page;
        RefreshTaskPage();
    }

    private void ShowTaskSession(Guid sessionId)
    {
        var filtered = GetFilteredTaskSessions();
        var index = filtered.ToList().FindIndex(x => x.SessionId == sessionId);
        if (index < 0)
        {
            SelectedSession = ProductSessions.FirstOrDefault(x => x.SessionId == sessionId) ?? SelectedSession;
            return;
        }

        TaskCurrentPage = (index / Math.Max(1, SelectedTaskPageSize)) + 1;
        RefreshTaskPage(sessionId);
    }

    private async Task LoadTaskPageReviewSummariesAsync(
        IReadOnlyList<DetectionSessionRowViewModel> sessions,
        int requestVersion)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        var results = await Task.WhenAll(sessions.Select(async session =>
        {
            try
            {
                var review = await _apiClient.GetManualReviewAsync(session.SessionId, CancellationToken.None);
                return (Session: session, Summary: (ManualReviewSummaryDto?)review.Summary);
            }
            catch
            {
                return (Session: session, Summary: (ManualReviewSummaryDto?)null);
            }
        }));

        if (requestVersion != _taskReviewLoadVersion)
        {
            return;
        }

        foreach (var result in results)
        {
            if (result.Summary is not null)
            {
                result.Session.ApplyReviewSummary(result.Summary);
            }
        }

        RaisePropertyChanged(nameof(CanOpenSelectedManualReview));
    }

    private void RefreshSessionChart()
    {
        SessionChartItems.Clear();
        if (SelectedSession is null)
        {
            SelectedSessionSummary = "请选择一条检测任务查看检测情况。";
            SessionQualityRing = QualityRingViewModel.Empty("任务良率");
            return;
        }

        SelectedSessionSummary = $"{SelectedSession.SessionCode}: {SelectedSession.TaskType} · {SelectedSession.ProductName} · {SelectedSession.ModelVersion}，总量 {SelectedSession.TotalCount}，正常 {SelectedSession.NormalCount}，异常 {SelectedSession.RejectCount}，良率 {SelectedSession.YieldRate}";
        SessionQualityRing = BuildQualityRing("任务良率", SelectedSession.NormalCount, SelectedSession.RejectCount);
        AddDefectChartItems(SessionChartItems, SelectedSession.DefectStats);
    }

    private async Task LoadManualReviewAsync(DetectionSessionRowViewModel? session)
    {
        ManualReviewCandidates.Clear();
        SelectedReviewCandidate = null;
        if (session is null)
        {
            ManualReviewSummary = "请选择一条检测任务加载异常剔除复核。";
            ManualReviewRecallStatus = "严格召回率暂不计算。";
            return;
        }

        try
        {
            var review = await _apiClient.GetManualReviewAsync(session.SessionId, CancellationToken.None);
            ApplyManualReview(review);
        }
        catch (Exception ex)
        {
            ManualReviewSummary = $"读取人工复核失败: {ex.Message}";
        }
    }

    private void ApplyManualReview(ManualReviewSessionDto review)
    {
        ManualReviewCandidates.Clear();
        foreach (var candidate in review.Candidates)
        {
            ManualReviewCandidates.Add(ManualReviewCandidateRowViewModel.FromDto(candidate));
        }

        var summary = review.Summary;
        var session = RecentSessions.FirstOrDefault(x => x.SessionId == review.SessionId);
        session?.ApplyReviewSummary(summary);
        RaisePropertyChanged(nameof(CanOpenSelectedManualReview));
        var completionText = summary.IsComplete
            ? "复核完成"
            : $"未完成，还剩 {Math.Max(0, summary.CandidateCount - summary.ReviewedCount)} 个";
        ManualReviewSummary =
            $"异常集合 S: {summary.CandidateCount} 个；已复核 {summary.ReviewedCount} 个；{completionText}；正常误检 {summary.FalsePositiveCount} 个；类别修正 {summary.RelabeledAbnormalCount} 个。";
        ManualReviewRecallStatus = summary.RecallStatus;
        SelectedReviewCandidate = ManualReviewCandidates.FirstOrDefault(x => !x.IsReviewed) ?? ManualReviewCandidates.FirstOrDefault();
    }

    private async Task ConfirmReviewCandidateAsync(ManualReviewCandidateRowViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        await SaveReviewAsync(candidate, candidate.ModelLabel, "人工确认模型异常判定。");
    }

    private async Task MarkFalsePositiveAsync(ManualReviewCandidateRowViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        await SaveReviewAsync(candidate, "正常", "人工复核为正常，属于误剔。");
    }

    private async Task SaveManualReviewAsync()
    {
        if (SelectedReviewCandidate is null)
        {
            StatusMessage = "请先选择一个待复核检测框。";
            return;
        }

        await SaveReviewAsync(SelectedReviewCandidate, ManualReviewHumanLabel, ManualReviewNotes);
    }

    private async Task SaveReviewAsync(ManualReviewCandidateRowViewModel candidate, string humanLabel, string notes)
    {
        if (SelectedSession is null)
        {
            return;
        }

        try
        {
            var review = await _apiClient.UpsertManualReviewAsync(
                SelectedSession.SessionId,
                new UpsertManualReviewRequest(
                    candidate.InspectionRecordId,
                    candidate.DetectionId,
                    humanLabel,
                    "operator",
                    notes),
                CancellationToken.None);
            ApplyManualReview(review);
            StatusMessage = "人工复核已保存，模型评估指标已更新。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存人工复核失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "人工复核", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static QualityRingViewModel BuildQualityRing(string title, string normalText, string rejectedText)
    {
        var normal = ParseCount(normalText);
        var rejected = ParseCount(rejectedText);
        var total = Math.Max(1, normal + rejected);
        var normalPercent = decimal.Round(normal * 100m / total, 2);
        var rejectedPercent = decimal.Round(rejected * 100m / total, 2);
        return new QualityRingViewModel(
            title,
            $"{normalPercent:0.##}%",
            $"正常 {normal}",
            $"剔除 {rejected}",
            $"{normalPercent:0.##}%",
            $"{rejectedPercent:0.##}%",
            BuildRingArc(-90, (double)normalPercent * 3.6),
            BuildRingArc(-90 + ((double)normalPercent * 3.6), (double)rejectedPercent * 3.6));
    }

    private static void AddDefectChartItems(ObservableCollection<StatisticChartItemViewModel> target, IReadOnlyList<DashboardDefectStatDto> defects)
    {
        foreach (var defect in defects.OrderByDescending(x => x.Count))
        {
            target.Add(new StatisticChartItemViewModel(defect.Label, defect.Count, defect.Percent, ResolveStatisticColor(defect.Label)));
        }
    }

    private void RefreshYieldTrend()
    {
        YieldTrendItems.Clear();
        var trendPoints = SelectedProduct is null
            ? []
            : _allYieldTrendPoints
                .Where(x => x.ProductId == SelectedProduct.ProductId)
                .OrderBy(x => x.Timestamp)
                .ToList();
        YieldTrendLinePoints = BuildTrendLinePoints(trendPoints);
        YieldTrendLineGeometry = BuildTrendLineGeometry(YieldTrendLinePoints);
        YieldTrendQualifiedLineGeometry = BuildTrendSegmentGeometry(trendPoints, YieldTrendLinePoints, alertSegment: false);
        YieldTrendAlertLineGeometry = BuildTrendSegmentGeometry(trendPoints, YieldTrendLinePoints, alertSegment: true);
        YieldTrendAreaGeometry = BuildTrendAreaGeometry(YieldTrendLinePoints);
        RefreshYieldTrendItems(trendPoints, YieldTrendLinePoints);
        RefreshYieldTrendAxisLabels(trendPoints);
        YieldTrendSummary = trendPoints.Count == 0
            ? SelectedProduct is null ? "请选择海鲜产品查看良率趋势。" : "当前范围内暂无检测任务趋势数据。"
            : $"当前范围内 {trendPoints.Count} 轮检测任务";
    }

    private void RefreshYieldTrendItems(IReadOnlyList<YieldTrendPointDto> trendPoints, PointCollection linePoints)
    {
        YieldTrendItems.Clear();
        for (var index = 0; index < trendPoints.Count && index < linePoints.Count; index++)
        {
            var point = trendPoints[index];
            var chartPoint = linePoints[index];
            YieldTrendItems.Add(new YieldTrendPointViewModel(
                point.SessionId,
                point.Label,
                $"{point.YieldRate:0.##}%",
                point.TotalCount.ToString(CultureInfo.InvariantCulture),
                point.RejectCount.ToString(CultureInfo.InvariantCulture),
                chartPoint.X,
                chartPoint.Y));
        }
    }

    private void RefreshYieldTrendAxisLabels(IReadOnlyList<YieldTrendPointDto> trendPoints)
    {
        YieldTrendAxisLabels.Clear();
        if (trendPoints.Count == 0)
        {
            return;
        }

        int[] indexes = trendPoints.Count <= 4
            ? Enumerable.Range(0, trendPoints.Count).ToArray()
            : [0, trendPoints.Count / 2, trendPoints.Count - 1];

        const double left = 62;
        const double right = 1060;
        var usableWidth = right - left;
        foreach (var index in indexes.Distinct())
        {
            var x = trendPoints.Count == 1
                ? left + (usableWidth / 2)
                : left + (usableWidth * index / (trendPoints.Count - 1));
            YieldTrendAxisLabels.Add(new YieldTrendAxisLabelViewModel(BuildAxisLabel(trendPoints[index]), x));
        }
    }

    private string BuildAxisLabel(YieldTrendPointDto point)
    {
        return string.IsNullOrWhiteSpace(point.Label) ? point.Timestamp.LocalDateTime.ToString("MM-dd", CultureInfo.InvariantCulture) : point.Label;
    }

    private static PointCollection BuildTrendLinePoints(IReadOnlyList<YieldTrendPointDto> trendPoints)
    {
        var points = new PointCollection();
        if (trendPoints.Count == 0)
        {
            return points;
        }

        const double left = 62;
        const double right = 1060;
        const double top = 18;
        const double bottom = 392;
        var usableWidth = right - left;
        var usableHeight = bottom - top;
        for (var i = 0; i < trendPoints.Count; i++)
        {
            var x = trendPoints.Count == 1
                ? left + (usableWidth / 2)
                : left + (usableWidth * i / (trendPoints.Count - 1));
            var boundedYield = Math.Clamp(trendPoints[i].YieldRate, 0m, 100m);
            var y = top + usableHeight - ((double)boundedYield * usableHeight / 100.0);
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static Geometry BuildTrendAreaGeometry(PointCollection linePoints)
    {
        if (linePoints.Count == 0)
        {
            return Geometry.Empty;
        }

        const double bottom = 392;
        var figure = new PathFigure
        {
            StartPoint = new Point(linePoints[0].X, bottom),
            IsClosed = true
        };
        foreach (var point in linePoints)
        {
            figure.Segments.Add(new LineSegment(point, true));
        }

        var lastPoint = linePoints[linePoints.Count - 1];
        figure.Segments.Add(new LineSegment(new Point(lastPoint.X, bottom), true));
        return new PathGeometry([figure]);
    }

    private static Geometry BuildTrendSegmentGeometry(
        IReadOnlyList<YieldTrendPointDto> trendPoints,
        PointCollection linePoints,
        bool alertSegment)
    {
        if (trendPoints.Count < 2 || linePoints.Count < 2)
        {
            return Geometry.Empty;
        }

        var geometry = new PathGeometry();
        PathFigure? activeFigure = null;
        var previousSegmentIncluded = false;
        var segmentCount = Math.Min(trendPoints.Count, linePoints.Count) - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            var previousIsAlert = trendPoints[index].YieldRate < YieldTargetThreshold;
            var currentIsAlert = trendPoints[index + 1].YieldRate < YieldTargetThreshold;
            var includeSegment = alertSegment
                ? previousIsAlert || currentIsAlert
                : !previousIsAlert && !currentIsAlert;

            if (!includeSegment)
            {
                activeFigure = null;
                previousSegmentIncluded = false;
                continue;
            }

            if (!previousSegmentIncluded || activeFigure is null)
            {
                activeFigure = new PathFigure
                {
                    StartPoint = linePoints[index],
                    IsClosed = false
                };
                geometry.Figures.Add(activeFigure);
            }

            activeFigure.Segments.Add(new LineSegment(linePoints[index + 1], true));
            previousSegmentIncluded = true;
        }

        return geometry;
    }

    private static Geometry BuildTrendLineGeometry(PointCollection linePoints)
    {
        if (linePoints.Count == 0)
        {
            return Geometry.Empty;
        }

        var figure = new PathFigure
        {
            StartPoint = linePoints[0],
            IsClosed = false
        };

        if (linePoints.Count == 1)
        {
            figure.StartPoint = new Point(linePoints[0].X - 6, linePoints[0].Y);
            figure.Segments.Add(new LineSegment(new Point(linePoints[0].X + 6, linePoints[0].Y), true));
            return new PathGeometry([figure]);
        }

        for (var index = 0; index < linePoints.Count - 1; index++)
        {
            var current = linePoints[index];
            var next = linePoints[index + 1];
            var previous = index == 0 ? current : linePoints[index - 1];
            var afterNext = index + 2 >= linePoints.Count ? next : linePoints[index + 2];
            var control1 = new Point(
                current.X + ((next.X - previous.X) / 6),
                current.Y + ((next.Y - previous.Y) / 6));
            var control2 = new Point(
                next.X - ((afterNext.X - current.X) / 6),
                next.Y - ((afterNext.Y - current.Y) / 6));
            figure.Segments.Add(new BezierSegment(control1, control2, next, true));
        }

        return new PathGeometry([figure]);
    }

    private static Geometry BuildRingArc(double startDegrees, double sweepDegrees)
    {
        if (sweepDegrees <= 0)
        {
            return Geometry.Empty;
        }

        sweepDegrees = Math.Min(359.99, sweepDegrees);
        const double center = 82;
        const double radius = 74;
        var start = PointOnCircle(center, radius, startDegrees);
        var end = PointOnCircle(center, radius, startDegrees + sweepDegrees);
        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false
        };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweepDegrees > 180,
            SweepDirection.Clockwise,
            true));
        return new PathGeometry([figure]);
    }

    private static Point PointOnCircle(double center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(
            center + (radius * Math.Cos(radians)),
            center + (radius * Math.Sin(radians)));
    }

    private static int ParseCount(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string ResolveStatisticColor(string label)
    {
        if (label.Contains("破", StringComparison.OrdinalIgnoreCase) || label.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            return "#3B82F6";
        }

        if (label.Contains("空", StringComparison.OrdinalIgnoreCase) || label.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return "#94A3B8";
        }

        if (label.Contains("泥", StringComparison.OrdinalIgnoreCase) || label.Contains("muddy", StringComparison.OrdinalIgnoreCase))
        {
            return "#F59E0B";
        }

        return "#60A5FA";
    }
}

public sealed class ProductionRangeOptionViewModel(string label, string value, bool isSelected) : ViewModelBase
{
    private bool _isSelected = isSelected;

    public string Label { get; } = label;

    public string Value { get; } = value;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record TaskTypeFilterOptionViewModel(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed class ProductionStatisticsTabViewModel(string code, string label, bool isSelected) : ViewModelBase
{
    private bool _isSelected = isSelected;

    public string Code { get; } = code;

    public string Label { get; } = label;

    public string DisplayText => $"{Code}  {Label}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record ProductProductionStatRowViewModel(
    Guid ProductId,
    string ProductName,
    string TotalCount,
    string NormalCount,
    string RejectCount,
    string YieldRate,
    string DefectSummary,
    IReadOnlyList<DashboardDefectStatDto> DefectStats)
{
    public override string ToString() => ProductName;
}

public sealed class DetectionSessionRowViewModel : ViewModelBase
{
    private int _reviewCandidateCount;
    private int _reviewedCount;
    private bool _isReviewComplete;

    public DetectionSessionRowViewModel(
        Guid sessionId,
        Guid productId,
        string sessionCode,
        string productName,
        string modelVersion,
        string taskType,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        string status,
        string totalCount,
        string normalCount,
        string rejectCount,
        string yieldRate,
        IReadOnlyList<DashboardDefectStatDto> defectStats)
    {
        SessionId = sessionId;
        ProductId = productId;
        SessionCode = sessionCode;
        ProductName = productName;
        ModelVersion = modelVersion;
        TaskType = taskType;
        StartedAtValue = startedAt;
        EndedAtValue = endedAt;
        Status = status;
        TotalCount = totalCount;
        NormalCount = normalCount;
        RejectCount = rejectCount;
        YieldRate = yieldRate;
        DefectStats = defectStats;
        _reviewCandidateCount = int.TryParse(rejectCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    public Guid SessionId { get; }

    public Guid ProductId { get; }

    public string SessionCode { get; }

    public string ProductName { get; }

    public string ModelVersion { get; }

    public string TaskType { get; }

    public DateTimeOffset StartedAtValue { get; }

    public DateTimeOffset? EndedAtValue { get; }

    public string StartedAt => StartedAtValue.LocalDateTime.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string StartedAtFull => StartedAtValue.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string EndedAt => EndedAtValue?.LocalDateTime.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "进行中";

    public string EndedAtFull => EndedAtValue?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "进行中";

    public string Status { get; }

    public string StatusDotColor => Status == "检测中" ? "#22C98A" : "#87A5BB";

    public string TotalCount { get; }

    public string NormalCount { get; }

    public string RejectCount { get; }

    public string YieldRate { get; }

    public IReadOnlyList<DashboardDefectStatDto> DefectStats { get; }

    public int ReviewCandidateCount => _reviewCandidateCount;

    public int ReviewedCount => _reviewedCount;

    public bool IsReviewComplete => _isReviewComplete;

    public double ReviewProgressPercent => ReviewCandidateCount <= 0
        ? 0
        : Math.Clamp(ReviewedCount * 100.0 / ReviewCandidateCount, 0, 100);

    public string ReviewProgressText => ReviewCandidateCount <= 0
        ? "无需复核"
        : IsReviewComplete
            ? $"已复核 {ReviewedCount}/{ReviewCandidateCount}"
            : ReviewedCount > 0
                ? $"复核中 {ReviewedCount}/{ReviewCandidateCount}"
                : $"待复核 {ReviewCandidateCount}";

    public string ReviewCountText => $"{ReviewedCount} / {ReviewCandidateCount}";

    public string ReviewProgressColor => ReviewCandidateCount <= 0
        ? "#527087"
        : IsReviewComplete
            ? "#22C98A"
            : ReviewedCount > 0
                ? "#2F95FF"
                : "#F5A623";

    public string ReviewActionText => IsReviewComplete
        ? "查看复核结果"
        : ReviewedCount > 0
            ? "继续人工复核"
            : "进入人工复核";

    public bool CanOpenManualReview => Status != "检测中" && ReviewCandidateCount > 0;

    public void ApplyReviewSummary(ManualReviewSummaryDto summary)
    {
        _reviewCandidateCount = summary.CandidateCount;
        _reviewedCount = summary.ReviewedCount;
        _isReviewComplete = summary.IsComplete;
        RaisePropertyChanged(nameof(ReviewCandidateCount));
        RaisePropertyChanged(nameof(ReviewedCount));
        RaisePropertyChanged(nameof(IsReviewComplete));
        RaisePropertyChanged(nameof(ReviewProgressPercent));
        RaisePropertyChanged(nameof(ReviewProgressText));
        RaisePropertyChanged(nameof(ReviewCountText));
        RaisePropertyChanged(nameof(ReviewProgressColor));
        RaisePropertyChanged(nameof(ReviewActionText));
        RaisePropertyChanged(nameof(CanOpenManualReview));
    }
}

public sealed record ProductionStatisticsNavigationState(
    string RangeValue,
    Guid? ProductId,
    string TabCode,
    Guid? SessionId);

public sealed class ManualReviewPageViewModel : ViewModelBase, IActivatablePageViewModel
{
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private readonly DetectionSessionRowViewModel _session;
    private readonly ProductionStatisticsNavigationState _returnState;
    private ManualReviewCandidateRowViewModel? _selectedCandidate;
    private DefectTypeFilterViewModel? _selectedDefectFilter;
    private string _statusMessage = "正在准备人工复核工作台...";
    private string _summaryText = "已复核 0 / 剩余 0 / 误剔 0 / 修正 0";
    private string _humanLabel = "正常";
    private string _notes = string.Empty;
    private string _previewImagePath = string.Empty;
    private string _previewTitle = "请选择一张图片";
    private string _searchKeyword = string.Empty;
    private int _candidateCount;
    private int _reviewedCount;
    private int _remainingCount;
    private int _falsePositiveCount;
    private int _relabeledCount;
    private double _previewBoxX;
    private double _previewBoxY;
    private double _previewBoxWidth;
    private double _previewBoxHeight;
    private string _previewBoxLabel = "当前复核对象";
    private bool _hasPreviewTarget;
    private bool _isReviewComplete;
    private int _previewRequestVersion;

    public ManualReviewPageViewModel(DetectionSessionRowViewModel session, ProductionStatisticsNavigationState? returnState = null)
    {
        _session = session;
        _returnState = returnState ?? new ProductionStatisticsNavigationState("1d", session.ProductId, "R2", session.SessionId);
        BackCommand = new RelayCommand(_ => BackToDataCenter());
        SelectDefectFilterCommand = new RelayCommand(parameter => SelectDefectFilter(parameter as DefectTypeFilterViewModel));
        SelectReviewStatusFilterCommand = new RelayCommand(parameter => SelectReviewStatusFilter(parameter as ReviewStatusFilterViewModel));
        SetHumanLabelCommand = new RelayCommand(parameter => SetHumanLabel(parameter as string));
        ConfirmSelectedCommand = new RelayCommand(_ => _ = ConfirmSelectedAsync());
        MarkFalsePositiveCommand = new RelayCommand(_ => _ = MarkFalsePositiveAsync());
        SaveRelabelCommand = new RelayCommand(_ => _ = SaveRelabelAsync());
        SaveReviewCommand = new RelayCommand(_ => _ = SaveReviewAsync());
    }

    public string SessionTitle => $"{_session.SessionCode} · {_session.ProductName}";

    public string SessionSubtitle => $"{_session.TaskType} / {_session.ModelVersion} / 总量 {_session.TotalCount} / 异常 {_session.RejectCount} / 良率 {_session.YieldRate}";

    public ObservableCollection<DefectTypeFilterViewModel> DefectFilters { get; } = [];

    public ObservableCollection<ReviewStatusFilterViewModel> ReviewStatusFilters { get; } = [];

    public ObservableCollection<string> CorrectionLabels { get; } = [];

    public ObservableCollection<ManualReviewCandidateRowViewModel> AllCandidates { get; } = [];

    public ObservableCollection<ManualReviewCandidateRowViewModel> FilteredCandidates { get; } = [];

    public ICommand BackCommand { get; }

    public ICommand SelectDefectFilterCommand { get; }

    public ICommand SelectReviewStatusFilterCommand { get; }

    public ICommand SetHumanLabelCommand { get; }

    public ICommand ConfirmSelectedCommand { get; }

    public ICommand MarkFalsePositiveCommand { get; }

    public ICommand SaveRelabelCommand { get; }

    public ICommand SaveReviewCommand { get; }

    public DefectTypeFilterViewModel? SelectedDefectFilter
    {
        get => _selectedDefectFilter;
        private set => SetProperty(ref _selectedDefectFilter, value);
    }

    public ReviewStatusFilterViewModel? SelectedReviewStatusFilter { get; private set; }

    public ManualReviewCandidateRowViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            SetProperty(ref _selectedCandidate, value);
            if (value is null)
            {
                _previewRequestVersion++;
                PreviewTitle = "请选择一张图片";
                PreviewImagePath = string.Empty;
                HasPreviewTarget = false;
                HumanLabel = "正常";
                Notes = string.Empty;
                BuildCorrectionLabels();
                RaiseSelectedCandidateDerivedProperties();
                return;
            }

            PreviewTitle = $"{value.ImageName} · 模型判定 {value.ModelLabel} · 置信度 {value.Confidence}";
            PreviewImagePath = string.Empty;
            HasPreviewTarget = true;
            HumanLabel = string.IsNullOrWhiteSpace(value.HumanLabel) ? value.ModelLabel : value.HumanLabel;
            Notes = value.Notes;
            BuildCorrectionLabels();
            RaiseSelectedCandidateDerivedProperties();
            StatusMessage = "正在生成当前目标的单框复核图...";
            _ = LoadSelectedPreviewAsync(value, ++_previewRequestVersion);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string HumanLabel
    {
        get => _humanLabel;
        set
        {
            if (string.Equals(_humanLabel, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _humanLabel, value);
            RaisePropertyChanged(nameof(CanSaveRelabel));
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (string.Equals(_searchKeyword, value, StringComparison.Ordinal))
            {
                return;
            }

            SetProperty(ref _searchKeyword, value);
            if (AllCandidates.Count > 0)
            {
                ApplyFilter(SelectedCandidate?.DetectionId);
            }
        }
    }

    public string PreviewImagePath
    {
        get => _previewImagePath;
        private set => SetProperty(ref _previewImagePath, value);
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    public double PreviewBoxX
    {
        get => _previewBoxX;
        private set => SetProperty(ref _previewBoxX, value);
    }

    public double PreviewBoxY
    {
        get => _previewBoxY;
        private set => SetProperty(ref _previewBoxY, value);
    }

    public double PreviewBoxWidth
    {
        get => _previewBoxWidth;
        private set => SetProperty(ref _previewBoxWidth, value);
    }

    public double PreviewBoxHeight
    {
        get => _previewBoxHeight;
        private set => SetProperty(ref _previewBoxHeight, value);
    }

    public string PreviewBoxLabel
    {
        get => _previewBoxLabel;
        private set => SetProperty(ref _previewBoxLabel, value);
    }

    public bool HasPreviewTarget
    {
        get => _hasPreviewTarget;
        private set => SetProperty(ref _hasPreviewTarget, value);
    }

    public int CandidateCount
    {
        get => _candidateCount;
        private set
        {
            if (_candidateCount == value)
            {
                return;
            }

            SetProperty(ref _candidateCount, value);
            RaisePropertyChanged(nameof(CompletionPercent));
            RaisePropertyChanged(nameof(CompletionText));
            RaisePropertyChanged(nameof(CompletionGateText));
        }
    }

    public int ReviewedCount
    {
        get => _reviewedCount;
        private set
        {
            if (_reviewedCount == value)
            {
                return;
            }

            SetProperty(ref _reviewedCount, value);
            RaisePropertyChanged(nameof(CompletionPercent));
            RaisePropertyChanged(nameof(CompletionText));
            RaisePropertyChanged(nameof(CompletionGateText));
        }
    }

    public int RemainingCount
    {
        get => _remainingCount;
        private set
        {
            if (_remainingCount == value)
            {
                return;
            }

            SetProperty(ref _remainingCount, value);
            RaisePropertyChanged(nameof(CompletionGateText));
        }
    }

    public int FalsePositiveCount
    {
        get => _falsePositiveCount;
        private set => SetProperty(ref _falsePositiveCount, value);
    }

    public int RelabeledCount
    {
        get => _relabeledCount;
        private set => SetProperty(ref _relabeledCount, value);
    }

    public bool IsReviewComplete
    {
        get => _isReviewComplete;
        private set
        {
            if (_isReviewComplete == value)
            {
                return;
            }

            SetProperty(ref _isReviewComplete, value);
            RaisePropertyChanged(nameof(CompletionGateText));
        }
    }

    public double CompletionPercent => CandidateCount <= 0 ? 0 : Math.Round(ReviewedCount * 100.0 / CandidateCount, 1);

    public string CompletionText => $"{CompletionPercent:0.#}%";

    public string CompletionGateText => IsReviewComplete
        ? "已全部复核，可以完成"
        : $"剩余 {RemainingCount} 个，暂不可完成";

    public string CompletionGuidanceText => "全部目标复核后可完成本次任务";

    public string SelectedModelLabelText => SelectedCandidate is null
        ? "模型判定：-"
        : $"模型判定： {SelectedCandidate.ModelLabel}";

    public string CorrectionDefaultText => SelectedCandidate is null
        ? "不修正"
        : $"不修正，按{SelectedCandidate.ModelLabel}保存";

    public bool CanSaveRelabel =>
        SelectedCandidate is not null &&
        !string.IsNullOrWhiteSpace(HumanLabel) &&
        !IsNormalLabel(HumanLabel) &&
        !string.Equals(HumanLabel, SelectedCandidate.ModelLabel, StringComparison.CurrentCultureIgnoreCase);

    public Task ActivateAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var review = await _apiClient.GetManualReviewAsync(_session.SessionId, CancellationToken.None);
            ApplyReview(review, SelectedCandidate?.DetectionId);
            StatusMessage = "请选择缺陷类型和图片进行人工复核。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载人工复核数据失败: {ex.Message}";
        }
    }

    private void ApplyReview(ManualReviewSessionDto review, Guid? preferredDetectionId)
    {
        AllCandidates.Clear();
        foreach (var candidate in review.Candidates)
        {
            AllCandidates.Add(ManualReviewCandidateRowViewModel.FromDto(candidate));
        }

        BuildDefectFilters();
        BuildCorrectionLabels();
        BuildReviewStatusFilters();
        ApplyFilter(preferredDetectionId);
        var summary = review.Summary;
        CandidateCount = summary.CandidateCount;
        ReviewedCount = summary.ReviewedCount;
        RemainingCount = Math.Max(0, summary.CandidateCount - summary.ReviewedCount);
        FalsePositiveCount = summary.FalsePositiveCount;
        RelabeledCount = summary.RelabeledAbnormalCount;
        IsReviewComplete = summary.IsComplete;
        SummaryText = $"已复核 {summary.ReviewedCount} / 剩余 {RemainingCount} / 误剔 {summary.FalsePositiveCount} / 修正 {summary.RelabeledAbnormalCount}";
    }

    private void BuildDefectFilters()
    {
        var selectedLabel = SelectedDefectFilter?.Label ?? "全部异常";
        DefectFilters.Clear();
        DefectFilters.Add(new DefectTypeFilterViewModel(
            "全部异常",
            AllCandidates.Count,
            AllCandidates.Count(x => x.IsReviewed),
            "#4BA3FF",
            selectedLabel == "全部异常"));
        foreach (var group in AllCandidates.GroupBy(x => x.ModelLabel).OrderByDescending(x => x.Count()))
        {
            var items = group.ToArray();
            DefectFilters.Add(new DefectTypeFilterViewModel(
                group.Key,
                items.Length,
                items.Count(x => x.IsReviewed),
                ResolveReviewAccentColor(group.Key),
                string.Equals(group.Key, selectedLabel, StringComparison.CurrentCultureIgnoreCase)));
        }

        SelectedDefectFilter = DefectFilters.FirstOrDefault(x => x.IsSelected) ?? DefectFilters.FirstOrDefault();
        if (SelectedDefectFilter is not null)
        {
            SelectedDefectFilter.IsSelected = true;
        }
    }

    private void BuildCorrectionLabels()
    {
        CorrectionLabels.Clear();
        var selectedModelLabel = SelectedCandidate?.ModelLabel ?? string.Empty;
        foreach (var label in AllCandidates
                     .Select(x => x.ModelLabel)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Where(x => !IsNormalLabel(x))
                     .Where(x => !string.Equals(x, selectedModelLabel, StringComparison.CurrentCultureIgnoreCase))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(x => x))
        {
            CorrectionLabels.Add(label);
        }
    }

    private void SelectDefectFilter(DefectTypeFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        foreach (var item in DefectFilters)
        {
            item.IsSelected = ReferenceEquals(item, filter);
        }

        SelectedDefectFilter = filter;
        ApplyFilter(null);
    }

    private void BuildReviewStatusFilters()
    {
        var selectedKey = SelectedReviewStatusFilter?.Key ?? "pending";
        ReviewStatusFilters.Clear();
        ReviewStatusFilters.Add(new ReviewStatusFilterViewModel("pending", "未复核", AllCandidates.Count(x => !x.IsReviewed), selectedKey == "pending"));
        ReviewStatusFilters.Add(new ReviewStatusFilterViewModel("confirmed", "已确认", AllCandidates.Count(IsConfirmedAbnormal), selectedKey == "confirmed"));
        ReviewStatusFilters.Add(new ReviewStatusFilterViewModel("falsePositive", "误检", AllCandidates.Count(IsFalsePositive), selectedKey == "falsePositive"));
        ReviewStatusFilters.Add(new ReviewStatusFilterViewModel("relabeled", "类别修正", AllCandidates.Count(IsRelabeled), selectedKey == "relabeled"));
        SelectedReviewStatusFilter = ReviewStatusFilters.FirstOrDefault(x => x.IsSelected) ?? ReviewStatusFilters.FirstOrDefault();
        if (SelectedReviewStatusFilter is not null)
        {
            SelectedReviewStatusFilter.IsSelected = true;
        }
    }

    private void SelectReviewStatusFilter(ReviewStatusFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        foreach (var item in ReviewStatusFilters)
        {
            item.IsSelected = ReferenceEquals(item, filter);
        }

        SelectedReviewStatusFilter = filter;
        ApplyFilter(null);
    }

    private void ApplyFilter(Guid? preferredDetectionId)
    {
        FilteredCandidates.Clear();
        var label = SelectedDefectFilter?.Label ?? "全部异常";
        var filtered = string.Equals(label, "全部异常", StringComparison.CurrentCultureIgnoreCase)
            ? AllCandidates
            : AllCandidates.Where(x => string.Equals(x.ModelLabel, label, StringComparison.CurrentCultureIgnoreCase));
        filtered = ApplyStatusFilter(filtered);
        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            filtered = filtered.Where(x =>
                x.ImageName.Contains(SearchKeyword, StringComparison.CurrentCultureIgnoreCase) ||
                x.ModelLabel.Contains(SearchKeyword, StringComparison.CurrentCultureIgnoreCase) ||
                x.Confidence.Contains(SearchKeyword, StringComparison.CurrentCultureIgnoreCase));
        }
        foreach (var candidate in filtered)
        {
            FilteredCandidates.Add(candidate);
        }

        SelectedCandidate = preferredDetectionId is Guid id
            ? FilteredCandidates.FirstOrDefault(x => x.DetectionId == id) ?? FilteredCandidates.FirstOrDefault()
            : FilteredCandidates.FirstOrDefault();
    }

    private IEnumerable<ManualReviewCandidateRowViewModel> ApplyStatusFilter(IEnumerable<ManualReviewCandidateRowViewModel> source)
    {
        return SelectedReviewStatusFilter?.Key switch
        {
            "confirmed" => source.Where(IsConfirmedAbnormal),
            "falsePositive" => source.Where(IsFalsePositive),
            "relabeled" => source.Where(IsRelabeled),
            "pending" or null => source.Where(x => !x.IsReviewed),
            _ => source
        };
    }

    private static bool IsFalsePositive(ManualReviewCandidateRowViewModel candidate) =>
        candidate.IsReviewed &&
        (IsNormalLabel(candidate.HumanLabel) ||
         candidate.JudgementText.Contains("误", StringComparison.CurrentCultureIgnoreCase));

    private static bool IsRelabeled(ManualReviewCandidateRowViewModel candidate) =>
        candidate.IsReviewed &&
        !IsFalsePositive(candidate) &&
        !string.Equals(candidate.HumanLabel, candidate.ModelLabel, StringComparison.CurrentCultureIgnoreCase);

    private static bool IsConfirmedAbnormal(ManualReviewCandidateRowViewModel candidate) =>
        candidate.IsReviewed &&
        !IsFalsePositive(candidate) &&
        !IsRelabeled(candidate);

    private void SetHumanLabel(string? label)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            HumanLabel = label;
        }
    }

    private void RaiseSelectedCandidateDerivedProperties()
    {
        RaisePropertyChanged(nameof(SelectedModelLabelText));
        RaisePropertyChanged(nameof(CorrectionDefaultText));
        RaisePropertyChanged(nameof(CanSaveRelabel));
    }

    private static bool IsNormalLabel(string? label) =>
        string.Equals(label, "正常", StringComparison.CurrentCultureIgnoreCase) ||
        string.Equals(label, "normal", StringComparison.CurrentCultureIgnoreCase);

    private static string ResolveReviewAccentColor(string label)
    {
        if (label.Contains("泥", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("muddy", StringComparison.OrdinalIgnoreCase))
        {
            return "#F59E0B";
        }

        if (label.Contains("空", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return "#A78BFA";
        }

        if (label.Contains("碎", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("破", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            return "#10D7A7";
        }

        return "#4BA3FF";
    }

    private async Task ConfirmSelectedAsync()
    {
        if (SelectedCandidate is null)
        {
            StatusMessage = "请先选择一张待复核图片。";
            return;
        }

        await SaveReviewAsync(SelectedCandidate.ModelLabel, "人工确认模型异常判定。");
    }

    private async Task MarkFalsePositiveAsync()
    {
        if (SelectedCandidate is null)
        {
            StatusMessage = "请先选择一张待复核图片。";
            return;
        }

        await SaveReviewAsync("正常", "人工复核为正常，属于误剔。");
    }

    private async Task SaveRelabelAsync()
    {
        if (SelectedCandidate is null)
        {
            StatusMessage = "请先选择一张待复核图片。";
            return;
        }

        if (string.IsNullOrWhiteSpace(HumanLabel) ||
            IsNormalLabel(HumanLabel))
        {
            StatusMessage = "类别修正需要填写一个异常类别；如果该目标其实正常，请使用“正常误检”。";
            return;
        }

        if (string.Equals(HumanLabel, SelectedCandidate.ModelLabel, StringComparison.CurrentCultureIgnoreCase))
        {
            StatusMessage = $"当前仍按模型标签“{SelectedCandidate.ModelLabel}”保存，无需类别修正。";
            return;
        }

        await SaveReviewAsync(HumanLabel, string.IsNullOrWhiteSpace(Notes) ? "人工修正异常类别。" : Notes);
    }

    private Task SaveReviewAsync() => SaveReviewAsync(HumanLabel, Notes);

    private async Task SaveReviewAsync(string humanLabel, string notes)
    {
        if (SelectedCandidate is null)
        {
            StatusMessage = "请先选择一张待复核图片。";
            return;
        }

        try
        {
            var currentDetectionId = SelectedCandidate.DetectionId;
            var nextDetectionId = FindNextUnreviewedDetectionId(currentDetectionId);
            var review = await _apiClient.UpsertManualReviewAsync(
                _session.SessionId,
                new UpsertManualReviewRequest(
                    SelectedCandidate.InspectionRecordId,
                    SelectedCandidate.DetectionId,
                    humanLabel,
                    "operator",
                    notes),
                CancellationToken.None);
            ApplyReview(review, nextDetectionId ?? currentDetectionId);
            StatusMessage = nextDetectionId is null
                ? "人工复核已保存，本筛选范围内暂无下一条未复核对象。"
                : "人工复核已保存，已自动进入下一条未复核对象。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存人工复核失败: {ex.Message}";
            MessageBox.Show(StatusMessage, "人工复核", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BackToDataCenter() =>
        ShellNavigationService.Navigate(new ProductionStatisticsPageViewModel(_returnState), "数据中心", string.Empty);

    private Guid? FindNextUnreviewedDetectionId(Guid currentDetectionId)
    {
        var items = FilteredCandidates.ToList();
        var currentIndex = items.FindIndex(x => x.DetectionId == currentDetectionId);
        if (currentIndex < 0)
        {
            return items.FirstOrDefault(x => !x.IsReviewed)?.DetectionId;
        }

        return items
            .Skip(currentIndex + 1)
            .Concat(items.Take(currentIndex))
            .FirstOrDefault(x => !x.IsReviewed && x.DetectionId != currentDetectionId)
            ?.DetectionId;
    }

    private async Task LoadSelectedPreviewAsync(ManualReviewCandidateRowViewModel candidate, int requestVersion)
    {
        try
        {
            var preview = await _apiClient.GetManualReviewPreviewAsync(
                _session.SessionId,
                candidate.InspectionRecordId,
                candidate.DetectionId,
                CancellationToken.None);
            if (requestVersion != _previewRequestVersion || SelectedCandidate?.DetectionId != candidate.DetectionId)
            {
                return;
            }

            PreviewImagePath = preview.IsAvailable ? preview.ReviewImagePath : string.Empty;
            StatusMessage = preview.IsAvailable
                ? "当前复核图已生成：画面中只保留当前目标的一个红框。"
                : preview.Message;
        }
        catch (Exception ex)
        {
            if (requestVersion != _previewRequestVersion)
            {
                return;
            }

            PreviewImagePath = string.Empty;
            StatusMessage = $"生成复核预览图失败: {ex.Message}";
        }
    }
}

public sealed class DefectTypeFilterViewModel(string label, int count, int reviewedCount, string accentColor, bool isSelected) : ViewModelBase
{
    private bool _isSelected = isSelected;

    public string Label { get; } = label;

    public int Count { get; } = count;

    public int ReviewedCount { get; } = reviewedCount;

    public string AccentColor { get; } = accentColor;

    public string DisplayText => $"{Label}  {Count}";

    public double ProgressWidth => Count <= 0 || ReviewedCount <= 0
        ? 0
        : Math.Min(174, 174.0 * ReviewedCount / Count);

    public string ReviewedText => Count <= 0
        ? "已复核 0 / 0"
        : $"已复核 {ReviewedCount} / {Count}";

    public string ProgressPercentText => Count <= 0
        ? "0%"
        : $"{ReviewedCount * 100.0 / Count:0.#}%";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ReviewStatusFilterViewModel(string key, string label, int count, bool isSelected) : ViewModelBase
{
    private bool _isSelected = isSelected;

    public string Key { get; } = key;

    public string Label { get; } = label;

    public int Count { get; } = count;

    public string DisplayText => $"{Label} {Count}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record ManualReviewCandidateRowViewModel(
    Guid InspectionRecordId,
    Guid DetectionId,
    string ImageName,
    string ImagePath,
    string ReviewImagePath,
    string ModelLabel,
    string Confidence,
    double X,
    double Y,
    double Width,
    double Height,
    string Box,
    bool IsReviewed,
    string HumanLabel,
    string JudgementText,
    string Reviewer,
    string CapturedAt,
    string ReviewedAt,
    string Notes)
{
    public string ReviewState => IsReviewed ? JudgementText : "待复核";

    public string StatusGlyph => IsReviewed ? "✓" : "待";

    public string StatusColor => IsReviewed ? "#10B981" : "#F59E0B";

    public string AiSummary => $"AI判定: {ModelLabel}    置信度: {Confidence}";

    public static ManualReviewCandidateRowViewModel FromDto(ManualReviewCandidateDto candidate) =>
        new(
            candidate.InspectionRecordId,
            candidate.DetectionId,
            string.IsNullOrWhiteSpace(candidate.ImagePath) ? "未知图片" : Path.GetFileName(candidate.ImagePath),
            candidate.ImagePath,
            candidate.ReviewImagePath,
            candidate.ModelLabel,
            candidate.Confidence.ToString("0.000", CultureInfo.InvariantCulture),
            candidate.X,
            candidate.Y,
            candidate.Width,
            candidate.Height,
            $"{candidate.X},{candidate.Y},{candidate.Width},{candidate.Height}",
            candidate.IsReviewed,
            candidate.HumanLabel,
            candidate.JudgementText,
            candidate.Reviewer,
            candidate.CapturedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            candidate.ReviewedAt?.LocalDateTime.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            candidate.Notes);
}

public sealed record QualityRingViewModel(
    string Title,
    string YieldRateText,
    string NormalText,
    string RejectedText,
    string NormalPercentText,
    string RejectedPercentText,
    Geometry NormalArc,
    Geometry RejectedArc)
{
    public static QualityRingViewModel Empty(string title) =>
        new(title, "100%", "正常 0", "剔除 0", "100%", "0%", Geometry.Empty, Geometry.Empty);
}

public sealed record StatisticChartItemViewModel(string Label, int Count, decimal Percent, string Color)
{
    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public string PercentText => $"{Percent:0.##}%";

    public double BarWidth => Math.Max(8, (double)Percent * 2.4);

    public IReadOnlyList<LedSegmentViewModel> LedSegments
    {
        get
        {
            const int segmentCount = 28;
            var activeCount = Math.Clamp((int)Math.Round((double)Percent / 100 * segmentCount), Count > 0 ? 1 : 0, segmentCount);
            return Enumerable.Range(1, segmentCount)
                .Select(index => new LedSegmentViewModel(index <= activeCount, Color))
                .ToList();
        }
    }
}

public sealed record YieldTrendPointViewModel(
    Guid SessionId,
    string Label,
    string YieldRate,
    string TotalCount,
    string RejectCount,
    double X,
    double Y)
{
    public string Summary => $"{Label} · 良率 {YieldRate} · 总量 {TotalCount} · 异常 {RejectCount}";

    public bool IsBelowTarget =>
        decimal.TryParse(
            YieldRate.TrimEnd('%'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var rate) &&
        rate < 80m;

    public string MarkerFill => IsBelowTarget ? "#FF6268" : "#409BFF";

    public string MarkerStroke => IsBelowTarget ? "#FCE7E8" : "#DDEBFA";

    public double MarkerLeft => X - 12;

    public double MarkerTop => Y - 12;

    public double LabelLeft => X - 25;

    public double LabelTop => Y - 30;

    public Geometry MarkerGeometry
    {
        get
        {
            var radius = IsBelowTarget ? 6 : 4;
            return new EllipseGeometry(new Point(X, Y), radius, radius);
        }
    }

    public Geometry HitGeometry => new EllipseGeometry(new Point(X, Y), 12, 12);
}

public sealed record YieldTrendAxisLabelViewModel(string Label, double Left);

public sealed record DeviceRowViewModel(
    Guid DeviceId,
    string DeviceNo,
    string Name,
    DeviceType Type,
    string TypeText,
    string FirmwareVersion,
    DeviceState State,
    string StateText,
    string LastSelfCheckText,
    bool IsEnabled,
    string EnabledText,
    string Notes)
{
    public static DeviceRowViewModel FromDevice(HardwareDevice device) =>
        new(
            device.Id,
            device.DeviceNo,
            device.Name,
            device.Type,
            MapDeviceType(device.Type),
            device.FirmwareVersion,
            device.State,
            MapDeviceState(device.State),
            device.LastSelfCheckAt is null
                ? device.LastSelfCheckResult
                : $"{device.LastSelfCheckResult} · {device.LastSelfCheckAt.Value.LocalDateTime:MM-dd HH:mm}",
            device.IsEnabled,
            device.IsEnabled ? "启用" : "停用",
            device.Notes);

    private static string MapDeviceType(DeviceType type) => type switch
    {
        DeviceType.Conveyor => "传送带",
        DeviceType.XraySource => "X 光光源",
        DeviceType.XrayDetector => "X 光探测器",
        DeviceType.Ejector => "剔除设备",
        DeviceType.Controller => "控制器",
        _ => type.ToString()
    };

    private static string MapDeviceState(DeviceState state) => state switch
    {
        DeviceState.Offline => "离线",
        DeviceState.Idle => "待机",
        DeviceState.Running => "运行中",
        DeviceState.Warning => "预警",
        DeviceState.Faulted => "故障",
        _ => state.ToString()
    };
}

public sealed record AlarmRowViewModel(
    Guid AlarmId,
    string Severity,
    string Source,
    string Code,
    string Message,
    string RaisedAt)
{
    public string SeverityColor => Severity switch
    {
        "严重" => "#E14855",
        "预警" => "#D99124",
        _ => "#4B91C8"
    };

    public static AlarmRowViewModel FromAlarm(AlarmEvent alarm) =>
        new(
            alarm.Id,
            MapSeverity(alarm.Severity),
            alarm.Source,
            alarm.Code,
            alarm.Message,
            alarm.RaisedAt.LocalDateTime.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

    private static string MapSeverity(AlarmSeverity severity) => severity switch
    {
        AlarmSeverity.Info => "信息",
        AlarmSeverity.Warning => "预警",
        AlarmSeverity.Critical => "严重",
        _ => severity.ToString()
    };
}

public sealed record DeviceStatusRowViewModel(
    string DeviceKey,
    string State,
    string Message,
    string UpdatedAt,
    string Color)
{
    public static DeviceStatusRowViewModel FromStatus(DeviceStatus status) =>
        new(
            status.DeviceKey,
            MapDeviceState(status.State),
            status.Message,
            status.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            MapDeviceColor(status.State));

    private static string MapDeviceState(DeviceState state) => state switch
    {
        DeviceState.Offline => "离线",
        DeviceState.Idle => "待机",
        DeviceState.Running => "运行中",
        DeviceState.Warning => "预警",
        DeviceState.Faulted => "故障",
        _ => state.ToString()
    };

    private static string MapDeviceColor(DeviceState state) => state switch
    {
        DeviceState.Running or DeviceState.Idle => "#10B981",
        DeviceState.Warning => "#F59E0B",
        DeviceState.Offline or DeviceState.Faulted => "#EF4444",
        _ => "#94A3B8"
    };
}

public sealed record SoftwareUpdateRecordRowViewModel(
    string Version,
    string InstalledAt,
    string Result,
    string Notes)
{
    public string UpdateMethod => Notes.Contains("网络", StringComparison.OrdinalIgnoreCase)
        ? "网络更新"
        : "本地维护";

    public string Operator => "系统";

    public static SoftwareUpdateRecordRowViewModel FromRecord(SoftwareUpdateRecord record) =>
        new(
            record.Version,
            record.InstalledAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            record.Result,
            record.Notes);
}

public sealed record ManualDetectionItemViewModel(
    string Label,
    string ConfidenceText,
    string BoundingBoxText)
{
    public string Color => ResolveColor(Label);

    public string DisplayTitle => Label;

    public string DisplayConfidence => $"置信度 {ConfidenceText}";

    public string OverlayText => $"{Label} {ConfidenceText}";

    public double BoxLeft => ParseBoxValue(0);

    public double BoxTop => ParseBoxValue(1);

    public double BoxWidth => ParseBoxValue(2);

    public double BoxHeight => ParseBoxValue(3);

    private double ParseBoxValue(int index)
    {
        var parts = BoundingBoxText.Split(',', StringSplitOptions.TrimEntries);
        if (index >= parts.Length)
        {
            return 0;
        }

        return double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string ResolveColor(string label)
    {
        if (label.Contains("正常", StringComparison.OrdinalIgnoreCase) || label.Contains("normal", StringComparison.OrdinalIgnoreCase))
        {
            return "#10B981";
        }

        if (label.Contains("碎", StringComparison.OrdinalIgnoreCase) || label.Contains("破", StringComparison.OrdinalIgnoreCase) || label.Contains("broken", StringComparison.OrdinalIgnoreCase))
        {
            return "#3B82F6";
        }

        if (label.Contains("泥", StringComparison.OrdinalIgnoreCase) || label.Contains("muddy", StringComparison.OrdinalIgnoreCase))
        {
            return "#F59E0B";
        }

        if (label.Contains("空", StringComparison.OrdinalIgnoreCase) || label.Contains("empty", StringComparison.OrdinalIgnoreCase))
        {
            return "#94A3B8";
        }

        return "#60A5FA";
    }
}

public sealed record ManualEjectCommandItemViewModel(
    string DefectLabel,
    string ActionText,
    string NozzleText,
    string DelayText);

public sealed record TraitCountItemViewModel(
    string Label,
    int Count,
    string? Tone = null,
    string PercentText = "0%",
    double BarWidth = 6,
    IReadOnlyList<LedSegmentViewModel>? Segments = null)
{
    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public string Color => string.IsNullOrWhiteSpace(Tone) ? "#60A5FA" : Tone;

    public string IconGlyph
    {
        get
        {
            if (Label.Contains("正常", StringComparison.OrdinalIgnoreCase) ||
                Label.Contains("normal", StringComparison.OrdinalIgnoreCase))
            {
                return "\u2713";
            }

            if (Label.Contains("碎", StringComparison.OrdinalIgnoreCase) ||
                Label.Contains("破", StringComparison.OrdinalIgnoreCase) ||
                Label.Contains("broken", StringComparison.OrdinalIgnoreCase))
            {
                return "\u26A1";
            }

            if (Label.Contains("泥", StringComparison.OrdinalIgnoreCase) ||
                Label.Contains("muddy", StringComparison.OrdinalIgnoreCase))
            {
                return "\u25A6";
            }

            if (Label.Contains("空", StringComparison.OrdinalIgnoreCase) ||
                Label.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                return "\u25EF";
            }

            return "\u25A3";
        }
    }

    public string GlowBackground => Count > 0 ? Color : "#102B44";

    public double GlowOpacity => Count > 0 ? 0.22 : 0.08;

    public IReadOnlyList<LedSegmentViewModel> LedSegments => Segments ?? [];
}

public sealed record LedSegmentViewModel(bool IsActive, string Color)
{
    public string Fill => IsActive ? Color : "#20384F";

    public double Opacity => IsActive ? 1.0 : 0.55;
}

public sealed record ProductRowViewModel(
    Guid ProductId,
    string Name,
    string Code,
    string NormalLabel,
    string DefectTraitsDisplay,
    int TraitCount,
    string PredictConfigPath,
    string ClassesFilePath = "")
{
    public IReadOnlyList<string> DefectTraits => DefectTraitsDisplay
        .Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record ProductLabelMappingRowViewModel(
    int Index,
    string ModelLabel,
    string ProductTrait);

public sealed record ProductOptionViewModel(
    Guid Id,
    string Name,
    string CategoryName,
    string NormalLabel,
    string DefectTraitsSummary)
{
    public override string ToString() => Name;
}

public sealed record ModelImportProductOptionViewModel(
    Guid Id,
    string Name,
    string CategoryName,
    IReadOnlyList<string> AllTraits,
    IReadOnlyList<string> DefectTraits,
    string ClassesFilePath,
    string LabelMapJson)
{
    public string TraitSummary => string.Join(" / ", AllTraits);

    public override string ToString() => Name;
}

public sealed class ModelTraitMappingRowViewModel(int classIndex, string modelLabel, IReadOnlyList<string> traitOptions) : ViewModelBase
{
    private string? _selectedTraitName;

    public int ClassIndex { get; } = classIndex;

    public string ClassIndexText => classIndex.ToString(CultureInfo.InvariantCulture);

    public string ModelLabel { get; } = modelLabel;

    public IReadOnlyList<string> TraitOptions { get; } = traitOptions;

    public string? SelectedTraitName
    {
        get => _selectedTraitName;
        set => SetProperty(ref _selectedTraitName, value);
    }
}

public sealed record ModelOptionViewModel(
    Guid Id,
    string Version,
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
    bool IsActive,
    string RunningStatus,
    string? LastRuntimePredictConfigPath,
    string? LastRuntimeOutputDirectory);

public sealed record ProductFilterOptionViewModel(string ProductName, string Label)
{
    public override string ToString() => Label;
}

public sealed record ActiveChannelOptionViewModel(Guid ChannelId, string Name)
{
    public override string ToString() => Name;
}

public sealed record RuntimeChannelOptionViewModel(
    Guid ChannelId,
    int ChannelNo,
    string Name,
    string ProductName,
    string ModelVersion,
    string DefectSummary,
    bool IsCurrent)
{
    public string DisplayText => $"{Name}  ·  {ProductName}  ·  {ModelVersion}";

    public IReadOnlyList<string> DefectTypes => string.IsNullOrWhiteSpace(DefectSummary)
        ? []
        : DefectSummary.Split(
            ['/', '、', ',', '，'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public override string ToString() => Name;
}

public sealed record SeafoodCategoryOptionViewModel(Guid Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record ModelRowViewModel(
    Guid ModelId,
    string CategoryName,
    string Version,
    string BoundChannelNames,
    string Description,
    string WeightFilePath,
    string WeightFileName,
    int TrainingImageSize = 0)
{
    public int BoundChannelCount => string.IsNullOrWhiteSpace(BoundChannelNames)
        ? 0
        : BoundChannelNames.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    public string BindingStatusText => BoundChannelCount == 0 ? "未绑定" : "已绑定";

    public string BoundChannelCountText => BoundChannelCount == 0 ? string.Empty : $"{BoundChannelCount} 个通道";

    public string UsageSummary => BoundChannelCount == 0
        ? "未绑定"
        : $"已绑定 · {BoundChannelCount} 个通道";
}

public sealed record ModelBoundChannelRowViewModel(
    string ChannelName,
    string Status);

public static class ModelCatalogState
{
    private static readonly List<ModelRowViewModel> SeedModels =
    [
        new(Guid.Parse("8fbd7e7a-0ec8-4cd1-9ad8-93148cfdd111"), "花蛤", "HG-MV-001", "2号通道", "花蛤标准缺陷模型", @"E:\Models\HG\HG-MV-001.pt", "HG-MV-001.pt"),
        new(Guid.Parse("5b145f39-c338-452e-a2a2-4fd20f275222"), "花蛤", "HG-MV-002", string.Empty, "花蛤复检模型", @"E:\Models\HG\HG-MV-002.onnx", "HG-MV-002.onnx"),
        new(Guid.Parse("95d2d4a4-7074-4e38-b5b6-4e3fd8ef3333"), "油蛤", "YG-MV-001", "1号通道", "油蛤常规检测模型", @"E:\Models\YG\YG-MV-001.pt", "YG-MV-001.pt"),
        new(Guid.Parse("4fd3317d-b0ab-45cf-a669-6f0dc07d4444"), "美贝", "MB-MV-001", string.Empty, "美贝备用检测模型", @"E:\Models\MB\MB-MV-001.onnx", "MB-MV-001.onnx")
    ];

    private static readonly List<ModelRowViewModel> Models = SeedModels.ToList();

    public static IReadOnlyList<ModelRowViewModel> GetAll() => Models.ToList();

    public static void AddOrReplace(ModelRowViewModel model)
    {
        var existing = Models.FindIndex(x => x.ModelId == model.ModelId || string.Equals(x.Version, model.Version, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            Models[existing] = model;
        }
        else
        {
            Models.Add(model);
        }
    }

    public static void ReplaceAll(IEnumerable<ModelRowViewModel> models)
    {
        Models.Clear();
        Models.AddRange(models);
    }

    public static ModelRowViewModel? GetByVersion(string version) =>
        Models.FirstOrDefault(x => string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase));

    public static void RemoveByVersion(string version) =>
        Models.RemoveAll(x => string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase));

    public static void ApplyChannelBindings(IEnumerable<ChannelRowViewModel> channels)
    {
        var channelGroups = channels
            .Where(x => !string.IsNullOrWhiteSpace(x.ModelVersion) && x.ModelVersion != "未选择")
            .GroupBy(x => x.ModelVersion, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Names = string.Join("、", group.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                },
                StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < Models.Count; i++)
        {
            var current = Models[i];
            if (channelGroups.TryGetValue(current.Version, out var binding))
            {
                Models[i] = current with
                {
                    BoundChannelNames = binding.Names
                };
            }
            else
            {
                Models[i] = current with
                {
                    BoundChannelNames = string.Empty
                };
            }
        }
    }

    public static void ResetForTests()
    {
        Models.Clear();
        Models.AddRange(SeedModels);
    }
}
