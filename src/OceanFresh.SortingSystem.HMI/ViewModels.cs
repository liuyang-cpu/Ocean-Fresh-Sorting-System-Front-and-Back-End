using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OceanFresh.SortingSystem.Application;
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
    private readonly OceanFreshLocalApiClient _apiClient = new();
    private object _currentPage;
    private string _headerTitle;
    private string _headerSubtitle;
    private bool _isAuthenticated;
    private UserRole? _currentRole;
    private string _currentUserDisplay;
    private DateTimeOffset? _currentLoginAt;
    private string _adminPassword = string.Empty;
    private string _loginStatusMessage = string.Empty;
    private bool _isFeedbackPanelOpen;
    private string _draftFeedbackComment = string.Empty;
    private string _selectedFeedbackTargetCode = string.Empty;
    private string _feedbackStatusMessage = "打开批注模式后，可以直接把意见记到当前页面。";
    private string _feedbackHint = string.Empty;
    private bool _isSettingsWorkspace;
    private bool _isUserWorkspace;

    public MainWindowViewModel()
    {
        ShellNavigationService.NavigateRequested = OpenPage;
        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new("首页", "\uE80F", null),
            new("用户", "\uE77B", null),
            new("数据", "\uE9D2", null),
            new("设置", "\uE713", UserRole.Administrator)
        };

        NavigateCommand = new RelayCommand(Navigate);
        LoginAsOperatorCommand = new AsyncRelayCommand(LoginAsOperatorAsync);
        LoginAsAdminCommand = new AsyncRelayCommand(LoginAsAdminAsync);
        LogoutCommand = new RelayCommand(_ => Logout());
        ToggleFeedbackPanelCommand = new RelayCommand(_ => IsFeedbackPanelOpen = !IsFeedbackPanelOpen);
        SaveFeedbackCommand = new RelayCommand(_ => SaveFeedback());
        SelectFeedbackTargetCommand = new RelayCommand(SelectFeedbackTarget);
        _currentPage = new MonitoringPageViewModel();
        _headerTitle = "首页";
        _headerSubtitle = string.Empty;
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

    public string AdminPassword
    {
        get => _adminPassword;
        set => SetProperty(ref _adminPassword, value);
    }

    public string LoginStatusMessage
    {
        get => _loginStatusMessage;
        private set => SetProperty(ref _loginStatusMessage, value);
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

    public bool IsSettingsWorkspace
    {
        get => _isSettingsWorkspace;
        private set => SetProperty(ref _isSettingsWorkspace, value);
    }

    public bool IsUserWorkspace
    {
        get => _isUserWorkspace;
        private set => SetProperty(ref _isUserWorkspace, value);
    }

    private void OpenPage(object pageViewModel, string title, string subtitle)
    {
        try
        {
            if (!ReferenceEquals(CurrentPage, pageViewModel) && CurrentPage is IDisposable disposablePage)
            {
                disposablePage.Dispose();
            }

            CurrentPage = pageViewModel;
            HeaderTitle = title;
            HeaderSubtitle = subtitle;
            IsSettingsWorkspace = pageViewModel is SettingsPageViewModel
                or ProductEditorPageViewModel
                or ProductDetailPageViewModel
                or ModelImportPageViewModel
                or ModelDetailPageViewModel
                or ChannelEditorPageViewModel
                or ChannelDetailPageViewModel;
            IsUserWorkspace = pageViewModel is UserCenterPageViewModel;
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
            IsSettingsWorkspace = false;
            IsUserWorkspace = false;
            CurrentPage = new MonitoringPageViewModel();
        }
    }

    private async Task LoginAsOperatorAsync()
    {
        var result = await _apiClient.LoginOperatorAsync(CancellationToken.None);
        Login(result);
    }

    private async Task LoginAsAdminAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(AdminPassword))
            {
                LoginStatusMessage = "请输入管理员密码。";
                return;
            }

            var result = await _apiClient.LoginAdminAsync(AdminPassword, CancellationToken.None);
            AdminPassword = string.Empty;
            Login(result);
        }
        catch (Exception ex)
        {
            LoginStatusMessage = ex.Message;
        }
    }

    private void Login(LoginResultDto result)
    {
        _currentRole = result.Role;
        _currentLoginAt = result.LoginAt;
        IsAuthenticated = true;
        CurrentUserDisplay = $"{result.DisplayName} {result.UserName}";
        LoginStatusMessage = string.Empty;
        UpdateNavigationVisibility();

        var defaultPage = result.Role == UserRole.Administrator ? "设置" : "首页";
        Navigate(defaultPage);
    }

    private void Logout()
    {
        OceanFreshLocalApiClient.ClearSession();
        if (CurrentPage is IDisposable disposablePage)
        {
            disposablePage.Dispose();
        }

        _currentRole = null;
        _currentLoginAt = null;
        IsAuthenticated = false;
        CurrentUserDisplay = "未登录";
        AdminPassword = string.Empty;
        foreach (var item in NavigationItems)
        {
            item.IsVisible = true;
        }

        CurrentPage = new MonitoringPageViewModel();
        HeaderTitle = "首页";
        HeaderSubtitle = string.Empty;
        IsSettingsWorkspace = false;
        IsUserWorkspace = false;
        IsFeedbackPanelOpen = false;
        LoginStatusMessage = string.Empty;
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
        foreach (var item in NavigationItems)
        {
            item.IsSelected = string.Equals(item.Title, target, StringComparison.OrdinalIgnoreCase);
        }

        switch (target)
        {
            case "数据":
                OpenPage(new ProductionStatisticsPageViewModel(), "数据中心", string.Empty);
                break;
            case "用户":
                OpenPage(
                    new UserCenterPageViewModel(
                        CurrentUserDisplay,
                        _currentRole ?? UserRole.Operator,
                        _currentLoginAt ?? DateTimeOffset.Now),
                    "用户",
                    string.Empty);
                break;
            case "设置":
                OpenPage(new SettingsPageViewModel(), "设置", string.Empty);
                break;
            default:
                OpenPage(new MonitoringPageViewModel(), "首页", string.Empty);
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

        if (title.StartsWith("产品详情", StringComparison.Ordinal))
        {
            return
            [
                new("PD1", "顶部与返回"),
                new("PD2", "基础信息"),
                new("PD3", "缺陷信息"),
                new("PD4", "编辑与删除")
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

        if (title.StartsWith("模型详情", StringComparison.Ordinal))
        {
            return
            [
                new("MD1", "顶部与返回"),
                new("MD2", "基础状态"),
                new("MD3", "权重文件"),
                new("MD4", "模型描述")
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
            "首页" =>
            [
                new("H1", "食品检测"),
                new("H2", "状态记录")
            ],
            "数据中心" =>
            [
                new("R1", "历史统计"),
                new("R2", "检测任务记录"),
                new("R3", "报表与追溯")
            ],
            "用户" =>
            [
                new("U1", "当前身份"),
                new("U2", "操作提示")
            ],
            "设置" =>
            [
                new("S1", "顶部设置页签"),
                new("S2", "模块设置区")
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

public sealed class NavigationItemViewModel(string title, string icon, UserRole? requiredRole) : ViewModelBase
{
    private bool _isVisible = true;
    private bool _isSelected;

    public string Title { get; } = title;

    public string Icon { get; } = icon;

    public string IconSource => Title switch
    {
        "首页" => "/Assets/Figma/nav-home.png",
        "用户" => "/Assets/Figma/nav-user.png",
        "数据" => "/Assets/Figma/nav-data.png",
        "设置" => "/Assets/Figma/nav-user.png",
        _ => "/Assets/Figma/nav-home.png"
    };

    public UserRole? RequiredRole { get; } = requiredRole;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
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
