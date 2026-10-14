using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Components.Installer;
using MChub.Core.Minecraft.Models;

namespace MChub.Core.Minecraft;

/// <summary>
/// 装载器目录：集中管理各装载器的元数据根、版本枚举与兼容性判断，
/// 免得把「Ornithe 用哪个 meta 地址」这类信息散落到一堆 switch 里。
/// </summary>
public static class LoaderCatalog
{
    /// <summary>Ornithe 元数据根（Fabric 兼容，v2）。</summary>
    public const string OrnitheMetaBaseUrl = "https://meta.ornithemc.net/v2";

    /// <summary>LegacyFabric 元数据根（Fabric 兼容，v2）。</summary>
    public const string LegacyFabricMetaBaseUrl = "https://meta.legacyfabric.net/v2";

    /// <summary>Babric 元数据根（Fabric 兼容，v2）。</summary>
    public const string BabricMetaBaseUrl = "https://meta.babric.glass-launcher.net/v2";

    /// <summary>是否为 Fabric 系（元数据与 Fabric 兼容，可复用 FabricInstaller）。</summary>
    public static bool IsFabricCompatible(LoaderKind kind) =>
        kind is LoaderKind.Fabric or LoaderKind.Ornithe or LoaderKind.LegacyFabric or LoaderKind.Babric;

    /// <summary>取该装载器对应的 Fabric 元数据根；非 Fabric 系返回 null。</summary>
    public static string? FabricMetaBaseUrl(LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => FabricInstaller.DefaultMetaBaseUrl,
        LoaderKind.Ornithe => OrnitheMetaBaseUrl,
        LoaderKind.LegacyFabric => LegacyFabricMetaBaseUrl,
        LoaderKind.Babric => BabricMetaBaseUrl,
        _ => null,
    };

    /// <summary>该装载器是否需要在下载/安装时装 Java 运行时。</summary>
    public static bool RequiresJavaRuntime(LoaderKind kind) =>
        kind is LoaderKind.Forge or LoaderKind.NeoForge or LoaderKind.Quilt or LoaderKind.OptiFine
            or LoaderKind.Rift or LoaderKind.Bta;

    /// <summary>枚举某原版版本下可用的装载器版本（不支持的组合返回空列表）。</summary>
    public static async Task<IReadOnlyList<IInstallEntry>> FetchVersionsAsync(LoaderKind kind, string mcVersion,
        CancellationToken cancellationToken = default)
    {
        if (IsFabricCompatible(kind))
        {
            var entries = await FabricInstaller.EnumerableFabricAsync(mcVersion, cancellationToken,
                FabricMetaBaseUrl(kind));
            return entries.Cast<IInstallEntry>().ToList();
        }

        return kind switch
        {
            LoaderKind.Bta => (await BtaInstaller.EnumerableBtaAsync(mcVersion, cancellationToken))
                .Cast<IInstallEntry>().ToList(),
            LoaderKind.Rift => (await RiftInstaller.EnumerableRiftAsync(mcVersion, cancellationToken))
                .Cast<IInstallEntry>().ToList(),
            LoaderKind.LiteLoader => (await LiteLoaderInstaller.EnumerableLiteLoaderAsync(mcVersion, cancellationToken))
                .Cast<IInstallEntry>().ToList(),
            _ => [],
        };
    }
}
