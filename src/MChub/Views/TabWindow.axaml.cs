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
using MChub.Classes.Config;
using MChub.Core.Classes;
using MChub.Core.Classes.Entries;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
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
            var pageType = DefaultPageRegistry.Pages
                .FirstOrDefault(item => item.PageType.AssemblyQualifiedName == Data.ConfigEntry.DefaultPage)
                ?.PageType;
            var page = pageType != null && Activator.CreateInstance(pageType) is ITioTabPage tabPage
                ? tabPage
                : new NewTabPage();
            var tab = new TabEntry(this, page);
            AddTab(tab);
            SelectTab(tab);
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
    }

    private async void TabWindow_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= TabWindow_OnLoaded;
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
        return pixelWidth > 0 ? pixelWidth / scaling + WindowChromeMetrics.Gap : 0;
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

    public void ApplyBackground()
    {
        var entry = Data.ConfigEntry;

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
