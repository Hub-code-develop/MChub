using Iridium.Models.Minecraft;

namespace Iridium.Interfaces;

public interface IMinecraftLayout {
    /// <summary>
    /// Format identity of this layout, e.g. "Standard", "Prism", "CurseForge". The layout is
    /// the single source of the Minecraft format: it defines both the directory layout and the
    /// format it belongs to.
    /// </summary>
    string Format { get; }

    /// <summary>
    /// Root-relative path of an instance directory, e.g. "versions/1.21".
    /// </summary>
    string GetInstanceDirectory(string id);

    string GetInstanceRoot(MinecraftEntry entry);
    string GetGameDirectory(MinecraftEntry entry);

    /// <summary>
    /// Game directory to use when the instance is explicitly version-isolated.
    /// Most launchers already give every instance its own directory, so the default
    /// is the same as <see cref="GetGameDirectory"/>; traditional layouts override it
    /// to point at <c>versions/{id}</c>.
    /// </summary>
    string GetIsolatedGameDirectory(MinecraftEntry entry) => GetGameDirectory(entry);
    string GetLibrariesRoot(MinecraftEntry entry);
    string GetAssetsRoot(MinecraftEntry entry);
    string GetNativesDirectory(MinecraftEntry entry);
    string GetVersionJarPath(MinecraftEntry entry);
    string GetVersionJsonPath(MinecraftEntry entry);
}
