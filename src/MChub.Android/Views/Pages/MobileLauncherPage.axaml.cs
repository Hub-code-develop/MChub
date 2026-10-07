using Avalonia.Controls;
using Avalonia.Interactivity;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Localization;
using MChub.Mobile.Services;
// Android 隐式全局 using 引入了 Android.Widget，Button 会与 Avalonia.Controls.Button 冲突。
using Button = Avalonia.Controls.Button;

namespace MChub.Mobile.Views.Pages;

/// <summary>
/// 移动端主页。布局参照 Zalith Launcher 2 的 LauncherScreen：
/// 上方一张「操作卡片」（账户 → 当前实例 → 启动），下方是实例列表。
/// </summary>
public partial class MobileLauncherPage : UserControl
{
    /// <summary>实例下拉与实例列表指向同一份选中项，防止互相触发形成回环。</summary>
    private bool _syncingSelection;

    public MobileLauncherPage()
    {
        InitializeComponent();
        AccountText.Text = Data.ConfigEntry.CurrentAccountDisplay;
        Refresh();
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (!MobileBootstrap.CoreReady)
        {
            StatusText.Text = MobileBootstrap.FailureReason ??
                              MobileLanguageManager.Instance.mobile_instancesEmpty.CurrentValue();
            EmptyHint.IsVisible = true;
            return;
        }

        StatusText.Text = MobileLanguageManager.Instance.mobile_instancesScanning.CurrentValue();
        try
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            EmptyHint.IsVisible = true;
            return;
        }

        var instances = InstanceManager.Instance.Instances;
        InstanceList.ItemsSource = instances;
        InstancePicker.ItemsSource = instances;
        EmptyHint.IsVisible = instances.Count == 0;

        // 进来就默认选中第一个实例，让「当前实例 + 启动」可以直接用。
        if (InstancePicker.SelectedItem is null && instances.Count > 0)
            SetCurrent(instances[0]);

        StatusText.Text = string.Empty;
    }

    private void InstancePicker_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (InstancePicker.SelectedItem is MinecraftInstance instance)
            SetCurrent(instance);
    }

    private void InstanceList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (InstanceList.SelectedItem is MinecraftInstance instance)
            SetCurrent(instance);
    }

    private void SetCurrent(MinecraftInstance instance)
    {
        _syncingSelection = true;
        try
        {
            InstancePicker.SelectedItem = instance;
            InstanceList.SelectedItem = instance;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void Launch_OnClick(object? sender, RoutedEventArgs e)
    {
        if (InstancePicker.SelectedItem is not MinecraftInstance instance)
        {
            StatusText.Text = MobileLanguageManager.Instance.mobile_launcherNoInstance.CurrentValue();
            return;
        }

        ReportLaunch(instance);
    }

    private void LaunchInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MinecraftInstance instance })
            return;

        SetCurrent(instance);
        ReportLaunch(instance);
    }

    private void ReportLaunch(MinecraftInstance instance)
        => StatusText.Text = $"{instance.InstanceName} · {DescribeLaunchReadiness()}";

    /// <summary>
    /// 启动前的就绪情况。
    ///
    /// <p>移动端启动链路分三层：C# 侧准备资源（复用 MChub.Core）→ Java 后端校验运行时 →
    /// native 层创建 JVM 并驱动 LWJGL 与图形翻译层。这里如实反馈前两层的真实状态：
    /// 缺运行时就说缺运行时，缺原生层就说缺原生层，不做"点了没反应"。</p>
    /// </summary>
    private static string DescribeLaunchReadiness()
    {
        var language = MobileLanguageManager.Instance;

        if (!MobileBootstrap.CoreReady)
            return MobileBootstrap.FailureReason ?? language.mobile_instancesEmpty.CurrentValue();

        if (!JavaRuntimeBridge.IsAvailable)
            return string.Format(language.mobile_instancesJavaBackendUnavailable.CurrentValue(),
                JavaRuntimeBridge.UnavailableReason ?? language.mobile_settingsRuntimePending.CurrentValue());

        var runtimeDir = MobileRuntimePaths.JavaRuntimeDirectory;
        if (!JavaRuntimeBridge.TryIsRuntimeReady(runtimeDir, out var ready, out var failure))
            return string.Format(language.mobile_instancesJavaBackendUnavailable.CurrentValue(),
                failure ?? language.mobile_settingsRuntimePending.CurrentValue());

        if (!ready)
            return language.mobile_instancesRuntimeMissing.CurrentValue();

        var version = JavaRuntimeBridge.ReadRuntimeVersion(runtimeDir) ?? "?";
        return string.Format(language.mobile_instancesRuntimeReady.CurrentValue(), version);
    }
}
