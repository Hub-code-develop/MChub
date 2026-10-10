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
}
