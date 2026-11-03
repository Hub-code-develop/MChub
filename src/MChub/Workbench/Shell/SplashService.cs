using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Workbench.Shell;

/// <summary>
/// 启动画面的生命周期。约定：
/// <list type="bullet">
/// <item>每次启动都会 <see cref="Show"/>，不管后面走到主界面、登录页、OOBE 还是整合包导入；</item>
/// <item>窗口至少停留 <see cref="MinimumVisibleMs"/>，避免一闪而过；</item>
/// <item>调用方准备好交接时调 <see cref="Ready"/> 或 <see cref="CloseWhenReady"/>；</item>
/// <item>万一没人调，<see cref="HardTimeoutMs"/> 后强制关掉，绝不会卡住启动。</item>
/// </list>
/// </summary>
public static class SplashService
{
    private const int MinimumVisibleMs = 1200;
    private const int HardTimeoutMs = 25_000;

    private static SplashWindow? _window;
    private static readonly Stopwatch Clock = new();
    private static bool _closing;

    public static bool IsVisible => _window is not null;

    /// <summary>显示启动画面。重复调用安全。</summary>
    public static void Show()
    {
        if (_window is not null || _closing)
            return;

        Clock.Restart();
        try
        {
            var window = new SplashWindow();
            window.SetTitle(Text(AppLanguageManager.Instance.splash_starting.CurrentValue(), "正在启动"));
            window.SetStepLabels(new[]
            {
                Text(AppLanguageManager.Instance.splash_stepEnv.CurrentValue(), "运行环境"),
                Text(AppLanguageManager.Instance.splash_stepAccount.CurrentValue(), "账号与配置"),
                Text(AppLanguageManager.Instance.splash_stepInstances.CurrentValue(), "游戏实例"),
                Text(AppLanguageManager.Instance.splash_stepWorkspace.CurrentValue(), "工作区")
            });
            window.SetStage(0, 0.08,
                Text(AppLanguageManager.Instance.splash_detailEnv.CurrentValue(), "正在读取本地配置与运行环境…"));

            _window = window;
            window.Show();

            DispatcherTimer.RunOnce(HardClose, TimeSpan.FromMilliseconds(HardTimeoutMs));
        }
        catch (Exception ex)
        {
            Logger.Error("启动画面创建失败", ex);
            _window = null;
        }
    }

    /// <summary>推进到某个阶段（0..4，4 表示全部完成）。</summary>
    public static void Stage(int step, double progress, string? detail = null)
    {
        if (_window is not { } window)
            return;

        try
        {
            window.SetStage(step, progress, detail);
        }
        catch (Exception ex)
        {
            Logger.Warning("启动画面进度更新失败: " + ex.Message);
        }
    }

    /// <summary>数据加载完成：进度拉满并交接。</summary>
    public static void Ready()
    {
        Stage(4, 1, Text(AppLanguageManager.Instance.splash_detailReady.CurrentValue(), "准备就绪"));
        CloseWhenReady();
    }

    public static void Fail(Exception exception)
    {
        if (_window is not { } window)
            return;

        try
        {
            window.MarkFailed(Text(AppLanguageManager.Instance.splash_detailFailed.CurrentValue(),
                "启动过程中出现问题，已记录日志"));
        }
        catch
        {
            // 忽略
        }

        Logger.Error("启动流程异常", exception);
        // 失败也要收场，停留 2.5 秒让用户看清提示。
        DispatcherTimer.RunOnce(() => _ = CloseAsync(), TimeSpan.FromMilliseconds(2500));
    }

    /// <summary>交接：等到最短停留时间后淡出关闭。</summary>
    public static void CloseWhenReady()
    {
        if (_window is null || _closing)
            return;

        _closing = true;
        var remain = MinimumVisibleMs - (int)Clock.ElapsedMilliseconds;
        DispatcherTimer.RunOnce(() => _ = CloseAsync(), TimeSpan.FromMilliseconds(Math.Max(0, remain)));
    }

    private static void HardClose()
    {
        if (_window is not null && !_closing)
        {
            Logger.Warning("启动画面达到超时上限，强制关闭");
            _closing = true;
        }

        _ = CloseAsync();
    }

    private static async Task CloseAsync()
    {
        var window = _window;
        if (window is null)
            return;

        _window = null;
        _closing = true;

        try
        {
            await window.FadeOutAsync();
        }
        catch
        {
            // 淡出失败直接关
        }

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            Logger.Warning("启动画面关闭失败: " + ex.Message);
        }
    }

    private static string Text(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
