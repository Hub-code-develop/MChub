using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MChub.Core.Creator;
using MChub.Module;
using MChub.Module.DefaultPage;
using MChub.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace MChub.Views.Pages;

/// <summary>
/// 创作台：截图、录像、结构、资源包对比、设置。
/// 只读本地文件，不联网。
/// </summary>
[DefaultPage("创作台")]
public partial class CreatorPage : Dsc, ITioTabPage
{
    private readonly List<string> _screenshotPaths = [];
    private IReadOnlyList<string> _instances = [];
    private IReadOnlyList<ReplayEntry> _replays = [];
    private IReadOnlyList<StructureEntry> _structures = [];
    private IReadOnlyList<PackEntry> _packs = [];
    private CreatorSettings _settings = new();
    private bool _loaded;

    public CreatorPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            LoadSettingsToUi();
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

    private void LoadSettingsToUi()
    {
        _settings = CreatorSettings.Load();
        IncludeDisabledBox.IsChecked = _settings.IncludeDisabledMods;
        ThumbLimitBox.Value = _settings.ThumbnailLimit;
        DefaultTextureBox.Text = _settings.DefaultTexturePath;
        TexturePathBox.Text = _settings.DefaultTexturePath;
    }

    private void Refresh(object? sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        _instances = CreatorAssetService.FindInstanceDirectories();
        var screenshots = CreatorAssetService.FindScreenshots();
        _replays = CreatorAssetService.FindReplays();
        _structures = CreatorAssetService.FindStructures();
        _packs = CreatorAssetService.FindPacks().Where(pack => !pack.IsShader).ToList();

        _screenshotPaths.Clear();
        _screenshotPaths.AddRange(screenshots.Select(entry => entry.FilePath));

        SummaryText.Text = $"{_screenshotPaths.Count} 张截图 · {_replays.Count} 段录像 · {_structures.Count} 个结构";

        var limit = Math.Max(20, _settings.ThumbnailLimit);
        ShotCountText.Text = _screenshotPaths.Count > limit
            ? $"共 {_screenshotPaths.Count} 张，先显示最近 {limit} 张"
            : $"共 {_screenshotPaths.Count} 张";
        ReplayCountText.Text = $"共 {_replays.Count} 段（.mcpr）";
        StructureCountText.Text = $"共 {_structures.Count} 个（.litematic / .schematic）";

        BuildThumbnails(screenshots.Take(limit).ToList());
        BuildReplayList();
        BuildStructureList();
        BuildInstanceList();
        BuildPackLists();
    }

