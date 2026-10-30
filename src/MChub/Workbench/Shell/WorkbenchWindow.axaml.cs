using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MChub.Localization;
using MChub.Workbench.Commands;
using MChub.Workbench.Documents;

namespace MChub.Workbench.Shell;

/// <summary>
/// 工作台主窗口：标题菜单栏 / 活动栏 / 侧边栏 / 编辑器 / 底部面板 / 状态栏。
/// 这个类只负责"布局"和"把各部分接起来"，具体功能通过命令注册进来。
/// </summary>
public partial class WorkbenchWindow : Window
{
    private readonly CommandService _commands = new();
    private readonly DocumentService _documents = new();

    private readonly List<ActivityEntry> _activities = new();
    private ActivityEntry? _activeActivity;

    private readonly List<Button> _paletteItems = new();
    private int _paletteIndex = -1;
    private string _activePanel = "output";

    /// <summary>启动阶段用于标记界面还在装载（App 初始化完成后置 false）。</summary>
    public bool IsUiLoading { get; set; } = true;

    public WorkbenchWindow()
    {
        InitializeComponent();

        WorkbenchServices.Initialize(_commands, _documents);

        BuildMenus();
        BuildActivities();
        BuildPanelTabs();
        BuildStatusBar();
        RegisterCoreCommands();

        _documents.StateChanged += (_, _) => RefreshEditor();
        _commands.Changed += (_, _) =>
        {
            if (PaletteOverlay.IsVisible)
                RefreshPalette(PaletteInput.Text ?? string.Empty);
        };
        PaletteInput.KeyDown += OnPaletteKeyDown;
        PaletteInput.TextChanged += (_, _) => RefreshPalette(PaletteInput.Text ?? string.Empty);
        PaletteOverlay.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, PaletteOverlay))
                ClosePalette();
        };

        ShowActivity("explorer");
        RefreshEditor();
        UpdateEmptyState();
        ShowPanel("output");

        Opened += (_, _) => PaletteInput.Watermark = L(AppLanguageManager.Instance.workbench_paletteHint.CurrentValue());
    }

    private static string L(string value) => string.IsNullOrEmpty(value) ? string.Empty : value;

    private IBrush Brush(string key, string fallback)
    {
        if (Resources.TryGetResource(key, ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallback));
    }

    // ───────────────────────── 菜单栏 ─────────────────────────

    private void BuildMenus()
    {
        AddMenu(AppLanguageManager.Instance.workbench_menuFile.CurrentValue(),
            ("workbench.showCommands", "Ctrl+Shift+P"),
            ("workbench.closeAllEditors", "Ctrl+K W"),
            ("workbench.exit", null));

        AddMenu(AppLanguageManager.Instance.workbench_menuEdit.CurrentValue());
        AddMenu(AppLanguageManager.Instance.workbench_menuView.CurrentValue(),
            ("workbench.toggleSideBar", "Ctrl+B"),
            ("workbench.togglePanel", "Ctrl+J"),
            ("workbench.showCommands", "Ctrl+Shift+P"));
        AddMenu(AppLanguageManager.Instance.workbench_menuRun.CurrentValue());
        AddMenu(AppLanguageManager.Instance.workbench_menuTools.CurrentValue(),
            ("workbench.openSettings", null));
        AddMenu(AppLanguageManager.Instance.workbench_menuHelp.CurrentValue());
    }

    private void AddMenu(string header, params (string CommandId, string? Gesture)[] items)
    {
        var button = new Button
        {
            Content = header,
            Classes = { "wb-flat" },
            Height = 32,
            Padding = new Thickness(10, 0)
        };

        if (items.Length > 0)
        {
            var flyout = new MenuFlyout();
            foreach (var (commandId, gesture) in items)
            {
                var command = _commands.Find(commandId);
                var text = command?.Title ?? commandId;
                if (gesture is not null)
                    text = $"{text}\t{gesture}";

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

    private sealed record ActivityEntry(string Id, string Glyph, string Title);

    private void BuildActivities()
    {
        AddActivity("explorer", "\ue611", AppLanguageManager.Instance.workbench_activityExplorer.CurrentValue());
        AddActivity("search", "\ue615", AppLanguageManager.Instance.workbench_activitySearch.CurrentValue());
        AddActivity("instances", "\ue638", AppLanguageManager.Instance.workbench_activityInstances.CurrentValue());
        AddActivity("downloads", "\ue622", AppLanguageManager.Instance.workbench_activityDownloads.CurrentValue());
        AddActivity("creator", "\ue60a", AppLanguageManager.Instance.workbench_activityCreator.CurrentValue());
        AddActivity("tools", "\ue641", AppLanguageManager.Instance.workbench_activityTools.CurrentValue());

        ActivityBar.Children.Add(new Border { Height = 12 });

        AddActivity("settings", "\ue627", AppLanguageManager.Instance.workbench_activitySettings.CurrentValue());
    }

    private void AddActivity(string id, string glyph, string title)
    {
        var host = new Grid { Width = 48, Height = 46 };

        var indicator = new Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush("WbAccent", "#007ACC"),
            IsVisible = false
        };

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Resources["WbIconFont"]!,
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("WbFgMuted", "#8C8C8C")
        };

        var button = new Button
        {
            Classes = { "wb-activity" },
            Content = icon,
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(button, title);
        button.Click += (_, _) => ShowActivity(id);

        host.Children.Add(indicator);
        host.Children.Add(button);
        ActivityBar.Children.Add(host);

        _activities.Add(new ActivityEntry(id, glyph, title));
        _activityVisuals[id] = (indicator, icon);
    }

    private readonly Dictionary<string, (Border Indicator, TextBlock Icon)> _activityVisuals = new();

    private void ShowActivity(string id)
    {
        var entry = _activities.FirstOrDefault(a => a.Id == id);
        if (entry is null)
            return;

        // 再点一次已经激活的项 → 收起侧边栏（和 VS Code 一致）
        if (_activeActivity?.Id == id && SideBarRoot.IsVisible)
        {
            SideBarRoot.IsVisible = false;
            SideBarSplitter.IsVisible = false;
            UpdateActivityVisuals();
            return;
        }

        _activeActivity = entry;
        SideBarRoot.IsVisible = true;
        SideBarSplitter.IsVisible = true;
        SideBarTitle.Text = entry.Title.ToUpperInvariant();
        SideBarHost.Content = BuildPlaceholder(entry.Title);
        UpdateActivityVisuals();
    }

    private void UpdateActivityVisuals()
    {
        foreach (var (id, visual) in _activityVisuals)
        {
            var active = _activeActivity?.Id == id && SideBarRoot.IsVisible;
            visual.Indicator.IsVisible = active;
            visual.Icon.Foreground = active
                ? Brush("WbFg", "#CCCCCC")
                : Brush("WbFgMuted", "#8C8C8C");
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
                    Foreground = Brush("WbFg", "#CCCCCC")
                },
                new TextBlock
                {
                    Text = AppLanguageManager.Instance.workbench_sidebarPlaceholder.CurrentValue(),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("WbFgFaint", "#6E7681")
                }
            }
        };
    }

    // ───────────────────────── 底部面板 ─────────────────────────

    private void BuildPanelTabs()
    {
        AddPanelTab("output", "\ue633", AppLanguageManager.Instance.workbench_panelOutput.CurrentValue());
        AddPanelTab("tasks", "\ue635", AppLanguageManager.Instance.workbench_panelTasks.CurrentValue());
        AddPanelTab("problems", "\ue63a", AppLanguageManager.Instance.workbench_panelProblems.CurrentValue());
    }

    private readonly Dictionary<string, Button> _panelTabButtons = new();

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
                    FontFamily = (FontFamily)Resources["WbIconFont"]!,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("WbFgMuted", "#8C8C8C")
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

    private void ShowPanel(string id)
    {
        _activePanel = id;

        foreach (var (key, button) in _panelTabButtons)
        {
            button.Foreground = key == id
                ? Brush("WbFg", "#CCCCCC")
                : Brush("WbFgMuted", "#8C8C8C");
        }

        PanelHost.Content = id switch
        {
            "output" => BuildOutputView(),
            "tasks" => BuildPlaceholder(AppLanguageManager.Instance.workbench_panelTasks.CurrentValue()),
            _ => BuildPlaceholder(AppLanguageManager.Instance.workbench_panelProblems.CurrentValue())
        };
    }

    private Control BuildOutputView()
    {
        var output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = (FontFamily)Resources["WbCodeFont"]!,
            FontSize = 12,
            Foreground = Brush("WbFgMuted", "#8C8C8C"),
            Padding = new Thickness(12, 8),
            Text = "[MChub] 工作台已启动\n"
        };

        return output;
    }

    // ───────────────────────── 状态栏 ─────────────────────────

    private void BuildStatusBar()
    {
        StatusLeft.Children.Add(StatusText(AppLanguageManager.Instance.workbench_ready.CurrentValue()));
        StatusRight.Children.Add(StatusText("MChub · Workbench P0"));
    }

    private TextBlock StatusText(string text) => new()
    {
        Text = text,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 14, 0),
        Foreground = Brush("WbStatusFg", "#FFFFFF")
    };

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
        {
            TabStrip.Children.Add(BuildTab(document));
        }
    }

    private Control BuildTab(IWorkbenchDocument document)
    {
        var isActive = ReferenceEquals(document, _documents.Active);

        var title = new TextBlock
        {
            Text = document.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isActive ? Brush("WbFg", "#CCCCCC") : Brush("WbFgMuted", "#8C8C8C")
        };

        var close = new Button
        {
            Content = new TextBlock
            {
                Text = "\ue609",
                FontFamily = (FontFamily)Resources["WbIconFont"]!,
                FontSize = 11,
                Foreground = Brush("WbFgMuted", "#8C8C8C")
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
            Background = isActive ? Brush("WbEditorBg", "#1E1E1E") : Brushes.Transparent,
            BorderBrush = Brush("WbAccent", "#007ACC"),
            BorderThickness = isActive ? new Thickness(0, 1, 0, 0) : new Thickness(0),
            Child = content
        };

        tab.PointerPressed += (_, _) => _documents.Activate(document);
        return tab;
    }

    private void UpdateEmptyState()
    {
        EmptyTitle.Text = AppLanguageManager.Instance.workbench_noEditor.CurrentValue();
        EmptyHint.Text = AppLanguageManager.Instance.workbench_noEditorHint.CurrentValue();
    }

    // ───────────────────────── 命令 ─────────────────────────

    private void RegisterCoreCommands()
    {
        _commands.RegisterRange(
            new WorkbenchCommand
            {
                Id = "workbench.showCommands",
                Title = "命令面板: 显示所有命令",
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
                Execute = () =>
                {
                    SideBarRoot.IsVisible = !SideBarRoot.IsVisible;
                    SideBarSplitter.IsVisible = SideBarRoot.IsVisible;
                    UpdateActivityVisuals();
                }
            },
            new WorkbenchCommand
            {
                Id = "workbench.togglePanel",
                Title = "切换底部面板",
                Category = "视图",
                Gesture = "Ctrl+J",
                Execute = () => { }
            },
            new WorkbenchCommand
            {
                Id = "workbench.closeAllEditors",
                Title = "关闭所有编辑器",
                Category = "文件",
                Execute = () => _documents.CloseAll()
            },
            new WorkbenchCommand
            {
                Id = "workbench.openSettings",
                Title = "打开设置",
                Category = "工具",
                Execute = () => { }
            },
            new WorkbenchCommand
            {
                Id = "workbench.exit",
                Title = "退出 MChub",
                Category = "文件",
                Execute = Close
            });
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
    }

    private void RefreshPalette(string query)
    {
        PaletteList.Children.Clear();
        _paletteItems.Clear();
        _paletteIndex = -1;

        var matches = _commands.All
            .Where(c => string.IsNullOrWhiteSpace(query) ||
                        c.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();

        if (matches.Count == 0)
        {
            PaletteList.Children.Add(new TextBlock
            {
                Text = AppLanguageManager.Instance.workbench_paletteEmpty.CurrentValue(),
                FontSize = 12,
                Margin = new Thickness(10, 8),
                Foreground = Brush("WbFgFaint", "#6E7681")
            });
            return;
        }

        foreach (var command in matches)
        {
            var item = new Button
            {
                Classes = { "wb-flat" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 30,
                Padding = new Thickness(10, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = command.Glyph ?? "\ue611",
                            FontFamily = (FontFamily)Resources["WbIconFont"]!,
                            FontSize = 13,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brush("WbFgMuted", "#8C8C8C")
                        },
                        new TextBlock
                        {
                            Text = command.Title,
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            var id = command.Id;
            item.Click += (_, _) =>
            {
                ClosePalette();
                _commands.Execute(id);
            };

            PaletteList.Children.Add(item);
            _paletteItems.Add(item);
        }

        if (_paletteItems.Count > 0)
            SelectPaletteItem(0);
    }

    private void SelectPaletteItem(int index)
    {
        if (_paletteItems.Count == 0) return;

        index = Math.Clamp(index, 0, _paletteItems.Count - 1);

        if (_paletteIndex >= 0 && _paletteIndex < _paletteItems.Count)
            _paletteItems[_paletteIndex].Background = Brushes.Transparent;

        _paletteIndex = index;
        _paletteItems[index].Background = Brush("WbSelectionBg", "#094771");
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
                    _paletteItems[_paletteIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        var ctrlOrCmd = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);

        if (ctrlOrCmd && mods.HasFlag(KeyModifiers.Shift) && e.Key == Key.P)
        {
            OpenPalette();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.B)
        {
            _commands.Execute("workbench.toggleSideBar");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && PaletteOverlay.IsVisible)
        {
            ClosePalette();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
