using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MChub.Classes.Config;
using MChub.Core.Classes;
using MChub.Core.Classes.Entries;
using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Core.Minecraft.Services;
using MChub.Core.Module.Initialize;
using MChub.Core.Module.Multiplayer;
using MChub.Localization;
using MChub.Module;
using MChub.Module.DefaultPage;
using MChub.Platform.Windows;
using MChub.Views.Components;
using MChub.Views.Components.Operations.OpenFile;
using MChub.Views.Pages;
using MChub.Views.Pages.DownloadPages;
using SkiaSharp;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Helper;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Helpers;
using TioUi.Controls;
using AutoCompleteBox = Avalonia.Controls.AutoCompleteBox;
using NewMinecraftFolderViewModel = MChub.Views.Components.Operations.OpenFile.NewMinecraftFolderViewModel;
#if DEBUG
using HotAvalonia;
#endif

namespace MChub.Views;

public partial class TabWindow : TioTabWindowBase
{
    /// <summary>标题栏行高，与 TabWindow.axaml 中 TitleBarThings 的高度保持一致。</summary>
    private const double TitleBarHeight = 44;

    private Image? _backgroundImageLayer;
    private Border? _backgroundMaskLayer;
    private string? _cachedBackgroundPath;
    private Bitmap? _cachedOriginalBackground;
    private WindowCompositionMaterial? _compositionMaterial;
    private int _compositionMaterialAttachAttempt;
    private bool _compositionMaterialAttachPending;
    private WindowCompositionMaterialKind _compositionMaterialKind;
    private Debouncer _hideDropTipDebouncer;


    private bool _hideDropTipScheduled;
    private string? _lastDragMessage;

    public TabWindow()
    {
        Build();
    }

    public TabWindow(bool isMainWindow)
    {
        IsMainWindow = isMainWindow;
        Build();
    }

    public bool IsTabMaskVisible
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsUiLoading
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>外壳已是唯一形态：沉浸背景 + 底部悬浮胶囊导航，不再可切换。</summary>
    public bool IsLauncherX => true;

    /// <summary>内容区底部为悬浮导航条留出的空间。</summary>
    public Thickness LauncherXContentMargin => new(0, 0, 0, 88);

    public override bool OnClose()
    {
        if (AllWindows.Count == 1)
        {
            ConfigSaver.FlushConfig();
            Environment.Exit(0);
            return true;
        }

        return false;
    }

