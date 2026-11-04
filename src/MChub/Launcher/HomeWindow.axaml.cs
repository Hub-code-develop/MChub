using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Core.Module.Initialize;

namespace MChub.Launcher;

/// <summary>
/// 启动器主界面。整个应用打开先到这里：
/// 左边一块图片位（远端配置下发，拿不到就整列收起），右边是字标 + 版本列表。
/// 版本不靠用户手动加文件夹 —— 打开就自动扫全盘，扫描过程有进度条。
/// 点任意一行进工作台。
/// </summary>
public partial class HomeWindow : Window
{
    private static readonly IBrush Fg = new SolidColorBrush(Color.Parse("#EDEDF0"));
    private static readonly IBrush FgMuted = new SolidColorBrush(Color.Parse("#8A8A94"));
    private static readonly IBrush FgFaint = new SolidColorBrush(Color.Parse("#6A6A74"));
    private static readonly IBrush RowIconBg = new SolidColorBrush(Color.Parse("#26262C"));

    private bool _scanStarted;
    private bool _scanning;
    private double _progress;

    /// <summary>用户点了某个版本（由 App 负责打开工作台）。</summary>
    public event Action<MinecraftInstance>? InstanceActivated;

    public HomeWindow()
    {
        InitializeComponent();

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
        RescanButton.Click += (_, _) => _ = ScanAsync();

        RootCard.PointerPressed += OnCardPointerPressed;
        ScanTrack.SizeChanged += (_, _) => ApplyProgressWidth();

        Opened += (_, _) =>
        {
            if (_scanStarted)
                return;
            _scanStarted = true;
            _ = LoadBannerAsync();
            _ = ScanAsync();
            SetupScreenshotHook();
        };
    }

    /// <summary>
    /// 开发期自查：<c>MCHUB_HOME_SHOT=/path/x.png</c> 时把首页渲染成 PNG。
    /// 位图尺寸用逻辑尺寸 + 96dpi，别乘 RenderScaling，否则内容会被放大两次。
    /// </summary>
    private void SetupScreenshotHook()
    {
        var path = Environment.GetEnvironmentVariable("MCHUB_HOME_SHOT");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var delay = int.TryParse(Environment.GetEnvironmentVariable("MCHUB_HOME_SHOT_DELAY"), out var d) ? d : 6000;
        DispatcherTimer.RunOnce(() => Capture(path), TimeSpan.FromMilliseconds(delay));
    }

    private void Capture(string path)
    {
        try
        {
            if (Content is not Visual root)
                return;

            var size = new PixelSize(
                Math.Max(1, (int)Math.Round(Bounds.Width)),
                Math.Max(1, (int)Math.Round(Bounds.Height)));

            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(root);
            bitmap.Save(path);
            Console.WriteLine($"[HOME-SHOT] {path} {size.Width}x{size.Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[HOME-SHOT] 失败: " + ex);
        }
    }

