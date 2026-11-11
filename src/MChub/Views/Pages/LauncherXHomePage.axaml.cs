using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MChub.Core.Classes;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Localization;
using MChub.Module;
using Tio.Avalonia.Standard.Tab.Entries;
using MChub.Views.Widgets;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace MChub.Views.Pages;

/// <summary>
/// LauncherX 风格首页：沉浸背景上的超大版本标题 + 右下账户卡片。
/// 数据取自最近游玩的实例与当前账户。
/// </summary>
public partial class LauncherXHomePage : UserControl, ITioTabPage
{
    private readonly LauncherXHomeViewModel _viewModel = new();

    public LauncherXHomePage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, _) => ApplyVisibility();
        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        InstanceManager.Instance.StatisticsChanged += OnInstancesChanged;
        Data.ConfigEntry.PropertyChanged += OnConfigChanged;
        LaunchTarget.Changed += OnLaunchTargetChanged;
        Loaded += (_, _) =>
        {
            _viewModel.Refresh();
            ApplyVisibility();
        };
    }

    /// <summary>
    /// 直接同步标题区/空状态的可见性。这两个容器同时挂着绑定，
    /// 但首页在实例扫描完成前会被创建，绑定通知在极端时序下会滞后，这里兜一层。
    /// </summary>
    private void ApplyVisibility()
    {
        TitlePanel.IsVisible = _viewModel.HasInstance;
        EmptyPanel.IsVisible = _viewModel.ShowEmptyState;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "MChub",
        IconGlyph = "\ue619",
        IconFont = IconResources.FontFamilyName,
        IsCloseable = false
    };

    public TabEntry HostTab { get; set; } = null!;

    public void OnClose()
    {
        InstanceManager.Instance.InstancesChanged -= OnInstancesChanged;
        InstanceManager.Instance.StatisticsChanged -= OnInstancesChanged;
        Data.ConfigEntry.PropertyChanged -= OnConfigChanged;
        LaunchTarget.Changed -= OnLaunchTargetChanged;
    }

    private void OnLaunchTargetChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.Refresh();
            ApplyVisibility();
        });
    }

    /// <summary>点左下大标题：选一个实例作为启动目标。</summary>
    private async void PickTarget_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var result = await OverlayDialog.ShowCustomAsync<InstancePickerDialog, InstancePickerDialogViewModel, object?>(
            new InstancePickerDialogViewModel(), this.TryGetHostId(), new OverlayDialogOptions
            {
                Buttons = DialogButton.None,
                CanLightDismiss = true,
                CanDragMove = true,
                CanResize = true,
                IsCloseButtonVisible = true
            });

        if (result is not MinecraftInstance instance) return;

        LaunchTarget.Set(instance);
        _viewModel.Refresh();
        ApplyVisibility();
    }

    /// <summary>点右下账户卡：跳到「设置 → 账户」去切换账户。</summary>
    private void AccountCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (TopLevel.GetTopLevel(this) is TabWindow window)
            window.OpenAccountSettings();
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(_viewModel.Refresh);
    }

    private void OnConfigChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Data.ConfigEntry.UsingMinecraftMinecraftAccount))
            Dispatcher.UIThread.Post(_viewModel.Refresh);
    }
}

public partial class LauncherXHomeViewModel : ObservableObject
{
    private MinecraftInstance? _instance;

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;

    [ObservableProperty] public partial string VersionText { get; set; } = string.Empty;

    [ObservableProperty] public partial string LoaderText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasLoader { get; set; }

    [ObservableProperty] public partial string LastPlayedText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasLastPlayed { get; set; }

    private bool _hasInstance;

    public bool HasInstance
    {
        get => _hasInstance;
        private set => SetProperty(ref _hasInstance, value);
    }

    private bool _showEmptyState;

    /// <summary>空状态：等实例扫描落定后再显示，避免启动瞬间闪一下"还没有游戏"。</summary>
    public bool ShowEmptyState
    {
        get => _showEmptyState;
        private set => SetProperty(ref _showEmptyState, value);
    }

    [ObservableProperty] public partial string AccountName { get; set; } = string.Empty;

    [ObservableProperty] public partial string AccountTypeText { get; set; } = string.Empty;

    [ObservableProperty] public partial Bitmap? AccountHead { get; set; }

    private bool _emptyStateSettled;

    public void Refresh()
    {
        _instance = LaunchTarget.Current;

        HasInstance = _instance is not null;

        if (_instance is null && !_emptyStateSettled)
        {
            // 首次刷新可能赶在实例扫描完成前，稍后再判一次，避免误导性的空状态。
            ShowEmptyState = false;
            DispatcherTimer.RunOnce(() =>
            {
                _emptyStateSettled = true;
                Refresh();
            }, TimeSpan.FromMilliseconds(1200));
        }
        else
        {
            _emptyStateSettled = true;
            ShowEmptyState = _instance is null;
        }
        if (_instance is not null)
        {
            Title = _instance.InstanceName;
            VersionText = _instance.VersionId;
            LoaderText = _instance.LoaderDescription;
            HasLoader = !string.IsNullOrWhiteSpace(LoaderText);
            LastPlayedText = _instance.DisplayLastPlayTime;
            HasLastPlayed = !string.IsNullOrWhiteSpace(LastPlayedText);
        }
        else
        {
            Title = string.Empty;
            VersionText = string.Empty;
            LoaderText = string.Empty;
            HasLoader = false;
            LastPlayedText = string.Empty;
            HasLastPlayed = false;
        }

        var account = Data.ConfigEntry.UsingMinecraftMinecraftAccount;        AccountName = account?.Name ?? "未登录";
        AccountTypeText = account switch
        {
            { AccountType: AccountType.Microsoft } => "Microsoft 账户",
            { AccountType: AccountType.Offline } => "离线账户",
            { AccountType: AccountType.Yggdrasil } => "第三方账户",
            null => "点击右上角账户按钮登录",
            _ => account.ShortType
        };
        AccountHead = account?.Head;
    }
}
