using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Localization;
using MChub.Views;
using MChub.Workbench.Commands;
using MChub.Workbench.Documents;
using MChub.Workbench.Services;
using MChub.Workbench.Views;

namespace MChub.Workbench.Shell;

/// <summary>
/// 工作台主窗口。布局照 IntelliJ IDEA 新 UI 组织：
/// 标题栏 + 主工具栏 + 左右工具窗口条（各分上下两段）+ 编辑区 + 底部工具窗口 + 状态栏。
/// 这个类只负责"布局"和"把各部分接起来"，具体功能通过命令注册进来。
/// </summary>
public partial class WorkbenchWindow : Window
{
    private enum ToolStripe
    {
        Left,
        Right,
        Bottom
    }

    private enum PaletteMode
    {
        /// <summary>查找操作（IDEA 的 Find Action，Ctrl+Shift+A）。</summary>
        Actions,

        /// <summary>搜索所有位置（IDEA 的 Search Everywhere，双击 Shift）。</summary>
        SearchEverywhere
    }

    private sealed record ToolWindowEntry(string Id, string Glyph, string Title, ToolStripe Stripe);

    private sealed record PaletteEntry(string Title, string Subtitle, string Glyph, bool Enabled, Action Invoke);

    private readonly CommandService _commands = new();
    private readonly DocumentService _documents = new();
    private readonly KeybindingService _keybindings = new();
    private readonly OutputService _output = new();
    private readonly WorkbenchLayoutState _layout;

    private readonly List<ToolWindowEntry> _toolWindows = new();
    private readonly Dictionary<string, (TextBlock Icon, Border Indicator)> _toolVisuals = new();
    private readonly List<Button> _paletteItems = new();
    private readonly List<PaletteEntry> _paletteEntries = new();

    private ToolWindowEntry? _leftTool;
    private ToolWindowEntry? _rightTool;
    private ToolWindowEntry? _bottomTool;

    private bool _leftVisible = true;
    private bool _rightVisible;
    private bool _bottomVisible = true;

    private ExplorerView? _explorer;
    private AccountsView? _accounts;
    private Control? _outputView;
    private TextBox? _outputBox;
    private ComboBox? _channelPicker;
    private Button? _runButton;
    private TextBlock? _projectLabel;
    private TextBlock? _accountInitial;
    private TextBlock? _statusMode;
    private TextBlock? _statusMemory;
    private TextBlock? _statusAccount;
    private TextBlock? _statusInstances;
    private StackPanel? _breadcrumb;

    private PaletteMode _paletteMode = PaletteMode.Actions;
    private int _paletteIndex = -1;
    private string _activeChannel = OutputService.ChannelWorkbench;

    private DateTime _lastShiftAt;
    private DispatcherTimer? _memoryTimer;
    private bool _outputRefreshPending;
    private bool _suppressLayoutSave;

    /// <summary>启动阶段用于标记界面还在装载（App 初始化完成后置 false）。</summary>
    public bool IsUiLoading { get; set; } = true;

    public WorkbenchWindow()
    {
        InitializeComponent();

        _layout = WorkbenchLayoutStore.Load();
        WorkbenchServices.Initialize(_commands, _documents, _layout, this);

        RegisterCommands();
        // 工具窗口先建：视图菜单要列它们的命令。
        BuildToolWindows();
        BuildMenus();
        BuildToolbar();
        BuildStatusBar();
        BuildInspectionWidget();

        _keybindings.BindFrom(_commands);

        _documents.StateChanged += (_, _) =>
        {
            RefreshEditor();
            UpdateStatusBar();
        };
        _commands.Changed += (_, _) =>
        {
            if (PaletteOverlay.IsVisible)
                RefreshPalette(PaletteInput.Text ?? string.Empty);
        };
        _output.ChannelChanged += OnOutputChannelChanged;
        InstanceLauncher.LaunchStateChanged += (_, _) => UpdateStatusBar();

        // 实例是在后台扫描完才填进集合的，扫描结束时界面要跟着刷新，
        // 否则状态栏和工具栏会一直停在「0 个实例」。
        InstanceManager.Instance.InstancesChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                UpdateStatusBar();
            else
                Dispatcher.UIThread.Post(UpdateStatusBar);
        };

