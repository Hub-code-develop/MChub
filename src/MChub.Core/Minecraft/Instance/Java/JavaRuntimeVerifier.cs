using System.Collections.Concurrent;
using System.Diagnostics;
using Iridium.Launch;

namespace MChub.Core.Minecraft.Instance.Java;

public static class JavaRuntimeVerifier
{
    private static readonly ConcurrentDictionary<string, bool> ModuleCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<bool> IsUsableAsync(string javaPath, int majorVersion,
        CancellationToken cancellationToken = default)
        => await IsUsableAsync(javaPath, majorVersion, null, cancellationToken);

    /// <param name="javaVersion">
    /// 该运行时的完整版本串（如 <c>25.0.1</c> / <c>25-loom</c> / <c>25-ea</c>）。传入后可直接识别
    /// 预发布/早期预览（EA）构建；为 null 时跳过该判断，仅做模块探测。
    /// </param>
    public static async Task<bool> IsUsableAsync(string javaPath, int majorVersion, string? javaVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            return false;
        if (majorVersion < 9)
            return true;

        // 预发布/早期预览（EA）构建（例如 25-loom / 25-ea / 25-preview）常缺少正式版才有的 API。
        // 实际案例：Project Loom EA 构建 25-loom+1-11 缺少 Math.powExact，导致 MC 26.3 启动即
        // java.lang.NoSuchMethodError 崩溃。这类运行时直接判为不可用，交由上层换用正式版或自动安装。
        if (IsPreReleaseBuild(javaVersion))
            return false;

        var key = Path.GetFullPath(javaPath);
        if (ModuleCache.TryGetValue(key, out var cached))
            return cached;

        var usable = await ProbeModulesAsync(javaPath, cancellationToken);
        ModuleCache[key] = usable;
        return usable;
    }

    /// <summary>
    /// 依据版本串判断是否为「非正式版」运行时：早期预览（EA）、Project Loom、内部/预览快照等。
    /// 这些构建可能缺少正式版 API，不应作为游戏运行时使用。
    /// </summary>
    public static bool IsPreReleaseBuild(string? javaVersion)
    {
        if (string.IsNullOrWhiteSpace(javaVersion))
            return false;

        var version = javaVersion.ToLowerInvariant();
        return version.Contains("-ea")
               || version.Contains("-loom")
               || version.Contains("-preview")
               || version.Contains("-internal")
               || version.Contains("-earlyaccess")
               || version.Contains("-prerelease")
               || version.Contains("-snapshot")
               || version.Contains("-dev")
               || version.Contains("-beta")
               || version.Contains("-alpha");
    }

    private static async Task<bool> ProbeModulesAsync(string javaPath, CancellationToken cancellationToken)
    {
        var executable = javaPath;
        if (OperatingSystem.IsWindows())
        {
            var windowless = Path.GetFileName(javaPath).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase);
            var console = Path.Combine(Path.GetDirectoryName(javaPath) ?? string.Empty, "java.exe");
            if (windowless && File.Exists(console))
                executable = console;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            var startInfo = new ProcessStartInfo(executable, "--list-modules")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // macOS SIP/AMFI-disabled workaround: preload the JIT SIGBUS fix so
            // `java --list-modules` does not crash in CodeHeap::allocate.
            MacOSJitFix.Apply(startInfo);
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                return false;

            return output.Contains("jdk.zipfs", StringComparison.OrdinalIgnoreCase)
                   && output.Contains("jdk.unsupported", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}