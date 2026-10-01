using System.Runtime.InteropServices;
using MChub.Core.Module.Ipc;
using MChub.Core.Services;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Desktop;

internal static class PrimaryInstanceStartup
{
    public static bool Run(string[] args)
    {
        MChubCommandQueue.Initialize();
        PackagePathResolver.TryGetBedrockPackagePath(args, out var packagePath);
        if (packagePath != null)
            App.BedrockPackagePath = packagePath;

        if (PackagePathResolver.TryGetJavaPackagePath(args, out var javaPackagePath))
        {
            var javaCommand = new MChubCommand
            {
                Kind = MChubCommandKind.DownloadModpack,
                Source = javaPackagePath
            };

            if (packagePath == null && MChubCommandService.TryForwardToRunningInstance(javaCommand))
            {
                Logger.Info(string.Format(LogLanguageManager.Instance.desktop_primaryInstance_javaForwarded.CurrentValue(), javaPackagePath));
                return false;
            }

            App.JavaPackagePath = javaPackagePath;
            if (packagePath == null)
                MChubCommandQueue.Enqueue(javaCommand);
        }

        switch (packagePath)
        {
            case null when javaPackagePath == null && MChubCommandService.TryHandleStartupArgs(args):
                return false;
#if WINDOWS
            case null when javaPackagePath == null && WindowsJumpListService.TryForwardToRunningInstance(args):
                return false;
#endif
            case null:
#if WINDOWS
                WindowsJumpListService.StartCommandServer();
#endif
                break;
        }

#if WINDOWS

        WindowsJumpListService.SetAppUserModelId();
#endif

        if (packagePath == null)
            MChubCommandService.StartCommandServer();

        Logger.Info(string.Format(LogLanguageManager.Instance.desktop_primaryInstance_starting.CurrentValue(), args.Length));
        var versionInfo = AppVersionService.Instance.Version;
        Initializer.Program("MChub", "hub.code.MChub", versionInfo.VersionTitle);

        Logger.Info(LogLanguageManager.Instance.desktop_primaryInstance_mainEntry.CurrentValue());

#if WINDOWS || LINUX || MACOS
        AppSetup.RegisterBedrockLauncher();
#endif

        LogOperatingSystem();
        _ = RunDeferredMaintenanceAsync();
        return true;
    }

    private static async Task RunDeferredMaintenanceAsync()
    {
        // Keep shell integration maintenance off the critical path to the first window.
        await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        await ProtocolRegistration.TryRegisterLinuxOnStartupAsync().ConfigureAwait(false);
        await MChubCommandRegistration.RegisterAsync().ConfigureAwait(false);
#if WINDOWS
        await Task.Run(() =>
        {
            WindowsBedrockFileAssociationService.Register();
            WindowsJavaFileAssociationService.Register();
        }).ConfigureAwait(false);
#endif
    }

    private static void LogOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Logger.Info(LogLanguageManager.Instance.desktop_startup_osWindows.CurrentValue());
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Logger.Info(LogLanguageManager.Instance.desktop_startup_osLinux.CurrentValue());
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Logger.Info(LogLanguageManager.Instance.desktop_startup_osMacos.CurrentValue());
    }
}
