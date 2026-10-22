using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MChub.Core.Structure;
using MChub.LitematicaViewer.Models;
using MChub.LitematicaViewer.Parsers;
using MChub.LitematicaViewer.Services;
using MChub.Module;
using MChub.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace MChub.Views.Pages;

/// <summary>
/// 结构编辑器：打开 .litematic / .schematic，替换方块，3D 预览，另存为。
///
/// 3D 预览走 <see cref="StructureRenderers"/> 抽象：
/// - 默认是内置等轴软件渲染（无第三方依赖）
/// - 以后换成真 3D 库时，只要写一个 IStructureRendererFactory 注册进去，
///   这个页面一行都不用改（下拉框和相机按钮都会自适应）。
/// </summary>
public partial class StructureEditorPage : Dsc, ITioTabPage
{
    private const int MaxHarvest = 400_000;

    private readonly LitematicParser _parser = new();
    private readonly BlockReplacer _replacer = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    private LitematicFile? _file;
    private string? _path;
    private IStructureRenderer? _renderer;
    private bool _rendererReady;

    public StructureEditorPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_rendererReady) return;
            _rendererReady = true;
            BuildRendererList();
        };
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "结构编辑",
        IconGlyph = "\ue632",
        IconFont = IconResources.FontFamilyName
    };

    public TabEntry HostTab { get; set; } = null!;

    /// <summary>从创作台带文件进来打开。</summary>
    public void OpenFile(string path)
    {
        try
        {
            _file = _parser.Load(path);
            _path = path;
            FileText.Text = Path.GetFileName(path);
            StatusText.Text = $"{_file.Regions.Count} 个区域，{_file.TotalBlocks:N0} 个方块";
            RefreshView();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"打开失败：{ex.Message}";
        }
    }

    private void BuildRendererList()
    {
        RendererBox.Items.Clear();
        foreach (var factory in StructureRenderers.All)
            RendererBox.Items.Add(factory.DisplayName);

        if (RendererBox.ItemCount > 0)
            RendererBox.SelectedIndex = 0;

        RendererBox.SelectionChanged += (_, _) => CreateRenderer();
        CreateRenderer();
    }

    private void CreateRenderer()
    {
        _renderer?.Dispose();

        var factories = StructureRenderers.All;
        var index = RendererBox.SelectedIndex >= 0 && RendererBox.SelectedIndex < factories.Count
            ? RendererBox.SelectedIndex
            : 0;

        _renderer = factories.Count > 0 ? factories[index].Create() : null;
        RendererHost.Content = _renderer?.CreateView();

        // 渲染器不支持相机就藏掉按钮，而不是让它点了没反应
        var hasCamera = _renderer is IStructureViewCamera;
        RotateLeftButton.IsVisible = hasCamera;
        RotateRightButton.IsVisible = hasCamera;
        ZoomInButton.IsVisible = hasCamera;
        ZoomOutButton.IsVisible = hasCamera;

        RefreshView();
    }

    private void RefreshView()
    {
        if (_file is null || _renderer is null)
            return;

        var scene = BuildScene(_file);
        _renderer.SetScene(scene);

        BlockList.Items.Clear();
        foreach (var (blockId, count) in _counts.OrderByDescending(pair => pair.Value))
            BlockList.Items.Add($"{blockId}  ×{count}");

        FootText.Text = scene.Skipped > 0
            ? $"显示 {scene.Blocks.Count:N0} 个方块（另有 {scene.Skipped:N0} 个因体积上限未画）。渲染器：{_renderer.Name}"
            : $"显示 {scene.Blocks.Count:N0} 个方块。渲染器：{_renderer.Name}";
    }

    /// <summary>把 Litematica 的 region 展开成渲染器能吃的平面列表（跳过空气）。</summary>
    private StructureScene BuildScene(LitematicFile file)
    {
        _counts.Clear();
        var blocks = new List<StructureBlock>();
        var skipped = 0;

        foreach (var region in file.Regions)
        {
            var air = region.Palette.FirstOrDefault(pair => pair.Value.BlockId.Contains("air", StringComparison.OrdinalIgnoreCase)).Key;
            var hasAir = region.Palette.Any(pair => pair.Value.BlockId.Contains("air", StringComparison.OrdinalIgnoreCase));

            for (var y = region.MinY; y <= region.MaxY; y++)
            for (var z = region.MinZ; z <= region.MaxZ; z++)
            for (var x = region.MinX; x <= region.MaxX; x++)
            {
                var paletteIndex = region.GetPaletteIndexAt(x, y, z);
                if (hasAir && paletteIndex == air) continue;

                var state = region.GetBlock(x, y, z);
                if (state is null) continue;
                if (state.BlockId.Contains("air", StringComparison.OrdinalIgnoreCase)) continue;

                _counts.TryGetValue(state.BlockId, out var count);
                _counts[state.BlockId] = count + 1;

                if (blocks.Count >= MaxHarvest)
                {
                    skipped++;
                    continue;
                }

                blocks.Add(new StructureBlock(x, y, z, state.BlockId, BlockColors.For(state.BlockId)));
            }
        }

        return new StructureScene
        {
            Blocks = blocks,
            SizeX = (int)file.EnclosingSizeX,
            SizeY = (int)file.EnclosingSizeY,
            SizeZ = (int)file.EnclosingSizeZ,
            Skipped = skipped
        };
    }

    private async void Browse(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择结构文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Litematica / Schematic")
                {
                    Patterns = ["*.litematic", "*.schematic"]
                }
            ]
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is not null) OpenFile(path);
    }

    private async void SaveAs(object? sender, RoutedEventArgs e)
    {
        if (_file is null)
        {
            StatusText.Text = "还没有打开文件。";
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var suggested = string.IsNullOrWhiteSpace(_path)
            ? "structure.litematic"
            : Path.GetFileNameWithoutExtension(_path) + "-edited.litematic";

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为",
            SuggestedFileName = suggested,
            DefaultExtension = "litematic",
            FileTypeChoices = [new FilePickerFileType("Litematica") { Patterns = ["*.litematic"] }]
        });

        var target = file?.TryGetLocalPath();
        if (target is null) return;

        try
        {
            _parser.Save(_file, target);
            StatusText.Text = $"已保存：{Path.GetFileName(target)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"保存失败：{ex.Message}";
        }
    }

    private void ReplaceSelected(object? sender, RoutedEventArgs e)
    {
        if (_file is null)
        {
            StatusText.Text = "还没有打开文件。";
            return;
        }

        var selected = BlockList.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected))
        {
            StatusText.Text = "先在左边选一个方块类型。";
            return;
        }

        var from = selected.Split("  ×")[0].Trim();
        var to = TargetBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(to))
        {
            StatusText.Text = "填一下要替换成什么方块 id，比如 minecraft:stone。";
            return;
        }

        if (!to.Contains(':'))
            to = "minecraft:" + to;

        try
        {
            var replaced = _replacer.Replace(_file, from, to);
            StatusText.Text = replaced > 0
                ? $"已把 {from} 替换为 {to}（{replaced:N0} 个方块），记得另存为。"
                : $"没有找到 {from}，未改动。";
            RefreshView();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"替换失败：{ex.Message}";
        }
    }

    private void RotateLeft(object? sender, RoutedEventArgs e) => (_renderer as IStructureViewCamera)?.Rotate(-1);

    private void RotateRight(object? sender, RoutedEventArgs e) => (_renderer as IStructureViewCamera)?.Rotate(1);

    private void ZoomIn(object? sender, RoutedEventArgs e) => (_renderer as IStructureViewCamera)?.Zoom(1.2);

    private void ZoomOut(object? sender, RoutedEventArgs e) => (_renderer as IStructureViewCamera)?.Zoom(1 / 1.2);
}
