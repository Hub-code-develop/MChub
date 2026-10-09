using System.Text.RegularExpressions;

using Iridium.Minecraft;
using Iridium.Models.Authentication;
using Iridium.Models.Java;
using Iridium.Models.Launch;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;
using MChub.Localization;

namespace MChub.Mobile.Services;

/// <summary>移动端启动计划：交给 Java/native 侧的最终参数。</summary>
internal sealed record MobileLaunchPlan(
    string MainClass,
    IReadOnlyList<string> JvmArguments,
    IReadOnlyList<string> GameArguments,
    IReadOnlyList<string> Natives,
    string GameDirectory);

/// <summary>
/// 组装移动端的启动参数。
///
/// <p>复用桌面端同一套版本解析（Iridium 的 <see cref="ArgumentParser"/>），所以实例版本、
/// 加载器（Forge/NeoForge/Fabric）的参数差异、类路径拼装等逻辑与桌面端完全一致，
/// 移动端只是把最终结果交给 native 层创建 JVM，而不是 exec 一个 java 进程。</p>
/// </summary>
internal static partial class MobileLaunchPlanBuilder
{
    public static async Task<MobileLaunchPlan> BuildAsync(MinecraftInstance instance, string runtimeRoot,
        CancellationToken cancellationToken = default)
    {
        var context = instance.Context
                      ?? throw new InvalidOperationException("实例未解析成功（版本 JSON 读取失败），无法构建启动参数");
        var entry = context.Entry;

        var account = await ResolveAccountAsync(cancellationToken);
        var java = CreateJavaEntry(runtimeRoot);

        var javaConfig = instance.JavaConfig;
        var maxMemory = javaConfig?.EnableOverrideMaxMemory == true
            ? javaConfig.MinecraftMaxMemory
            : Data.ConfigEntry.MinecraftMaxMemory;

        var config = new LaunchConfig
        {
            Account = account,
            JavaPath = java,
            LauncherName = "MChub",
            IsEnableIndependency = instance.RequiresIndependentInstance ||
                                   javaConfig?.EnableIndependentInstance == true,
            Width = Data.ConfigEntry.MinecraftWindowWidth,
            Height = Data.ConfigEntry.MinecraftWindowHeight,
            MinMemorySize = 512,
            MaxMemorySize = maxMemory > 0 ? maxMemory : 1024
        };

        var arguments = new ArgumentParser().Build(context, config);
        var gameDirectory = entry.InstancePath;

        return new MobileLaunchPlan(
            arguments.MainClass,
            ApplyMobileRuntime(NormalizeForJni(arguments.JvmArguments)),
            arguments.GameArguments.ToArray(),
            arguments.Natives.ToArray(),
            gameDirectory);
    }

