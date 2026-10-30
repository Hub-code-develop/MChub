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
    private readonly StackPanel _tree = new() { Spacing = 0 };
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
            Watermark = "筛选实例",
            FontSize = 11,
            Height = 24,
            Padding = new Thickness(8, 0, 6, 0),
            MinHeight = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = WorkbenchTheme.Brush("WbPanelBg", "#1E1E1E"),
            BorderBrush = WorkbenchTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Foreground = WorkbenchTheme.Foreground
        };
        _filter.TextChanged += (_, _) => Rebuild();

        // 刷新按钮在面板标题栏上，这里只留筛选框和「新建」。
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(10, 6, 8, 6)
        };
        Grid.SetColumn(_filter, 0);
        toolbar.Children.Add(_filter);
        toolbar.Children.Add(ToolButton("\ue645", "新建实例", 1, () => CreateRequested?.Invoke()));

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
                FontSize = 11,
                Foreground = WorkbenchTheme.ForegroundMuted
            },
            Classes = { "wb-tool-action" },
            Margin = new Thickness(4, 0, 0, 0),
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
            FontSize = 8,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundFaint
        };

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = WorkbenchTheme.Foreground
        };

        var countText = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = WorkbenchTheme.ForegroundFaint
        };

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(chevron, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(countText, 2);
        content.Children.Add(chevron);
        content.Children.Add(titleText);
        content.Children.Add(countText);

        var button = new Button
        {
            Content = content,
            Classes = { "wb-flat" },
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 0, 10, 0),
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

    /// <summary>
    /// 单行实例行：图标 + 名称 + 右侧版本号。
    /// VS 的解决方案资源管理器就是这个密度 —— 一行一个条目，
    /// 版本这类次要信息靠右、用小字、淡色，不占第二行。
    /// </summary>
    private Control BuildInstanceRow(MinecraftInstance instance)
    {
        var icon = BuildIcon(instance);
        icon.Margin = new Thickness(0, 0, 9, 0);

        var name = new TextBlock
        {
            Text = instance.InstanceName,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = WorkbenchTheme.Foreground
        };

        // 实例名大多已经带版本号（"1.21.11 LiteMC Fabric"），右边再重复一遍只是噪音；
        // 名字里没有版本才补一个。
        var showVersion = !string.IsNullOrEmpty(instance.VersionId) &&
                          !instance.InstanceName.Contains(instance.VersionId, StringComparison.OrdinalIgnoreCase);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(name, 1);
        row.Children.Add(icon);
        row.Children.Add(name);

        if (showVersion)
        {
            var version = new TextBlock
            {
                Text = instance.VersionId,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = WorkbenchTheme.ForegroundFaint
            };
            Grid.SetColumn(version, 2);
            row.Children.Add(version);
        }

        var button = new Button
        {
            Content = row,
            Classes = { "wb-flat" },
            Height = 24,
            Padding = new Thickness(26, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
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
                Width = 16,
                Height = 16,
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
                FontSize = 13,
                Width = 16,
                Height = 16,
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
