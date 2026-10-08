using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;

namespace MChub.Mobile.Services;

/// <summary>一次启动尝试的结果。</summary>
/// <param name="Launched">Java/native 调用是否成功走完（不等于游戏玩得起来）。</param>
/// <param name="ExitCode">游戏退出码；调用失败为 -1。</param>
/// <param name="Message">失败原因或游戏侧错误（已脱敏，可安全展示）。</param>
internal sealed record MobileLaunchResult(bool Launched, int ExitCode, string? Message);

/// <summary>
/// 移动端启动编排：准备运行时 → 组装参数 → 交给 Java/native 侧创建 JVM 跑游戏。
///
/// <p>启动日志会写到应用私有目录 <c>Logs/mobile-launch.log</c>（含完整命令行，
/// 访问令牌已脱敏），出问题时可以直接查看，不用靠猜。</p>
/// </summary>
internal static class MobileGameLauncher
{
    /// <summary>游戏是否正在运行。</summary>
    public static bool IsRunning => JavaRuntimeBridge.IsGameRunning();

    /// <summary>请求停止游戏。</summary>
    public static void Abort() => JavaRuntimeBridge.Abort();

    /// <summary>
    /// 启动前把能提前发现的问题一次性说清楚；返回 null 表示可以启动。
    /// 传入实例时会按该实例要求的 Java 版本判断运行时是否够用。
    /// </summary>
    public static string? DescribeBlocker(MinecraftInstance? instance = null)
    {
        if (!JavaRuntimeBridge.IsAvailable)
            return JavaRuntimeBridge.UnavailableReason ?? "Java 后端不可用";

        if (!JavaRuntimeBridge.IsNativeLayerLoaded())
            return "原生启动层未随本安装包提供（libmchubjvm.so 缺失）";

        var requiredMajor = instance is null ? 0 : GetRequiredJavaMajorVersion(instance);
        if (instance is null)
        {
            if (MobileJavaRuntime.FindAnyRuntimeRoot() is null)
                return "尚未安装 Java 运行时";
        }
        else if (MobileJavaRuntime.FindRuntimeRoot(requiredMajor) is null)
        {
            return $"尚未安装 Java {requiredMajor} 运行时";
        }

        if (!MobileRuntimeInstaller.AreNativesInstalled)
            return "运行组件不完整（native 库或移动端 LWJGL 缺失），请用官方发行包或联网补齐";

        return null;
    }

    /// <summary>是否缺运行环境（缺了可以一键安装，与"原生层缺失"这类不可自愈的阻塞区分开）。</summary>
    public static bool NeedsInstall(MinecraftInstance? instance = null)
    {
        if (!JavaRuntimeBridge.IsAvailable || !JavaRuntimeBridge.IsNativeLayerLoaded())
            return false;

        var runtimeMissing = instance is null
            ? MobileJavaRuntime.FindAnyRuntimeRoot() is null
            : MobileJavaRuntime.FindRuntimeRoot(GetRequiredJavaMajorVersion(instance)) is null;

        return runtimeMissing || !MobileRuntimeInstaller.AreNativesInstalled;
    }

    /// <summary>
    /// 补齐缺失的运行环境：JRE（按实例要求的 Java 版本自动选 8/17/21/25）+ 运行组件。
    /// 已就绪的部分会跳过，可重复调用。
    ///
    /// <p><b>优先走 APK 内预置</b>：发行包里已带 JRE 归档与移动端 LWJGL jar，直接从 assets
    /// 解出即可，用户无需联网。只有在预置缺失时（本地自行构建的包）才回退到联网下载。</p>
    /// </summary>
    public static async Task EnsureReadyAsync(int requiredJavaMajorVersion,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!MobileRuntimeInstaller.IsRuntimeInstalled(requiredJavaMajorVersion))
        {
            if (MobileRuntimeInstaller.HasBundledRuntime(requiredJavaMajorVersion))
                await MobileRuntimeInstaller.InstallBundledRuntimeAsync(
                    requiredJavaMajorVersion, progress, cancellationToken);
            else
                await MobileRuntimeInstaller.InstallRuntimeAsync(
                    requiredJavaMajorVersion, progress, cancellationToken);
        }

