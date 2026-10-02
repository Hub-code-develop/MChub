using MChub.Localization;

namespace MChub.Bedrock.MacOS;

/// <summary>
/// macOS 上启动 Windows GDK 版基岩版所需的 Wine 系运行时。
/// <paramref name="PrefixAsArgument"/> 为 true 时调用形式为
/// <c>&lt;运行时&gt; &lt;前缀&gt; &lt;可执行文件&gt; [参数]</c>（Apple Game Porting Toolkit 的包装脚本），
/// 否则为 <c>&lt;运行时&gt; &lt;可执行文件&gt; [参数]</c> 并依赖 <c>WINEPREFIX</c>。
/// </summary>
public sealed record MacBedrockRuntime(string WineBinary, string WineRoot, string PrefixPath,
    bool PrefixAsArgument, McbeMacOSRuntime? Bundled = null)
{
    /// <summary>标准 Wine 布局下的 drive_c 根目录。</summary>
    public string DriveC => Path.Combine(PrefixPath, "drive_c");

    /// <summary>标准 Wine 布局下的 system32 目录。</summary>
    public string System32 => Path.Combine(DriveC, "windows", "system32");

    /// <summary>把本机绝对路径映射为 Wine 可识别的 Z: 盘路径。</summary>
    public static string ToWinePath(string path) =>
        $"Z:{Path.GetFullPath(path).Replace('/', '\\')}";
}

/// <summary>macOS 侧的缓存与数据目录约定，与 <see cref="MChub.Core"/> 的数据路径解析保持一致。</summary>
internal static class MacBedrockPaths
{
    /// <summary>下载缓存根目录（GamePatch 等）。</summary>
    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Caches", "MChub", "Bedrock");

    public static string DefaultPrefixPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "MChub", "Bedrock", "wine-prefix");
}

/// <summary>
/// macOS 的 Wine 运行时探测。基岩版在 macOS 上没有原生客户端，只能通过 Wine 系运行时
/// （Apple Game Porting Toolkit / CrossOver / Whisky / Homebrew wine）运行 Windows GDK 版。
/// 与 Linux 不同，macOS 侧没有可自动下载的 GDK-Proton 发行包，因此这里只做本机探测。
/// </summary>
public sealed class MacBedrockRuntimeResolver
{
    public const string WinePathVariable = "MCHUB_WINE_PATH";
    public const string PrefixPathVariable = "MCHUB_BEDROCK_PREFIX";

    private static readonly string[] GptkWrappers =
    [
        "gameportingtoolkit-no-hud",
        "gameportingtoolkit",
    ];

    public MacBedrockRuntime Resolve(Action<string>? progress = null)
    {
        EnsureSupportedPlatform();
        progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_macResolvingWineRuntime.CurrentValue());

        // 随应用分发的运行时（CI 打进 MChub.app 的 mcbe-macos）优先：它带 WineGDK 的 XUser 实现与 xodus-service，
        // 是本机安装的 GPTK/CrossOver/Whisky 之外唯一能走 Xbox 登录的组合。
        var bundled = McbeMacOSRuntime.Locate();
        if (bundled is not null && bundled.IsComplete)
        {
            var bundledPrefix = ResolvePrefixPath();
            Directory.CreateDirectory(bundledPrefix);
            return new MacBedrockRuntime(bundled.WineBinary, bundled.WineRoot, bundledPrefix, false, bundled);
        }

        var wineBinary = ResolveWineBinary();
        var prefixPath = ResolvePrefixPath();
        Directory.CreateDirectory(prefixPath);
        var wineRoot = Path.GetDirectoryName(wineBinary) ?? prefixPath;
        return new MacBedrockRuntime(wineBinary, wineRoot, prefixPath,
            Path.GetFileName(wineBinary).StartsWith("gameportingtoolkit", StringComparison.OrdinalIgnoreCase));
    }

    public static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                CommonLanguageManager.Instance.bedrockLaunch_macPlatformOnlyGdk.CurrentValue());
    }

    private static string ResolveWineBinary()
    {
        var configured = Environment.GetEnvironmentVariable(WinePathVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolved = NormalizeWinePath(configured);
            if (File.Exists(resolved)) return resolved;
            throw new FileNotFoundException(
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_macWinePathInvalid.CurrentValue(),
                    WinePathVariable, resolved), resolved);
        }

        foreach (var candidate in EnumerateWineCandidates())
            if (File.Exists(candidate))
                return candidate;

        foreach (var wrapper in GptkWrappers)
        {
            var found = FindInPath(wrapper);
            if (found is not null) return found;
        }

        var wine = FindInPath("wine64") ?? FindInPath("wine");
        if (wine is not null) return wine;

        throw new FileNotFoundException(
            CommonLanguageManager.Instance.bedrockLaunch_macMissingWineRuntime.CurrentValue());
    }

    private static IEnumerable<string> EnumerateWineCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Apple Game Porting Toolkit：包装脚本优先，其次才是裸 wine 可执行文件。
        var gptkRoots = new[]
        {
            "/opt/homebrew/opt/game-porting-toolkit",
            "/usr/local/opt/game-porting-toolkit",
            "/opt/local/libexec/game-porting-toolkit",
            Path.Combine(home, ".local", "share", "game-porting-toolkit")
        };
        foreach (var root in gptkRoots)
        foreach (var wrapper in GptkWrappers)
            yield return Path.Combine(root, "bin", wrapper);
        yield return "/opt/local/bin/gameportingtoolkit";
        foreach (var root in gptkRoots)
            yield return Path.Combine(root, "bin", "wine64");

        // Whisky：图形化封装的 GPTK，自带 Wine 运行时。
        yield return Path.Combine(home, "Library", "Application Support", "com.isaacmarovitz.Whisky",
            "Libraries", "Wine", "bin", "wine64");

        // CrossOver：商业 Wine 发行版，随应用包分发。
        foreach (var app in new[]
                 {
                     "/Applications/CrossOver.app", Path.Combine(home, "Applications", "CrossOver.app")
                 })
            yield return Path.Combine(app, "Contents", "SharedSupport", "CrossOver", "bin", "wine64");

        // Homebrew / 官方 Wine 二进制。
        foreach (var prefix in new[] { "/opt/homebrew", "/usr/local" })
        {
            yield return Path.Combine(prefix, "bin", "wine64");
            yield return Path.Combine(prefix, "bin", "wine");
        }

        yield return "/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine64";
        yield return Path.Combine(home, "Library", "Application Support", "MChub", "wine", "bin", "wine64");
    }

    private static string? FindInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string NormalizeWinePath(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (Directory.Exists(full))
        {
            foreach (var wrapper in GptkWrappers)
            {
                var candidate = Path.Combine(full, wrapper);
                if (File.Exists(candidate)) return candidate;
            }

            var inDirectory = Path.Combine(full, "wine64");
            return File.Exists(inDirectory) ? inDirectory : Path.Combine(full, "wine");
        }

        return full;
    }

    /// <summary>Wine 前缀根目录；与 <see cref="MChub.Core"/> 侧的数据路径解析保持同一套约定。</summary>
    public static string ResolvePrefixPath()
    {
        var configured = Environment.GetEnvironmentVariable(PrefixPathVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        return MacBedrockPaths.DefaultPrefixPath;
    }
}