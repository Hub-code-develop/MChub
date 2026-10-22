using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MChub.Core.Structure;

/// <summary>内置等轴渲染器工厂。换成真 3D 库时，这个类可以整块删掉。</summary>
public sealed class IsometricRendererFactory : IStructureRendererFactory
{
    public string Id => "isometric";
    public string DisplayName => "等轴投影（内置 / 无第三方依赖）";
    public IStructureRenderer Create() => new IsometricRenderer();
}

/// <summary>
/// 纯软件等轴投影：把方块画成菱形顶面 + 两个侧面，painter's algorithm 从远到近。
/// 没有任何图形库依赖，所以能跑在任何平台；性能上限也摆在这里（大结构会采样）。
/// </summary>
public sealed class IsometricRenderer : IStructureRenderer, IStructureViewCamera
{
    private const int MaxDrawnBlocks = 12000;
    private const double Tile = 9.0;

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Dictionary<uint, IBrush> _brushes = [];

    private StructureScene? _scene;
    private int _quarter;
    private double _zoom = 1.0;

    public string Name => "等轴投影";

    public Control CreateView() => _image;

    public void SetScene(StructureScene scene)
    {
        _scene = scene;
        Render();
    }

    public void Rotate(int delta)
    {
        _quarter = (_quarter + delta % 4 + 4) % 4;
        Render();
    }

    public void Zoom(double factor)
    {
        _zoom = Math.Clamp(_zoom * factor, 0.35, 3.0);
        Render();
    }

    private void Render()
    {
        if (_scene is not { Blocks.Count: > 0 } scene)
        {
            _image.Source = null;
            return;
        }

        var tile = Tile * _zoom;
        var step = Math.Max(1, (int)Math.Ceiling(scene.Blocks.Count / (double)MaxDrawnBlocks));

        var blocks = new List<(int A, int B, int Y, uint Color)>(Math.Min(scene.Blocks.Count, MaxDrawnBlocks));
        for (var i = 0; i < scene.Blocks.Count; i += step)
        {
            var block = scene.Blocks[i];
            var (a, b) = Rotate(block.X, block.Z, _quarter);
            blocks.Add((a, b, block.Y, block.Color));
        }

        // 远 → 近
        blocks.Sort((left, right) =>
        {
            var depth = (left.A + left.B).CompareTo(right.A + right.B);
            return depth != 0 ? depth : left.Y.CompareTo(right.Y);
        });

        var minA = blocks.Min(item => item.A);
        var minB = blocks.Min(item => item.B);
        var maxA = blocks.Max(item => item.A);
        var maxB = blocks.Max(item => item.B);
        var maxY = blocks.Max(item => item.Y);

        var halfWidth = (maxA - minA + maxB - minB + 2) * tile;
        var height = (maxA - minA + maxB - minB + 2) * tile / 2 + (maxY + 2) * tile;

        var width = (int)Math.Clamp(halfWidth, 64, 2400);
        var pixelHeight = (int)Math.Clamp(height, 64, 1600);

        var bitmap = new RenderTargetBitmap(new PixelSize(width, pixelHeight), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.Transparent, new Rect(0, 0, width, pixelHeight));

            var originX = (minB - minA) * tile + width / 2.0;
            var originY = tile * 2 + (maxY) * tile;

            foreach (var (a, b, y, color) in blocks)
            {
                var sx = originX + (a - minA) * tile - (b - minB) * tile;
                var sy = originY + (a - minA) * tile / 2 + (b - minB) * tile / 2 - y * tile;

                DrawCube(context, sx, sy, tile, color);
            }
        }

        _image.Source = bitmap;
    }

    private void DrawCube(DrawingContext context, double x, double y, double tile, uint color)
    {
        var top = GetBrush(Lighten(color, 1.18));
        var left = GetBrush(Lighten(color, 0.72));
        var right = GetBrush(Lighten(color, 0.88));
        var height = tile;

        // 顶面菱形
        var topGeometry = new StreamGeometry();
        using (var ctx = topGeometry.Open())
        {
            ctx.BeginFigure(new Point(x, y), true);
            ctx.LineTo(new Point(x + tile, y + tile / 2));
            ctx.LineTo(new Point(x, y + tile));
            ctx.LineTo(new Point(x - tile, y + tile / 2));
            ctx.EndFigure(true);
        }

        // 左侧面
        var leftGeometry = new StreamGeometry();
        using (var ctx = leftGeometry.Open())
        {
            ctx.BeginFigure(new Point(x - tile, y + tile / 2), true);
            ctx.LineTo(new Point(x, y + tile));
            ctx.LineTo(new Point(x, y + tile + height));
            ctx.LineTo(new Point(x - tile, y + tile / 2 + height));
            ctx.EndFigure(true);
        }

        // 右侧面
        var rightGeometry = new StreamGeometry();
        using (var ctx = rightGeometry.Open())
        {
            ctx.BeginFigure(new Point(x, y + tile), true);
            ctx.LineTo(new Point(x + tile, y + tile / 2));
            ctx.LineTo(new Point(x + tile, y + tile / 2 + height));
            ctx.LineTo(new Point(x, y + tile + height));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(left, null, leftGeometry);
        context.DrawGeometry(right, null, rightGeometry);
        context.DrawGeometry(top, null, topGeometry);
    }

    private IBrush GetBrush(uint argb)
    {
        if (_brushes.TryGetValue(argb, out var brush))
            return brush;

        brush = new SolidColorBrush(Color.FromUInt32(argb));
        _brushes[argb] = brush;
        return brush;
    }

    private static uint Lighten(uint argb, double factor)
    {
        var a = (argb >> 24) & 0xFF;
        var r = Math.Clamp((int)(((argb >> 16) & 0xFF) * factor), 0, 255);
        var g = Math.Clamp((int)(((argb >> 8) & 0xFF) * factor), 0, 255);
        var b = Math.Clamp((int)((argb & 0xFF) * factor), 0, 255);
        return (a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    private static (int A, int B) Rotate(int x, int z, int quarter) => quarter switch
    {
        1 => (z, -x),
        2 => (-x, -z),
        3 => (-z, x),
        _ => (x, z)
    };

    public void Dispose()
    {
        _image.Source = null;
        _scene = null;
        _brushes.Clear();
    }
}