    private void Build()
    {
        _hideDropTipDebouncer = new Debouncer(OnHideDropTipDebounce, 300);
        InitializeComponent();
        ConfigureChrome();


        if (Data.ConfigEntry.HasTabWindowSize)
        {
            Width = Math.Max(Data.ConfigEntry.TabWindowWidth, MinWidth);
            Height = Math.Max(Data.ConfigEntry.TabWindowHeight, MinHeight);
        }

        Notification = new TioNotificationManager(this);
        Toast = new TioToastManager(this);
        Window = this;
        DataContext = this;
        Events();
        Keys();
        AttachDropDrag();
        CreateNewTabFunc = () =>
        {
            ITioTabPage page = Data.ConfigEntry.NewTabContent switch
            {
                NewTabContent.NewTabPage => new NewTabPage(),
                NewTabContent.StartPage => new StartPage(),
                NewTabContent.Widget => new WidgetsPage(),
                NewTabContent.HomePage => new HomePage(),
                NewTabContent.LaunchPage => new LaunchPage(),
                _ => new NewTabPage()
            };
            var tab = new TabEntry(this, page)
            {
                IconMargin = Data.ConfigEntry.NewTabContent switch
                {
                    _ => new Thickness(0, 0, 4, 0)
                }
            };

            AddTab(tab);
            SelectTab(tab);
            NavScrollViewer.Offset = new Vector(double.PositiveInfinity, 0);
        };
        if (IsMainWindow)
        {
            IsUiLoading = true;
            // 外壳固化：主窗口不用多标签默认页，首页固定在底部导航的"主页"槽位。
            var homePage = new LauncherXHomePage();
            var homeTab = new TabEntry(this, homePage);
            AddTab(homeTab);
            _dockTabs["home"] = homeTab;
            SelectTab(homeTab);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            TabSelectionList.EnableTabDragDrop(this);
        else
            TabSelectionList.PointerPressed += (_, e) =>
            {
                if (!e.Properties.IsLeftButtonPressed) return;
                BeginMoveDrag(e);
            };

        Loaded += TabWindow_OnLoaded;
        InitLauncherXDock();
        InfinityToast.Register(InfinityToasts);
    }

#if DEBUG
    /// <summary>
    /// 调试用：设 MCHUB_CAPTURE=&lt;目录&gt; 时，启动后依次切换底部导航并自渲染截图，
    /// 便于在没有屏幕录制权限的环境里核对界面。仅 DEBUG 编译生效。
    /// </summary>
    private async void RunCaptureTourIfRequested()
    {
        var dir = Environment.GetEnvironmentVariable("MCHUB_CAPTURE");
        if (string.IsNullOrWhiteSpace(dir)) return;

        await Task.Delay(4200);
        CaptureTo(dir, "01-home");

        // 顺便把三种语义通知摆出来，核对 infinityUI 的 Metro 直角色条
        InfinityToasts.Show("无法连接认证服务器，请检查网络或代理设置", InfinityUI.Controls.InfinityToastLevel.Error);
        InfinityToasts.Show("Java 运行时版本过低，可能导致游戏无法启动", InfinityUI.Controls.InfinityToastLevel.Warning);
        InfinityToasts.Show("Minecraft 1.21.1 安装完成", InfinityUI.Controls.InfinityToastLevel.Success);
        await Task.Delay(900);
        CaptureTo(dir, "05-toasts");
        InfinityToasts.Clear();

        NavigateToDock("instances");
        await Task.Delay(1400);
        CaptureTo(dir, "02-instances");
        NavigateToDock("download");
        await Task.Delay(1600);
        CaptureTo(dir, "03-download");
        NavigateToDock("settings");
        await Task.Delay(1600);
        CaptureTo(dir, "04-settings");

        // 验证首页账户卡 → 设置账户页的跳转链路
        OpenAccountSettings();
        await Task.Delay(1200);
        CaptureTo(dir, "06-account-settings");

        // 验证"换个实例当启动目标"的数据链路（选择器确认后走的就是这条）
        var other = InstanceManager.Instance.Instances
            .FirstOrDefault(i => !ReferenceEquals(i, LaunchTarget.Current) && i.Config is not null);
        if (other is not null)
        {
            LaunchTarget.Set(other);
            NavigateToDock("home");
            await Task.Delay(1000);
            CaptureTo(dir, "07-target-switch");
            Logger.Info($"[Capture] launch target switched to {other.InstanceName}");
        }

        if (Environment.GetEnvironmentVariable("MCHUB_CAPTURE_EXIT") == "1")
        {
            await Task.Delay(300);
            Environment.Exit(0);
        }
    }

    private void CaptureTo(string dir, string name)
    {
        try
        {
            var width = (int)Math.Round(Bounds.Width);
            var height = (int)Math.Round(Bounds.Height);
            if (width <= 0 || height <= 0) return;

            // 渲染窗口内容根而不是 Window 本身：Window 的快照来自 GPU 合成表面，
            // macOS 上经常拿到上一帧；直接渲染视觉树下的 Border 才是当前状态。
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            bitmap.Render(RootBorder);
            Directory.CreateDirectory(dir);
            bitmap.Save(Path.Combine(dir, name + ".png"));
            Logger.Info($"[Capture] saved {name}.png ({width}x{height})");
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }
#endif

    #region LauncherX 外壳

    private readonly Dictionary<string, TabEntry> _dockTabs = new(StringComparer.Ordinal);
    private string _lastDockKey = "home";

    private static readonly (string Key, string Label)[] DockDefinitions =
    [
        ("home", "主页"),
        ("instances", "游戏列表"),
        ("download", "下载"),
        ("settings", "设置")
    ];

    private void InitLauncherXDock()
    {
        Dock.Items.Clear();
        foreach (var (key, label) in DockDefinitions)
            Dock.Items.Add(new LauncherXDockItem(key, label));

        Dock.ShowLaunchButton = Data.ConfigEntry.LauncherXDockShowLaunchButton;
        Dock.ItemInvoked += (_, item) => NavigateToDock(item.Key);
        Dock.BackRequested += (_, _) => NavigateToDock(_lastDockKey);
        Dock.LaunchRequested += (_, _) => LaunchRecentInstance();
        Tabs.CollectionChanged += (_, _) => SyncDockSelection();
        Data.ConfigEntry.PropertyChanged += OnShellConfigChanged;
        InstanceManager.Instance.InstancesChanged += (_, _) => UpdateDockInstance();
        InstanceManager.Instance.StatisticsChanged += (_, _) => UpdateDockInstance();
        LaunchTarget.Changed += (_, _) => UpdateDockInstance();
        SyncDockSelection();
        UpdateDockInstance();
    }

    private void OnShellConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Data.ConfigEntry.LauncherXDockShowLaunchButton):
                Dock.ShowLaunchButton = Data.ConfigEntry.LauncherXDockShowLaunchButton;
                break;
            case nameof(Data.ConfigEntry.LauncherXBackdropUseGradient):
            case nameof(Data.ConfigEntry.LauncherXBackdropStartColor):
            case nameof(Data.ConfigEntry.LauncherXBackdropEndColor):
            case nameof(Data.ConfigEntry.LauncherXBackdropSolidColor):
                ApplyBackground();
                break;
            case nameof(Data.ConfigEntry.ThemeColor):
                // 用户改主题色时，外壳模式下仍要压回中性色
                if (IsLauncherX) ApplyShellThemeVariant();
                break;
        }
    }

    /// <summary>打开（必要时创建）底部导航对应的页面。</summary>
    private void NavigateToDock(string key)
    {
        if (!IsLauncherX) return;

        _lastDockKey = key;

        if (_dockTabs.TryGetValue(key, out var cached) && Tabs.Contains(cached))
        {
            SelectTab(cached);
            SyncDockSelection();
            return;
        }

        var page = CreateDockPage(key);
        if (page is null) return;

        var tab = new TabEntry(this, page);
        AddTab(tab);
        _dockTabs[key] = tab;
        SelectTab(tab);
        SyncDockSelection();
    }

    private ITioTabPage? CreateDockPage(string key)
    {
        return key switch
        {
            "home" => new LauncherXHomePage(),
            "instances" => new InstancesPage(),
            "download" => new DownloadPage(),
            "settings" => new SettingPage(),
            _ => null
        };
    }

    /// <summary>根据当前选中页刷新导航高亮与返回按钮。</summary>
    private void SyncDockSelection()
    {
        var current = SelectedTab;
        string? activeKey = null;
        if (current is not null)
        {
            foreach (var (key, tab) in _dockTabs)
            {
                if (!ReferenceEquals(tab, current)) continue;
                activeKey = key;
                break;
            }
        }

        Dock.SetSelected(activeKey);
        Dock.CanGoBack = current is not null && activeKey is null;
        UpdateDockInstance();
    }

    private void UpdateDockInstance()
    {
        var instance = LaunchTarget.Current;
        Dock.HasCurrentInstance = instance is not null;
        Dock.CurrentInstanceName = instance?.InstanceName;
    }

    private void LaunchRecentInstance()
    {
        var instance = LaunchTarget.Current;
        var topLevel = TopLevel.GetTopLevel(this);
        if (instance is null || topLevel is null)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, topLevel,
            MinecraftLaunchOptionsFactory.Create(instance,
                logSession => MinecraftLogPage.Open(logSession, topLevel)));
    }

    /// <summary>从首页账户卡跳转到「设置 → 账户」，那里才是切换/新增账户的地方。</summary>
    public void OpenAccountSettings()
    {
        NavigateToDock("settings");
        if (_dockTabs.TryGetValue("settings", out var tab) && tab.Content is SettingPage page)
            page.NavigateTo(typeof(MChub.Views.Pages.SettingPages.Account));
    }

    #endregion

    private async void TabWindow_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= TabWindow_OnLoaded;
