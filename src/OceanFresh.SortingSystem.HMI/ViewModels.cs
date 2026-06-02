using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OceanFresh.SortingSystem.Domain;
using System.Windows.Input;
using System.Windows;
using OceanFresh.SortingSystem.HMI.Pages;

namespace OceanFresh.SortingSystem.HMI;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        RaisePropertyChanged(propertyName);
    }
}

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}

public sealed class AsyncRelayCommand(Func<Task> executeAsync) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
        {
            return;
        }

        try
        {
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            await executeAsync();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private object _currentPage;
    private string _headerTitle;
    private string _headerSubtitle;
    private bool _isAuthenticated;
    private UserRole? _currentRole;
    private string _currentUserDisplay;
    private bool _isFeedbackPanelOpen;
    private string _draftFeedbackComment = string.Empty;
    private string _selectedFeedbackTargetCode = string.Empty;
    private string _feedbackStatusMessage = "打开批注模式后，可以直接把意见记到当前页面。";
    private string _feedbackHint = string.Empty;

    public MainWindowViewModel()
    {
        ShellNavigationService.NavigateRequested = OpenPage;
        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new("首页总览", null),
            new("检测监控", null),
            new("海鲜产品", null),
            new("通道配置", null),
            new("模型管理", UserRole.Administrator),
            new("系统设置", UserRole.Administrator)
        };

        NavigateCommand = new RelayCommand(Navigate);
        LoginAsOperatorCommand = new RelayCommand(_ => Login(UserRole.Operator));
        LoginAsAdminCommand = new RelayCommand(_ => Login(UserRole.Administrator));
        LogoutCommand = new RelayCommand(_ => Logout());
        ToggleFeedbackPanelCommand = new RelayCommand(_ => IsFeedbackPanelOpen = !IsFeedbackPanelOpen);
        SaveFeedbackCommand = new RelayCommand(_ => SaveFeedback());
        SelectFeedbackTargetCommand = new RelayCommand(SelectFeedbackTarget);
        _currentPage = new DashboardPageViewModel();
        _headerTitle = "首页总览";
        _headerSubtitle = "单机闭环、模型多版本切换、实时状态总览";
        _currentUserDisplay = "未登录";
        LoadFeedbackEntries();
        UpdateFeedbackTargets(_headerTitle);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public ObservableCollection<FeedbackTargetViewModel> CurrentFeedbackTargets { get; } = [];

    public ObservableCollection<FeedbackEntryViewModel> RecentFeedbackEntries { get; } = [];

    public ICommand NavigateCommand { get; }

    public ICommand LoginAsOperatorCommand { get; }

    public ICommand LoginAsAdminCommand { get; }

    public ICommand LogoutCommand { get; }

    public ICommand ToggleFeedbackPanelCommand { get; }

    public ICommand SaveFeedbackCommand { get; }

    public ICommand SelectFeedbackTargetCommand { get; }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => SetProperty(ref _isAuthenticated, value);
    }

    public string CurrentUserDisplay
    {
        get => _currentUserDisplay;
        private set => SetProperty(ref _currentUserDisplay, value);
    }

    public bool IsFeedbackPanelOpen
    {
        get => _isFeedbackPanelOpen;
        set => SetProperty(ref _isFeedbackPanelOpen, value);
    }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string HeaderTitle
    {
        get => _headerTitle;
        private set => SetProperty(ref _headerTitle, value);
    }

    public string HeaderSubtitle
    {
        get => _headerSubtitle;
        private set => SetProperty(ref _headerSubtitle, value);
    }

    public string FeedbackHint
    {
        get => _feedbackHint;
        private set => SetProperty(ref _feedbackHint, value);
    }

    public string DraftFeedbackComment
    {
        get => _draftFeedbackComment;
        set => SetProperty(ref _draftFeedbackComment, value);
    }

    public string SelectedFeedbackTargetCode
    {
        get => _selectedFeedbackTargetCode;
        set => SetProperty(ref _selectedFeedbackTargetCode, value);
    }

    public string FeedbackStatusMessage
    {
        get => _feedbackStatusMessage;
        private set => SetProperty(ref _feedbackStatusMessage, value);
    }

    private void OpenPage(object pageViewModel, string title, string subtitle)
    {
        try
        {
            CurrentPage = pageViewModel;
            HeaderTitle = title;
            HeaderSubtitle = subtitle;
            UpdateFeedbackTargets(title);

            if (pageViewModel is IActivatablePageViewModel activatablePageViewModel)
            {
                _ = activatablePageViewModel.ActivateAsync();
            }
        }
        catch (Exception ex)
        {
            HeaderTitle = "页面打开失败";
            HeaderSubtitle = ex.Message;
            CurrentPage = new DashboardPageViewModel();
        }
    }

    private void Login(UserRole role)
    {
        _currentRole = role;
        IsAuthenticated = true;
        CurrentUserDisplay = role == UserRole.Administrator ? "管理员 admin" : "操作员 operator";
        UpdateNavigationVisibility();

        var defaultPage = role == UserRole.Administrator ? "模型管理" : "检测监控";
        Navigate(defaultPage);
    }

    private void Logout()
    {
        _currentRole = null;
        IsAuthenticated = false;
        CurrentUserDisplay = "未登录";
        foreach (var item in NavigationItems)
        {
            item.IsVisible = true;
        }

        CurrentPage = new DashboardPageViewModel();
        HeaderTitle = "首页总览";
        HeaderSubtitle = "单机闭环、模型多版本切换、实时状态总览";
        IsFeedbackPanelOpen = false;
        UpdateFeedbackTargets(HeaderTitle);
    }

    private void UpdateNavigationVisibility()
    {
        foreach (var item in NavigationItems)
        {
            item.IsVisible = item.RequiredRole is null || item.RequiredRole == _currentRole;
        }
    }

    private void Navigate(object? parameter)
    {
        var target = parameter switch
        {
            NavigationItemViewModel item => item.Title,
            _ => parameter?.ToString()
        };
        switch (target)
        {
            case "检测监控":
                OpenPage(new MonitoringPageViewModel(), "检测监控", "查看实时图像、识别结果、异常类别和设备状态");
                break;
            case "海鲜产品":
                OpenPage(new RecipesPageViewModel(), "海鲜产品", "只维护海鲜名称与性状类别，不在这里配置通道和运行参数");
                break;
            case "通道配置":
                OpenPage(new ChannelConfigsPageViewModel(), "通道配置", "每个通道一次只能选择一种海鲜，再绑定模型和阈值");
                break;
            case "模型管理":
                OpenPage(new ModelManagementPageViewModel(), "模型管理", "按海鲜类别管理多版本模型、启用与回滚");
                break;
            case "系统设置":
                OpenPage(new SettingsPageViewModel(), "系统设置", "角色权限、日志策略、告警联锁和设备基础配置");
                break;
            default:
                OpenPage(new DashboardPageViewModel(), "首页总览", "单机闭环、模型多版本切换、实时状态总览");
                break;
        }
    }

    private void UpdateFeedbackTargets(string title)
    {
        CurrentFeedbackTargets.Clear();

        foreach (var item in BuildFeedbackTargets(title))
        {
            CurrentFeedbackTargets.Add(item);
        }

        SelectedFeedbackTargetCode = CurrentFeedbackTargets.FirstOrDefault()?.Code ?? string.Empty;
        FeedbackHint = CurrentFeedbackTargets.Count == 0
            ? "当前页面还没有评审编号。"
            : "评审编号: " + string.Join("  ", CurrentFeedbackTargets.Select(x => $"{x.Code} {x.Label}"));
    }

    private static IReadOnlyList<FeedbackTargetViewModel> BuildFeedbackTargets(string title)
    {
        if (title.StartsWith("编辑海鲜产品", StringComparison.Ordinal))
        {
            return
            [
                new("PE1", "顶部与返回"),
                new("PE2", "基础信息"),
                new("PE3", "缺陷新增"),
                new("PE4", "缺陷列表"),
                new("PE5", "底部操作")
            ];
        }

        if (title.StartsWith("编辑通道", StringComparison.Ordinal))
        {
            return
            [
                new("CE1", "顶部与返回"),
                new("CE2", "基础信息"),
                new("CE3", "产品与模型"),
                new("CE4", "运行参数"),
                new("CE5", "底部操作")
            ];
        }

        if (title.StartsWith("通道详情", StringComparison.Ordinal))
        {
            return
            [
                new("CD1", "顶部与返回"),
                new("CD2", "通道状态"),
                new("CD3", "详情信息"),
                new("CD4", "启停与编辑")
            ];
        }

        if (title == "导入新模型")
        {
            return
            [
                new("MI1", "顶部与返回"),
                new("MI2", "导入类别"),
                new("MI3", "版本与描述"),
                new("MI4", "保存操作")
            ];
        }

        return title switch
        {
            "海鲜产品" =>
            [
                new("P1", "顶部操作"),
                new("P2", "状态提示"),
                new("P3", "产品列表"),
                new("P4", "底部操作")
            ],
            "通道配置" =>
            [
                new("C1", "顶部操作"),
                new("C2", "状态条"),
                new("C3", "通道列表"),
                new("C4", "底部操作")
            ],
            "首页总览" =>
            [
                new("D1", "总览指标"),
                new("D2", "设备状态")
            ],
            "检测监控" =>
            [
                new("M1", "检测结果"),
                new("M2", "设备状态")
            ],
            "模型管理" =>
            [
                new("MM1", "模型列表")
            ],
            "系统设置" =>
            [
                new("S1", "设置区")
            ],
            _ => []
        };
    }

    private void SelectFeedbackTarget(object? parameter)
    {
        if (parameter is FeedbackTargetViewModel target)
        {
            SelectedFeedbackTargetCode = target.Code;
            FeedbackStatusMessage = $"当前批注区块已切到 {target.Code} {target.Label}";
        }
    }

    private void SaveFeedback()
    {
        if (string.IsNullOrWhiteSpace(SelectedFeedbackTargetCode))
        {
            FeedbackStatusMessage = "请先选择一个区块编号。";
            return;
        }

        if (string.IsNullOrWhiteSpace(DraftFeedbackComment))
        {
            FeedbackStatusMessage = "请先写下你的意见。";
            return;
        }

        var target = CurrentFeedbackTargets.FirstOrDefault(x => x.Code == SelectedFeedbackTargetCode);
        var entry = new FeedbackEntryViewModel(
            DateTime.Now,
            HeaderTitle,
            SelectedFeedbackTargetCode,
            target?.Label ?? string.Empty,
            DraftFeedbackComment.Trim());

        RecentFeedbackEntries.Insert(0, entry);
        while (RecentFeedbackEntries.Count > 12)
        {
            RecentFeedbackEntries.RemoveAt(RecentFeedbackEntries.Count - 1);
        }

        PersistFeedbackEntry(entry);
        DraftFeedbackComment = string.Empty;
        FeedbackStatusMessage = $"已记录批注: {entry.TargetCode} {entry.TargetLabel}";
    }

    private void LoadFeedbackEntries()
    {
        try
        {
            var path = GetFeedbackLogPath();
            if (!File.Exists(path))
            {
                return;
            }

            var entries = File.ReadAllLines(path, Encoding.UTF8)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<FeedbackEntryViewModel>(line))
                .Where(x => x is not null)
                .Cast<FeedbackEntryViewModel>()
                .OrderByDescending(x => x.CreatedAt)
                .Take(12);

            foreach (var entry in entries)
            {
                RecentFeedbackEntries.Add(entry);
            }
        }
        catch
        {
            FeedbackStatusMessage = "批注历史读取失败，但不影响继续使用。";
        }
    }

    private static void PersistFeedbackEntry(FeedbackEntryViewModel entry)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "feedback");
        Directory.CreateDirectory(directory);
        var path = GetFeedbackLogPath();
        var json = JsonSerializer.Serialize(entry);
        File.AppendAllText(path, json + Environment.NewLine, Encoding.UTF8);
    }

    private static string GetFeedbackLogPath() =>
        Path.Combine(AppContext.BaseDirectory, "feedback", "review-comments.jsonl");
}

public interface IActivatablePageViewModel
{
    Task ActivateAsync();
}

public sealed class NavigationItemViewModel(string title, UserRole? requiredRole) : ViewModelBase
{
    private bool _isVisible = true;

    public string Title { get; } = title;

    public UserRole? RequiredRole { get; } = requiredRole;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public override string ToString() => Title;
}

public sealed record FeedbackTargetViewModel(string Code, string Label);

public sealed record FeedbackEntryViewModel(
    DateTime CreatedAt,
    string PageTitle,
    string TargetCode,
    string TargetLabel,
    string Comment);
