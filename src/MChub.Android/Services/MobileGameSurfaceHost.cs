using Android.App;
using Android.Views;
using Android.Widget;
using Avalonia.Threading;

namespace MChub.Mobile.Services;

/// <summary>
/// 给游戏 JVM 提供一块 Android <see cref="Surface"/>。
///
/// <para><b>为什么必须有这个东西</b>：游戏 JVM 里的 GLFW stub 在第一次
/// <c>glfwCreateWindow</c> 时，会向 native 侧要「往哪画」——那个 Surface 必须由
/// Android 侧（也就是这里）准备好并交给 <c>JREUtils.setupBridgeWindow(surface)</c>。
/// 没有它，JVM 即使建起来也画不出任何东西。</para>
///
/// <para><b>怎么接</b>：往当前 Activity 的 content 根里再挂一个全屏 <see cref="SurfaceView"/>
/// （后加的在上层，于是盖住 Avalonia 界面）；等它的 SurfaceHolder 回调
/// <c>SurfaceCreated</c>，把持有者里的 Surface 交出去。要停游戏/退出时把视图摘掉。</para>
///
/// <para>触摸事件先由这个视图吃掉（设为 Clickable/Focusable），免得游戏运行时
/// 下面的启动器界面还在响应点击。真正的触屏输入映射到游戏属于后续工作。</para>
/// </summary>
internal sealed class MobileGameSurfaceHost : Java.Lang.Object, ISurfaceHolderCallback
{
    private static MobileGameSurfaceHost? _current;

    private readonly SurfaceView _surfaceView;
    private readonly TaskCompletionSource<Surface?> _surfaceReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private MobileGameSurfaceHost(Activity activity)
    {
        _surfaceView = new SurfaceView(activity)
        {
            Focusable = true,
            Clickable = true,
            FocusableInTouchMode = true
        };
        _surfaceView.Holder!.AddCallback(this);
    }

    /// <summary>
    /// 在 UI 线程上准备好一块 Surface 并返回它；超时返回 <c>null</c>（调用方要如实报错，
    /// 不能拿一个空 Surface 去启动）。
    /// </summary>
    /// <param name="timeout">等待 SurfaceCreated 的最长时间。</param>
    public static async Task<Surface?> AcquireAsync(TimeSpan timeout)
    {
        var activity = MainActivity.Current;
        if (activity is null)
        {
            return null;
        }

        MobileGameSurfaceHost host = null!;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host = new MobileGameSurfaceHost(activity);
            var parameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            activity.AddContentView(host._surfaceView, parameters);
            _current = host;
        });

        var surface = await host._surfaceReady.Task.WaitAsync(timeout).ConfigureAwait(false);
        return surface;
    }

    /// <summary>摘掉游戏视图，把屏幕还给启动器界面。</summary>
    public static async Task ReleaseAsync()
    {
        var host = Interlocked.Exchange(ref _current, null);
        if (host is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            host._surfaceView.Holder?.RemoveCallback(host);
            if (host._surfaceView.Parent is ViewGroup parent)
            {
                parent.RemoveView(host._surfaceView);
            }
        });
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        // 只在第一次创建时交付：SurfaceView 在重建（切后台再回来）时会反复回调，
        // 而启动只需要一次；后续的 Surface 变化由 native 侧自己处理。
        _surfaceReady.TrySetResult(holder.Surface);
    }

    public void SurfaceChanged(ISurfaceHolder holder, Android.Graphics.Format format, int width, int height)
    {
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        // Surface 被销毁时启动还没拿到它 → 直接放弃，让上层报「拿不到渲染目标」。
        _surfaceReady.TrySetResult(null);
    }
}
