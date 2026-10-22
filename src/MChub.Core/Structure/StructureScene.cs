using Avalonia.Controls;

namespace MChub.Core.Structure;

/// <summary>渲染用的一个方块（只有位置、id 和颜色，够任何渲染器用）。</summary>
public readonly record struct StructureBlock(int X, int Y, int Z, string BlockId, uint Color);

/// <summary>
/// 与渲染库无关的场景描述。换渲染库时只需要能把这份数据喂给新实现，
/// 上层（编辑页）完全不用动。
/// </summary>
public sealed class StructureScene
{
    public required IReadOnlyList<StructureBlock> Blocks { get; init; }
    public int SizeX { get; init; }
    public int SizeY { get; init; }
    public int SizeZ { get; init; }

    /// <summary>因为太大而被跳过的方块数（0 表示全部画了）。</summary>
    public int Skipped { get; init; }
}

/// <summary>
/// 结构渲染器。实现方只需要给出一个 Control 并把场景画出来。
/// </summary>
public interface IStructureRenderer : IDisposable
{
    string Name { get; }

    /// <summary>创建一个可以塞进任意容器的视图（同一个实例只调用一次）。</summary>
    Control CreateView();

    /// <summary>载入 / 更新场景，实现方自己决定是否重绘。</summary>
    void SetScene(StructureScene scene);
}

/// <summary>
/// 可选能力：带相机操作的渲染器实现它，编辑页就会显示旋转/缩放按钮。
/// 新渲染库如果没有这个概念，不实现即可，编辑页会自动隐藏按钮。
/// </summary>
public interface IStructureViewCamera
{
    void Rotate(int delta);
    void Zoom(double factor);
}

/// <summary>渲染器工厂：注册进 <see cref="StructureRenderers"/> 即可被界面发现。</summary>
public interface IStructureRendererFactory
{
    string Id { get; }
    string DisplayName { get; }
    IStructureRenderer Create();
}

/// <summary>
/// 渲染器注册表。默认注册内置等轴渲染器；
/// 将来接真 3D 库时，只要写一个 <see cref="IStructureRendererFactory"/> 并在启动时 Register，
/// 编辑页的下拉框就会自动多出一项，不用改编辑页代码。
/// </summary>
public static class StructureRenderers
{
    private static readonly List<IStructureRendererFactory> Registered = [];

    static StructureRenderers()
    {
        Register(new IsometricRendererFactory());
    }

    public static void Register(IStructureRendererFactory factory)
    {
        if (Registered.Any(item => string.Equals(item.Id, factory.Id, StringComparison.OrdinalIgnoreCase)))
            return;
        Registered.Add(factory);
    }

    public static IReadOnlyList<IStructureRendererFactory> All => Registered;

    public static IStructureRenderer CreateDefault() => Registered[0].Create();
}

/// <summary>给没有贴图的方块一个稳定的近似色（常见方块手写配色，其余按 id 哈希）。</summary>
public static class BlockColors
{
    private static readonly Dictionary<string, uint> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minecraft:stone"] = 0xFF7F7F7F,
        ["minecraft:cobblestone"] = 0xFF6E6E6E,
        ["minecraft:dirt"] = 0xFF79553A,
        ["minecraft:grass_block"] = 0xFF5D9C3C,
        ["minecraft:sand"] = 0xFFDBCE9A,
        ["minecraft:gravel"] = 0xFF8A8683,
        ["minecraft:oak_planks"] = 0xFFB58B54,
        ["minecraft:spruce_planks"] = 0xFF7A5A34,
        ["minecraft:birch_planks"] = 0xFFD7C68B,
        ["minecraft:oak_log"] = 0xFF6C5535,
        ["minecraft:spruce_log"] = 0xFF3F2E1B,
        ["minecraft:glass"] = 0xFFAFD7E3,
        ["minecraft:water"] = 0xFF3A62C8,
        ["minecraft:lava"] = 0xFFE2601B,
        ["minecraft:snow_block"] = 0xFFF2F7F7,
        ["minecraft:ice"] = 0xFFA8C6E8,
        ["minecraft:bricks"] = 0xFF9A5B4B,
        ["minecraft:deepslate"] = 0xFF4A4A4F,
        ["minecraft:netherrack"] = 0xFF6E2A2A,
        ["minecraft:obsidian"] = 0xFF1B1426,
        ["minecraft:quartz_block"] = 0xFFE9E4D9,
        ["minecraft:iron_block"] = 0xFFD8D8D8,
        ["minecraft:gold_block"] = 0xFFF5D547,
        ["minecraft:diamond_block"] = 0xFF61DCD5,
        ["minecraft:emerald_block"] = 0xFF3FDC6B,
        ["minecraft:redstone_block"] = 0xFFB01E1E,
        ["minecraft:white_wool"] = 0xFFE9ECEC,
        ["minecraft:black_wool"] = 0xFF1D1D21
    };

    public static uint For(string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
            return 0xFF9A9A9A;

        if (Known.TryGetValue(blockId, out var color))
            return color;

        // 按名字里的关键词猜一下，再不行就哈希
        var id = blockId.ToLowerInvariant();
        if (id.Contains("leaves")) return 0xFF4E7A32;
        if (id.Contains("wool") || id.Contains("concrete") || id.Contains("terracotta")) return HashColor(id, 0.55f, 0.45f);
        if (id.Contains("planks") || id.Contains("log") || id.Contains("wood")) return 0xFF9A7448;
        if (id.Contains("glass")) return 0xFFB7DCE8;
        if (id.Contains("brick") || id.Contains("tile")) return 0xFF9A5B4B;
        if (id.Contains("ore")) return 0xFF8A8A8A;

        return HashColor(id, 0.45f, 0.35f);
    }

    private static uint HashColor(string value, float saturation, float lightness)
    {
        unchecked
        {
            var hash = 17u;
            foreach (var c in value) hash = hash * 31u + c;

            var hue = hash % 360u;
            return FromHsl(hue / 360.0, saturation, lightness);
        }
    }

    private static uint FromHsl(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        return 0xFF000000u | ((uint)(r * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(b * 255);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
