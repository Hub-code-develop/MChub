namespace MChub.Mobile.Services;

/// <summary>
/// 设备内的「java 命令」—— Forge / NeoForge / OptiFine 的安装处理器要靠它跑。
///
/// <p>那批安装器（MinecraftLaunch 的 <c>ForgeInstaller</c> / <c>OptifineInstaller</c>）是用
/// <c>Process.Start(javaPath, ["-cp", cp, mainClass, ...])</c> 调一个**外部 java 可执行文件**的，
/// 所以移动端必须自带一个：<c>Native/mchubjava.cpp</c> 编成可执行文件，
/// 以 <c>libmchubjava.so</c> 的名字随 APK 打进 <c>lib/arm64-v8a/</c>
/// （Android 上只有这个目录解出来的文件既允许 execve、又受系统保护）。</p>
///
/// <p>它靠 <c>MCHUB_JAVA_HOME</c> 环境变量定位运行时；环境变量在 <see cref="EnsureAsync"/> 里
/// 设好，子进程会继承。</p>
/// </summary>
internal static class MobileInstallerJava
{
    private const string JavaHomeVariable = "MCHUB_JAVA_HOME";

    /// <summary>执行文件在设备上的绝对路径。</summary>
    public static string ExecutablePath =>
        Path.Combine(MobileRuntimePaths.NativeLibraryDirectory, "libmchubjava.so");

    /// <summary>本次安装包是否带了它（本地没编 native 的包会缺）。</summary>
    public static bool IsAvailable => File.Exists(ExecutablePath);

    /// <summary>
    /// 准备设备内的 java 命令：确保实例所需主版本的运行时已落盘，并把它写进
    /// <c>MCHUB_JAVA_HOME</c> 供子进程继承。返回可交给安装器的可执行文件路径。
    /// </summary>
    public static async Task<string> EnsureAsync(int requiredMajorVersion,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "安装包里没有内置的 java 命令（libmchubjava.so 缺失），无法在设备上安装该加载器。" +
                "请使用官方发行包，或改选不需要安装处理器的原版 / Fabric。");
        }

        if (!MobileRuntimeInstaller.IsRuntimeInstalled(requiredMajorVersion))
        {
            if (MobileRuntimeInstaller.HasBundledRuntime(requiredMajorVersion))
                await MobileRuntimeInstaller.InstallBundledRuntimeAsync(requiredMajorVersion, progress,
                    cancellationToken);
            else
                await MobileRuntimeInstaller.InstallRuntimeAsync(requiredMajorVersion, progress, cancellationToken);
        }

        var runtimeRoot = MobileJavaRuntime.FindRuntimeRoot(requiredMajorVersion)
                          ?? throw new InvalidOperationException($"Java {requiredMajorVersion} 运行时仍未就绪");

        Environment.SetEnvironmentVariable(JavaHomeVariable, runtimeRoot);
        return ExecutablePath;
    }
}
