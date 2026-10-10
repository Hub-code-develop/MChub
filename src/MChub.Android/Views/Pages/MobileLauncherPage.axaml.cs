using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Localization;
using MChub.Mobile.Services;
using MChub.Mobile.ViewModels;
// Android 隐式全局 using 引入了 Android.Widget，Button 会与 Avalonia.Controls.Button 冲突。
using Button = Avalonia.Controls.Button;

namespace MChub.Mobile.Views.Pages;

/// <summary>
/// 移动端主页。布局对齐 Zalith Launcher 2 的 LauncherScreen（横屏两列）：
/// 左侧是实例卡片网格（卡片自带启动按钮，点卡片切换「当前实例」），
/// 右侧是操作栏（账户 → 当前实例 → 启动按钮），与 ZL2 的 ActionMenu 同构。
/// </summary>
public partial class MobileLauncherPage : UserControl
{
    /// <summary>用户点了「新建实例」——外壳负责切到新建页。</summary>
    public event EventHandler? NewInstanceRequested;

    private readonly ObservableCollection<MobileInstanceEntry> _entries = [];

    /// <summary>当前实例（左侧卡片的选中项，右侧操作栏也读它）。</summary>
    private MobileInstanceEntry? _current;

    /// <summary>一次启动调用是否还在进行（游戏跑起来期间一直为 true）。</summary>
    private bool _launching;

    private DispatcherTimer? _runningTimer;

    public MobileLauncherPage()
    {
        InitializeComponent();
        AccountText.Text = Data.ConfigEntry.CurrentAccountDisplay;
        InstanceGrid.ItemsSource = _entries;
        Refresh();
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) => Refresh();

    private void NewInstance_OnClick(object? sender, RoutedEventArgs e)
        => NewInstanceRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>重新扫描实例（安装完 / 切回主页时由外壳调用）。</summary>
    public void Reload() => Refresh();

    private void Refresh()
    {
        var language = MobileLanguageManager.Instance;

        if (!MobileBootstrap.CoreReady)
        {
            Fail(MobileBootstrap.FailureReason ?? language.mobile_instancesEmpty.CurrentValue());
            return;
        }

        StatusText.Text = language.mobile_instancesScanning.CurrentValue();

        try
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return;
        }

        var instances = InstanceManager.Instance.Instances;
        RebuildEntries(instances);

        // 进来就默认选中第一个实例，让「当前实例 + 启动」可以直接用。
        if (_current is null && _entries.Count > 0)
            SetCurrent(_entries[0]);
        else
            UpdateCurrentCard();

        EmptyHint.Text = language.mobile_launcherNoInstance.CurrentValue();
        EmptyHint.IsVisible = _entries.Count == 0;

        // 有阻塞项（缺运行时 / 缺运行组件 / 缺原生层）就明说，别让用户点了才发现。
        UpdateBlockerStatus();
    }

    /// <summary>扫描失败或 Core 未就绪：清空网格并如实显示原因，不留一片空白。</summary>
    private void Fail(string reason)
    {
        _entries.Clear();
        _current = null;
        UpdateCurrentCard();
        EmptyHint.Text = reason;
        EmptyHint.IsVisible = true;
        StatusText.Text = string.Empty;
    }

    /// <summary>
    /// 重建卡片。尽量保住当前选中项（按实例目录比），重扫之后不至于跳回第一个。
    /// </summary>
    private void RebuildEntries(IEnumerable<MinecraftInstance> instances)
    {
        var previousFolder = _current?.Instance.FolderPath;

        _entries.Clear();
        _current = null;

        foreach (var instance in instances)
        {
            var entry = new MobileInstanceEntry(instance);
            if (previousFolder is not null && instance.FolderPath == previousFolder)
            {
                entry.IsCurrent = true;
                _current = entry;
            }

            _entries.Add(entry);
        }
    }

    private void InstanceCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: MobileInstanceEntry entry })
            SetCurrent(entry);
    }

    private void SetCurrent(MobileInstanceEntry entry)
    {
        if (!ReferenceEquals(_current, entry))
        {
            _current = entry;
            foreach (var item in _entries)
                item.IsCurrent = ReferenceEquals(item, entry);
        }

        UpdateCurrentCard();
        UpdateBlockerStatus();
    }

    /// <summary>右侧「当前实例」卡片与左侧选中态共用同一份状态。</summary>
    private void UpdateCurrentCard()
    {
        var instance = _current?.Instance;
        CurrentInstanceIcon.Source = instance?.Icon;
        CurrentInstanceName.Text = instance?.InstanceName
                                   ?? MobileLanguageManager.Instance.mobile_launcherPickInstance.CurrentValue();
        CurrentInstanceInfo.Text = instance?.ShortDisplay ?? string.Empty;
    }

    private void UpdateBlockerStatus()
        => StatusText.Text = MobileGameLauncher.DescribeBlocker(_current?.Instance) ?? string.Empty;

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

        if (_current?.Instance is not { } instance)
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

    /// <summary>卡片里的启动按钮：先切到该实例，再走与右侧按钮同一条路径。</summary>
    private void LaunchInstance_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MobileInstanceEntry entry })
            return;

        SetCurrent(entry);
        Launch_OnClick(sender, e);
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
