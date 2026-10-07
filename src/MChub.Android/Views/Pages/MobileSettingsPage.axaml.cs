using Avalonia.Controls;
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
    /// 移动端运行时的真实状态。分三层判定：Core 引导 → Java 后端可用性 → 运行时是否已安装，
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

        var runtimeDir = MobileRuntimePaths.JavaRuntimeDirectory;
        if (!JavaRuntimeBridge.TryIsRuntimeReady(runtimeDir, out var ready, out var failure))
        {
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = string.Format(
                language.mobile_instancesJavaBackendUnavailable.CurrentValue(),
                failure ?? language.mobile_settingsRuntimePending.CurrentValue());
            return;
        }

        if (!ready)
        {
            RuntimeInfoBar.Severity = FAInfoBarSeverity.Warning;
            RuntimeInfoBar.Message = language.mobile_instancesRuntimeMissing.CurrentValue();
            return;
        }

        RuntimeInfoBar.Severity = FAInfoBarSeverity.Success;
        RuntimeInfoBar.Message = string.Format(
            language.mobile_instancesRuntimeReady.CurrentValue(),
            JavaRuntimeBridge.ReadRuntimeVersion(runtimeDir) ?? "?");
    }
}
