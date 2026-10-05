using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Threading;
using MChub.Core.Const;
using MChub.Core.Minecraft.Instance.Java;
using MChub.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace MChub.Core.Minecraft.Services;

public static class JavaAutoInstallCoordinator
{
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    public static async Task<JavaRuntimeEntry?> EnsureAsync(int majorVersion,
        JavaInstallProgressHandler? progress = null,
        CancellationToken cancellationToken = default)
    {
        var reconcile = Dispatcher.UIThread.CheckAccess()
            ? await JavaRuntimeManager.ReconcileAsync(Data.ConfigEntry.JavaRuntimes,
                Data.ConfigEntry.JavaVersionDefaultPaths, cancellationToken)
            : await Dispatcher.UIThread.InvokeAsync(() =>
                JavaRuntimeManager.ReconcileAsync(Data.ConfigEntry.JavaRuntimes,
                    Data.ConfigEntry.JavaVersionDefaultPaths, cancellationToken));
        NotifyReconcile(reconcile);

        // 只认“可用”的已装运行时：预发布/EA 构建（如 25-loom）虽报大版本匹配，但可能缺少
        // 游戏所需 API，不能当作已满足要求而跳过安装。
        var existing = await FindUsableAsync(majorVersion, cancellationToken);
        if (existing is not null) return existing;
        var approved = await ConfirmAsync(majorVersion);
        if (!approved) return null;

        await InstallLock.WaitAsync(cancellationToken);
        try
        {
            existing = await FindUsableAsync(majorVersion, cancellationToken);
            if (existing is not null) return existing;
            var runtime = await JavaDistributionService.InstallMojangAsync(majorVersion, ConfigPath.JavaRuntimesPath,
                progress, cancellationToken);
            if (runtime is null)
            {
                var version = await JavaDistributionService.GetFastestVersionAsync(majorVersion, cancellationToken)
                              ?? throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.javaAutoInstall_noJavaForPlatform.CurrentValue(), majorVersion));
                runtime = await JavaDistributionService.InstallAsync(version, ConfigPath.JavaRuntimesPath,
                    ConfigPath.TempFolderPath, progress, cancellationToken);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Data.ConfigEntry.JavaRuntimes.Contains(runtime)) Data.ConfigEntry.JavaRuntimes.Add(runtime);
                if (Data.ConfigEntry.GetJavaDefault(runtime.MajorVersion) is null)
                    Data.ConfigEntry.SetJavaDefault(runtime.MajorVersion, runtime);
            });
            return runtime;
        }
        finally
        {
            InstallLock.Release();
        }
    }

    /// <summary>
    /// 在已配置的运行时中找出大版本匹配且「可用」的一个（会拒绝预发布/EA 构建等）。
    /// </summary>
    private static async Task<JavaRuntimeEntry?> FindUsableAsync(int majorVersion,
        CancellationToken cancellationToken)
    {
        foreach (var runtime in Data.ConfigEntry.JavaRuntimes)
        {
            if (runtime.MajorVersion != majorVersion || !File.Exists(runtime.JavaPath))
                continue;
            if (await JavaRuntimeVerifier.IsUsableAsync(runtime.JavaPath, runtime.MajorVersion,
                    runtime.JavaVersion, cancellationToken))
                return runtime;
        }

        return null;
    }

    private static async Task<bool> ConfirmAsync(int majorVersion)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
            if (topLevel is null) return false;
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Thickness(24),
                Text = CommonLanguageManager.Instance.javaAutoInstall_confirmText.CurrentValue(),
                TextWrapping = TextWrapping.Wrap
            }, null, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = string.Format(CommonLanguageManager.Instance.javaAutoInstall_title.CurrentValue(), majorVersion),
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.javaAutoInstall_yesButton.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false, CanResize = false
            });
            return result == DialogResult.Yes;
        });
    }

    private static void NotifyReconcile(JavaReconcileResult result)
    {
        var messages = JavaRuntimeManager.BuildMessages(result);
        if (messages.Count == 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            var topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
            if (topLevel is null)
                return;

            foreach (var message in messages)
                topLevel.Notice(message.Text,
                    message.IsError ? NotificationType.Error : NotificationType.Information);
        });
    }
}