using Iridium.Installation;
using Iridium.Models.Authentication;
using Iridium.Models.Minecraft;
using Iridium.Interfaces;
using Iridium.Models.Java;

namespace Iridium.Models.Launch;

public sealed record LaunchConfig {
    public bool IsFullscreen { get; set; }
    public bool IsEnableIndependency { get; set; } = true;
    
    public int Width { get; set; } = 854;
    public int Height { get; set; } = 480;
    public int MinMemorySize { get; set; } = 512;
    public int MaxMemorySize { get; set; } = 1024;
    
    public string? LauncherName { get; set; }
    public string? NativesFolder { get; set; }
    public string? SaveName { get; set; }

    public Account? Account { get; set; }
    public JavaEntry? JavaPath { get; set; }
    public ServerInfo? ServerInfo { get; set; }
    
    public IEnumerable<string> JvmArguments { get; set; } = [];

    public IDictionary<string, string>? EnvironmentVariables { get; set; }

    public string? WrapperCommand { get; set; }
}

public sealed record ServerInfo {
    public int Port { get; set; } = 25565;
    public string Address { get; set; } = null!;
}

internal sealed class LaunchDirectories {
    public string InstanceRoot { get; init; } = string.Empty;
    public string GameDirectory { get; init; } = string.Empty;
    public string LibrariesRoot { get; init; } = string.Empty;
    public string AssetsRoot { get; init; } = string.Empty;
    public string NativesDirectory { get; init; } = string.Empty;
    public string VersionJarPath { get; init; } = string.Empty;

    public static LaunchDirectories Resolve(IMinecraftLayout layout, MinecraftEntry entry, LaunchConfig config) {
        return new LaunchDirectories {
            InstanceRoot = layout.GetInstanceRoot(entry),
            VersionJarPath = layout.GetVersionJarPath(entry),
            LibrariesRoot = layout.GetLibrariesRoot(entry),
            AssetsRoot = new AssetsReconstructor(layout).ResolveActualAssetsRoot(entry),
            NativesDirectory = string.IsNullOrEmpty(config.NativesFolder)
                ? layout.GetNativesDirectory(entry)
                : config.NativesFolder,
            // 独立实例：显式用 versions/{id}（标准布局）。
            // 非独立：仍走自动探测，这样 HMCL 之类已经隔离过的目录不会被强行拉回根目录。
            GameDirectory = config.IsEnableIndependency
                ? layout.GetIsolatedGameDirectory(entry)
                : layout.GetGameDirectory(entry),
        };
    }
}
