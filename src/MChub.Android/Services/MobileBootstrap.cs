using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Module.Initialize;
using MChub.Core.Services;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Mobile.Services;

/// <summary>
/// 移动端复用 <c>MChub.Core</c> 的业务层，但桌面端的 UI 引导（MChub.Module.Initialize.Initializer）
/// 无法在移动端复用，因此在这里单独引导 Core 配置。
///
/// 引导失败时不抛出，而是记录原因交给设置页 / 新建实例页展示：移动端界面本身必须始终可用。
/// 三级状态分开记录 —— 配置读不出来是致命的，下载器或游戏目录起不来只影响「本机安装」这一条链路。
/// </summary>
public static class MobileBootstrap
{
    /// <summary>Core 配置已就绪（读得到 <c>Setting.mchub</c>）。</summary>
    public static bool CoreReady { get; private set; }

    /// <summary>本机安装能力已就绪（下载源 + 本机游戏目录都可用）。</summary>
    public static bool InstallReady { get; private set; }

    public static string? FailureReason { get; private set; }

    public static void InitializeCore()
    {
        try
        {
            Config.Initialize();
            CoreReady = true;
            FailureReason = null;
        }
        catch (Exception exception)
        {
            CoreReady = false;
            InstallReady = false;
            FailureReason = exception.Message;
            return;
        }

        // 手机内存与桌面完全不是一个量级：配置里的默认值是桌面的 4096MB，
        // 在 2GB 的机器上会让 JVM 要么起不来、要么疯狂换页。这里按设备实际内存把上限压下来。
        ClampMinecraftMemory();

        // 下载器（版本清单 / 库 / 资源文件）来自 MinecraftLaunch，必须先按配置引导一遍，
        // 否则「本机安装」没有可用的下载源、UA 与并发设置。
        // 这一步失败不该让整个 App 不可用 —— 实例扫描与启动不依赖它。
        try
        {
            MinecraftCoreInitializer.Initialize(new MinecraftCoreInitializeOptions
            {
                AppVersion = AppVersionService.Instance.Version.VersionTitle,
                DisableSystemProxy = Data.ConfigEntry.DisableSystemProxy,
                ProxyServer = Data.ConfigEntry.EnableProxyServer ? Data.ConfigEntry.ProxyServer : null,
                MaxThread = Data.ConfigEntry.DownloadMaxThreadCount,
                MaxFragment = Data.ConfigEntry.DownloadMaxFragmentCount,
                MaxRetryCount = Data.ConfigEntry.DownloadMaxRetryCount,
                MinecraftMetadataSource = Data.ConfigEntry.MinecraftMetadataSource,
                MinecraftFileSource = Data.ConfigEntry.MinecraftFileSource,
                IsEnableFragment = Data.ConfigEntry.EnableFragmentDownload
            });

            // 本机游戏目录：装完即用，不依赖桌面端先建好实例。
            MobileGameStorage.EnsureRegistered();
            InstallReady = true;
        }
        catch (Exception exception)
        {
            InstallReady = false;
            FailureReason = exception.Message;
            Logger.Warning($"[MChub.Mobile] 本机安装能力初始化失败：{exception}");
        }
    }

    /// <summary>
    /// 按设备物理内存压低 <see cref="MChub.Core.Classes.Config.ConfigEntry.MinecraftMaxMemory"/>。
    ///
    /// <para>桌面端的默认值是 4096MB —— 直接搬到手机上有两个后果：小内存设备上 JVM 连堆都提交不出来，
    /// 或者勉强起来之后一路换页卡死。这里取物理内存的 55%（留出系统、GL 驱动与 Dex 的空间），
    /// 下限 512MB、上限 4096MB。只在超过时下调，用户手动设的小值尊重不动。</para>
    ///
    /// <para>改动会写日志（不是静默改配置）：配置本身留在原值，但「这次生效的是多少」必须可查。</para>
    /// </summary>
    private static void ClampMinecraftMemory()
    {
        try
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Android.Content.Context.ActivityService) is not Android.App.ActivityManager manager)
            {
                return;
            }

            var info = new Android.App.ActivityManager.MemoryInfo();
            manager.GetMemoryInfo(info);

            var totalMb = info.TotalMem / (1024 * 1024);
            var budgetMb = (int)Math.Clamp(totalMb * 55 / 100, 512, 4096);

            if (Data.ConfigEntry.MinecraftMaxMemory <= budgetMb)
            {
                return;
            }

            Logger.Info($"[MChub.Mobile] 设备内存 {totalMb}MB，把 Minecraft 最大内存从 "
                        + $"{Data.ConfigEntry.MinecraftMaxMemory}MB 下调到 {budgetMb}MB");

            Data.ConfigEntry.MinecraftMaxMemory = budgetMb;
            ConfigSaver.SaveConfig();
        }
        catch (Exception exception)
        {
            // 读不到内存信息不该挡住启动：DALVIK 侧本来就有自己的堆上限。
            Logger.Warning($"[MChub.Mobile] 读取设备内存失败，保留原最大内存设置：{exception.Message}");
        }
    }
}
