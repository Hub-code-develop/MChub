namespace MChub.Core.Minecraft.Models;

public enum LoaderKind
{
    Fabric,
    Forge,
    NeoForge,
    Quilt,
    OptiFine,

    // 旧版 / Fabric 系分支装载器（元数据与 Fabric 兼容，走 fabricmc 那套 API）。
    Ornithe,
    LegacyFabric,
    Babric,

    // 独立机制装载器（不走 Fabric 元数据）。
    Rift,
    Bta,

    // 1.5.2 ~ 1.12.2 时代的轻量装载器（自带 maven 仓库与 tweakClass）。
    LiteLoader
}