using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
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

    /// <summary>一次启动调用是否还在进行（游戏跑起来期间一直为 true）。</summary>
    private bool _launching;

    private DispatcherTimer? _runningTimer;

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

        // 有阻塞项（缺运行时 / 缺原生层）就明说，别让用户点了才发现。
        // 有阻塞项（缺运行时 / 缺运行组件 / 缺原生层）就明说，别让用户点了才发现。
        StatusText.Text = MobileGameLauncher.DescribeBlocker(InstancePicker.SelectedItem as MinecraftInstance)
                          ?? string.Empty;
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

    /// <summary>
    /// 同一个按钮两种语义：空闲时启动，启动中时请求停止（ZL2 也是一个启动按钮）。
    /// </summary>
    private async void Launch_OnClick(object? sender, RoutedEventArgs e)
    {
        var language = MobileLanguageManager.Instance;

        if (_launching)
        {
            StatusText.Text = language.mobile_launcherStopping.CurrentValue();
            MobileGameLauncher.Abort();
            return;
        }

        if (InstancePicker.SelectedItem is not MinecraftInstance instance)
        {
            StatusText.Text = language.mobile_launcherNoInstance.CurrentValue();
            return;
        }

        var blocker = MobileGameLauncher.DescribeBlocker(instance);
        var needsInstall = MobileGameLauncher.NeedsInstall(instance);
        if (blocker is not null && !needsInstall)
        {
            // 原生层缺失、后端不可用这类问题没法自愈，如实报告即可。
            StatusText.Text = blocker;
            return;
        }

        _launching = true;
        SetLaunchButton(running: true);

        try
        {
            // 首次使用：缺运行时 / 运行组件就直接装上，不用让用户自己去翻包。
            if (needsInstall)
            {
                await InstallEnvironmentAsync(instance);

                var afterInstall = MobileGameLauncher.DescribeBlocker(instance);
                if (afterInstall is not null)
                {
                    StatusText.Text = afterInstall;
                    return;
                }
            }

            StatusText.Text = language.mobile_launcherLaunching.CurrentValue();
            StartRunningTimer();

            var result = await MobileGameLauncher.LaunchAsync(instance);
            StatusText.Text = result switch
            {
                { Launched: false } => string.Format(
                    language.mobile_launcherFailed.CurrentValue(), result.Message),
                { Message: not null } => result.Message,
                _ => string.Format(language.mobile_launcherExitCode.CurrentValue(), result.ExitCode)
            };
        }
        catch (Exception exception)
        {
            StatusText.Text = string.Format(language.mobile_launcherFailed.CurrentValue(), exception.Message);
        }
        finally
        {
            StopRunningTimer();
            _launching = false;
            SetLaunchButton(running: false);
        }
    }

    /// <summary>下载安装 Java 运行时与运行组件，进度直接反映在状态文本上。</summary>
    private async Task InstallEnvironmentAsync(MinecraftInstance instance)
    {
        var language = MobileLanguageManager.Instance;
        var requiredMajor = MobileGameLauncher.GetRequiredJavaMajorVersion(instance);

        StatusText.Text = string.Format(language.mobile_launcherInstalling.CurrentValue(), requiredMajor);

        var progress = new Progress<MobileInstallProgress>(report =>
        {
            var percentage = report.Fraction > 0 ? $" {report.Fraction:P0}" : string.Empty;
            StatusText.Text = report.Stage + percentage;
        });

        await MobileGameLauncher.EnsureReadyAsync(requiredMajor, progress);
        StatusText.Text = language.mobile_launcherInstallDone.CurrentValue();
    }

    private void LaunchInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MinecraftInstance instance })
            return;

        SetCurrent(instance);
        Launch_OnClick(sender, e);
    }

    private void SetLaunchButton(bool running)
    {
        var language = MobileLanguageManager.Instance;
        LaunchIcon.Symbol = running ? FASymbol.Stop : FASymbol.Play;
        LaunchLabel.Text = running
            ? language.mobile_launcherStopGame.CurrentValue()
            : language.mobile_launcherLaunchGame.CurrentValue();
    }

    /// <summary>
    /// 游戏跑起来后，启动调用会一直阻塞在等待游戏退出的地方；用定时器把「运行中」如实显示出来。
    /// </summary>
    private void StartRunningTimer()
    {
        _runningTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _runningTimer.Tick -= RunningTimer_OnTick;
        _runningTimer.Tick += RunningTimer_OnTick;
        _runningTimer.Start();
    }

    private void RunningTimer_OnTick(object? sender, EventArgs e)
    {
        if (MobileGameLauncher.IsRunning)
            StatusText.Text = MobileLanguageManager.Instance.mobile_launcherRunning.CurrentValue();
    }

    private void StopRunningTimer()
    {
        if (_runningTimer is null)
            return;

        _runningTimer.Stop();
        _runningTimer.Tick -= RunningTimer_OnTick;
    }
}