        // native 库在预置方案下由系统解压到 nativeLibraryDir，无需动作；
        // 需要落盘的是 LWJGL jar（classpath 用的是文件路径）。
        if (!MobileRuntimeInstaller.AreLwjglJarsInstalled)
        {
            if (MobileRuntimeInstaller.HasBundledJars)
                await MobileRuntimeInstaller.InstallBundledJarsAsync(
                    MobileRuntimeCatalog.LwjglVersion, progress, cancellationToken);
            else if (!MobileRuntimeInstaller.AreNativeLibrariesInstalled)
                await MobileRuntimeInstaller.InstallNativesAsync(progress, cancellationToken);
        }
    }

    /// <summary>实例要求的 Java 主版本（MC 26.x → 25，1.21 → 21，1.18~1.20.4 → 17，≤1.16 → 8）。</summary>
    public static int GetRequiredJavaMajorVersion(MinecraftInstance instance)
        => MinecraftInstallationTasks.GetRecommendedJavaVersion(instance.VersionId);

    public static async Task<MobileLaunchResult> LaunchAsync(MinecraftInstance instance,
        CancellationToken cancellationToken = default)
    {
        var blocker = DescribeBlocker(instance);
        if (blocker is not null)
            throw new InvalidOperationException(blocker);

        var runtimeRoot = MobileJavaRuntime.FindRuntimeRoot(GetRequiredJavaMajorVersion(instance))
                          ?? throw new InvalidOperationException("没有满足该实例要求的 Java 运行时");
        var plan = await MobileLaunchPlanBuilder.BuildAsync(instance, runtimeRoot, cancellationToken);

        await AppendLaunchLogAsync(runtimeRoot, plan);

        // 创建 JVM 并 CallStaticVoidMethod(main) 是同步阻塞调用，
        // 会一直占到游戏退出，因此必须放到线程池，不能占住 UI 线程。
        return await Task.Run(() =>
        {
            var invoked = JavaRuntimeBridge.TryLaunch(runtimeRoot, plan.MainClass,
                plan.JvmArguments.ToArray(), plan.GameArguments.ToArray(), plan.GameDirectory,
                out var exitCode, out var failure);

            if (!invoked)
                return new MobileLaunchResult(false, -1, failure ?? "调用启动接口失败");

            // 调用成功但游戏可能自己崩了：此时 Java 侧会带回具体原因（原生层回传的异常文本）。
            var gameError = JavaRuntimeBridge.TakeLastError();
            return new MobileLaunchResult(true, exitCode, gameError);
        }, cancellationToken);
    }

    /// <summary>把这次启动的命令行落到日志文件（令牌脱敏）。</summary>
    private static async Task AppendLaunchLogAsync(string runtimeRoot, MobileLaunchPlan plan)
    {
        try
        {
            var logDirectory = Path.Combine(MobileRuntimePaths.AppDataDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "mobile-launch.log");

            var lines = new List<string>
            {
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动 {plan.GameDirectory} =====",
                $"运行时      : {runtimeRoot}",
                $"主类        : {plan.MainClass}",
                $"工作目录    : {plan.GameDirectory}",
                $"natives     : {string.Join(Path.PathSeparator, plan.Natives)}",
                "JVM 参数:"
            };
            lines.AddRange(plan.JvmArguments.Select(argument => "  " + argument));
            lines.Add("游戏参数:");
            lines.AddRange(Redact(plan.GameArguments).Select(argument => "  " + argument));
            lines.Add(string.Empty);

            await File.AppendAllLinesAsync(logPath, lines);
        }
        catch (Exception)
        {
            // 写不到日志不该阻断启动本身。
        }
    }

    /// <summary>把 --accessToken 之类的凭据替换掉，避免日志里留明文令牌。</summary>
    private static IEnumerable<string> Redact(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            yield return argument;

            if (argument is "--accessToken" or "--clientId" or "--xuid" && index + 1 < arguments.Count)
            {
                yield return "<redacted>";
                index++;
            }
        }
    }
}
