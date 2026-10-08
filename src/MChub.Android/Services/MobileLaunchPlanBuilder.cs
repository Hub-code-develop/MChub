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
internal static class MobileLaunchPlanBuilder
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
            NormalizeForJni(arguments.JvmArguments),
            arguments.GameArguments.ToArray(),
            arguments.Natives.ToArray(),
            gameDirectory);
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
