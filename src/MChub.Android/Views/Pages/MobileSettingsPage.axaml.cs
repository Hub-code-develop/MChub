using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using MChub.Core.Const;
using MChub.Localization;
using MChub.Mobile.Services;

namespace MChub.Mobile.Views.Pages;

public partial class MobileSettingsPage : UserControl
{
    /// <summary>
    /// 设置页没有实例上下文，"一键安装"按 MC 1.21 的需求装 Java 21（覆盖面最广的现代版本）。
    /// 具体实例需要 8 / 17 / 25 时，启动该实例会自动补齐对应版本。
    /// </summary>
    private const int DefaultJavaMajorVersion = 21;

    public MobileSettingsPage()
    {
        InitializeComponent();

        var language = MobileLanguageManager.Instance;

        LanguageValue.Text = LocalizationService.CurrentCulture.Name;
        ThemeValue.Text = Avalonia.Application.Current?.RequestedThemeVariant?.ToString() ?? "-";
        StorageValue.Text = ConfigPath.UserDataRootPath;

        if (MobileBootstrap.CoreReady)
        {
            VersionValue.Text = Data.Instance.Version.VersionTitle;
        }
        else
        {
            VersionValue.Text = language.mobile_settingsStorageUnknown.CurrentValue();
            CoreStatusText.Text = MobileBootstrap.FailureReason;
            CoreStatusText.IsVisible = !string.IsNullOrWhiteSpace(MobileBootstrap.FailureReason);
        }

        ApplyRuntimeState();
    }

    /// <summary>
    /// 移动端运行时的真实状态。分三层判定：Core 引导 → Java 后端可用性 → 运行时与原生层，
    /// 各层用 InfoBar 的严重程度表达，不写死"未接入"这类占位文案。
    /// </summary>
    private void ApplyRuntimeState()
    {
        var language = MobileLanguageManager.Instance;
        RuntimeInfoBar.Title = language.mobile_settingsRuntimeTitle.CurrentValue();

        if (!MobileBootstrap.CoreReady)
        {
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Error;
            RuntimeInfoBar.Message = MobileBootstrap.FailureReason ??
                                     language.mobile_settingsRuntimePending.CurrentValue();
            return;
        }

        if (!JavaRuntimeBridge.IsAvailable)
        {
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = string.Format(
                language.mobile_instancesJavaBackendUnavailable.CurrentValue(),
                JavaRuntimeBridge.UnavailableReason ?? language.mobile_settingsRuntimePending.CurrentValue());
            return;
        }

        var installed = MobileJavaRuntime.GetInstalledRuntimes();
        if (installed.Count == 0)
        {
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = language.mobile_instancesRuntimeMissing.CurrentValue();
            return;
        }

        var versions = string.Join(" / ",
            installed.Keys.OrderBy(major => major).Select(major => major.ToString()));

        if (!JavaRuntimeBridge.IsNativeLayerLoaded())
        {
            // 运行时有了但原生层没打进包：界面可用，但启动必然失败，这里如实说明。
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = language.mobile_settingsRuntimeNativeMissing.CurrentValue() +
                                     " · " + string.Format(language.mobile_instancesRuntimeReady.CurrentValue(), versions);
            return;
        }

        RuntimeInfoBar.Severity = MobileRuntimeInstaller.AreNativesInstalled
            ? FAInfoBarSeverity.Success
            : FAInfoBarSeverity.Warning;

        RuntimeInfoBar.Message = string.Format(language.mobile_instancesRuntimeReady.CurrentValue(), versions) +
                                 (MobileRuntimeInstaller.AreNativesInstalled
                                     ? string.Empty
                                     : " · " + language.mobile_settingsRuntimeComponentsMissing.CurrentValue());
    }

    /// <summary>
    /// 一键安装：按需下载 Java 运行时（公开源，arm64）+ 运行组件（LWJGL / GL 翻译层）。
    /// 这里没有实例上下文，按 MC 1.21 的需求装 Java 21；具体实例需要别的版本时，启动时会自动补齐。
    /// </summary>
    private async void InstallAll_OnClick(object? sender, RoutedEventArgs e)
    {
        var language = MobileLanguageManager.Instance;

        InstallAllButton.IsEnabled = false;
        ImportStatusText.IsVisible = true;

        try
        {
            var progress = new Progress<MobileInstallProgress>(report =>
            {
                var percentage = report.Fraction > 0 ? $" {report.Fraction:P0}" : string.Empty;
                ImportStatusText.Text = report.Stage + percentage;
            });

            await MobileGameLauncher.EnsureReadyAsync(DefaultJavaMajorVersion, progress);
            ImportStatusText.Text = language.mobile_settingsInstallDone.CurrentValue();
        }
        catch (Exception exception)
        {
            ImportStatusText.Text = string.Format(
                language.mobile_settingsRuntimeImportFailed.CurrentValue(), exception.Message);
        }
        finally
        {
            InstallAllButton.IsEnabled = true;
            ApplyRuntimeState();
        }
    }

    /// <summary>
    /// 导入移动端 Java 运行时。
    ///
    /// <p>Android 能用的运行时（携带 arm64 libjvm.so 的 OpenJDK 移植版）没有稳定的公开下载地址，
    /// 所以走"用户提供归档"这条路：选一个 <c>.tar.xz</c> / <c>.zip</c>，解压安装到应用私有目录。</p>
    /// </summary>
    private async void ImportRuntime_OnClick(object? sender, RoutedEventArgs e)
    {
        var language = MobileLanguageManager.Instance;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        string? localArchive = null;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = language.mobile_settingsRuntimeImport.CurrentValue(),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Java runtime archive")
                    {
                        Patterns = ["*.tar.xz", "*.xz", "*.tar", "*.zip"]
                    }
                ]
            });

            if (files.Count == 0)
                return;

            var picked = files[0];
            ImportRuntimeButton.IsEnabled = false;
            ImportStatusText.IsVisible = true;
            ImportStatusText.Text = language.mobile_settingsRuntimeImporting.CurrentValue();

            // 先落到应用私有缓存再解压：Android 的文件选择器给的是内容 URI，不是普通文件路径。
            localArchive = Path.Combine(Path.GetTempPath(), picked.Name);
            await using (var source = await picked.OpenReadAsync())
            await using (var destination = File.Create(localArchive))
            {
                await source.CopyToAsync(destination);
            }

            var progress = new Progress<string>(message => ImportStatusText.Text = message);
            // 0 = 按归档里的 release 自动判定主版本。
            var runtimeRoot = await MobileJavaRuntime.ImportArchiveAsync(localArchive, 0, progress);

            MobileJavaRuntime.TryReadJavaVersion(runtimeRoot, out var version, out _);
            ImportStatusText.Text = string.Format(
                language.mobile_settingsRuntimeImported.CurrentValue(),
                string.IsNullOrWhiteSpace(version) ? "?" : version);
        }
        catch (Exception exception)
        {
            ImportStatusText.IsVisible = true;
            ImportStatusText.Text = string.Format(
                language.mobile_settingsRuntimeImportFailed.CurrentValue(), exception.Message);
        }
        finally
        {
            if (localArchive is not null)
            {
                try
                {
                    File.Delete(localArchive);
                }
                catch (IOException)
                {
                }
            }

            ImportRuntimeButton.IsEnabled = true;
            ApplyRuntimeState();
        }
    }
}
