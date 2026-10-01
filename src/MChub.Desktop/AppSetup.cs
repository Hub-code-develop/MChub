using MChub.Bedrock;
using MChub.Bedrock.Standard.Interface;
using MChub.Core.Minecraft;
using MChub.Core.Services;

namespace MChub.Desktop;

internal static class AppSetup
{
#if WINDOWS || LINUX || MACOS
    public static void RegisterBedrockLauncher()
    {
#if WINDOWS
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config =>
            {
                config.LauncherVersion = AppVersionService.Instance.Version.VersionTitle;
                return new BedrockLaunch(config);
            };
        BedrockInstallationService.DefaultInstaller =
            new BedrockInstaller();
        BedrockToolsService.Default =
            new BedrockWindowsToolsService();
#elif LINUX
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config =>
            {
                config.LauncherVersion = AppVersionService.Instance.Version.VersionTitle;
                return new Bedrock.Linux.BedrockLaunch(config);
            };
        Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Bedrock.Linux.BedrockInstaller();
#elif MACOS
        // 基岩版没有 macOS 原生客户端，这里注册的是通过 Wine 系运行时承载的 Windows GDK 版。
        MinecraftLaunchService.DefaultBedrockLauncherFactory =
            config =>
            {
                config.LauncherVersion = AppVersionService.Instance.Version.VersionTitle;
                return new Bedrock.MacOS.BedrockLaunch(config);
            };
        Bedrock.Standard.Interface.BedrockInstallationService.DefaultInstaller =
            new Bedrock.MacOS.BedrockInstaller();
#endif
    }
#endif
}
