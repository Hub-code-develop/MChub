using Iridium.Models.Minecraft;

namespace Iridium.Extension.Minecraft.Layout;

/// <summary>
/// MChub keeps its shared metadata under a <c>meta/</c> directory instead of the
/// launcher root: libraries in <c>meta/libraries</c>, assets in <c>meta/assets</c>,
/// native libraries in <c>meta/natives</c> and version manifests/jars in
/// <c>meta/versions/&lt;id&gt;</c>. Instances live under <c>instances/&lt;name&gt;</c>
/// with game files directly inside the instance directory.
/// </summary>
public sealed class MChubMcLayout : SharedMetadataLayout {
    public override string Format => "MChubMc";

    protected override string InstanceFolder => "instances";

    protected override string MetadataFolder => "meta";

    public override string GetNativesDirectory(MinecraftEntry entry) => Path.Combine(GetMetadataRoot(entry), "natives");
}
