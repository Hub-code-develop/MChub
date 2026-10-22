using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MChub.Core.Creator;
using MChub.Module;
using MChub.Module.DefaultPage;
using MChub.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace MChub.Views.Pages;

/// <summary>
/// 创作台：给内容创作者用的两件事 —— 截图的集中管理和「模组清单」导出。
/// 只读本地文件，不联网。
/// </summary>
[DefaultPage("创作台")]
public partial class CreatorPage : Dsc, ITioTabPage
{
    private const int MaxThumbnails = 120;

    private readonly List<string> _screenshotPaths = [];
    private IReadOnlyList<string> _instances = [];
    private bool _loaded;

    public CreatorPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            RefreshAll();
        };
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "创作台",
        IconGlyph = "\ue632",
        IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; } = null!;

    private void Refresh(object? sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        _instances = CreatorAssetService.FindInstanceDirectories();
        var screenshots = CreatorAssetService.FindScreenshots();

        _screenshotPaths.Clear();
        _screenshotPaths.AddRange(screenshots.Select(entry => entry.FilePath));

        SummaryText.Text = $"{_screenshotPaths.Count} 张截图 · {_instances.Count} 个可用实例";
        ShotCountText.Text = _screenshotPaths.Count > MaxThumbnails
            ? $"共 {_screenshotPaths.Count} 张，先显示最近 {MaxThumbnails} 张"
            : $"共 {_screenshotPaths.Count} 张";

        BuildThumbnails(screenshots);
        BuildInstanceList();
    }

    private void BuildThumbnails(IReadOnlyList<ScreenshotEntry> screenshots)
    {
        ShotPanel.Children.Clear();

        foreach (var entry in screenshots.Take(MaxThumbnails))
        {
            var image = new Image
            {
                Width = 200,
                Height = 112,
                Stretch = Stretch.UniformToFill
            };

            try
            {
                using var stream = File.OpenRead(entry.FilePath);
                image.Source = Bitmap.DecodeToWidth(stream, 400);
            }
            catch
            {
                // 坏图就留空框，不打断整个列表
            }

            var border = new Border
            {
                Width = 200,
                Height = 112,
                Margin = new Avalonia.Thickness(0, 0, 10, 10),
                CornerRadius = new Avalonia.CornerRadius(8),
                ClipToBounds = true,
                Background = Brushes.Black,
                Child = image,
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
                Tag = entry.FilePath
            };
            ToolTip.SetTip(border, $"{Path.GetFileName(entry.FilePath)}\n{entry.TakenAt:yyyy-MM-dd HH:mm}");
            border.PointerPressed += (_, _) => OpenPath(entry.FilePath);

            ShotPanel.Children.Add(border);
        }
    }

    private void BuildInstanceList()
    {
        InstanceBox.Items.Clear();
        foreach (var instance in _instances)
            InstanceBox.Items.Add(instance);

        if (_instances.Count > 0)
            InstanceBox.SelectedIndex = 0;
        else
            ModSummaryText.Text = "还没有可分析的实例：先在「实例」页安装一个带 mods/resourcepacks 的版本。";
    }

    private void OpenScreenshotFolder(object? sender, RoutedEventArgs e)
    {
        var target = _screenshotPaths.FirstOrDefault();
        var folder = target is not null
            ? Path.GetDirectoryName(target)
            : MChub.Core.Const.Data.ConfigEntry.MinecraftFolders
                .Select(item => Path.Combine(item.FolderPath, "screenshots"))
                .FirstOrDefault(Directory.Exists);

        if (folder is not null)
            OpenPath(folder);
    }

    private void GenerateMarkdown(object? sender, RoutedEventArgs e)
    {
        if (InstanceBox.SelectedItem is not string instancePath)
        {
            ModSummaryText.Text = "先选择一个实例。";
            return;
        }

        try
        {
            var assets = CreatorAssetService.ReadInstance(instancePath);
            Marketing.Mark = assets;
            MarkdownBox.Text = CreatorAssetService.BuildMarkdown(assets);
            ModSummaryText.Text =
                $"{assets.Name}：{assets.EnabledModCount} 个启用模组" +
                (assets.Mods.Count > assets.EnabledModCount ? $"（另有 {assets.Mods.Count - assets.EnabledModCount} 个已禁用）" : string.Empty) +
                $"，资源包 {assets.ResourcePackCount}，光影 {assets.ShaderPackCount}";
        }
        catch (Exception ex)
        {
            ModSummaryText.Text = $"读取失败：{ex.Message}";
        }
    }

    private async void CopyMarkdown(object? sender, RoutedEventArgs e)
    {
        var text = MarkdownBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 打开失败不影响页面
        }
    }
}

internal static class Marketing
{
    /// <summary>最近一次生成的素材，供后续扩展（比如导出 PNG 卡片）使用。</summary>
    internal static InstanceAssets? Mark;
}