    // ───────────────────────── 窗口行为 ─────────────────────────

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 按钮上的点击不能拿来拖窗口
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        BeginMoveDrag(e);
    }

    // ───────────────────────── 扫描 ─────────────────────────

    private async Task ScanAsync()
    {
        if (_scanning)
            return;

        _scanning = true;
        SetProgress(0.05, "正在查找版本目录…");

        try
        {
            var reporter = new Progress<MinecraftFolderDiscovery.Progress>(p =>
            {
                if (p.Total <= 0)
                    return;
                var ratio = 0.05 + 0.45 * (p.Index / (double)p.Total);
                SetProgress(ratio, string.IsNullOrEmpty(p.CurrentRoot)
                    ? $"找到 {p.Found} 个目录"
                    : $"正在扫描 {p.Index}/{p.Total} · {Shorten(p.CurrentRoot)}");
            });

            var found = await MinecraftFolderDiscovery.DiscoverAsync(reporter);
            MergeFolders(found);

            SetProgress(0.55, $"正在读取 {found.Count} 个目录里的版本…");
            var instances = await InstanceManager.ScanAllAsync(Data.ConfigEntry.MinecraftFolders);
            InstanceManager.Instance.ApplyInstances(instances);

            SetProgress(1, instances.Count == 0
                ? "没有找到任何版本"
                : $"已找到 {instances.Count} 个版本");
            RebuildList();

            // 扫描很快时也别让进度条一闪而过，留一点时间让人看清结果
            await Task.Delay(2400);
        }
        catch (Exception exception)
        {
            SetProgress(1, "扫描失败：" + exception.Message);
            RebuildList();
            return;
        }
        finally
        {
            _scanning = false;
            ScanArea.IsVisible = false;
            RescanButton.IsEnabled = true;
        }
    }

    /// <summary>把发现的目录并进配置，已经有的不重复加。</summary>
    private static void MergeFolders(IReadOnlyList<MinecraftFolderDiscovery.Found> found)
    {
        if (found.Count == 0)
            return;

        var existing = Data.ConfigEntry.MinecraftFolders
            .Select(folder => Normalize(folder.FolderPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var item in found)
        {
            var path = Normalize(item.FolderPath);
            if (string.IsNullOrEmpty(path) || !existing.Add(path))
                continue;

            Data.ConfigEntry.MinecraftFolders.Add(new MinecraftFolderEntry
            {
                FolderName = item.DisplayName,
                FolderPath = item.FolderPath,
                FolderKind = MinecraftFolderKind.Auto
            });
            added = true;
        }

        if (added)
            ConfigSaver.SaveConfig();
    }

    private static string Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.TrimEnd('/', '\\');

    private static string Shorten(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            return "~" + path[home.Length..];
        return path;
    }

    private void SetProgress(double progress, string text)
    {
        _progress = Math.Clamp(progress, 0, 1);
        ScanArea.IsVisible = true;
        ScanText.Text = text;
        ApplyProgressWidth();
    }

    private void ApplyProgressWidth()
    {
        var width = ScanTrack.Bounds.Width;
        if (width > 0)
            ScanFill.Width = Math.Max(0, width * _progress);
    }

    // ───────────────────────── 列表 ─────────────────────────

    private void RebuildList()
    {
        ListHost.Children.Clear();

        var instances = InstanceManager.Instance.Instances
            .OrderBy(instance => instance.InstanceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ListCount.Text = instances.Count == 0 ? string.Empty : $"{instances.Count} 个";

        if (instances.Count == 0)
        {
            ListHost.Children.Add(BuildEmptyState());
            return;
        }

        foreach (var instance in instances)
            ListHost.Children.Add(BuildRow(instance));
    }

    private Control BuildEmptyState()
    {
        var text = new TextBlock
        {
            Text = "没有找到任何 Minecraft 版本",
            FontSize = 12,
            Foreground = FgMuted,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var hint = new TextBlock
        {
            Text = "会自动扫描整台电脑；如果你刚装了新版本，点右上角的刷新再找一次。",
            FontSize = 11,
            Foreground = FgFaint,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };

        return new StackPanel
        {
            Margin = new Thickness(0, 40, 0, 0),
            Spacing = 2,
            Children = { text, hint }
        };
    }

    private Control BuildRow(MinecraftInstance instance)
    {
        var icon = BuildIcon(instance);

        var name = new TextBlock
        {
            Text = instance.InstanceName,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Fg,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var sub = new TextBlock
        {
            Text = BuildSubtitle(instance),
            FontSize = 11,
            Foreground = FgFaint,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0)
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name }
        };
        if (!string.IsNullOrEmpty(sub.Text))
            stack.Children.Add(sub);

        // 名称里基本都带版本号，右边重复一遍没意义；改放"上次玩"更有用。
        var meta = new TextBlock
        {
            Text = FormatLastPlayed(instance.LastPlayTime),
            FontSize = 11,
            Foreground = FgFaint,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 6, 0)
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(stack, 1);
        Grid.SetColumn(meta, 2);
        grid.Children.Add(icon);
        grid.Children.Add(stack);
        if (!string.IsNullOrEmpty(meta.Text))
            grid.Children.Add(meta);

        var button = new Button
        {
            Content = grid,
            Classes = { "list-row" }
        };
        ToolTip.SetTip(button, instance.FolderPath);
        button.Click += (_, _) => InstanceActivated?.Invoke(instance);

        return button;
    }

    /// <summary>副标题：目录名 + 加载器（能拿到的话），不重复版本号。</summary>
    /// <summary>
    /// 副标题只放真正有区分度的信息（加载器/整合包类型）。
    /// 全是 "minecraft" 的普通实例就不占这一行，列表更干净。
    /// </summary>
    private static string BuildSubtitle(MinecraftInstance instance)
    {
        var layout = instance.Layout?.KindDisplayName;
        if (string.IsNullOrWhiteSpace(layout))
            return string.Empty;

        return layout.Equals("Minecraft", StringComparison.OrdinalIgnoreCase) ||
               layout.Equals("minecraft", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : layout;
    }

    /// <summary>上次游玩时间：今天 / N 天前 / 具体日期，没玩过就不显示。</summary>
    private static string FormatLastPlayed(DateTime lastPlayed)
    {
        if (lastPlayed == DateTime.MinValue)
            return string.Empty;

        var days = (DateTime.Now.Date - lastPlayed.Date).Days;
        return days switch
        {
            <= 0 => "今天",
            1 => "昨天",
            < 7 => $"{days} 天前",
            < 30 => $"{days / 7} 周前",
            _ => lastPlayed.ToString("yyyy/MM/dd")
        };
    }

    private static Control BuildIcon(MinecraftInstance instance)
    {
        var container = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = RowIconBg,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        try
        {
            var bitmap = instance[36];
            if (bitmap is not null)
            {
                container.Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill
                };
                return container;
            }
        }
        catch
        {
            // 落到下面的默认图标
        }

        container.Child = new TextBlock
        {
            Text = "\ue638",
            FontFamily = new FontFamily("avares://MChub/Assets/Fonts/iconfont.ttf#iconfont"),
            FontSize = 15,
            Foreground = FgFaint,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return container;
    }


    // ───────────────────────── 左侧图片 ─────────────────────────

    private async Task LoadBannerAsync()
    {
        HomeBannerService.Banner? banner = null;
        try
        {
            banner = await HomeBannerService.GetAsync();
        }
        catch
        {
            // 交给下面的降级
        }

        if (banner is null)
        {
            CollapseBanner();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            BannerImage.Source = banner.Image;
            BannerTitle.Text = banner.Title;
            BannerSubtitle.Text = banner.Subtitle;
            BannerPlaceholder.IsVisible = false;
            BannerShade.IsVisible = true;
            BannerCaption.IsVisible = true;
        });
    }

    /// <summary>没有图片时整列收起，列表直接吃掉这块宽度。</summary>
    private void CollapseBanner()
    {
        BannerCard.IsVisible = false;
        ContentGrid.ColumnDefinitions = new ColumnDefinitions("0,0,*");
    }
}
