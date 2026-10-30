using MChub.Bedrock.Standard.Interface;
using MChub.Core.Classes;
using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Module.Initialize;
using MChub.Core.Services;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Module.Initialize;

public static partial class Initializer
{
    public static void BedrockPackageImport()
    {
        Logger.Info(LogLanguageManager.Instance.app_bedrockImportInitStart.CurrentValue());
        Config.Initialize();
        Logger.Info(LogLanguageManager.Instance.app_bedrockImportInitComplete.CurrentValue());
    }

    public static void App()
    {
        Logger.Info(LogLanguageManager.Instance.app_servicesInitStart.CurrentValue());
        Config.Initialize();
        BedrockNetworkConfiguration.Configure(Data.ConfigEntry.DisableSystemProxy,
            Data.ConfigEntry.EnableProxyServer ? Data.ConfigEntry.ProxyServer : null, Data.Instance.UserAgent,
            Data.ConfigEntry.EnableGithubMirror, Data.ConfigEntry.GithubMirrorUrl,
            Data.ConfigEntry.GithubMirrorMode == GithubMirrorMode.Direct,
            Data.ConfigEntry.EnableFragmentDownload, Data.ConfigEntry.DownloadMaxFragmentCount,
            Data.ConfigEntry.DownloadMaxRetryCount);
        MinecraftCoreInitializer.Initialize(new MinecraftCoreInitializeOptions
        {
            AppVersion = AppVersionService.Instance.Version.VersionTitle,
            EnableCustomUserAgent = Data.ConfigEntry.EnableCustomUserAgent,
            CustomUserAgent = Data.ConfigEntry.CustomUserAgent,
            DisableSystemProxy = Data.ConfigEntry.DisableSystemProxy,
            ProxyServer = Data.ConfigEntry.EnableProxyServer ? Data.ConfigEntry.ProxyServer : null,
            MaxThread = Data.ConfigEntry.DownloadMaxThreadCount,
            MaxFragment = Data.ConfigEntry.DownloadMaxFragmentCount,
            MaxRetryCount = Data.ConfigEntry.DownloadMaxRetryCount,
            MinecraftMetadataSource = Data.ConfigEntry.MinecraftMetadataSource,
            MinecraftFileSource = Data.ConfigEntry.MinecraftFileSource,
            IsEnableFragment = Data.ConfigEntry.EnableFragmentDownload
        });
        Logger.Info(LogLanguageManager.Instance.app_servicesInitComplete.CurrentValue());
    }
}