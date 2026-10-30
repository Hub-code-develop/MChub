using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
/// 工作台主窗口：标题菜单栏 / 活动栏 / 侧边栏 / 编辑器 / 底部面板 / 状态栏。
/// 这个类只负责"布局"和"把各部分接起来"，具体功能通过命令注册进来。
/// </summary>
public partial class WorkbenchWindow : Window
{
    private readonly CommandService _commands = new();
    private readonly DocumentService _documents = new();
    private readonly KeybindingService _keybindings = new();
    private readonly OutputService _output = new();
    private readonly WorkbenchLayoutState _layout;

    private readonly List<ActivityEntry> _activities = new();
    private readonly Dictionary<string, (Border Indicator, TextBlock Icon)> _activityVisuals = new();
    private readonly Dictionary<string, Button> _panelTabButtons = new();
    private readonly List<Button> _paletteItems = new();
    private readonly List<WorkbenchCommand> _paletteCommands = new();

    private ActivityEntry? _activeActivity;
    private int _paletteIndex = -1;
    private string _activePanel = "output";

    private ExplorerView? _explorer;
    private AccountsView? _accounts;
    private TextBox? _outputBox;
    private ComboBox? _channelPicker;
    private TextBlock? _statusMode;
    private TextBlock? _statusInstance;
    private TextBlock? _statusAccount;

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
        _keybindings.BindFrom(_commands);

        BuildMenus();
        BuildActivities();
        BuildPanelTabs();
        BuildStatusBar();

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

