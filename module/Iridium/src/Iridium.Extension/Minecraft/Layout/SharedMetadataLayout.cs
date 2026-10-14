using Iridium.Minecraft.Layout;
using Iridium.Models.Minecraft;
using Iridium.Interfaces;

namespace Iridium.Extension.Minecraft.Layout;

/// <summary>
/// Base layout for launchers that keep their shared metadata (libraries, assets, version
/// manifests, natives) in a root-relative directory while game directories live in
/// per-instance folders, e.g. <c>Instances/&lt;name&gt;</c> + <c>Install/</c> (CurseForge)
/// or <c>instances/&lt;name&gt;</c> + <c>meta/</c> (MChub, Modrinth).
/// </summary>
public abstract class SharedMetadataLayout : IMinecraftLayout {
    /// <summary>Format identity of this layout (e.g. "CurseForge", "Modrinth", "MChubMc").</summary>
    public abstract string Format { get; }

    /// <summary>Name of the folder holding per-instance game directories (e.g. "instances").</summary>
    protected abstract string InstanceFolder { get; }

    /// <summary>Name of the folder holding the launcher's shared metadata (e.g. "meta").</summary>
    protected abstract string MetadataFolder { get; }

    public abstract string GetNativesDirectory(MinecraftEntry entry);

    public string GetInstanceDirectory(string id) => Path.Combine(InstanceFolder, id);

    public string GetInstanceRoot(MinecraftEntry entry) => entry.InstancePath;

    public string GetGameDirectory(MinecraftEntry entry) => entry.InstancePath;

    public string GetLibrariesRoot(MinecraftEntry entry) => Path.Combine(GetMetadataRoot(entry), "libraries");

    public string GetAssetsRoot(MinecraftEntry entry) => Path.Combine(GetMetadataRoot(entry), "assets");

    public string GetVersionJarPath(MinecraftEntry entry) {
        var id = ResolveVersionId(entry);
        return Path.Combine(GetMetadataRoot(entry), "versions", id, $"{id}.jar");
    }

    public string GetVersionJsonPath(MinecraftEntry entry) {
        var id = ResolveVersionId(entry);
        return Path.Combine(GetMetadataRoot(entry), "versions", id, $"{id}.json");
    }

    /// <summary>
    /// Version id used for the manifest/jar lookup. Defaults to the Minecraft version;
    /// providers that resolve a distinct loader version id override this.
    /// </summary>
    protected virtual string ResolveVersionId(MinecraftEntry entry) =>
        entry.MinecraftVersion.Length > 0 ? entry.MinecraftVersion : entry.Id;

    protected string GetMetadataRoot(MinecraftEntry entry) {
        // InstancePath = {root}/{InstanceFolder}/{name} -> metadata root is {root}/{MetadataFolder}.
        var instanceDir = Path.GetFullPath(entry.InstancePath);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(instanceDir)) ?? instanceDir;
        return Path.Combine(root, MetadataFolder);
    }
}