        PaletteInput.KeyDown += OnPaletteKeyDown;
        PaletteInput.TextChanged += (_, _) => RefreshPalette(PaletteInput.Text ?? string.Empty);
        PaletteOverlay.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, PaletteOverlay))
                ClosePalette();
        };

        TitleBar.PointerPressed += OnTitleBarPointerPressed;
        LeftSplitter.DragCompleted += (_, _) => CaptureCurrentLayout();
        RightSplitter.DragCompleted += (_, _) => CaptureCurrentLayout();
        PanelSplitter.DragCompleted += (_, _) => CaptureCurrentLayout();
        Resized += (_, _) => { if (WindowState == WindowState.Normal) CaptureCurrentLayout(); };
        Closing += (_, _) =>
        {
            _memoryTimer?.Stop();
            CaptureCurrentLayout(flush: true);
        };

        ApplyLayout();
        RefreshEditor();
        UpdateEmptyState();
        UpdateStatusBar();

        Opened += (_, _) =>
        {
            SetupScreenshotHook();
            PaletteInput.Watermark = L(AppLanguageManager.Instance.workbench_paletteHint.CurrentValue());
            _output.Write(OutputService.ChannelWorkbench,
                $"工作台已就绪 · 实例 {InstanceManager.Instance.Instances.Count} 个 · 账户 {Data.ConfigEntry.MinecraftAccounts.Count} 个");
            if (_leftTool?.Id is "explorer" or null)
                _explorer?.Rebuild();
            StartMemoryTimer();
        };
    }

    private static string L(string value) => string.IsNullOrEmpty(value) ? string.Empty : value;

    private static string MenuView => L(AppLanguageManager.Instance.workbench_menuView.CurrentValue());

    /// <summary>
    /// 仅开发期使用：设置环境变量 <c>MCHUB_SHOT=/path/x.png</c> 后，
    /// 窗口渲染完成会把画面存成 PNG，方便无 GUI 权限时核对布局。
    /// 另可用 <c>MCHUB_SHOT_VIEW=explorer|accounts|output|instance</c> 先切到某个界面再截。
    /// </summary>
    private void SetupScreenshotHook()
    {
        var path = Environment.GetEnvironmentVariable("MCHUB_SHOT");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var delay = int.TryParse(Environment.GetEnvironmentVariable("MCHUB_SHOT_DELAY"), out var d) ? d : 5000;
        var view = Environment.GetEnvironmentVariable("MCHUB_SHOT_VIEW");

        DispatcherTimer.RunOnce(() =>
        {
            switch (view)
            {
                case "accounts":
                    ShowToolWindow("accounts");
                    break;
                case "output":
                    ShowToolWindow("output");
                    break;
                case "instance":
                    var first = InstanceManager.Instance.Instances.FirstOrDefault();
                    if (first is not null)
                        OpenInstanceOverview(first);
                    break;
            }

            // 切界面后等一帧让布局稳定，再截图。
            DispatcherTimer.RunOnce(() => CaptureShot(path),
                TimeSpan.FromMilliseconds(view is null ? 0 : 1200));
        }, TimeSpan.FromMilliseconds(delay));
    }

    private void CaptureShot(string path)
    {
        try
        {
            if (Content is not Visual root)
            {
                Console.WriteLine("[SHOT] 没有可渲染的根视觉");
                return;
            }

            // 根面板本身没有背景，直接渲染会留下透明像素（存成 PNG 看起来是白色）。
            // 先把窗口底色刷到根面板上，截出来的才是真实观感。
            if (root is Panel panel && panel.Background is null)
                panel.Background = Background;

            var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
            var size = new PixelSize(
                Math.Max(1, (int)Math.Round(Bounds.Width * scaling)),
                Math.Max(1, (int)Math.Round(Bounds.Height * scaling)));

            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(root);
            bitmap.Save(path);
            Console.WriteLine($"[SHOT] {path} {size.Width}x{size.Height} @{scaling}x");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SHOT] 失败: " + ex);
        }
    }


    // ───────────────────────── 窗口与布局 ─────────────────────────

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 客户区已经延伸进标题栏，拖动和双击最大化要自己实现。
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 菜单按钮在标题栏里，点在按钮上时不能启动拖动，否则点不开菜单。
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    private void ApplyLayout()
    {
        _suppressLayoutSave = true;

        if (_layout.Width >= MinWidth && _layout.Height >= MinHeight)
        {
            Width = _layout.Width;
            Height = _layout.Height;
        }

        if (_layout.Maximized)
            WindowState = WindowState.Maximized;

        _leftTool = ResolveTool(_layout.ActiveActivity, ToolStripe.Left, "explorer");
        _rightTool = ResolveTool(_layout.ActiveRightTool, ToolStripe.Right, "news");
        _bottomTool = ResolveTool(_layout.ActivePanel, ToolStripe.Bottom, "output");

        SetLeftVisible(_layout.SideBarVisible, save: false);
        SetRightVisible(_layout.RightBarVisible, save: false);
        SetBottomVisible(_layout.PanelVisible, save: false);

        _suppressLayoutSave = false;
    }

    private ToolWindowEntry? ResolveTool(string? id, ToolStripe stripe, string fallback)
    {
        return _toolWindows.FirstOrDefault(t => t.Id == id && t.Stripe == stripe)
               ?? _toolWindows.FirstOrDefault(t => t.Id == fallback && t.Stripe == stripe);
    }

    private void CaptureCurrentLayout(bool flush = false)
    {
        if (_suppressLayoutSave)
            return;

        if (WindowState == WindowState.Normal)
        {
            _layout.Width = Width;
            _layout.Height = Height;
        }

        _layout.Maximized = WindowState == WindowState.Maximized;

        var leftWidth = MainArea.ColumnDefinitions[1].Width;
        if (leftWidth.IsAbsolute && leftWidth.Value > 120)
            _layout.SideBarWidth = leftWidth.Value;

        var rightWidth = MainArea.ColumnDefinitions[5].Width;
        if (rightWidth.IsAbsolute && rightWidth.Value > 160)
            _layout.RightBarWidth = rightWidth.Value;

        var bottomHeight = EditorArea.RowDefinitions[3].Height;
        if (bottomHeight.IsAbsolute && bottomHeight.Value > 60)
            _layout.PanelHeight = bottomHeight.Value;

        _layout.SideBarVisible = _leftVisible;
        _layout.RightBarVisible = _rightVisible;
        _layout.PanelVisible = _bottomVisible;
        _layout.ActiveActivity = _leftTool?.Id;
        _layout.ActiveRightTool = _rightTool?.Id;
        _layout.ActivePanel = _bottomTool?.Id;

        if (flush)
            WorkbenchServices.FlushLayout();
        else
            WorkbenchServices.SaveLayout();
    }

    // ───────────────────────── 工具窗口（左 / 右 / 底） ─────────────────────────

    private void BuildToolWindows()
    {
        AddToolWindow("explorer", "\ue611", L(AppLanguageManager.Instance.workbench_activityInstances.CurrentValue()),
            ToolStripe.Left, LeftStripeTop, "Alt+1");
        AddToolWindow("accounts", "\ue63e", L(AppLanguageManager.Instance.workbench_activityAccounts.CurrentValue()),
            ToolStripe.Left, LeftStripeTop, "Alt+2");
        AddToolWindow("downloads", "\ue622", L(AppLanguageManager.Instance.workbench_activityDownloads.CurrentValue()),
            ToolStripe.Left, LeftStripeTop, "Alt+3");
        AddToolWindow("creator", "\ue60a", L(AppLanguageManager.Instance.workbench_activityCreator.CurrentValue()),
            ToolStripe.Left, LeftStripeTop);
        AddToolWindow("tools", "\ue641", L(AppLanguageManager.Instance.workbench_activityTools.CurrentValue()),
            ToolStripe.Left, LeftStripeTop);

        AddToolWindow("news", "\ue62a", L(AppLanguageManager.Instance.workbench_activityNews.CurrentValue()),
            ToolStripe.Right, RightStripeTop);
        AddToolWindow("skins", "\ue618", L(AppLanguageManager.Instance.workbench_activitySkins.CurrentValue()),
            ToolStripe.Right, RightStripeTop);
        AddToolWindow("stats", "\ue63b", L(AppLanguageManager.Instance.workbench_activityStats.CurrentValue()),
            ToolStripe.Right, RightStripeTop);

        // 底部工具窗口的按钮先建到临时容器，再按 IDEA 的习惯塞进左条下半段（设置之上）。
        var bottomButtons = new StackPanel();
        AddToolWindow("output", "\ue633", L(AppLanguageManager.Instance.workbench_panelOutput.CurrentValue()),
            ToolStripe.Bottom, bottomButtons, "Alt+F12");
        AddToolWindow("problems", "\ue63a", L(AppLanguageManager.Instance.workbench_panelProblems.CurrentValue()),
            ToolStripe.Bottom, bottomButtons, "Alt+6");
        AddToolWindow("notifications", "\ue60d", L(AppLanguageManager.Instance.workbench_panelNotifications.CurrentValue()),
            ToolStripe.Bottom, bottomButtons);

        foreach (var child in bottomButtons.Children.ToList())
        {
            bottomButtons.Children.Remove(child);
            LeftStripeBottom.Children.Add(child);
        }

        LeftStripeBottom.Children.Add(BuildStripeSeparator());
        LeftStripeBottom.Children.Add(BuildStripeButton("settings", "\ue617",
            L(AppLanguageManager.Instance.workbench_activitySettings.CurrentValue()),
            () => _commands.Execute("workbench.openSettings")));
    }

    private void AddToolWindow(string id, string glyph, string title, ToolStripe stripe, StackPanel host,
        string? gesture = null)
    {
        var entry = new ToolWindowEntry(id, glyph, title, stripe);
        _toolWindows.Add(entry);

        _commands.Register(new WorkbenchCommand
        {
            Id = $"workbench.view.{id}",
            Title = title,
            Category = MenuView,
            Glyph = glyph,
            Gesture = gesture,
            Execute = () => ShowToolWindow(id)
        });

        var grid = BuildStripeButton(id, glyph, title, () => ToggleToolWindow(id));
        host.Children.Add(grid);

        var button = (Button)grid.Children[1];
        var indicator = (Border)grid.Children[0];
        _toolVisuals[id] = ((TextBlock)button.Content!, indicator);
    }

    private static Grid BuildStripeButton(string id, string glyph, string title, Action onClick)
    {
        var indicator = new Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = WorkbenchTheme.Accent,
            IsVisible = false
        };

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 19,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        var button = new Button
        {
            Classes = { "wb-stripe" },
            Content = icon
        };
        ToolTip.SetTip(button, title);
        button.Click += (_, _) => onClick();

        var grid = new Grid
        {
            Width = 44,
            Height = 40,
            Tag = id
        };
        grid.Children.Add(indicator);
        grid.Children.Add(button);
        return grid;
    }

    private static Border BuildStripeSeparator() => new()
    {
        Width = 28,
        Height = 1,
        Margin = new Thickness(8, 4, 8, 4),
        Background = WorkbenchTheme.Border
    };

    private void ToggleToolWindow(string id)
    {
        var entry = _toolWindows.FirstOrDefault(t => t.Id == id);
        if (entry is null)
            return;

        switch (entry.Stripe)
        {
            case ToolStripe.Left:
                if (_leftTool?.Id == id && _leftVisible)
                {
                    SetLeftVisible(false);
                    return;
                }

                _leftTool = entry;
                SetLeftVisible(true);
                break;

            case ToolStripe.Right:
                if (_rightTool?.Id == id && _rightVisible)
                {
                    SetRightVisible(false);
                    return;
                }

                _rightTool = entry;
                SetRightVisible(true);
                break;

            default:
                if (_bottomTool?.Id == id && _bottomVisible)
                {
                    SetBottomVisible(false);
                    return;
                }

                _bottomTool = entry;
                SetBottomVisible(true);
                break;
        }

        CaptureCurrentLayout();
    }

    private void ShowToolWindow(string id)
    {
        var entry = _toolWindows.FirstOrDefault(t => t.Id == id);
        if (entry is null)
            return;

        switch (entry.Stripe)
        {
            case ToolStripe.Left:
                _leftTool = entry;
                SetLeftVisible(true);
                break;
            case ToolStripe.Right:
                _rightTool = entry;
                SetRightVisible(true);
                break;
            default:
                _bottomTool = entry;
                SetBottomVisible(true);
                break;
        }

        CaptureCurrentLayout();
    }

    private void SetLeftVisible(bool visible, bool save = true)
    {
        _leftVisible = visible;
        LeftToolWindow.IsVisible = visible;
        MainArea.ColumnDefinitions[1].Width = visible
            ? new GridLength(Math.Max(180, _layout.SideBarWidth))
            : new GridLength(0);
        MainArea.ColumnDefinitions[2].Width = visible ? new GridLength(4) : new GridLength(0);

        if (visible)
            RenderLeftTool();

        UpdateToolVisuals();

        if (save)
            CaptureCurrentLayout();
    }

    private void SetRightVisible(bool visible, bool save = true)
    {
        _rightVisible = visible;
        RightToolWindow.IsVisible = visible;
        MainArea.ColumnDefinitions[5].Width = visible
            ? new GridLength(Math.Max(200, _layout.RightBarWidth))
            : new GridLength(0);
        MainArea.ColumnDefinitions[4].Width = visible ? new GridLength(4) : new GridLength(0);

        if (visible)
            RenderRightTool();

        UpdateToolVisuals();

        if (save)
            CaptureCurrentLayout();
    }

    private void SetBottomVisible(bool visible, bool save = true)
    {
        _bottomVisible = visible;
        BottomToolWindow.IsVisible = visible;
        PanelSplitter.IsVisible = visible;
        EditorArea.RowDefinitions[2].Height = visible ? new GridLength(4) : new GridLength(0);
        EditorArea.RowDefinitions[3].Height = visible
            ? new GridLength(Math.Max(80, _layout.PanelHeight))
            : new GridLength(0);

        if (visible)
            RenderBottomTool();

        UpdateToolVisuals();

        if (save)
            CaptureCurrentLayout();
    }

    private void RenderLeftTool()
    {
        if (_leftTool is null)
            return;

        LeftToolTitle.Text = _leftTool.Title.ToUpperInvariant();
        BuildToolWindowActions(LeftToolActions, _leftTool);
        LeftToolHost.Content = BuildToolContent(_leftTool);
    }

    private void RenderRightTool()
    {
        if (_rightTool is null)
            return;

        RightToolTitle.Text = _rightTool.Title.ToUpperInvariant();
        BuildToolWindowActions(RightToolActions, _rightTool);
        RightToolHost.Content = BuildToolContent(_rightTool);
    }

    private void RenderBottomTool()
    {
        if (_bottomTool is null)
            return;

        RebuildPanelTabs();
        PanelHost.Content = BuildToolContent(_bottomTool);

        PanelTools.Children.Clear();
        if (_bottomTool.Id == "output")
        {
            PanelTools.Children.Add(BuildChannelPicker());
            PanelTools.Children.Add(BuildToolHeaderButton("\ue609", "清空当前通道",
                () => _commands.Execute("mchub.output.clear")));
        }
    }

    private void BuildToolWindowActions(StackPanel host, ToolWindowEntry entry)
    {
        host.Children.Clear();

        if (entry.Id is "explorer" or "accounts")
        {
            host.Children.Add(BuildToolHeaderButton("\ue63c", "刷新",
                () => _ = RefreshInstancesAsync()));
        }

        host.Children.Add(BuildToolHeaderButton("\ue608",
            L(AppLanguageManager.Instance.workbench_hideToolWindow.CurrentValue()),
            () => ToggleToolWindow(entry.Id)));
    }

    private static Button BuildToolHeaderButton(string glyph, string tip, Action onClick)
    {
        var button = new Button
        {
            Classes = { "wb-tool-action" },
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 11,
                Foreground = WorkbenchTheme.ForegroundMuted
            }
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void UpdateToolVisuals()
    {
        foreach (var (id, visual) in _toolVisuals)
        {
            var entry = _toolWindows.FirstOrDefault(t => t.Id == id);
            if (entry is null)
                continue;

            var active = entry.Stripe switch
            {
                ToolStripe.Left => _leftTool?.Id == id && _leftVisible,
                ToolStripe.Right => _rightTool?.Id == id && _rightVisible,
                _ => _bottomTool?.Id == id && _bottomVisible
            };

            visual.Indicator.IsVisible = active;
            visual.Icon.Foreground = active ? WorkbenchTheme.Foreground : WorkbenchTheme.ForegroundMuted;
        }
    }

    private Control BuildToolContent(ToolWindowEntry entry)
    {
        switch (entry.Id)
        {
            case "explorer":
                if (_explorer is null)
                {
                    _explorer = new ExplorerView();
                    _explorer.InstanceActivated += OpenInstanceOverview;
                    _explorer.LaunchRequested += instance =>
                    {
                        InstanceContext.Current = instance;
                        _commands.Execute("mchub.instance.launch");
                    };
                    _explorer.CreateRequested += () => _output.Write(OutputService.ChannelWorkbench,
                        "新建实例向导会在下载/安装功能接入后启用。");
                }

                _explorer.Rebuild();
                return _explorer;

            case "accounts":
                if (_accounts is null)
                {
                    _accounts = new AccountsView();
                    _accounts.AccountsChanged += UpdateStatusBar;
                }

                _accounts.Rebuild();
                return _accounts;

            case "output":
                return _outputView ??= BuildOutputView();

            case "problems":
                return BuildEmptyToolView("\ue63a", entry.Title,
                    "没有发现问题。代码检查会在实例配置分析接入后启用。");

            case "notifications":
                return BuildEmptyToolView("\ue60d", entry.Title,
                    "没有新通知。启动失败、下载异常会出现在这里。");

            case "news":
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    "新闻与更新日志将以列表形式接回（P3 待迁移）。");

            case "skins":
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    "皮肤库与皮肤预览将以独立编辑器标签接回（P3 待迁移）。");

            case "stats":
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    "游玩时长与启动次数统计将在实例数据接回后显示。");

            case "downloads":
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    "下载中心（游戏版本 / 模组 / 整合包）为 P3 待迁移项。");

            case "creator":
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    "创作工具（截图库、素材清单、录像、结构编辑器）为 P4 待迁移项。");

            case "tools":
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    "工具集合（Java 管理、配置修复等）为 P3 待迁移项。");

            default:
                return BuildEmptyToolView(entry.Glyph, entry.Title,
                    L(AppLanguageManager.Instance.workbench_sidebarPlaceholder.CurrentValue()));
        }
    }

    private static Control BuildEmptyToolView(string glyph, string title, string hint)
    {
        return new StackPanel
        {
            Margin = new Thickness(16, 18),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = glyph,
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 22,
                    Foreground = WorkbenchTheme.ForegroundFaint
                },
                new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    Foreground = WorkbenchTheme.Foreground
                },
                new TextBlock
                {
                    Text = hint,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    Foreground = WorkbenchTheme.ForegroundFaint
                }
            }
        };
    }

    // ───────────────────────── 主工具栏 ─────────────────────────

    private void BuildToolbar()
    {
        BuildProjectWidget();
        BuildRunWidget();

        ToolbarRight.Children.Add(BuildToolbarIcon("\ue615",
            L(AppLanguageManager.Instance.workbench_searchEverywhere.CurrentValue()),
            () => OpenPalette(PaletteMode.SearchEverywhere)));
        ToolbarRight.Children.Add(BuildToolbarIcon("\ue617",
            L(AppLanguageManager.Instance.workbench_activitySettings.CurrentValue()),
            () => _commands.Execute("workbench.openSettings")));
        ToolbarRight.Children.Add(BuildAccountWidget());
    }

    private void BuildProjectWidget()
    {
        var label = new TextBlock
        {
            Text = L(AppLanguageManager.Instance.workbench_toolbarNoInstance.CurrentValue()),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.Foreground
        };
        _projectLabel = label;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "\ue651",
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.Accent
                },
                label,
                new TextBlock
                {
                    Text = "\ue623",
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.ForegroundFaint
                }
            }
        };

        var button = new Button
        {
            Classes = { "wb-toolbar" },
            Content = content
        };
        ToolTip.SetTip(button, "当前实例 · 切换或打开其他实例");

        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) => RebuildProjectFlyout(flyout);
        button.Flyout = flyout;

        ToolbarProject.Children.Add(button);
    }

    private void RebuildProjectFlyout(MenuFlyout flyout)
    {
        flyout.Items.Clear();

        var instances = InstanceManager.Instance.Instances;
        if (instances.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "还没有实例", IsEnabled = false });
        }
        else
        {
            foreach (var instance in instances.Take(40))
            {
                var target = instance;
                var item = new MenuItem { Header = $"{target.InstanceName}    {target.VersionId}" };
                item.Click += (_, _) => OpenInstanceOverview(target);
                flyout.Items.Add(item);
            }

            if (instances.Count > 40)
                flyout.Items.Add(new MenuItem { Header = $"…还有 {instances.Count - 40} 个", IsEnabled = false });
        }

        flyout.Items.Add(new Separator());

        var open = new MenuItem
        {
            Header = L(AppLanguageManager.Instance.workbench_activityInstances.CurrentValue())
        };
        open.Click += (_, _) => ShowToolWindow("explorer");
        flyout.Items.Add(open);

        var refresh = new MenuItem { Header = "刷新实例列表" };
        refresh.Click += (_, _) => _ = RefreshInstancesAsync();
        flyout.Items.Add(refresh);
    }

    private void BuildRunWidget()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "\ue607",
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.Brush("WbSuccess", "#3C8527")
                },
                new TextBlock
                {
                    Text = L(AppLanguageManager.Instance.workbench_toolbarRun.CurrentValue()),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.Foreground
                }
            }
        };

        _runButton = new Button
        {
            Classes = { "wb-toolbar" },
            Content = content,
            IsEnabled = false
        };
        ToolTip.SetTip(_runButton, "启动当前实例（F5）");
        _runButton.Click += (_, _) => _commands.Execute("mchub.instance.launch");

        var more = new Button
        {
            Classes = { "wb-toolbar" },
            Padding = new Thickness(6, 0),
            Content = new TextBlock
            {
                Text = "\ue623",
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WorkbenchTheme.ForegroundMuted
            }
        };
        ToolTip.SetTip(more, "运行选项");

        var flyout = new MenuFlyout();
        var overview = new MenuItem { Header = "打开实例概览" };
        overview.Click += (_, _) => _commands.Execute("mchub.instance.open");
        var reveal = new MenuItem { Header = "打开实例文件夹" };
        reveal.Click += (_, _) => _commands.Execute("mchub.instance.revealFolder");
        flyout.Items.Add(overview);
        flyout.Items.Add(reveal);
        more.Flyout = flyout;

        ToolbarRun.Children.Add(_runButton);
        ToolbarRun.Children.Add(more);
    }

    private static Button BuildToolbarIcon(string glyph, string tip, Action onClick)
    {
        var button = new Button
        {
            Classes = { "wb-toolbar" },
            Padding = new Thickness(7, 0),
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WorkbenchTheme.ForegroundMuted
            }
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private Button BuildAccountWidget()
    {
        var initial = new TextBlock
        {
            Text = "?",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.Foreground
        };
        _accountInitial = initial;

        // 中性底色而不是强调蓝：蓝色实心圆放在工具栏右上角太抢眼，像关闭按钮。
        var avatar = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = WorkbenchTheme.Brush("WbHoverBg", "#393B40"),
            BorderBrush = WorkbenchTheme.Border,
            BorderThickness = new Thickness(1),
            Child = initial
        };

        var button = new Button
        {
            Classes = { "wb-toolbar" },
            Padding = new Thickness(5, 0),
            Content = avatar
        };
        ToolTip.SetTip(button, "账户");
        button.Click += (_, _) => ShowToolWindow("accounts");
        return button;
    }

    // ───────────────────────── 状态栏（导航栏 + 部件区） ─────────────────────────

    private void BuildStatusBar()
    {
        _statusMode = new TextBlock
        {
            Text = L(AppLanguageManager.Instance.workbench_ready.CurrentValue()),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
            Foreground = WorkbenchTheme.StatusFg
        };

        StatusLeft.Children.Add(_statusMode);
        StatusLeft.Children.Add(new Border
        {
            Width = 1,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
            Background = WorkbenchTheme.BorderStrong
        });

        _breadcrumb = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        StatusLeft.Children.Add(_breadcrumb);

        _statusMemory = new TextBlock
        {
            Text = "—",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Foreground = WorkbenchTheme.StatusFg
        };
        ToolTip.SetTip(_statusMemory,
            L(AppLanguageManager.Instance.workbench_statusMemory.CurrentValue()));

        _statusInstances = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Foreground = WorkbenchTheme.StatusFg
        };

        _statusAccount = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.StatusFg
        };

        var accountButton = new Button
        {
            Content = _statusAccount,
            Classes = { "wb-widget" }
        };
        ToolTip.SetTip(accountButton, "管理账户");
        accountButton.Click += (_, _) => ShowToolWindow("accounts");

        StatusRight.Children.Add(_statusMemory);
        StatusRight.Children.Add(_statusInstances);
        StatusRight.Children.Add(accountButton);

        RebuildBreadcrumb();
    }

    private void RebuildBreadcrumb()
    {
        if (_breadcrumb is null)
            return;

        _breadcrumb.Children.Clear();

        var instance = _documents.Active is InstanceOverviewDocument document
            ? document.Instance
            : InstanceContext.Current;

        _breadcrumb.Children.Add(BuildBreadcrumbLink(
            L(AppLanguageManager.Instance.workbench_activityInstances.CurrentValue()),
            () => ShowToolWindow("explorer")));
        _breadcrumb.Children.Add(BuildBreadcrumbChevron());

        if (instance is null)
        {
            _breadcrumb.Children.Add(BuildBreadcrumbText(
                L(AppLanguageManager.Instance.workbench_statusNoInstance.CurrentValue())));
            return;
        }

        _breadcrumb.Children.Add(BuildBreadcrumbLink(instance.InstanceName, () => OpenInstanceOverview(instance)));
        _breadcrumb.Children.Add(BuildBreadcrumbChevron());
        _breadcrumb.Children.Add(BuildBreadcrumbText(instance.VersionId));
    }

    private static Button BuildBreadcrumbLink(string text, Action onClick)
    {
        var button = new Button
        {
            Classes = { "wb-widget" },
            Content = new TextBlock
            {
                Text = text,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static TextBlock BuildBreadcrumbChevron() => new()
    {
        Text = "\ue612",
        FontFamily = WorkbenchTheme.IconFont,
        FontSize = 8,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = WorkbenchTheme.ForegroundFaint
    };

    private static TextBlock BuildBreadcrumbText(string text) => new()
    {
        Text = text,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 0, 0),
        Foreground = WorkbenchTheme.ForegroundMuted
    };

    private void StartMemoryTimer()
    {
        _memoryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _memoryTimer.Tick -= OnMemoryTick;
        _memoryTimer.Tick += OnMemoryTick;
        _memoryTimer.Start();
        OnMemoryTick(null, EventArgs.Empty);
    }

    private void OnMemoryTick(object? sender, EventArgs e)
    {
        if (_statusMemory is null)
            return;

        try
        {
            using var process = Process.GetCurrentProcess();
            _statusMemory.Text = $"{process.WorkingSet64 / (1024 * 1024)} MB";
        }
        catch
        {
            _statusMemory.Text = "—";
        }
    }

    private void UpdateStatusBar()
    {
        if (_statusMode is not null)
            _statusMode.Text = InstanceLauncher.IsLaunching
                ? L(AppLanguageManager.Instance.workbench_statusLaunching.CurrentValue())
                : L(AppLanguageManager.Instance.workbench_ready.CurrentValue());

        if (_statusAccount is not null)
            _statusAccount.Text = Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name
                                  ?? L(AppLanguageManager.Instance.workbench_statusNoAccount.CurrentValue());

        var account = Data.ConfigEntry.UsingMinecraftMinecraftAccount;
        if (_accountInitial is not null)
            _accountInitial.Text = string.IsNullOrEmpty(account?.Name)
                ? "?"
                : account.Name[..1].ToUpperInvariant();

        if (_statusInstances is not null)
            _statusInstances.Text = string.Format(
                L(AppLanguageManager.Instance.workbench_statusInstanceCount.CurrentValue()),
                InstanceManager.Instance.Instances.Count);

        var target = ResolveLaunchTarget();
        if (_projectLabel is not null)
            _projectLabel.Text = target?.InstanceName
                                 ?? L(AppLanguageManager.Instance.workbench_toolbarNoInstance.CurrentValue());

        if (_runButton is not null)
            _runButton.IsEnabled = target is not null && !InstanceLauncher.IsLaunching;

        RebuildBreadcrumb();
    }

    // ───────────────────────── 菜单栏 ─────────────────────────

    private void BuildMenus()
    {
        AddMenu(L(AppLanguageManager.Instance.workbench_menuFile.CurrentValue()),
            "mchub.instances.open",
            "mchub.instance.open",
            "-",
            "workbench.closeAllEditors",
            "-",
            "workbench.exit");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuEdit.CurrentValue()),
            "workbench.findAction",
            "workbench.searchEverywhere",
            "-",
            "mchub.output.copy",
            "mchub.output.clear");

        var toolWindowIds = _toolWindows.Select(t => $"workbench.view.{t.Id}").ToArray();
        AddMenu(MenuView,
            (L(AppLanguageManager.Instance.workbench_toolWindows.CurrentValue()), toolWindowIds),
            "-",
            "workbench.toggleLeftToolWindow",
            "workbench.toggleRightToolWindow",
            "workbench.toggleBottomToolWindow");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuRun.CurrentValue()),
            "mchub.instance.launch",
            "mchub.instance.open",
            "mchub.instance.revealFolder");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuTools.CurrentValue()),
            "mchub.instances.refresh",
            "mchub.accounts.add",
            "-",
            "workbench.openSettings");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuHelp.CurrentValue()),
            "workbench.showLogFolder",
            "workbench.about");
    }

    private void AddMenu(string header, params object[] items)
    {
        var button = new Button
        {
            Content = header,
            Classes = { "wb-flat" },
            Height = 30,
            Padding = new Thickness(10, 0)
        };

        var flyout = new MenuFlyout();
        foreach (var item in items)
        {
            switch (item)
            {
                case "-":
                    flyout.Items.Add(new Separator());
                    break;
                case string id:
                    flyout.Items.Add(BuildMenuItem(id));
                    break;
                case ValueTuple<string, string[]> group:
                    flyout.Items.Add(BuildSubMenu(group.Item1, group.Item2));
                    break;
            }
        }

        button.Flyout = flyout;
        MenuStrip.Children.Add(button);
    }

    private MenuItem BuildMenuItem(string commandId)
    {
        var command = _commands.Find(commandId);
        var text = command?.Title ?? commandId;
        if (!string.IsNullOrEmpty(command?.Gesture))
            text = $"{text}\t{command.Gesture}";

        var item = new MenuItem { Header = text, IsEnabled = command?.IsEnabled ?? false };
        item.Click += (_, _) => _commands.Execute(commandId);
        return item;
    }

    private MenuItem BuildSubMenu(string header, string[] commandIds)
    {
        var menu = new MenuItem { Header = header };
        foreach (var id in commandIds)
            menu.Items.Add(BuildMenuItem(id));
        return menu;
    }

    // ───────────────────────── 底部工具窗口标签 ─────────────────────────

    private void RebuildPanelTabs()
    {
        PanelTabs.Children.Clear();

        foreach (var entry in _toolWindows.Where(t => t.Stripe == ToolStripe.Bottom))
        {
            var active = _bottomTool?.Id == entry.Id;

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = entry.Glyph,
                        FontFamily = WorkbenchTheme.IconFont,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = active ? WorkbenchTheme.Foreground : WorkbenchTheme.ForegroundFaint
                    },
                    new TextBlock
                    {
                        Text = entry.Title,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = active ? WorkbenchTheme.Foreground : WorkbenchTheme.ForegroundMuted
                    }
                }
            };

            var button = new Button
            {
                Content = content,
                Classes = { "wb-flat" },
                Height = 29,
                Padding = new Thickness(10, 0)
            };
            var id = entry.Id;
            button.Click += (_, _) => ShowToolWindow(id);

            PanelTabs.Children.Add(new Border
            {
                BorderBrush = WorkbenchTheme.Accent,
                BorderThickness = active ? new Thickness(0, 0, 0, 2) : new Thickness(0),
                Child = button
            });
        }
    }

    private ComboBox BuildChannelPicker()
    {
        var channelNames = new[]
        {
            OutputService.ChannelWorkbench,
            OutputService.ChannelLaunch,
            OutputService.ChannelGame,
            OutputService.ChannelDownload
        };

        _channelPicker = new ComboBox
        {
            ItemsSource = channelNames,
            SelectedIndex = Math.Max(0, Array.IndexOf(channelNames, _activeChannel)),
            Width = 118,
            Height = 24,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _channelPicker.SelectionChanged += (_, _) =>
        {
            if (_channelPicker?.SelectedItem is string name)
                _activeChannel = name;
            ScheduleOutputRefresh();
        };

        return _channelPicker;
    }

    private Control BuildOutputView()
    {
        _outputBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = WorkbenchTheme.CodeFont,
            FontSize = 12,
            Foreground = WorkbenchTheme.ForegroundMuted,
            Padding = new Thickness(12, 8),
            Text = _output.Get(_activeChannel).Text
        };

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _outputBox
        };
    }

    private void OnOutputChannelChanged(object? sender, OutputChannel channel)
    {
        if (channel.Name != _activeChannel || _outputBox is null || !_bottomVisible)
            return;

        ScheduleOutputRefresh();
    }

    private void ScheduleOutputRefresh()
    {
        if (_outputRefreshPending)
            return;

        _outputRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _outputRefreshPending = false;
            if (_outputBox is null) return;

            _outputBox.Text = _output.Get(_activeChannel).Text;
            _outputBox.CaretIndex = _outputBox.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    // ───────────────────────── 编辑器 ─────────────────────────

    private void BuildInspectionWidget()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "\ue61a",
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.ForegroundFaint
                },
                new TextBlock
                {
                    Text = "0",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.ForegroundFaint
                }
            }
        };

        InspectionWidget.Content = content;
        ToolTip.SetTip(InspectionWidget, "没有发现问题");
        InspectionWidget.Click += (_, _) => ShowToolWindow("problems");
    }

    private void RefreshEditor()
    {
        RebuildTabs();

        var active = _documents.Active;
        if (active is null)
        {
            EditorHost.Content = null;
            EmptyState.IsVisible = true;
            return;
        }

        EmptyState.IsVisible = false;
        EditorHost.Content = active.View;
    }

    private void RebuildTabs()
    {
        TabStrip.Children.Clear();

        foreach (var document in _documents.Documents)
            TabStrip.Children.Add(BuildTab(document));
    }

    private Control BuildTab(IWorkbenchDocument document)
    {
        var isActive = ReferenceEquals(document, _documents.Active);

        var icon = new TextBlock
        {
            Text = document.Glyph ?? "\ue638",
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isActive ? WorkbenchTheme.Accent : WorkbenchTheme.ForegroundFaint
        };

        var title = new TextBlock
        {
            Text = document.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isActive ? WorkbenchTheme.Foreground : WorkbenchTheme.ForegroundMuted
        };

        var close = new Button
        {
            Content = new TextBlock
            {
                Text = "\ue609",
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 10,
                Foreground = WorkbenchTheme.ForegroundMuted
            },
            Classes = { "wb-flat" },
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = isActive || document.IsDirty
        };
        close.Click += (_, _) => _documents.Close(document);

        var tab = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(7, 7, 0, 0),
            Padding = new Thickness(10, 0, 6, 0),
            Margin = new Thickness(0, 6, 2, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, title, close }
            }
        };

        // 激活标签用编辑器底色（和 IDEA 一致），未激活的交给 :pointerover 样式。
        if (isActive)
            tab.Background = WorkbenchTheme.EditorBg;
        else
            tab.Classes.Add("wb-tab");

        tab.PointerPressed += (_, _) => _documents.Activate(document);
        return tab;
    }

    private void UpdateEmptyState()
    {
        EmptyTitle.Text = L(AppLanguageManager.Instance.workbench_noEditor.CurrentValue());
        EmptyHint.Text = L(AppLanguageManager.Instance.workbench_noEditorHint.CurrentValue());
    }

    private void OpenInstanceOverview(MinecraftInstance instance)
    {
        var existing = _documents.Documents.FirstOrDefault(d => d.Id == $"mchub://instance/{instance.InstanceFolderPath}");
        if (existing is not null)
        {
            _documents.Activate(existing);
        }
        else
        {
            _documents.Open(new InstanceOverviewDocument(instance));
        }

        InstanceContext.Current = instance;
        _layout.RememberInstance(instance.InstanceFolderPath);
        UpdateStatusBar();
    }

    // ───────────────────────── 命令 ─────────────────────────

    private void RegisterCommands()
    {
        _commands.RegisterRange(
            new WorkbenchCommand
            {
                Id = "workbench.findAction",
                Title = L(AppLanguageManager.Instance.workbench_findAction.CurrentValue()),
                Category = L(AppLanguageManager.Instance.workbench_menuEdit.CurrentValue()),
                Glyph = "\ue615",
                Gesture = "Ctrl+Shift+A",
                Execute = () => Dispatcher.UIThread.Post(() => OpenPalette(PaletteMode.Actions))
            },
            new WorkbenchCommand
            {
                Id = "workbench.searchEverywhere",
                Title = L(AppLanguageManager.Instance.workbench_searchEverywhere.CurrentValue()),
                Category = L(AppLanguageManager.Instance.workbench_menuEdit.CurrentValue()),
                Glyph = "\ue615",
                Execute = () => Dispatcher.UIThread.Post(() => OpenPalette(PaletteMode.SearchEverywhere))
            },
            new WorkbenchCommand
            {
                Id = "workbench.toggleLeftToolWindow",
                Title = "切换左侧工具窗口",
                Category = MenuView,
                Gesture = "Ctrl+B",
                Execute = () => SetLeftVisible(!_leftVisible)
            },
            new WorkbenchCommand
            {
                Id = "workbench.toggleRightToolWindow",
                Title = "切换右侧工具窗口",
                Category = MenuView,
                Execute = () => SetRightVisible(!_rightVisible)
            },
            new WorkbenchCommand
            {
                Id = "workbench.toggleBottomToolWindow",
                Title = "切换底部工具窗口",
                Category = MenuView,
                Gesture = "Ctrl+J",
                Execute = () => SetBottomVisible(!_bottomVisible)
            },
            new WorkbenchCommand
            {
                Id = "workbench.closeAllEditors",
                Title = "关闭所有编辑器",
                Category = L(AppLanguageManager.Instance.workbench_menuFile.CurrentValue()),
                Gesture = "Ctrl+K W",
                Execute = () => _documents.CloseAll()
            },
            new WorkbenchCommand
            {
                Id = "workbench.openSettings",
                Title = "打开设置",
                Category = L(AppLanguageManager.Instance.workbench_menuTools.CurrentValue()),
                Glyph = "\ue617",
                Gesture = "Ctrl+,",
                Execute = () => _output.Write(OutputService.ChannelWorkbench,
                    "设置界面会在下一阶段以编辑器标签的形式接入。")
            },
            new WorkbenchCommand
            {
                Id = "workbench.showLogFolder",
                Title = "打开日志目录",
                Category = L(AppLanguageManager.Instance.workbench_menuHelp.CurrentValue()),
                Execute = () => RevealPath(ConfigPath.LogFolderPath)
            },
            new WorkbenchCommand
            {
                Id = "workbench.about",
                Title = "关于 MChub",
                Category = L(AppLanguageManager.Instance.workbench_menuHelp.CurrentValue()),
                Execute = () =>
                {
                    _output.Write(OutputService.ChannelWorkbench,
                        $"MChub {Data.Instance.Version.VersionTitle} · {Data.Instance.PackageType} · {Data.ConfigEntry.MinecraftFolders.Count} 个实例目录");
                    ShowToolWindow("output");
                }
            },
            new WorkbenchCommand
            {
                Id = "workbench.exit",
                Title = "退出 MChub",
                Category = L(AppLanguageManager.Instance.workbench_menuFile.CurrentValue()),
                Execute = Close
            },
            new WorkbenchCommand
            {
                Id = "mchub.instances.refresh",
                Title = "刷新实例列表",
                Category = "实例",
                Glyph = "\ue63c",
                Gesture = "Ctrl+R",
                Execute = () => _ = RefreshInstancesAsync()
            },
            new WorkbenchCommand
            {
                Id = "mchub.instance.open",
                Title = "在编辑器中打开实例",
                Category = "实例",
                Glyph = "\ue638",
                CanExecute = () => InstanceContext.Current is not null,
                Execute = () =>
                {
                    if (InstanceContext.Current is { } instance)
                        OpenInstanceOverview(instance);
                }
            },
            new WorkbenchCommand
            {
                Id = "mchub.instances.open",
                Title = "打开实例…",
                Category = "实例",
                Glyph = "\ue611",
                Execute = () => ShowToolWindow("explorer")
            },
            new WorkbenchCommand
            {
                Id = "mchub.instance.launch",
                Title = "启动游戏",
                Category = "运行",
                Glyph = "\ue607",
                Gesture = "F5",
                CanExecute = () => ResolveLaunchTarget() is not null && !InstanceLauncher.IsLaunching,
                Execute = () =>
                {
                    if (ResolveLaunchTarget() is { } instance)
                        _ = InstanceLauncher.LaunchAsync(instance);
                }
            },
            new WorkbenchCommand
            {
                Id = "mchub.instance.revealFolder",
                Title = "打开实例文件夹",
                Category = "实例",
                CanExecute = () => InstanceContext.Current is not null,
                Execute = () =>
                {
                    if (InstanceContext.Current is { } instance)
                        RevealPath(instance.InstanceFolderPath);
                }
            },
            new WorkbenchCommand
            {
                Id = "mchub.accounts.add",
                Title = "添加账户…",
                Category = "账户",
                Glyph = "\ue63e",
                Execute = () => _ = AddAccountAsync()
            },
            new WorkbenchCommand
            {
                Id = "mchub.output.clear",
                Title = "清空输出",
                Category = "输出",
                Execute = () => _output.Get(_activeChannel).Clear()
            },
            new WorkbenchCommand
            {
                Id = "mchub.output.copy",
                Title = "复制输出内容",
                Category = "输出",
                Execute = () => _ = CopyOutputAsync()
            });
    }

    /// <summary>启动目标：右键选中的实例优先，其次当前打开的实例文档，最后是唯一实例。</summary>
    private static MinecraftInstance? ResolveLaunchTarget()
    {
        if (InstanceContext.Current is { } context)
            return context;

        if (InstanceManager.Instance.Instances.Count == 1)
            return InstanceManager.Instance.Instances[0];

        return null;
    }

    private async Task RefreshInstancesAsync()
    {
        _output.Write(OutputService.ChannelWorkbench,
            $"开始扫描实例：{Data.ConfigEntry.MinecraftFolders.Count} 个目录");
        try
        {
            var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
            var instances = await InstanceManager.ScanAllAsync(folders);
            InstanceManager.Instance.ApplyInstances(instances);
            _output.Write(OutputService.ChannelWorkbench, $"扫描完成：{instances.Count} 个实例");
        }
        catch (Exception exception)
        {
            _output.Write(OutputService.ChannelWorkbench, $"扫描实例失败：{exception.Message}");
        }

        _explorer?.Rebuild();
        UpdateStatusBar();
    }

    private async Task AddAccountAsync()
    {
        try
        {
            var login = new LoginWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            login.Completed += () =>
            {
                login.Close();
                _accounts?.Rebuild();
                UpdateStatusBar();
                _output.Write(OutputService.ChannelWorkbench,
                    $"账户已更新：当前为「{Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name ?? "未登录"}」");
            };

            await login.ShowDialog(this);
        }
        catch (Exception exception)
        {
            _output.Write(OutputService.ChannelWorkbench, $"打开登录窗口失败：{exception.Message}");
        }
    }

    private async Task CopyOutputAsync()
    {
        var text = _output.Get(_activeChannel).Text;
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private static void RevealPath(string path)
    {
        try
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);

            if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
            else if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            else
                Process.Start("xdg-open", path);
        }
        catch (Exception exception)
        {
            WorkbenchServices.Output.Write(OutputService.ChannelWorkbench, $"打开目录失败：{exception.Message}");
        }
    }

    // ───────────────────────── 搜索 / 查找操作 ─────────────────────────

    private void OpenPalette(PaletteMode mode)
    {
        _paletteMode = mode;
        PaletteOverlay.IsVisible = true;
        PaletteInput.Text = string.Empty;
        PaletteInput.Watermark = mode == PaletteMode.Actions
            ? L(AppLanguageManager.Instance.workbench_findAction.CurrentValue())
            : L(AppLanguageManager.Instance.workbench_searchEverywhere.CurrentValue());
        PaletteFooter.Text = mode == PaletteMode.Actions
            ? L(AppLanguageManager.Instance.workbench_paletteFooterActions.CurrentValue())
            : L(AppLanguageManager.Instance.workbench_paletteFooterSearch.CurrentValue());
        RefreshPalette(string.Empty);
        PaletteInput.Focus();
        _keybindings.ResetChord();
    }

    private void ClosePalette()
    {
        PaletteOverlay.IsVisible = false;
        _paletteIndex = -1;
        _keybindings.ResetChord();
    }

    private void RefreshPalette(string query)
    {
        PaletteList.Children.Clear();
        _paletteItems.Clear();
        _paletteEntries.Clear();
        _paletteIndex = -1;

        var normalized = query.Trim();
        if (normalized.StartsWith('>'))
            normalized = normalized[1..].Trim();

        var source = _paletteMode == PaletteMode.Actions
            ? CollectActionEntries()
            : CollectSearchEntries();

        var matches = string.IsNullOrWhiteSpace(normalized)
            ? source.Take(60)
            : source.Where(e =>
                e.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                e.Subtitle.Contains(normalized, StringComparison.OrdinalIgnoreCase)).Take(60);

        foreach (var entry in matches)
            PaletteList.Children.Add(BuildPaletteItem(entry));

        if (_paletteEntries.Count == 0)
        {
            PaletteList.Children.Add(new TextBlock
            {
                Text = L(AppLanguageManager.Instance.workbench_paletteEmpty.CurrentValue()),
                FontSize = 12,
                Margin = new Thickness(10, 8),
                Foreground = WorkbenchTheme.ForegroundFaint
            });
            return;
        }

        SelectPaletteItem(0);
    }

    private IEnumerable<PaletteEntry> CollectActionEntries()
    {
        return _commands.All.Select(command => new PaletteEntry(
            command.Title,
            string.Join(" · ", new[] { command.Category, command.Gesture }.Where(s => !string.IsNullOrEmpty(s))),
            command.Glyph ?? "\ue611",
            command.IsEnabled,
            () => _commands.Execute(command.Id)));
    }

    private IEnumerable<PaletteEntry> CollectSearchEntries()
    {
        foreach (var entry in CollectActionEntries())
            yield return entry;

        foreach (var instance in InstanceManager.Instance.Instances)
        {
            var target = instance;
            yield return new PaletteEntry(
                target.InstanceName,
                $"实例 · {target.VersionId}",
                "\ue638",
                true,
                () => OpenInstanceOverview(target));
        }

        foreach (var account in Data.ConfigEntry.MinecraftAccounts)
        {
            yield return new PaletteEntry(account.Name, "账户", "\ue63e", true,
                () => ShowToolWindow("accounts"));
        }
    }

    private Control BuildPaletteItem(PaletteEntry entry)
    {
        var glyph = new TextBlock
        {
            Text = entry.Glyph,
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 13,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.Foreground
        };

        var trailing = new TextBlock
        {
            Text = entry.Subtitle,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundFaint
        };

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(trailing, 2);
        title.Margin = new Thickness(8, 0, 8, 0);
        layout.Children.Add(glyph);
        layout.Children.Add(title);
        layout.Children.Add(trailing);

        var item = new Button
        {
            Content = layout,
            Classes = { "wb-flat" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 30,
            Padding = new Thickness(10, 0),
            IsEnabled = entry.Enabled
        };

        item.Click += (_, _) =>
        {
            ClosePalette();
            entry.Invoke();
        };

        _paletteItems.Add(item);
        _paletteEntries.Add(entry);
        return item;
    }

    private void SelectPaletteItem(int index)
    {
        if (_paletteItems.Count == 0) return;

        index = Math.Clamp(index, 0, _paletteItems.Count - 1);

        if (_paletteIndex >= 0 && _paletteIndex < _paletteItems.Count)
            _paletteItems[_paletteIndex].Background = Brushes.Transparent;

        _paletteIndex = index;
        _paletteItems[index].Background = WorkbenchTheme.SelectionBg;
        _paletteItems[index].BringIntoView();
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ClosePalette();
                e.Handled = true;
                break;
            case Key.Down:
                SelectPaletteItem(_paletteIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                SelectPaletteItem(_paletteIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_paletteIndex >= 0 && _paletteIndex < _paletteItems.Count)
                    _paletteItems[_paletteIndex].RaiseEvent(
                        new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 命令面板开着时，输入框优先，只有 Esc 会被上面处理掉。
        if (PaletteOverlay.IsVisible)
        {
            base.OnKeyDown(e);
            return;
        }

        // IDEA 的 Search Everywhere：双击 Shift。按住 Shift 会连发 KeyDown，用时间窗挡掉。
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            if (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastShiftAt).TotalMilliseconds;
                if (elapsed is > 40 and < 420)
                {
                    _lastShiftAt = default;
                    OpenPalette(PaletteMode.SearchEverywhere);
                    e.Handled = true;
                    base.OnKeyDown(e);
                    return;
                }

                _lastShiftAt = now;
            }
            else
            {
                _lastShiftAt = default;
            }
        }

        if (_keybindings.TryHandle(e))
        {
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        base.OnKeyDown(e);
    }
}