        PaletteInput.KeyDown += OnPaletteKeyDown;
        PaletteInput.TextChanged += (_, _) => RefreshPalette(PaletteInput.Text ?? string.Empty);
        PaletteOverlay.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, PaletteOverlay))
                ClosePalette();
        };

        SideBarSplitter.DragCompleted += (_, _) => CaptureCurrentLayout();
        PanelSplitter.DragCompleted += (_, _) => CaptureCurrentLayout();
        Resized += (_, _) => { if (WindowState == WindowState.Normal) CaptureCurrentLayout(); };
        Closing += (_, _) => { CaptureCurrentLayout(flush: true); };

        ApplyLayout();
        RefreshEditor();
        UpdateEmptyState();
        UpdateStatusBar();

        Opened += (_, _) =>
        {
            PaletteInput.Watermark = AppLanguageManager.Instance.workbench_paletteHint.CurrentValue();
            _output.Write(OutputService.ChannelWorkbench,
                $"工作台已就绪 · 实例 {InstanceManager.Instance.Instances.Count} 个 · 账户 {Data.ConfigEntry.MinecraftAccounts.Count} 个");
            if (_layout.ActiveActivity is "explorer" or null)
                _explorer?.Rebuild();
        };
    }

    private static string L(string value) => string.IsNullOrEmpty(value) ? string.Empty : value;

    // ───────────────────────── 布局 ─────────────────────────

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

        SetSideBarVisible(_layout.SideBarVisible, save: false);
        SetPanelVisible(_layout.PanelVisible, save: false);

        ShowPanel(_layout.ActivePanel ?? "output", save: false);

        var activity = _layout.ActiveActivity ?? "explorer";
        ShowActivity(activity, save: false);
        if (!_layout.SideBarVisible)
            UpdateActivityVisuals();

        _suppressLayoutSave = false;
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

        var sideBarWidth = MainArea.ColumnDefinitions[1].Width;
        if (sideBarWidth.IsAbsolute && sideBarWidth.Value > 120)
            _layout.SideBarWidth = sideBarWidth.Value;

        var panelHeight = EditorArea.RowDefinitions[2].Height;
        if (panelHeight.IsAbsolute && panelHeight.Value > 60)
            _layout.PanelHeight = panelHeight.Value;

        _layout.SideBarVisible = SideBarRoot.IsVisible;
        _layout.PanelVisible = PanelRoot.IsVisible;
        _layout.ActivePanel = _activePanel;
        _layout.ActiveActivity = _activeActivity?.Id;

        if (flush)
            WorkbenchServices.FlushLayout();
        else
            WorkbenchServices.SaveLayout();
    }

    private void SetSideBarVisible(bool visible, bool save = true)
    {
        SideBarRoot.IsVisible = visible;
        SideBarSplitter.IsVisible = visible;
        MainArea.ColumnDefinitions[1].Width = visible
            ? new GridLength(_layout.SideBarWidth)
            : new GridLength(0);
        MainArea.ColumnDefinitions[2].Width = visible ? new GridLength(4) : new GridLength(0);

        UpdateActivityVisuals();

        if (save)
            CaptureCurrentLayout();
    }

    private void ToggleSideBar() => SetSideBarVisible(!SideBarRoot.IsVisible);

    private void SetPanelVisible(bool visible, bool save = true)
    {
        PanelRoot.IsVisible = visible;
        PanelSplitter.IsVisible = visible;
        EditorArea.RowDefinitions[1].Height = visible ? new GridLength(4) : new GridLength(0);
        EditorArea.RowDefinitions[2].Height = visible ? new GridLength(_layout.PanelHeight) : new GridLength(0);

        if (save)
            CaptureCurrentLayout();
    }

    private void TogglePanel() => SetPanelVisible(!PanelRoot.IsVisible);

    // ───────────────────────── 菜单栏 ─────────────────────────

    private void BuildMenus()
    {
        AddMenu(L(AppLanguageManager.Instance.workbench_menuFile.CurrentValue()),
            "mchub.instances.open",
            "-",
            "workbench.closeAllEditors",
            "-",
            "workbench.exit");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuEdit.CurrentValue()),
            "workbench.showCommands",
            "-",
            "mchub.output.copy",
            "mchub.output.clear");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuView.CurrentValue()),
            "workbench.view.explorer",
            "workbench.view.accounts",
            "-",
            "workbench.toggleSideBar",
            "workbench.togglePanel",
            "-",
            "workbench.showCommands");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuRun.CurrentValue()),
            "mchub.instance.launch");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuTools.CurrentValue()),
            "mchub.instances.refresh",
            "mchub.accounts.add",
            "-",
            "workbench.openSettings");

        AddMenu(L(AppLanguageManager.Instance.workbench_menuHelp.CurrentValue()),
            "workbench.showLogFolder",
            "workbench.about");
    }

    private void AddMenu(string header, params string[] commandIds)
    {
        var button = new Button
        {
            Content = header,
            Classes = { "wb-flat" },
            Height = 32,
            Padding = new Thickness(10, 0)
        };

        if (commandIds.Length > 0)
        {
            var flyout = new MenuFlyout();
            foreach (var commandId in commandIds)
            {
                if (commandId == "-")
                {
                    flyout.Items.Add(new Separator());
                    continue;
                }

                var command = _commands.Find(commandId);
                var text = command?.Title ?? commandId;
                if (!string.IsNullOrEmpty(command?.Gesture))
                    text = $"{text}\t{command.Gesture}";

                var item = new MenuItem { Header = text };
                var id = commandId;
                item.Click += (_, _) => _commands.Execute(id);
                flyout.Items.Add(item);
            }

            button.Flyout = flyout;
        }

        MenuStrip.Children.Add(button);
    }

    // ───────────────────────── 活动栏 ─────────────────────────

    private sealed record ActivityEntry(string Id, string Glyph, string Title, string? CommandId);

    private void BuildActivities()
    {
        AddActivity("explorer", "\ue611", L(AppLanguageManager.Instance.workbench_activityExplorer.CurrentValue()), null);
        AddActivity("accounts", "\ue627", L(AppLanguageManager.Instance.workbench_activityAccounts.CurrentValue()), null);
        AddActivity("search", "\ue615", L(AppLanguageManager.Instance.workbench_activitySearch.CurrentValue()), null);
        AddActivity("downloads", "\ue622", L(AppLanguageManager.Instance.workbench_activityDownloads.CurrentValue()), null);
        AddActivity("creator", "\ue60a", L(AppLanguageManager.Instance.workbench_activityCreator.CurrentValue()), null);
        AddActivity("tools", "\ue641", L(AppLanguageManager.Instance.workbench_activityTools.CurrentValue()), null);

        ActivityBar.Children.Add(new Border { Height = 12, Width = 32, Background = WorkbenchTheme.Border });

        AddActivity("settings", "\ue60d", L(AppLanguageManager.Instance.workbench_activitySettings.CurrentValue()),
            "workbench.openSettings");
    }

    private void AddActivity(string id, string glyph, string title, string? commandId)
    {
        var host = new Grid { Width = 48, Height = 46 };

        var indicator = new Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = WorkbenchTheme.Brush("WbAccent", "#007ACC"),
            IsVisible = false
        };

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        var button = new Button
        {
            Classes = { "wb-activity" },
            Content = icon,
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(button, title);
        button.Click += (_, _) =>
        {
            if (commandId is not null && id == "settings")
            {
                _commands.Execute(commandId);
                return;
            }

            ShowActivity(id);
        };

        host.Children.Add(indicator);
        host.Children.Add(button);
        ActivityBar.Children.Add(host);

        _activities.Add(new ActivityEntry(id, glyph, title, commandId));
        _activityVisuals[id] = (indicator, icon);
    }

    private void ShowActivity(string id, bool save = true)
    {
        var entry = _activities.FirstOrDefault(a => a.Id == id);
        if (entry is null)
            return;

        // 再点一次已经激活的项 → 收起侧边栏（和 VS Code 一致）
        if (_activeActivity?.Id == id && SideBarRoot.IsVisible)
        {
            if (save) SetSideBarVisible(false);
            else SideBarRoot.IsVisible = false;
            return;
        }

        _activeActivity = entry;

        // 只有用户主动点击时才自动展开侧边栏；恢复布局时要尊重「上次是收起的」。
        if (save && !SideBarRoot.IsVisible)
            SetSideBarVisible(true);

        SideBarTitle.Text = entry.Title.ToUpperInvariant();
        SideBarHost.Content = BuildSideBarContent(entry.Id);
        UpdateActivityVisuals();

        if (save)
            CaptureCurrentLayout();
    }

    private Control BuildSideBarContent(string id)
    {
        switch (id)
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

            default:
                return BuildPlaceholder(_activities.FirstOrDefault(a => a.Id == id)?.Title ?? id);
        }
    }

    private void UpdateActivityVisuals()
    {
        foreach (var (id, visual) in _activityVisuals)
        {
            var active = _activeActivity?.Id == id && SideBarRoot.IsVisible;
            visual.Indicator.IsVisible = active;
            visual.Icon.Foreground = active ? WorkbenchTheme.Foreground : WorkbenchTheme.ForegroundMuted;
        }
    }

    private Control BuildPlaceholder(string title)
    {
        return new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    Foreground = WorkbenchTheme.Foreground
                },
                new TextBlock
                {
                    Text = L(AppLanguageManager.Instance.workbench_sidebarPlaceholder.CurrentValue()),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = WorkbenchTheme.ForegroundFaint
                }
            }
        };
    }

    // ───────────────────────── 底部面板 ─────────────────────────

    private void BuildPanelTabs()
    {
        AddPanelTab("output", "\ue633", L(AppLanguageManager.Instance.workbench_panelOutput.CurrentValue()));
        AddPanelTab("tasks", "\ue635", L(AppLanguageManager.Instance.workbench_panelTasks.CurrentValue()));
        AddPanelTab("problems", "\ue63a", L(AppLanguageManager.Instance.workbench_panelProblems.CurrentValue()));
    }

    private void AddPanelTab(string id, string glyph, string title)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = glyph,
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.ForegroundMuted
                },
                new TextBlock
                {
                    Text = title,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var button = new Button
        {
            Content = content,
            Classes = { "wb-flat" },
            Height = 31,
            Margin = new Thickness(0)
        };
        button.Click += (_, _) => ShowPanel(id);

        _panelTabButtons[id] = button;
        PanelTabs.Children.Add(button);
    }

    private void ShowPanel(string id, bool save = true)
    {
        _activePanel = id;

        foreach (var (key, button) in _panelTabButtons)
        {
            button.Foreground = key == id
                ? WorkbenchTheme.Foreground
                : WorkbenchTheme.ForegroundMuted;
        }

        PanelHost.Content = id switch
        {
            "output" => BuildOutputView(),
            "tasks" => BuildPlaceholder(L(AppLanguageManager.Instance.workbench_panelTasks.CurrentValue())),
            _ => BuildPlaceholder(L(AppLanguageManager.Instance.workbench_panelProblems.CurrentValue()))
        };

        if (save)
            CaptureCurrentLayout();
    }

    private Control BuildOutputView()
    {
        // 通道固定四个，即使还没写过内容也先出现在下拉里，免得用户找不到。
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
            SelectedIndex = Math.Max(0, Array.IndexOf(channelNames, _currentChannel)),
            Width = 120,
            Height = 24,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _channelPicker.SelectionChanged += (_, _) =>
        {
            if (_channelPicker?.SelectedItem is string name)
                _currentChannel = name;
            ScheduleOutputRefresh();
        };

        var clearButton = new Button
        {
            Content = new TextBlock
            {
                Text = "\ue609",
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 11,
                Foreground = WorkbenchTheme.ForegroundMuted
            },
            Classes = { "wb-flat" },
            Width = 24,
            Height = 22,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(clearButton, "清空当前通道");
        clearButton.Click += (_, _) => _commands.Execute("mchub.output.clear");

        PanelTools.Children.Clear();
        PanelTools.Children.Add(_channelPicker);
        PanelTools.Children.Add(clearButton);

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
            Text = _output.Get(_currentChannel).Text
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _outputBox
        };

        return scroll;
    }

    private string _currentChannel = OutputService.ChannelWorkbench;

    private void OnOutputChannelChanged(object? sender, OutputChannel channel)
    {
        if (channel.Name != _currentChannel || _outputBox is null)
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

            _outputBox.Text = _output.Get(_currentChannel).Text;
            _outputBox.CaretIndex = _outputBox.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    // ───────────────────────── 状态栏 ─────────────────────────

    private void BuildStatusBar()
    {
        _statusMode = new TextBlock
        {
            Text = L(AppLanguageManager.Instance.workbench_ready.CurrentValue()),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 12, 0),
            Foreground = WorkbenchTheme.Brush("WbStatusFg", "#FFFFFF")
        };

        _statusInstance = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Foreground = WorkbenchTheme.Brush("WbStatusFg", "#FFFFFF")
        };

        _statusAccount = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.Brush("WbStatusFg", "#FFFFFF")
        };

        StatusLeft.Children.Add(_statusMode);
        StatusLeft.Children.Add(_statusInstance);
        StatusRight.Children.Add(BuildStatusButton(_statusAccount, "mchub.accounts.add"));
        StatusRight.Children.Add(StatusText("MChub"));
    }

    private static Button BuildStatusButton(TextBlock text, string commandId)
    {
        var button = new Button
        {
            Content = text,
            Classes = { "wb-status" }
        };
        ToolTip.SetTip(button, "管理账户");
        button.Click += (_, _) => WorkbenchServices.Commands.Execute("workbench.view.accounts");
        return button;
    }

    private static TextBlock StatusText(string text) => new()
    {
        Text = text,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 14, 0),
        Foreground = WorkbenchTheme.Brush("WbStatusFg", "#FFFFFF")
    };

    private void UpdateStatusBar()
    {
        if (_statusMode is not null)
            _statusMode.Text = InstanceLauncher.IsLaunching
                ? L(AppLanguageManager.Instance.workbench_statusLaunching.CurrentValue())
                : L(AppLanguageManager.Instance.workbench_ready.CurrentValue());

        if (_statusInstance is not null)
        {
            // 优先显示当前编辑器里那个实例，否则显示实例总数。
            _statusInstance.Text = _documents.Active is InstanceOverviewDocument document
                ? document.Instance.InstanceName
                : string.Format(L(AppLanguageManager.Instance.workbench_statusInstanceCount.CurrentValue()),
                    InstanceManager.Instance.Instances.Count);
        }

        if (_statusAccount is not null)
            _statusAccount.Text = Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name
                                  ?? L(AppLanguageManager.Instance.workbench_statusNoAccount.CurrentValue());
    }

    // ───────────────────────── 编辑器 ─────────────────────────

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
                FontSize = 11,
                Foreground = WorkbenchTheme.ForegroundMuted
            },
            Classes = { "wb-flat" },
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = isActive || document.IsDirty
        };
        close.Click += (_, _) => _documents.Close(document);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, close }
        };

        var tab = new Border
        {
            Height = 34,
            Padding = new Thickness(14, 0, 6, 0),
            Background = isActive ? WorkbenchTheme.EditorBg : Brushes.Transparent,
            BorderBrush = WorkbenchTheme.Accent,
            BorderThickness = isActive ? new Thickness(0, 1, 0, 0) : new Thickness(0),
            Child = content
        };

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
                Id = "workbench.showCommands",
                Title = "显示所有命令",
                Category = "视图",
                Glyph = "\ue615",
                Gesture = "Ctrl+Shift+P",
                Execute = () => Dispatcher.UIThread.Post(OpenPalette)
            },
            new WorkbenchCommand
            {
                Id = "workbench.toggleSideBar",
                Title = "切换侧边栏",
                Category = "视图",
                Gesture = "Ctrl+B",
                Execute = ToggleSideBar
            },
            new WorkbenchCommand
            {
                Id = "workbench.togglePanel",
                Title = "切换底部面板",
                Category = "视图",
                Gesture = "Ctrl+J",
                Execute = TogglePanel
            },
            new WorkbenchCommand
            {
                Id = "workbench.closeAllEditors",
                Title = "关闭所有编辑器",
                Category = "文件",
                Gesture = "Ctrl+K W",
                Execute = () => _documents.CloseAll()
            },
            new WorkbenchCommand
            {
                Id = "workbench.view.explorer",
                Title = "显示资源管理器",
                Category = "视图",
                Glyph = "\ue611",
                Execute = () => ShowActivity("explorer")
            },
            new WorkbenchCommand
            {
                Id = "workbench.view.accounts",
                Title = "显示账户",
                Category = "视图",
                Glyph = "\ue627",
                Execute = () => ShowActivity("accounts")
            },
            new WorkbenchCommand
            {
                Id = "workbench.openSettings",
                Title = "打开设置",
                Category = "工具",
                Glyph = "\ue60d",
                Gesture = "Ctrl+,",
                Execute = () => _output.Write(OutputService.ChannelWorkbench,
                    "设置界面会在下一阶段以编辑器标签的形式接入。")
            },
            new WorkbenchCommand
            {
                Id = "workbench.showLogFolder",
                Title = "打开日志目录",
                Category = "帮助",
                Execute = () => RevealPath(ConfigPath.LogFolderPath)
            },
            new WorkbenchCommand
            {
                Id = "workbench.about",
                Title = "关于 MChub",
                Category = "帮助",
                Execute = () =>
                {
                    _output.Write(OutputService.ChannelWorkbench,
                        $"MChub {Data.Instance.Version.VersionTitle} · {Data.Instance.PackageType} · {Data.ConfigEntry.MinecraftFolders.Count} 个实例目录");
                    ShowPanel("output");
                }
            },
            new WorkbenchCommand
            {
                Id = "workbench.exit",
                Title = "退出 MChub",
                Category = "文件",
                Execute = Close
            },
            new WorkbenchCommand
            {
                Id = "mchub.instances.refresh",
                Title = "刷新实例列表",
                Category = "实例",
                Glyph = "\ue627",
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
                Glyph = "\ue638",
                Execute = () => ShowActivity("explorer")
            },
            new WorkbenchCommand
            {
                Id = "mchub.instance.launch",
                Title = "启动游戏",
                Category = "运行",
                Glyph = "\ue62c",
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
                Glyph = "\ue627",
                Execute = () => _ = AddAccountAsync()
            },
            new WorkbenchCommand
            {
                Id = "mchub.output.clear",
                Title = "清空输出",
                Category = "输出",
                Execute = () => _output.Get(_currentChannel).Clear()
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
        var text = _output.Get(_currentChannel).Text;
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
                System.Diagnostics.Process.Start("open", path);
            else if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path)
                    { UseShellExecute = true });
            else
                System.Diagnostics.Process.Start("xdg-open", path);
        }
        catch (Exception exception)
        {
            WorkbenchServices.Output.Write(OutputService.ChannelWorkbench, $"打开目录失败：{exception.Message}");
        }
    }

    // ───────────────────────── 命令面板 ─────────────────────────

    private void OpenPalette()
    {
        PaletteOverlay.IsVisible = true;
        PaletteInput.Text = string.Empty;
        RefreshPalette(string.Empty);
        PaletteInput.Focus();
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
        _paletteCommands.Clear();
        _paletteIndex = -1;

        var normalized = query.Trim();
        if (normalized.StartsWith('>'))
            normalized = normalized[1..].Trim();

        var matches = _commands.All
            .Where(c => string.IsNullOrWhiteSpace(normalized) ||
                        c.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                        (c.Category?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        c.Id.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();

        if (matches.Count == 0)
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

        foreach (var command in matches)
            PaletteList.Children.Add(BuildPaletteItem(command));

        _paletteCommands.AddRange(matches);

        if (_paletteItems.Count > 0)
            SelectPaletteItem(0);
    }

    private Control BuildPaletteItem(WorkbenchCommand command)
    {
        var glyph = new TextBlock
        {
            Text = command.Glyph ?? "\ue611",
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 13,
            Width = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        var title = new TextBlock
        {
            Text = command.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.Foreground
        };

        var trailing = new TextBlock
        {
            Text = string.Join(" · ", new[] { command.Category, command.Gesture }
                .Where(s => !string.IsNullOrEmpty(s))),
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
            IsEnabled = command.IsEnabled
        };

        var id = command.Id;
        item.Click += (_, _) =>
        {
            ClosePalette();
            _commands.Execute(id);
        };

        _paletteItems.Add(item);
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
                    _paletteItems[_paletteIndex].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
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

        if (_keybindings.TryHandle(e))
        {
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        base.OnKeyDown(e);
    }
}
