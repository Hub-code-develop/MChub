using Iridium.Models.Minecraft;
using Iridium.Utilities;
using Iridium.Interfaces;

namespace Iridium.Minecraft.Layout;

public sealed class StandardLayout : IMinecraftLayout {
    public string Format => "Standard";

    public string GetInstanceDirectory(string id) => Path.Combine("versions", id);

    public string GetInstanceRoot(MinecraftEntry entry) => GetRoot(entry);

    public string GetGameDirectory(MinecraftEntry entry) {
        var versionDir = GetVersionDirectory(entry);

        // Version-isolated instances (HMCL etc.) keep game files inside the version directory.
        if (File.Exists(Path.Combine(versionDir, "options.txt"))
            || Directory.Exists(Path.Combine(versionDir, "mods"))
            || Directory.Exists(Path.Combine(versionDir, "saves")))
            return versionDir;

        return GetRoot(entry);
    }

    /// <summary>
    /// 用户显式开启“独立实例”时，游戏目录就是 <c>versions/{id}</c>。
    /// 官方启动器装出来的版本没有隔离标记，光靠 GetGameDirectory 的自动探测
    /// 永远返回 .minecraft 根目录，开关看起来就没生效。
    /// </summary>
    public string GetIsolatedGameDirectory(MinecraftEntry entry) => GetVersionDirectory(entry);

    public string GetLibrariesRoot(MinecraftEntry entry) => Path.Combine(GetRoot(entry), "libraries");

    public string GetAssetsRoot(MinecraftEntry entry) => Path.Combine(GetRoot(entry), "assets");

    public string GetNativesDirectory(MinecraftEntry entry) => Path.Combine(GetVersionDirectory(entry), $"natives-{PlatformHelper.GetPlatformInfo()}");

    public string GetVersionJarPath(MinecraftEntry entry) {
        var jarName = string.IsNullOrEmpty(entry.Jar) ? entry.Id : entry.Jar;
        return Path.Combine(GetVersionDirectory(entry), $"{jarName}.jar");
    }

    public string GetVersionJsonPath(MinecraftEntry entry) => Path.Combine(GetRoot(entry), "versions", entry.Id, $"{entry.Id}.json");

    private static string GetVersionDirectory(MinecraftEntry entry) {
        return string.IsNullOrEmpty(entry.InstancePath)
            ? entry.InstancePath
            : Path.GetFullPath(entry.InstancePath);
    }

    private static string GetRoot(MinecraftEntry entry) {
        var versionDir = GetVersionDirectory(entry);
        if (versionDir.Length == 0)
            return versionDir;

        return Path.GetDirectoryName(Path.GetDirectoryName(versionDir)) ?? versionDir;
    }
}