    private void BuildThumbnails(IReadOnlyList<ScreenshotEntry> screenshots)
    {
        ShotPanel.Children.Clear();

        foreach (var entry in screenshots)
        {
            var image = new Image { Width = 200, Height = 112, Stretch = Stretch.UniformToFill };

            try
            {
                using var stream = File.OpenRead(entry.FilePath);
                image.Source = Bitmap.DecodeToWidth(stream, 400);
            }
            catch
            {
                // 坏图留空框
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
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(border, $"{Path.GetFileName(entry.FilePath)}\n{entry.TakenAt:yyyy-MM-dd HH:mm}");
            border.PointerPressed += (_, _) => OpenPath(entry.FilePath);

            ShotPanel.Children.Add(border);
        }
    }

    private void BuildReplayList()
    {
        ReplayPanel.Children.Clear();

        if (_replays.Count == 0)
        {
            ReplayPanel.Children.Add(Hint("还没有录像。装 ReplayMod 录一段，文件会出现在 replay_recordings/ 里。"));
            return;
        }

        foreach (var replay in _replays)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            row.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = Path.GetFileName(replay.FilePath) },
                    new TextBlock
                    {
                        Text = $"{replay.Modified:yyyy-MM-dd HH:mm} · {FormatSize(replay.SizeBytes)}",
                        FontSize = 12,
                        Opacity = 0.6
                    }
                }
            });

            var open = new Button { Content = "播放", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            open.Click += (_, _) => OpenPath(replay.FilePath);
            Grid.SetColumn(open, 1);
            row.Children.Add(open);

            var folder = new Button { Content = "打开文件夹", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            folder.Click += (_, _) => OpenPath(Path.GetDirectoryName(replay.FilePath)!);
            Grid.SetColumn(folder, 2);
            row.Children.Add(folder);

            ReplayPanel.Children.Add(row);
        }
    }

    private void BuildStructureList()
    {
        StructurePanel.Children.Clear();

        if (_structures.Count == 0)
        {
            StructurePanel.Children.Add(Hint("还没有结构文件。把 .litematic 放到 schematics/ 或 litematica/ 下就会出现在这里。"));
            return;
        }

        foreach (var structure in _structures)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
            row.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = Path.GetFileName(structure.FilePath) },
                    new TextBlock
                    {
                        Text = $"{structure.Modified:yyyy-MM-dd HH:mm} · {FormatSize(structure.SizeBytes)}",
                        FontSize = 12,
                        Opacity = 0.6
                    }
                }
            });

            var preview = new Button { Content = "预览", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            preview.Click += (_, _) => OpenInLitematica(structure.FilePath);
            Grid.SetColumn(preview, 1);
            row.Children.Add(preview);

            var edit = new Button { Content = "编辑", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            edit.Click += (_, _) => OpenInEditor(structure.FilePath);
            Grid.SetColumn(edit, 2);
            row.Children.Add(edit);

            var folder = new Button { Content = "打开文件夹", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            folder.Click += (_, _) => OpenPath(Path.GetDirectoryName(structure.FilePath)!);
            Grid.SetColumn(folder, 3);
            row.Children.Add(folder);

            StructurePanel.Children.Add(row);
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

    private void BuildPackLists()
    {
        PackABox.Items.Clear();
        PackBBox.Items.Clear();

        foreach (var pack in _packs)
        {
            PackABox.Items.Add(pack.Name);
            PackBBox.Items.Add(pack.Name);
        }

        if (_packs.Count > 0)
        {
            PackABox.SelectedIndex = 0;
            PackBBox.SelectedIndex = _packs.Count > 1 ? 1 : 0;
        }
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

    private void OpenReplayFolder(object? sender, RoutedEventArgs e)
    {
        var folder = _replays.FirstOrDefault() is { } replay
            ? Path.GetDirectoryName(replay.FilePath)
            : MChub.Core.Const.Data.ConfigEntry.MinecraftFolders
                .Select(item => Path.Combine(item.FolderPath, "replay_recordings"))
                .FirstOrDefault(Directory.Exists);

        if (folder is not null)
            OpenPath(folder);
    }

    private void OpenStructureFolder(object? sender, RoutedEventArgs e)
    {
        var folder = _structures.FirstOrDefault() is { } structure
            ? Path.GetDirectoryName(structure.FilePath)
            : MChub.Core.Const.Data.ConfigEntry.MinecraftFolders
                .Select(item => Path.Combine(item.FolderPath, "schematics"))
                .FirstOrDefault(Directory.Exists);

        if (folder is not null)
            OpenPath(folder);
    }

    /// <summary>用已有的 Litematica 查看器打开结构文件（新标签页）。</summary>
    private void OpenInLitematica(string filePath)
    {
        try
        {
            if (MChub.App.MainWindow is not { } window)
                return;

            var page = new LitematicaPage();
            var tab = new TabEntry(window, page);
            window.CreateTab(tab);
            window.SelectTab(tab);
            page.OpenFile(filePath);
            StructureStatusText.Text = $"已在 Litematica 查看器打开：{Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StructureStatusText.Text = $"打开失败：{ex.Message}";
        }
    }

    /// <summary>在结构编辑器里打开（带 3D 预览与方块替换）。</summary>
    private void OpenInEditor(string filePath)
    {
        try
        {
            if (MChub.App.MainWindow is not { } window)
                return;

            var page = new StructureEditorPage();
            var tab = new TabEntry(window, page);
            window.CreateTab(tab);
            window.SelectTab(tab);
            page.OpenFile(filePath);
            StructureStatusText.Text = $"正在编辑：{Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StructureStatusText.Text = $"打开编辑器失败：{ex.Message}";
        }
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
            MarkdownBox.Text = CreatorAssetService.BuildMarkdown(assets, _settings.IncludeDisabledMods);
            ModSummaryText.Text =
                $"{assets.Name}：{assets.EnabledModCount} 个启用模组" +
                (assets.Mods.Count > assets.EnabledModCount
                    ? $"（另有 {assets.Mods.Count - assets.EnabledModCount} 个已禁用）"
                    : string.Empty) +
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

    private void CompareTexture(object? sender, RoutedEventArgs e)
    {
        if (_packs.Count == 0)
        {
            CompareStatus.Text = "没有找到资源包（resourcepacks/ 下没有目录或 zip）。";
            return;
        }

        var path = TexturePathBox.Text;
        if (string.IsNullOrWhiteSpace(path))
            path = _settings.DefaultTexturePath;

        var packA = PackABox.SelectedIndex >= 0 ? _packs[PackABox.SelectedIndex] : null;
        var packB = PackBBox.SelectedIndex >= 0 ? _packs[PackBBox.SelectedIndex] : null;

        var noteA = string.Empty;
        var noteB = string.Empty;
        Bitmap? imageA = null;
        Bitmap? imageB = null;

        if (packA is not null) imageA = LoadTexture(packA, path, out noteA);
        if (packB is not null) imageB = LoadTexture(packB, path, out noteB);

        PackAImage.Source = imageA;
        PackBImage.Source = imageB;

        PackALabel.Text = packA is null ? "（未选择）" : $"{packA.Name}  {noteA}";
        PackBLabel.Text = packB is null ? "（未选择）" : $"{packB.Name}  {noteB}";

        CompareStatus.Text = $"贴图：{path}";
    }

    private static Bitmap? LoadTexture(PackEntry pack, string path, out string note)
    {
        var bytes = CreatorAssetService.ReadTexture(pack, path);
        if (bytes is null)
        {
            note = "该包没有这张图";
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            note = string.Empty;
            return new Bitmap(stream);
        }
        catch
        {
            note = "图片无法解码";
            return null;
        }
    }

    private void SaveSettings(object? sender, RoutedEventArgs e)
    {
        _settings.IncludeDisabledMods = IncludeDisabledBox.IsChecked == true;
        if (ThumbLimitBox.Value is { } value)
            _settings.ThumbnailLimit = (int)value;
        if (!string.IsNullOrWhiteSpace(DefaultTextureBox.Text))
            _settings.DefaultTexturePath = DefaultTextureBox.Text!;

        _settings.Save();
        SettingsStatus.Text = "已保存。";
        RefreshAll();
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Opacity = 0.6,
        TextWrapping = TextWrapping.Wrap
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:F1} GB",
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };

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
