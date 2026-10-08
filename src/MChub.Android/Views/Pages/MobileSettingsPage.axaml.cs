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

        var runtimeRoot = MobileJavaRuntime.FindRuntimeRoot();
        if (runtimeRoot is null)
        {
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = language.mobile_instancesRuntimeMissing.CurrentValue();
            return;
        }

        if (!MobileJavaRuntime.TryReadJavaVersion(runtimeRoot, out var version, out _))
            version = "?";

        if (!JavaRuntimeBridge.IsNativeLayerLoaded())
        {
            // 运行时有了但原生层没打进包：界面可用，但启动必然失败，这里如实说明。
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = language.mobile_settingsRuntimeNativeMissing.CurrentValue() +
                                     " · " + string.Format(language.mobile_instancesRuntimeReady.CurrentValue(), version);
            return;
        }

        RuntimeInfoBar.Severity = FAInfoBarSeverity.Success;
        RuntimeInfoBar.Message = string.Format(language.mobile_instancesRuntimeReady.CurrentValue(), version);
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
            var runtimeRoot = await MobileJavaRuntime.ImportArchiveAsync(localArchive, progress);

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