#if DEBUG
        RunCaptureTourIfRequested();
#endif
        // 视觉树已建好，此时再应用一次外壳主题：构造期设置的变体会被
        // 已解析并缓存的主题资源盖掉（表现为下拉框等仍是浅色）。
        ApplyShellThemeVariant();
        var entry = Data.ConfigEntry;
        var path = entry.BackgroundImagePath;
        if (entry.BackgroundMode != BackgroundMode.Image || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ApplyBackground();
            return;
        }

        try
        {
            var targetWidth = Math.Max(1, Bounds.Width * RenderScaling * 1.25);
            var targetHeight = Math.Max(1, Bounds.Height * RenderScaling * 1.25);
            using var decoded = await Task.Run(() => DecodeBackground(path, targetWidth, targetHeight));
            if (Data.ConfigEntry.BackgroundMode != BackgroundMode.Image ||
                !string.Equals(Data.ConfigEntry.BackgroundImagePath, path, StringComparison.Ordinal))
                return;

            _cachedOriginalBackground?.Dispose();
            _cachedOriginalBackground = CreateAvaloniaBitmapFromSkBitmap(decoded);
            _cachedBackgroundPath = path;
            ApplyBackground();
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            ClearOriginalBackgroundCache();
            ClearBackgroundLayers();
        }
    }

#if DEBUG
    [AvaloniaHotReload]
