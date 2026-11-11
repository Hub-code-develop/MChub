using Avalonia.Controls;
using InfinityUI.Controls;

namespace MChub.Module;

/// <summary>
/// infinityUI 通知的统一入口。
///
/// 主窗口建好后会把自己的 <see cref="InfinityToastHost" /> 注册进来；
/// 其余地方（下载失败、Java 缺失、安装完成等）直接调用这里的 Show 即可，
/// 不必再各自去找 TioUi 的 ToastManager。
/// </summary>
public static class InfinityToast
{
    private static InfinityToastHost? _host;

    public static void Register(InfinityToastHost host)
    {
        _host = host;
    }

    public static void Unregister(InfinityToastHost host)
    {
        if (ReferenceEquals(_host, host)) _host = null;
    }

    public static void Info(string message, string? title = null)
        => Show(message, InfinityToastLevel.Info, title);

    public static void Success(string message, string? title = null)
        => Show(message, InfinityToastLevel.Success, title);

    public static void Warning(string message, string? title = null)
        => Show(message, InfinityToastLevel.Warning, title);

    public static void Error(string message, string? title = null)
        => Show(message, InfinityToastLevel.Error, title);

    public static void Show(string message, InfinityToastLevel level, string? title = null,
        TimeSpan? duration = null)
    {
        var host = _host;
        if (host is null) return;

        if (host.CheckAccess())
        {
            host.Show(message, level, title, duration);
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => host.Show(message, level, title, duration));
    }
}
