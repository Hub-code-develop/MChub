using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Workbench.Services;

namespace MChub.Workbench.Documents;

/// <summary>
/// 实例概览页。相当于 IDE 里打开的项目主页：
/// 只读地摊开这个实例的配置，并提供最常用的动作（启动 / 打开目录）。
/// 排版上保持「标题 + 标签 + 分区表格」的编辑器内容页风格，内容宽度受限，
/// 不把键值对拉满整屏。
/// </summary>
public sealed class InstanceOverviewDocument : IWorkbenchDocument
{
    private const double ContentWidth = 780;

    private readonly MinecraftInstance _instance;
    private readonly StackPanel _root;
    private readonly TextBlock _status;

    private Button? _launchButton;

    public InstanceOverviewDocument(MinecraftInstance instance)
    {
        _instance = instance;
        _status = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = WorkbenchTheme.ForegroundFaint
        };

        _root = new StackPanel
        {
            Margin = new Thickness(32, 26, 32, 40),
            Spacing = 26,
            MaxWidth = ContentWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = WorkbenchTheme.EditorBg,
            Content = _root
        };

        View = scroll;

        Rebuild();
        InstanceLauncher.LaunchStateChanged += OnLaunchStateChanged;
    }

    public string Id => $"mchub://instance/{_instance.InstanceFolderPath}";

    /// <summary>这份文档对应的实例。</summary>
    public MinecraftInstance Instance => _instance;

    public string Title => _instance.InstanceName;

    public string? Glyph => "\ue638";

    public bool IsDirty => false;

    public Control View { get; }

    public bool TryClose()
    {
        InstanceLauncher.LaunchStateChanged -= OnLaunchStateChanged;
        return true;
    }

    private void OnLaunchStateChanged(object? sender, EventArgs e) => UpdateLaunchButton();

    private void Rebuild()
    {
        _root.Children.Clear();
        _root.Children.Add(BuildHeader());
        _root.Children.Add(BuildSection("基本信息", BuildBasicRows()));
        _root.Children.Add(BuildSection("启动配置", BuildLaunchRows()));
        UpdateLaunchButton();
    }

    // ───────────────────────── 头部 ─────────────────────────

    private Control BuildHeader()
    {
        var icon = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(6),
            Background = WorkbenchTheme.Brush("WbPanelBg", "#1E1E1E"),
            BorderBrush = WorkbenchTheme.Border,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top,
            Child = BuildIcon()
        };

        var name = new TextBlock
        {
            Text = _instance.InstanceName,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = WorkbenchTheme.Foreground,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                Chip(string.IsNullOrEmpty(_instance.LoaderDescription) ? "原版" : _instance.LoaderDescription),
                Chip(_instance.VersionId),
                Chip(_instance.IsJava ? "Java 版" : "基岩版")
            }
        };

        var texts = new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(14, 2, 0, 0),
            Children = { name, chips }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _status, BuildRevealButton(), BuildLaunchButton() }
        };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(actions, 2);
        top.Children.Add(icon);
        top.Children.Add(texts);
        top.Children.Add(actions);

        return new StackPanel
        {
            Spacing = 18,
            Children = { top, Divider() }
        };
    }

    private static Border Chip(string text) => new()
    {
        Background = WorkbenchTheme.Brush("WbHoverBg", "#393B40"),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(9, 2, 9, 3),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = WorkbenchTheme.ForegroundMuted,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private static Border Divider() => new()
    {
        Height = 1,
        Background = WorkbenchTheme.Border
    };

    private Control BuildIcon()
    {
        try
        {
            Bitmap bitmap = _instance[56];
            return new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch (Exception)
        {
            return new TextBlock
            {
                Text = "\ue638",
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WorkbenchTheme.ForegroundFaint
            };
        }
    }

    private Button BuildLaunchButton()
    {
        _launchButton = new Button
        {
            Content = BuildLaunchButtonContent(),
            Height = 30,
            Padding = new Thickness(16, 0),
            CornerRadius = new CornerRadius(4),
            Background = WorkbenchTheme.Brush("WbAccent", "#3574F0"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _launchButton.Click += async (_, _) => await InstanceLauncher.LaunchAsync(_instance);
        return _launchButton;
    }

    private Button BuildRevealButton()
    {
        var button = new Button
        {
            Content = "打开目录",
            Classes = { "wb-flat" },
            Height = 30,
            Padding = new Thickness(12, 0),
            BorderBrush = WorkbenchTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => RevealFolder();
        return button;
    }

    private Control BuildLaunchButtonContent()
    {
        var label = new TextBlock
        {
            Text = InstanceLauncher.IsLaunching ? "启动中…" : "启动游戏",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children =
            {
                new TextBlock
                {
                    Text = "\ue607",
                    FontFamily = WorkbenchTheme.IconFont,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                },
                label
            }
        };
    }

    private void UpdateLaunchButton()
    {
        if (_launchButton is null) return;
        _launchButton.Content = BuildLaunchButtonContent();
        _launchButton.IsEnabled = !InstanceLauncher.IsLaunching;
        _launchButton.Opacity = InstanceLauncher.IsLaunching ? 0.6 : 1;
        _status.Text = InstanceLauncher.IsLaunching ? "游戏运行中" : string.Empty;
    }

    // ───────────────────────── 分区表格 ─────────────────────────

    private Control BuildBasicRows()
    {
        var rows = new StackPanel { Spacing = 0 };

        AddRow(rows, "版本", _instance.VersionId);
        AddRow(rows, "装载器", string.IsNullOrEmpty(_instance.LoaderDescription) ? "原版" : _instance.LoaderDescription);
        AddRow(rows, "类型", _instance.IsJava ? "Java 版" : "基岩版");
        AddRow(rows, "目录", _instance.FolderPath);
        AddRow(rows, "最后游玩", _instance.LastPlayTime == DateTime.MinValue
            ? "还没玩过"
            : _instance.LastPlayTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        AddRow(rows, "累计时长", FormatDuration(_instance.GetTotalPlayTimeSeconds()));

        return rows;
    }

    private Control BuildLaunchRows()
    {
        var rows = new StackPanel { Spacing = 0 };

        if (_instance.JavaConfig is { } java)
        {
            AddRow(rows, "Java",
                java.EnableSpecificJava
                    ? java.SpecificJavaEntry?.JavaVersion ?? java.SpecificJavaEntry?.JavaPath ?? "已指定"
                    : "自动选择");
            AddRow(rows, "内存", java.EnableOverrideMaxMemory
                ? $"{java.MinecraftMaxMemory} MB（实例覆盖）"
                : $"{Data.ConfigEntry.MinecraftMaxMemory} MB（全局）");
            AddRow(rows, "窗口", $"{java.MinecraftWindowWidth} × {java.MinecraftWindowHeight}");
            AddRow(rows, "图形后端", java.GraphicsBackend.ToString());
        }
        else
        {
            AddRow(rows, "配置", "该实例没有 Java 配置（基岩版）");
        }

        AddRow(rows, "当前账户", Data.ConfigEntry.UsingMinecraftMinecraftAccount?.Name ?? "未登录");

        return rows;
    }

    private static Control BuildSection(string title, Control content)
    {
        var header = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        return new StackPanel { Spacing = 0, Children = { header, content } };
    }

    private static void AddRow(StackPanel host, string label, string? value)
    {
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Width = 84,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = WorkbenchTheme.ForegroundMuted
        };

        var valueText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = WorkbenchTheme.Foreground
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 0, 7)
        };
        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);

        host.Children.Add(row);
    }

    private void RevealFolder()
    {
        try
        {
            var path = _instance.InstanceFolderPath;
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
            WorkbenchServices.Output.Write(OutputService.ChannelWorkbench,
                $"打开实例目录失败：{exception.Message}");
        }
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0) return "—";

        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} 小时 {span.Minutes} 分";
        return $"{span.Minutes} 分";
    }
}
