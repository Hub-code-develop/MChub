using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Workbench.Commands;

namespace MChub.Workbench.Views;

/// <summary>
/// 资源管理器：把「我有哪些实例」按实例目录分组列出来。
/// 相当于 IDE 的文件树 —— 只不过树里是 Minecraft 实例而不是文件夹。
/// </summary>
public sealed class ExplorerView : UserControl
{
    private readonly StackPanel _tree = new() { Spacing = 1 };
    private readonly TextBox _filter;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private bool _suppressRefresh;

    /// <summary>选中一个实例（在编辑器里打开概览）。</summary>
    public event Action<MinecraftInstance>? InstanceActivated;

    /// <summary>要求启动实例。</summary>
    public event Action<MinecraftInstance>? LaunchRequested;

    /// <summary>在编辑器里显示一个新实例（新建/导入）。</summary>
    public event Action? CreateRequested;

    public ExplorerView()
    {
        _filter = new TextBox
        {
            Watermark = "按名称筛选实例",
            FontSize = 12,
            Height = 28,
            Margin = new Thickness(8, 8, 8, 6),
            Background = WorkbenchTheme.Brush("WbEditorBg", "#1E1E1E"),
            BorderBrush = WorkbenchTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Foreground = WorkbenchTheme.Foreground
        };
        _filter.TextChanged += (_, _) => Rebuild();

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(_filter, 0);
        toolbar.Children.Add(_filter);
        toolbar.Children.Add(ToolButton("\ue627", "刷新实例列表", 1, () =>
            WorkbenchServices.Commands.Execute("mchub.instances.refresh")));
        toolbar.Children.Add(ToolButton("\ue609", "新建实例", 2, () => CreateRequested?.Invoke()));

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _tree,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(scroll, 1);

        ((Grid)Content).Children.Add(toolbar);
        ((Grid)Content).Children.Add(scroll);

        InstanceManager.Instance.InstancesChanged += OnInstancesChanged;
        AttachedToVisualTree += (_, _) => Rebuild();
    }

    private static Button ToolButton(string glyph, string tip, int column, Action onClick)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 13,
                Foreground = WorkbenchTheme.ForegroundMuted
            },
            Classes = { "wb-flat" },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 8, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        Grid.SetColumn(button, column);
        return button;
    }

    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Rebuild();
        else
            Dispatcher.UIThread.Post(Rebuild);
    }

    public void Rebuild()
    {
        if (_suppressRefresh) return;

        _tree.Children.Clear();

        var filter = _filter.Text?.Trim() ?? string.Empty;
        var instances = InstanceManager.Instance.Instances
            .Where(instance => string.IsNullOrEmpty(filter) ||
                               instance.InstanceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                               instance.VersionId.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (instances.Count == 0)
        {
            _tree.Children.Add(EmptyHint(InstanceManager.Instance.Instances.Count == 0
                ? "还没有实例。点右上角的 + 新建或导入一个。"
                : "没有匹配的实例。"));
            return;
        }

        // 按实例所在目录分组，目录名取配置里的显示名，取不到就退回文件夹名。
        var groups = instances
            .GroupBy(instance => instance.FolderPath ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var title = ResolveFolderName(group.Key);
            AddFolderHeader(group.Key, title, group.Count());

            if (_collapsed.Contains(group.Key))
                continue;

            foreach (var instance in group.OrderBy(i => i.InstanceName, StringComparer.OrdinalIgnoreCase))
                _tree.Children.Add(BuildInstanceRow(instance));
        }
    }

    private static string ResolveFolderName(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return "未知目录";

        var entry = Data.ConfigEntry.MinecraftFolders.FirstOrDefault(folder =>
            string.Equals(folder.FolderPath?.TrimEnd('/', '\\'), folderPath.TrimEnd('/', '\\'),
                StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            return entry.FolderName;

        var name = System.IO.Path.GetFileName(folderPath.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(name) ? folderPath : name;
    }

    private void AddFolderHeader(string folderPath, string title, int count)
    {
        var collapsed = _collapsed.Contains(folderPath);
        var chevron = new TextBlock
        {
            Text = collapsed ? "\ue60c" : "\ue60b",
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 10,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                chevron,
                new TextBlock
                {
                    Text = title,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.Foreground
                },
                new TextBlock
                {
                    Text = count.ToString(),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkbenchTheme.ForegroundFaint
                }
            }
        };

        var button = new Button
        {
            Content = content,
            Classes = { "wb-flat" },
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 0, 8, 0),
            Margin = new Thickness(0)
        };
        button.Click += (_, _) =>
        {
            if (!_collapsed.Remove(folderPath))
                _collapsed.Add(folderPath);
            Rebuild();
        };

        _tree.Children.Add(button);
    }

    private Control BuildInstanceRow(MinecraftInstance instance)
    {
        var icon = BuildIcon(instance);

        var name = new TextBlock
        {
            Text = instance.InstanceName,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = WorkbenchTheme.Foreground
        };

        var detail = new TextBlock
        {
            Text = string.IsNullOrEmpty(instance.ShortDisplay) ? instance.VersionId : instance.ShortDisplay,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = WorkbenchTheme.ForegroundFaint
        };

        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name, detail }
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 1, 0, 1)
        };
        icon.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(icon);
        row.Children.Add(text);

        var button = new Button
        {
            Content = row,
            Classes = { "wb-flat" },
            Height = 40,
            Padding = new Thickness(22, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0),
            ContextFlyout = BuildContextMenu(instance)
        };
        ToolTip.SetTip(button, $"{instance.InstanceName}\n{instance.FolderPath}");

        button.Click += (_, _) => InstanceActivated?.Invoke(instance);
        button.DoubleTapped += (_, _) => LaunchRequested?.Invoke(instance);

        return button;
    }

    private static Control BuildIcon(MinecraftInstance instance)
    {
        try
        {
            Bitmap bitmap = instance[20];
            return new Image
            {
                Source = bitmap,
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.UniformToFill
            };
        }
        catch (Exception)
        {
            return new TextBlock
            {
                Text = "\ue638",
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 15,
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WorkbenchTheme.ForegroundFaint
            };
        }
    }

    private MenuFlyout BuildContextMenu(MinecraftInstance instance)
    {
        var flyout = new MenuFlyout();

        flyout.Items.Add(MenuItemFor("启动", "mchub.instance.launch", instance));
        flyout.Items.Add(MenuItemFor("在编辑器中打开", "mchub.instance.open", instance));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(MenuItemFor("打开实例文件夹", "mchub.instance.revealFolder", instance));

        return flyout;
    }

    private MenuItem MenuItemFor(string header, string commandId, MinecraftInstance instance)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            InstanceContext.Current = instance;
            WorkbenchServices.Commands.Execute(commandId);
        };
        return item;
    }

    private static Control EmptyHint(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16, 10),
        Foreground = WorkbenchTheme.ForegroundFaint
    };
}

/// <summary>
/// 侧边栏与命令之间的"当前实例"传递点。
/// 命令是静态注册的、不带参数，右键菜单只负责把目标实例放进来。
/// </summary>
public static class InstanceContext
{
    public static MinecraftInstance? Current { get; set; }
}