    /// <summary>
    /// 把移动端运行组件接进 JVM 参数。
    ///
    /// <p>两处必须改，否则游戏会加载桌面版 LWJGL 然后失败：</p>
    /// <ol>
    ///   <li><b>classpath 前置移动端 LWJGL jar</b>：实例目录里的 <c>org/lwjgl/**</c> 是桌面构建，
    ///       必须让移动端补丁版先命中（classpath 前者优先）；</li>
    ///   <li><b>java.library.path / org.lwjgl.librarypath 指向本地运行组件目录</b>：原生库从
    ///       实例的 natives 目录换成本地目录（liblwjgl / libgl4es / ANGLE 等都在这里）。</li>
    /// </ol>
    ///
    /// <p>缺运行组件时不在这里报错 —— 由 <see cref="MobileGameLauncher.DescribeBlocker"/> 统一提示，
    /// 免得同一件事两处各说一遍。</p>
    /// </summary>
    /// <summary>从 classpath 里抓 LWJGL 版本号（版本 JSON 的 lwjgl 依赖会体现在 jar 路径/文件名）。</summary>
    [GeneratedRegex(@"lwjgl[-_/](\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LwjglVersionRegex();

    /// <summary>
    /// 找出实例实际使用的 LWJGL 版本对应的原生库目录（由预置资产按版本解出）。
    /// 没有匹配版本时返回 null，调用方退回公共 native 目录。
    /// </summary>
    private static string? ResolveLwjglNativesDirectory(IReadOnlyList<string> jvmArguments)
    {
        foreach (var argument in jvmArguments)
        {
            if (!argument.StartsWith("-Djava.class.path=", StringComparison.Ordinal))
                continue;

            var match = LwjglVersionRegex().Match(argument);
            if (!match.Success)
                continue;

            var directory = MobileRuntimePaths.LwjglNativesDirectoryFor(match.Groups[1].Value);
            if (Directory.Exists(directory))
                return directory;
        }

        return null;
    }

    private static IReadOnlyList<string> ApplyMobileRuntime(IReadOnlyList<string> jvmArguments)
    {
        var jarDirectory = MobileRuntimePaths.NativesJarDirectory;

        // library path 顺序 = 优先级：
        //   1) 实例所用 LWJGL 版本对应的原生库（3.3.3 / 3.4.1 同名 .so 各放一处，必须选对）
        //   2) 运行时原生库的**私有目录**（随 APK 以 assets/runtime/natives.zip 预置，首次启动解到这里）
        //   3) APK 的 lib/ 目录（现在只剩我们自己编的 libmchubjvm.so，留作兜底）
        // 为什么不把运行时库放 lib/：平台会做 16 KB 页对齐检查，上游预编译产物是 4 KB 对齐
        // （见 MobileRuntimePaths.BundledNativesArchivePath）。
        var candidateDirectories = new List<string>(3);
        if (ResolveLwjglNativesDirectory(jvmArguments) is { } versionedNatives)
            candidateDirectories.Add(versionedNatives);
        candidateDirectories.Add(MobileRuntimePaths.NativesLibraryDirectory);
        candidateDirectories.Add(MobileRuntimePaths.NativeLibraryDirectory);

        var libraryDirectories = candidateDirectories
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var libraryDirectory = string.Join(Path.PathSeparator, libraryDirectories);

        var jarFiles = Directory.Exists(jarDirectory)
            ? Directory.GetFiles(jarDirectory, "*.jar").OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : [];
        var hasLibraries = libraryDirectories.Length > 0;

        var result = new List<string>(jvmArguments.Count + 4);
        var classPathApplied = false;
        var libraryPathApplied = false;
        var lwjglPathApplied = false;

        foreach (var argument in jvmArguments)
        {
            if (jarFiles.Length > 0 && argument.StartsWith("-Djava.class.path=", StringComparison.Ordinal))
            {
                result.Add("-Djava.class.path=" + string.Join(Path.PathSeparator, jarFiles) +
                           Path.PathSeparator + argument["-Djava.class.path=".Length..]);
                classPathApplied = true;
                continue;
            }

            if (hasLibraries && argument.StartsWith("-Djava.library.path=", StringComparison.Ordinal))
            {
                // 桌面端那份 natives 目录仍保留（老版本会用到其中的 java 侧原生库），只在其后追加。
                result.Add(argument + Path.PathSeparator + libraryDirectory);
                libraryPathApplied = true;
                continue;
            }

            if (hasLibraries && argument.StartsWith("-Dorg.lwjgl.librarypath=", StringComparison.Ordinal))
                lwjglPathApplied = true;

            result.Add(argument);
        }

        if (hasLibraries && !libraryPathApplied)
            result.Add("-Djava.library.path=" + libraryDirectory);

        // LWJGL 自己也按这个属性找原生库，显式给上更稳。
        if (hasLibraries && !lwjglPathApplied)
            result.Add("-Dorg.lwjgl.librarypath=" + libraryDirectory);

        // 实例里没有 LWJGL 时（极少数），至少把移动端 jar 加上，别把 classpath 丢了。
        if (jarFiles.Length > 0 && !classPathApplied)
            result.Add("-Djava.class.path=" + string.Join(Path.PathSeparator, jarFiles));

        // 补齐在 Android 上运行 MC 必需的一批 JVM 属性（实例/加载器已给同名属性则不覆盖）。
        // 这是 Pojav 系启动器的既有做法，逐条都有具体原因，缺了会出各种怪问题。
        foreach (var (key, value) in RequiredJvmProperties())
        {
            var flag = "-D" + key + "=";
            if (!result.Any(argument => argument.StartsWith(flag, StringComparison.Ordinal)))
                result.Add(flag + value);
        }

        return result;
    }

    /// <summary>
    /// Android 上运行 MC 需要的 JVM 属性。
    /// </summary>
    private static IEnumerable<(string Key, string Value)> RequiredJvmProperties()
    {
        // 运行时原生库目录：Pojav 系 / OpenAL / GL 翻译层都在这里（jna 等要按目录找）。
        // 还没解压时退回 APK 的 lib/ 目录，至少不给出一个不存在的路径。
        var runtimeNativesDirectory = Directory.Exists(MobileRuntimePaths.NativesLibraryDirectory)
            ? MobileRuntimePaths.NativesLibraryDirectory
            : MobileRuntimePaths.NativeLibraryDirectory;
        var cacheDirectory = Android.App.Application.Context.CacheDir?.AbsolutePath ?? Path.GetTempPath();

        // MC 与各 loader 据此选平台分支（LWJGL natives 名、路径分隔符等）。
        yield return ("os.name", "Linux");
        yield return ("os.version", "Android-" + Android.OS.Build.VERSION.Release);
        // 默认的 POSIX_SPAWN 依赖 jspawnhelper，Android 上跑不起来。
        yield return ("jdk.lang.Process.launchMechanism", "FORK");
        // 指向应用私有缓存目录，保证可写。
        yield return ("java.io.tmpdir", cacheDirectory);
        // JNA 依赖它找原生库（部分 mod 会用）。
        yield return ("jna.boot.library.path", runtimeNativesDirectory);
        yield return ("org.lwjgl.vulkan.libname", "libvulkan.so");
        yield return ("glfwstub.initEgl", "false");
        // Log4j2 远程加载缓解。
        yield return ("log4j2.formatMsgNoLookups", "true");
        // Forge 1.14+ 的早期进度弹窗在移动端会出问题。
        yield return ("fml.earlyprogresswindow", "false");
        yield return ("loader.disable_forked_guis", "true");
        yield return ("net.minecraft.clientmodname", "MChub");
    }

    /// <summary>
    /// 把启动器风格的 JVM 参数改成 JNI 能吃的形式。
    ///
    /// <p><b>关键差异</b>：<c>-cp</c> / <c>-classpath</c> 是 java 启动器（launcher）的选项，
    /// 不是 JVM 选项 —— <c>JNI_CreateJavaVM</c> 只接受 <c>JavaVMOption</c>（<c>-D</c> / <c>-X</c> 等）。
    /// 直接把 <c>-cp</c> 塞进去会因为"无法识别的选项"导致创建 JVM 失败，必须转成
    /// <c>-Djava.class.path=</c>。</p>
    /// </summary>
    private static IReadOnlyList<string> NormalizeForJni(IReadOnlyList<string> jvmArguments)
    {
        var result = new List<string>(jvmArguments.Count);

        for (var index = 0; index < jvmArguments.Count; index++)
        {
            var argument = jvmArguments[index];
            if ((argument is "-cp" or "-classpath") && index + 1 < jvmArguments.Count)
            {
                result.Add("-Djava.class.path=" + jvmArguments[index + 1]);
                index++;
                continue;
            }

            if (argument.StartsWith("-cp=", StringComparison.Ordinal))
            {
                result.Add("-Djava.class.path=" + argument["-cp=".Length..]);
                continue;
            }

            result.Add(argument);
        }

        return result;
    }

    /// <summary>
    /// 构造 Iridium 的 JavaEntry。
    ///
    /// <p>移动端不执行 <c>java -version</c> 探测，版本号直接读运行时的 <c>release</c> 文件，
    /// 因此这里不启任何进程。</p>
    /// </summary>
    private static JavaEntry CreateJavaEntry(string runtimeRoot)
    {
        MobileJavaRuntime.TryReadJavaVersion(runtimeRoot, out var version, out var majorVersion);

        return new JavaEntry
        {
            JavaPath = MobileJavaRuntime.ResolveJavaExecutable(runtimeRoot),
            JavaHome = runtimeRoot,
            Vendor = "Android OpenJDK",
            Version = version,
            MajorVersion = majorVersion,
            Is64Bit = true
        };
    }

    /// <summary>
    /// 取当前账户并转成 Iridium 的账户模型。
    ///
    /// <p>微软账户与桌面端一样先刷新令牌（过期令牌会让游戏在联机认证阶段失败），
    /// 刷新结果写回配置，避免每次启动都重复刷新。</p>
    /// </summary>
    private static async Task<Account> ResolveAccountAsync(CancellationToken cancellationToken)
    {
        var account = Data.ConfigEntry.UsingMinecraftMinecraftAccount
                      ?? throw new InvalidOperationException(
                          CommonLanguageManager.Instance.launch_selectAccountFirst.CurrentValue());

        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException(
                CommonLanguageManager.Instance.launch_accountNoPlayerName.CurrentValue());

        switch (account.AccountType)
        {
            case AccountType.Offline:
                return new OfflineAccount(account.Name,
                    account.Uuid ?? MinecraftAccount.GetMinecraftOfflineUuid(account.Name),
                    account.AccessToken ?? Guid.NewGuid().ToString("N"));

            case AccountType.Yggdrasil:
                if (!account.Uuid.HasValue || string.IsNullOrWhiteSpace(account.AccessToken) ||
                    string.IsNullOrWhiteSpace(account.ClientToken) ||
                    string.IsNullOrWhiteSpace(account.YggdrasilServerUrl))
                    throw new InvalidOperationException(
                        CommonLanguageManager.Instance.launch_yggdrasilIncomplete.CurrentValue());

                return new YggdrasilAccount(account.Name, account.Uuid.Value, account.AccessToken,
                    account.YggdrasilServerUrl, account.ClientToken) { MetaData = account.MetaData };

            case AccountType.Microsoft:
                var refreshed = await AccountRefresher.RefreshMicrosoft(account)
                                ?? throw new InvalidOperationException(
                                    CommonLanguageManager.Instance.launch_microsoftRefreshFailed.CurrentValue());

                cancellationToken.ThrowIfCancellationRequested();
                PersistRefreshedAccount(account, refreshed);

                if (!refreshed.Uuid.HasValue || string.IsNullOrWhiteSpace(refreshed.AccessToken) ||
                    string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    throw new InvalidOperationException(
                        CommonLanguageManager.Instance.launch_microsoftMissingInfo.CurrentValue());

                return new MicrosoftAccount(refreshed.Name, refreshed.Uuid.Value, refreshed.AccessToken,
                    refreshed.RefreshToken, refreshed.LastRefreshTime ?? DateTime.Now);

            default:
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.launch_unsupportedAccountType.CurrentValue());
        }
    }

    private static void PersistRefreshedAccount(MinecraftAccount original, MinecraftAccount refreshed)
    {
        var accounts = Data.ConfigEntry.MinecraftAccounts;
        var index = accounts.IndexOf(original);
        if (index >= 0)
            accounts[index] = refreshed;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
    }
}
