using Iridium.Models.Minecraft;

namespace Iridium.Extension.Minecraft.Layout;

/// <summary>
/// MChub keeps its shared metadata under a <c>meta/</c> directory instead of the
/// launcher root: libraries in <c>meta/libraries</c>, assets in <c>meta/assets</c>,
/// native libraries in <c>meta/natives/&lt;instance&gt;</c> and version manifests/jars in
/// <c>meta/versions/&lt;id&gt;</c>. Instances live under <c>instances/&lt;name&gt;</c>
/// with game files directly inside the instance directory.
/// </summary>
public sealed class MChubMcLayout : SharedMetadataLayout {
    public override string Format => "MChubMc";

    protected override string InstanceFolder => "instances";

    protected override string MetadataFolder => "meta";

    /// <summary>
    /// 原生库必须**按实例隔离**：LWJGL 的 Java 包与原生库版本必须严格一致
    /// （1.16 用 3.2.1，1.21 用 3.3.x，1.21.5+ 用 3.4.x），放同一个目录会互相覆盖，
    /// 老实例就会加载到新版原生库，报
    /// "Incompatible Java and native library versions detected"。
    /// </summary>
    public override string GetNativesDirectory(MinecraftEntry entry) =>
        Path.Combine(GetMetadataRoot(entry), "natives", entry.Id);
}