#endif
    public void Hot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) TabSelectionList.EnableTabDragDrop(this);
    }

    private void Events()
    {
        Closed += TabWindow_OnClosed;

        // 构造阶段窗口还没有 HWND，量不到系统按钮宽度；显示后再量一次。
        UpdateChromeInsets();
        Loaded += TabWindow_OnLoadedChromeInsets;

        NavScrollViewer.ScrollChanged += (_, _) => { IsTabMaskVisible = NavScrollViewer.Offset.X > 0; };
        SizeChanged += TabWindow_OnSizeChanged;
        Resized += TabWindow_OnResized;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            PropertyChanged += TabWindow_OnWindowPropertyChanged;
            ScalingChanged += TabWindow_OnScalingChanged;
            ActualThemeVariantChanged += TabWindow_OnActualThemeVariantChanged;
        }
    }

    /// <summary>
    /// 标题栏 chrome 交给 FluentAvalonia + Avalonia 两层共同管理：
    /// 1. FA 层 TitleBar.ExtendsContentIntoTitleBar：由 FA 负责标题栏 chrome 与系统按钮；
    /// 2. Avalonia 层 ExtendClientAreaToDecorationsHint：真正把客户区延伸进窗口装饰
    ///    （macOS 上即隐藏原生标题栏，让自绘标题栏行 + 标签条顶到窗口顶端）。
    /// 只设第 1 项时原生标题栏依然存在，标签栏看起来仍像在标题栏下方。
    /// </summary>
    private void ConfigureChrome()
    {
        if (TitleBar is not null)
        {
            TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBar.Height = TitleBarHeight;
        }

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = TitleBarHeight;

        // LauncherX 是深色沉浸式设计：外壳开启时窗口级强制深色变体，
        // 这样背景、卡片、文字资源全部走 Dark 主题，白字才安全。
        ApplyShellThemeVariant();
    }

    private void ApplyShellThemeVariant()
    {
        ContentThemeScope.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        if (Application.Current is not { } app) return;

        // 外壳是深色沉浸式设计。这里改的是 Application 级变体而不是窗口级，
        // 否则 TioUi / FluentAvalonia 里按应用级解析的控件（下拉框、搜索框等）仍留在浅色，
        // 在深色底上会出现"白块 + 深字"的割裂感。
        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        App.ApplyFluentAvaloniaVariant(Avalonia.Styling.ThemeVariant.Dark);

        // infinityUI 纪律：全局只允许黑白 + 红黄绿。
        // 用户配置的主题色是彩色，会把开关、进度条、选中态染成杂色，这里压成中性白。
        TioUi.Common.Helpers.ThemeHelper.SetThemeColor(Color.Parse("#F5F5F5"));
    }

    /// <summary>
    /// 标签栏与标题栏同处一行（三列 Grid）：左侧给 macOS 系统红绿灯留出占位，
    /// 右侧给 Windows 系统窗口按钮留出占位（做成标题栏组件的右边距）。
    /// 中间标签条由 `*` 列自动占满，不做手工内缩 —— 曾因手工算导致标签条被压成 0 宽。
    /// </summary>
    private void UpdateChromeInsets()
    {
        SystemChromeSpacer.Width = SystemChromeLeftInset();
        BarComponent.Margin = new Thickness(0, 0, SystemChromeRightInset(), 0);
    }

    /// <summary>
    /// 标题栏拖动：标题条空白区域按下即拖动窗口，双击在最大化/还原之间切换。
    /// 标签条叠在同一行，标签自身仍按正常点击处理。
    /// </summary>
    private void OnTitleBarDragSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>
    /// macOS 扩展客户区时，系统红绿灯占用的左侧宽度由平台提供（WindowDecorationMargin）；
    /// 取不到时退回经验值，避免标签条压到红绿灯上。
    /// </summary>
    private double SystemChromeLeftInset()
    {
        var fromPlatform = WindowDecorationMargin.Left;
        if (fromPlatform > 0) return fromPlatform;
        return OperatingSystem.IsMacOS() ? 70 : 3;
    }

    /// <summary>
    /// Windows 上右上角是系统绘制的最小化 / 最大化 / 关闭按钮。FAAppWindow 在 Win32 走
    /// 系统边框、Avalonia 又不为其预留布局空间（WindowDecorationMargin.Right 恒为 0），
    /// 客户区延伸后这排按钮会压在标题栏组件上，把搜索等控制按钮遮住并抢走点击。
    /// 这里按实际按钮区宽度给标题栏组件留出右边距；其余平台没有右侧系统按钮，返回 0。
    /// </summary>
    private double SystemChromeRightInset()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        if (WindowState == WindowState.FullScreen) return 0;

        var scaling = RenderScaling;
        if (scaling <= 0) scaling = 1;

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var pixelWidth = WindowChromeMetrics.GetCaptionButtonsWidth(handle, (uint)Math.Round(96 * scaling));
        if (pixelWidth <= 0) return 0;

        var inset = pixelWidth / scaling + WindowChromeMetrics.Gap;

        // 防御：万一量到异常值（不同系统版本上该接口语义有差异），不要把标题栏推到看不见。
        // 系统按钮区正常情况下远小于窗口宽度的四成。
        var limit = Bounds.Width * 0.4;
        return limit <= 0 || inset <= limit ? inset : 0;
    }

    private void TabWindow_OnLoadedChromeInsets(object? sender, RoutedEventArgs e)
    {
        Loaded -= TabWindow_OnLoadedChromeInsets;
        UpdateChromeInsets();
    }

    private void TabWindow_OnClosed(object? sender, EventArgs e)
    {
        // Stop detached multiplayer daemons (Terracotta) before the app exits.
        _ = TerracottaMultiplayerService.Instance.StopAsync();


        TabSelectionList.DisableTabDragDrop();

        Loaded -= TabWindow_OnLoadedChromeInsets;

        RemoveHandler(DragDrop.DragLeaveEvent, OnLeaveHandler);
        RemoveHandler(DragDrop.DragOverEvent, OnDragHandler);
        RemoveHandler(DragDrop.DropEvent, OnDropHandler);

        SizeChanged -= TabWindow_OnSizeChanged;
        Resized -= TabWindow_OnResized;
        PropertyChanged -= TabWindow_OnWindowPropertyChanged;
        ScalingChanged -= TabWindow_OnScalingChanged;
        ActualThemeVariantChanged -= TabWindow_OnActualThemeVariantChanged;
        Closed -= TabWindow_OnClosed;

        _compositionMaterial?.Dispose();
        _compositionMaterial = null;
        ClearBackgroundLayers();
        ClearOriginalBackgroundCache();
        DataContext = null;
    }

    private void TabWindow_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateCompositionMaterialGeometry();

        if (Data.ConfigEntry.BackgroundMode != BackgroundMode.Image || _cachedOriginalBackground == null)
            return;

        var pixelSize = _cachedOriginalBackground.PixelSize;
        var scale = RenderScaling;
        var width = (int)Math.Ceiling(e.NewSize.Width * scale);
        var height = (int)Math.Ceiling(e.NewSize.Height * scale);
        if (width > pixelSize.Width * 1.5 || height > pixelSize.Height * 1.5)
        {
            ClearOriginalBackgroundCache();
            ApplyBackground();
        }
    }

    private void TabWindow_OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowState))
        {
            UpdateCompositionMaterialGeometry();
            // 最大化 / 还原会改变系统按钮区的位置与宽度，重新量一次留白。
            UpdateChromeInsets();
        }
    }

    private void TabWindow_OnScalingChanged(object? sender, EventArgs e)
    {
        UpdateCompositionMaterialGeometry();
        // 跨显示器拖动会改变 DPI，系统按钮宽度随之变化。
        UpdateChromeInsets();
    }

    private void TabWindow_OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (_compositionMaterial != null && Data.ConfigEntry.EnableManagedWindowDecorationsOnWindows &&
            Data.ConfigEntry.BackgroundMode is BackgroundMode.Acrylic or BackgroundMode.Mica)
            ApplyCompositionMaterial(_compositionMaterialKind);
    }

    private void TabWindow_OnResized(object? sender, WindowResizedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.Minimized) return;
        if (e.Reason != WindowResizeReason.User) return;

        var size = e.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        Data.ConfigEntry.TabWindowWidth = size.Width;
        Data.ConfigEntry.TabWindowHeight = size.Height;
        Data.ConfigEntry.HasTabWindowSize = true;
        ConfigSaver.SaveConfig();
    }

    public void OpenAggregatedSearchDialog()
    {
        if (FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox
            or TioUi.Controls.AutoCompleteBox)
            return;

        var options = new DialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            StyleClass = "undrag",
            CanResize = true,
            StartupLocation = WindowStartupLocation.CenterOwner,
            DialogWindowMinWidth = 770,
            DialogWindowMinHeight = 471,
            DialogWindowWidth = 770,
            DialogWindowHeight = 471,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _ = Dialog.ShowCustomAsync<AggregatedSearchDialog, AggregatedSearchDialogViewModel, object>(
            new AggregatedSearchDialogViewModel(this), options: options, owner: this);
    }

    private void Keys()
    {
        RemoveDefaultWindowKeyBindings();
        ShortcutManager.Apply(this);
    }

    private void RemoveDefaultWindowKeyBindings()
    {
        var toRemove = KeyBindings
            .Where(binding => binding.Gesture is KeyGesture gesture && IsDefaultWindowGesture(gesture))
            .ToArray();
        foreach (var binding in toRemove)
            KeyBindings.Remove(binding);
    }

    private static bool IsDefaultWindowGesture(KeyGesture gesture)
    {
        return (gesture.Key == Key.T && gesture.KeyModifiers == KeyModifiers.Control) ||
               (gesture.Key == Key.W && gesture.KeyModifiers == KeyModifiers.Control) ||
               (gesture.Key == Key.W && gesture.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift));
    }

    public void OpenPage(ITioTabPage page)
    {
        var tab = new TabEntry(this, page);
        CreateTab(tab);
        SelectTab(tab);
    }

    public void OpenDebugPage()
    {
        OpenPage(new DebugPage());
    }

    public void OpenCreateInstanceDialog()
    {
        var options = new OverlayDialogOptions
        {
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            CanResize = false,
            IsCloseButtonVisible = false
        };
        _ = OverlayDialog.ShowCustomAsync<CreateInstanceDialog, CreateInstanceDialogViewModel, bool>(
            new CreateInstanceDialogViewModel(), this.TryGetHostId(), options);
    }

    public async void OpenAddMinecraftFolderDialog()
    {
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                new NewMinecraftFolderViewModel(Data.ConfigEntry.MinecraftFolders
                    .Select(folder => folder.FolderPath).ToList()), this.TryGetHostId(), options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        CreateNewTabFunc();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (sender is not Border border) return;
            var tab = border.Tag as TabEntry;
            if (tab == null) return;
            var flyout = tab.BuildContextMenu();
            flyout.ShowAt(border);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsMiddleButtonPressed) return;
        var c = ((Border)sender).Tag as TabEntry;
        c?.Close();
    }

    private void InputElement_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        scrollViewer.Offset = new Vector(
            scrollViewer.Offset.X + e.Delta.Y * -20,
            scrollViewer.Offset.Y
        );
        e.Handled = true;
    }

    private void NM_NewTab(object? sender, EventArgs e)
    {
        CreateNewTabFunc();
    }

    private void NM_CloseTab(object? sender, EventArgs e)
    {
        SelectedTab.Close();
    }

    private void NM_CloseOtherTab(object? sender, EventArgs e)
    {
        SelectedTab.CloseOther();
    }

    private void NM_OpenInNewWindow(object? sender, EventArgs e)
    {
        SelectedTab.MoveTabToNewWindow();
    }

    private void AttachDropDrag()
    {
        DragDrop.SetAllowDrop(this, true);


        AddHandler(DragDrop.DragLeaveEvent, OnLeaveHandler);
        AddHandler(DragDrop.DragOverEvent, OnDragHandler);
        AddHandler(DragDrop.DropEvent, OnDropHandler);
    }

    private void OnDragHandler(object? sender, DragEventArgs e)
    {
        _hideDropTipScheduled = false;

        var msg = DragDropHandler.GetMsg(e);


        if (string.IsNullOrEmpty(msg) || msg == _lastDragMessage) return;
        _lastDragMessage = msg;
        BarComponent.DropMsg = msg;
    }

    private void OnLeaveHandler(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;


        _hideDropTipScheduled = true;
        _hideDropTipDebouncer.Invoke();
    }

    private void OnHideDropTipDebounce()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_hideDropTipScheduled) return;
            _hideDropTipScheduled = false;
            BarComponent.DropMsg = null;
            _lastDragMessage = null;
        });
    }

    private void OnDropHandler(object? sender, DragEventArgs e)
    {
        _hideDropTipScheduled = false;
        BarComponent.DropMsg = null;
        _lastDragMessage = null;
        DragDropHandler.ResetDragIdentification();
        DragDropHandler.Handle(e, this);
    }

    public static void ApplyBackgroundToAllWindows()
    {
        foreach (var windowBase in AllWindows)
            if (windowBase is TabWindow tabWin)
                tabWin.ApplyBackground();
    }

    public static void ApplyImageMaskToAllWindows()
    {
        foreach (var windowBase in AllWindows)
            if (windowBase is TabWindow tabWin)
                tabWin.ApplyImageMaskOverlay();
    }

    /// <summary>
    /// LauncherX 外壳的默认底：没有背景图时铺一层深蓝到近黑的斜向渐变，
    /// 让沉浸式布局里的白色大字与半透明面板有依托，而不是压在纯色上显得空。
    /// </summary>
    private void ApplyLauncherXBackdrop()
    {
        if (RootBorder is null) return;

        var entry = Data.ConfigEntry;

        if (!entry.LauncherXBackdropUseGradient)
        {
            RootBorder.Background = new SolidColorBrush(entry.LauncherXBackdropSolidColor);
            return;
        }

        RootBorder.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.45, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(entry.LauncherXBackdropStartColor, 0),
                new GradientStop(entry.LauncherXBackdropEndColor, 1)
            }
        };
    }

    public void ApplyBackground()
    {        var entry = Data.ConfigEntry;

        // UI 磨砂玻璃：标题栏 / 标签栏那一行（做成独立层，开关与强度直接取配置）
        if (TitleBarAcrylic is not null)
        {
            TitleBarAcrylic.IsAcrylicEnabled = entry.EnableSurfaceAcrylic;
            TitleBarAcrylic.TintOpacity = entry.SurfaceAcrylicOpacity;
            if (entry.SurfaceAcrylicTintColor != Colors.Transparent)
                TitleBarAcrylic.TintColor = entry.SurfaceAcrylicTintColor;
        }

        var useManagedCompositionMaterial = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                                            entry.EnableManagedWindowDecorationsOnWindows &&
                                            entry.BackgroundMode is BackgroundMode.Acrylic or BackgroundMode.Mica;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowDecorations = entry.EnableManagedWindowDecorationsOnWindows
                ? WindowDecorations.None
                : WindowDecorations.Full;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux uses client-side decorations by default.
            WindowDecorations = WindowDecorations.None;
        }

        switch (entry.BackgroundMode)
        {
            case BackgroundMode.Default:
                HideCompositionMaterial();
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                ClearValue(BackgroundProperty);
                ClearValue(TransparencyBackgroundFallbackProperty);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                ApplyLauncherXBackdrop();
                break;

            case BackgroundMode.Transparent:
                HideCompositionMaterial();
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                Background = Brushes.Transparent;
                TransparencyBackgroundFallback = Brushes.Transparent;
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                break;

            case BackgroundMode.Image:
                HideCompositionMaterial();
                if (!string.IsNullOrEmpty(entry.BackgroundImagePath) && File.Exists(entry.BackgroundImagePath))
                {
                    try
                    {
                        var original = GetOrCreateOriginalBackground(entry.BackgroundImagePath);
                        EnsureBackgroundLayers();
                        _backgroundImageLayer!.Source = original;
                        UpdateImageBlurEffect(entry.ImageBlurRadius);
                        ClearValue(BackgroundProperty);
                        ClearValue(TransparencyBackgroundFallbackProperty);
                        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception);
                        ClearOriginalBackgroundCache();
                        ClearBackgroundLayers();
                    }
                }
                else
                {
                    ClearOriginalBackgroundCache();
                    ClearBackgroundLayers();
                    ClearValue(TransparencyBackgroundFallbackProperty);
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
                }

                break;

            case BackgroundMode.Color:
                HideCompositionMaterial();
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = new SolidColorBrush(entry.BackgroundSolidColor);
                TransparencyBackgroundFallback = Brushes.Transparent;
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                break;

            case BackgroundMode.Acrylic:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                var color = entry.BackgroundSolidColor;
                var alpha = (byte)(entry.AcrylicOpacity * 255);
                var acrylicBrush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = acrylicBrush;
                TransparencyBackgroundFallback = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
                TransparencyLevelHint = new[]
                {
                    useManagedCompositionMaterial
                        ? WindowTransparencyLevel.Transparent
                        : WindowTransparencyLevel.AcrylicBlur
                };
                if (useManagedCompositionMaterial)
                    ApplyCompositionMaterial(WindowCompositionMaterialKind.Acrylic);
                else
                    HideCompositionMaterial();
                break;


            case BackgroundMode.Mica:
                ClearOriginalBackgroundCache();
                ClearBackgroundLayers();
                var micaColor = entry.BackgroundSolidColor;
                var micaAlpha = (byte)(entry.MicaOpacity * 255);
                var micaBrush = new SolidColorBrush(Color.FromArgb(micaAlpha, micaColor.R, micaColor.G, micaColor.B));
                Background = Brushes.Transparent;
                if (RootBorder != null)
                    RootBorder.Background = micaBrush;
                TransparencyBackgroundFallback =
                    new SolidColorBrush(Color.FromArgb(255, micaColor.R, micaColor.G, micaColor.B));
                TransparencyLevelHint = new[]
                {
                    useManagedCompositionMaterial
                        ? WindowTransparencyLevel.Transparent
                        : WindowTransparencyLevel.Mica
                };
                if (useManagedCompositionMaterial)
                    ApplyCompositionMaterial(WindowCompositionMaterialKind.Mica);
                else
                    HideCompositionMaterial();
                break;
        }

        ApplyImageMaskOverlay();
    }

    private void ApplyCompositionMaterial(WindowCompositionMaterialKind kind)
    {
        _compositionMaterialKind = kind;
        _compositionMaterial ??= WindowCompositionMaterial.TryCreate(this);
        var active = _compositionMaterial?.Apply(kind, GetCompositionMaterialCornerRadius()) == true;
        if (active)
        {
            _compositionMaterialAttachAttempt = 0;
            ApplyCompositionMaterialTint(kind);
            return;
        }

        _compositionMaterial?.Dispose();
        _compositionMaterial = null;

        if (RootBorder != null)
        {
            var color = Data.ConfigEntry.BackgroundSolidColor;
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
        }

        if (_compositionMaterialAttachPending || ++_compositionMaterialAttachAttempt >= 12) return;
        _compositionMaterialAttachPending = true;
        RequestAnimationFrame(_ =>
        {
            _compositionMaterialAttachPending = false;
            if (Data.ConfigEntry.EnableManagedWindowDecorationsOnWindows &&
                Data.ConfigEntry.BackgroundMode is BackgroundMode.Acrylic or BackgroundMode.Mica)
                ApplyCompositionMaterial(_compositionMaterialKind);
        });
    }

    private void HideCompositionMaterial()
    {
        _compositionMaterialAttachAttempt = 0;
        _compositionMaterialAttachPending = false;
        _compositionMaterial?.Hide();
    }

    private void ApplyCompositionMaterialTint(WindowCompositionMaterialKind kind)
    {
        if (RootBorder == null) return;
        var entry = Data.ConfigEntry;
        var alpha = (byte)((kind == WindowCompositionMaterialKind.Acrylic
            ? entry.AcrylicOpacity
            : entry.MicaOpacity) * 255);
        var color = entry.BackgroundSolidColor;
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private void UpdateCompositionMaterialGeometry()
    {
        if (_compositionMaterial?.UpdateGeometry(GetCompositionMaterialCornerRadius()) != false) return;
        _compositionMaterial.Dispose();
        _compositionMaterial = null;
        if (Data.ConfigEntry.EnableManagedWindowDecorationsOnWindows &&
            Data.ConfigEntry.BackgroundMode is BackgroundMode.Acrylic or BackgroundMode.Mica)
            ApplyCompositionMaterial(_compositionMaterialKind);
    }

    private double GetCompositionMaterialCornerRadius()
    {
        return WindowState == WindowState.Maximized ? 0 : Data.ConfigEntry.CustomWindowBorderCornerRadius;
    }

    private void ApplyImageMaskOverlay()
    {
        if (_backgroundMaskLayer == null) return;

        var entry = Data.ConfigEntry;
        if (entry.BackgroundMode == BackgroundMode.Image && entry.EnableImageMask)
        {
            var alpha = (byte)(entry.ImageMaskOpacity * 255);
            _backgroundMaskLayer.Background = new SolidColorBrush(
                Color.FromArgb(alpha, entry.ImageMaskColor.R, entry.ImageMaskColor.G, entry.ImageMaskColor.B));
        }
        else
        {
            _backgroundMaskLayer.Background = null;
        }
    }

    private void EnsureBackgroundLayers()
    {
        if (RootBorder == null) return;
        if (_backgroundImageLayer != null) return;

        var layoutTransformControl = RootBorder.Child as LayoutTransformControl;
        var content = layoutTransformControl?.Child ?? RootBorder.Child;
        if (content is not DockPanel dockPanel) return;

        _backgroundImageLayer = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _backgroundMaskLayer = new Border
        {
            IsHitTestVisible = false
        };

        var panel = new Panel();
        panel.Children.Add(_backgroundImageLayer);
        panel.Children.Add(_backgroundMaskLayer);

        if (layoutTransformControl != null)
        {
            layoutTransformControl.Child = null;
            panel.Children.Add(dockPanel);
            layoutTransformControl.Child = panel;
        }
        else
        {
            RootBorder.Child = null;
            panel.Children.Add(dockPanel);
            RootBorder.Child = panel;
        }
    }

    private void ClearBackgroundLayers()
    {
        if (_backgroundImageLayer != null)
        {
            _backgroundImageLayer.Source = null;
            _backgroundImageLayer.Effect = null;
        }

        if (_backgroundMaskLayer != null)
            _backgroundMaskLayer.Background = null;

        if (RootBorder != null)
            RootBorder.ClearValue(Border.BackgroundProperty);
    }

    private void UpdateImageBlurEffect(double imageBlurRadius)
    {
        if (_backgroundImageLayer == null) return;

        var radius = imageBlurRadius * 20;
        if (radius <= 0.5)
        {
            _backgroundImageLayer.Margin = default;
            _backgroundImageLayer.Effect = null;
            return;
        }

        // Keep the blur kernel's transparent sampling area outside the visible window.
        _backgroundImageLayer.Margin = new Thickness(-radius * 2);
        _backgroundImageLayer.Effect = new BlurEffect { Radius = radius };
    }

    private Bitmap GetOrCreateOriginalBackground(string path)
    {
        if (_cachedOriginalBackground != null && _cachedBackgroundPath == path)
            return _cachedOriginalBackground;

        _cachedOriginalBackground?.Dispose();
        var targetWidth = Math.Max(1, Bounds.Width * RenderScaling * 1.25);
        var targetHeight = Math.Max(1, Bounds.Height * RenderScaling * 1.25);
        using var sk = DecodeBackground(path, targetWidth, targetHeight);
        _cachedOriginalBackground = CreateAvaloniaBitmapFromSkBitmap(sk);
        _cachedBackgroundPath = path;
        return _cachedOriginalBackground;
    }

    private void ClearOriginalBackgroundCache()
    {
        _cachedOriginalBackground?.Dispose();
        _cachedOriginalBackground = null;
        _cachedBackgroundPath = null;
    }

    private static Bitmap CreateAvaloniaBitmapFromSkBitmap(SKBitmap bitmap)
    {
        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height),
            new Vector(96, 96),
            bitmap.RowBytes);
    }

    private static SKBitmap DecodeBackground(string path, double targetWidth, double targetHeight)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException(
            CommonLanguageManager.Instance.tabWindow_cannotReadBackground.CurrentValue());
        var source = codec.Info;
        var scale = Math.Min(1, Math.Max(targetWidth / source.Width, targetHeight / source.Height));
        var dimensions = codec.GetScaledDimensions((float)scale);
        var info = new SKImageInfo(Math.Max(1, dimensions.Width), Math.Max(1, dimensions.Height),
            SKColorType.Bgra8888, SKAlphaType.Premul, source.ColorSpace);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        throw new InvalidDataException(string.Format(
            CommonLanguageManager.Instance.tabWindow_backgroundDecodeFailed.CurrentValue(), result));
    }
}
