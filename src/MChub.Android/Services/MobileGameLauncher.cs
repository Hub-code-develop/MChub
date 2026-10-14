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

        // 注意：native 层是**启动时**才从私有目录装载的（见 JREUtils.loadNativeLayer），
        // 所以这里不能查「本次会话装载过没有」，只能查运行组件是否齐备。
        if (!MobileRuntimeInstaller.AreNativesInstalled)
            return "运行组件不完整（运行时原生库或移动端 LWJGL 缺失），请用官方发行包或联网补齐";

        return null;
    }

    /// <summary>是否缺运行环境（缺了可以一键安装，与"原生层缺失"这类不可自愈的阻塞区分开）。</summary>
    public static bool NeedsInstall(MinecraftInstance? instance = null)
    {
        if (!JavaRuntimeBridge.IsAvailable)
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

        // 原生库随 APK 以 zip 预置（assets/runtime/natives.zip），要落到私有目录才能按绝对路径
        // System.load / dlopen（见 MobileRuntimePaths.BundledNativesArchivePath 的说明）。
        if (!MobileRuntimeInstaller.AreNativeLibrariesInstalled)
        {
            if (MobileRuntimeInstaller.HasBundledNatives)
                await MobileRuntimeInstaller.InstallBundledNativesAsync(progress, cancellationToken);
            else
                await MobileRuntimeInstaller.InstallNativesAsync(progress, cancellationToken);
        }

        // LWJGL jar 同理要落盘：classpath 用的是真实文件路径。
        if (!MobileRuntimeInstaller.AreLwjglJarsInstalled && MobileRuntimeInstaller.HasBundledJars)
        {
            await MobileRuntimeInstaller.InstallBundledJarsAsync(
                MobileRuntimeCatalog.LwjglVersion, progress, cancellationToken);
        }

        // LWJGL 原生库按版本各解一份（3.3.3 / 3.4.1 有同名 .so，不能放同一目录），
        // 启动时由 MobileLaunchPlanBuilder 按实例实际使用的版本挑目录。
        if (MobileRuntimeInstaller.BundledLwjglNativesVersions.Count > 0)
            await MobileRuntimeInstaller.InstallBundledLwjglNativesAsync(progress, cancellationToken);
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

        // 游戏要有块 Android Surface 才能画（glfwCreateWindow → native 建 EGLSurface）。
        // 没有它，JVM 起来了也是一片全黑 —— 所以拿不到就如实失败，不硬着头皮往下走。
        var surface = await MobileGameSurfaceHost.AcquireAsync(TimeSpan.FromSeconds(10));
        if (surface is null)
            return new MobileLaunchResult(false, -1, "拿不到可用的渲染 Surface（界面未就绪）");

        try
        {
            // 游戏渲染层之上挂上触屏操作层（触摸板 + 屏幕按键 + 软键盘）。
            // 必须在 Surface 之后挂，保证操作层在更上层、优先收到触摸事件；
            // MC 抓取鼠标后由 MobileTouchpadView 把手势翻成相对鼠标移动，未抓取时则绝对定位。
            await MobileGameControls.AttachAsync();

            // 创建 JVM 并跑主类是同步阻塞调用（JLI_Launch 会一直占到游戏退出），
            // 因此必须放到线程池，不能占住 UI 线程。
            return await Task.Run(() =>
            {
                var invoked = JavaRuntimeBridge.TryLaunch(runtimeRoot, plan.MainClass,
                    plan.JvmArguments.ToArray(), plan.GameArguments.ToArray(), plan.GameDirectory,
                    // 运行时原生库目录（私有目录），不是 ApplicationInfo.nativeLibraryDir ——
                    // Java/native 侧靠它找 libpojavexec、GL 翻译层与 libopenal。
                    MobileRuntimePaths.NativesLibraryDirectory, surface,
                    out var exitCode, out var failure);

                if (!invoked)
                    return new MobileLaunchResult(false, -1, failure ?? "调用启动接口失败");

                // 调用成功但游戏可能自己崩了：此时 Java 侧会带回具体原因。
                var gameError = JavaRuntimeBridge.TakeLastError();
                return new MobileLaunchResult(true, exitCode, gameError);
            }, cancellationToken);
        }
        finally
        {
            // 操作层先摘（触摸板/按键/软键盘），再把渲染 Surface 交还启动器界面，
            // 否则退出后屏幕还被视图占着、点不到启动器。
            await MobileGameControls.DetachAsync();
            await MobileGameSurfaceHost.ReleaseAsync();
        }
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
