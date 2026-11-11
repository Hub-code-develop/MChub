using Avalonia.Controls.Notifications;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Extensions;
using TioUi.Common.Classes;
using TopLevel = Avalonia.Controls.TopLevel;

namespace Tio.Avalonia.Standard.Tab.Gateway;

public static class NotificationGateway
{
    /// <summary>通知卡片左上角显示的启动器名（旧品牌 "Portal" 已弃用）。</summary>
    private const string NotificationTitle = "MChub";

    public static Func<bool> IsToastFunc = null;
    
    public static void Notice(this TopLevel topLevel, NotificationOptions options)
    {
        Logger.Info($"[Notice] ({options.Type}) {options.Content}");
        if (IsToastFunc())
        {
            topLevel.TryGetToast()?.Show(options);
        }
        else
        {
            topLevel.TryGetNotification()?.Show(options);
        }
    }

    public static void Notice(this TopLevel topLevel, string msg, NotificationType type = NotificationType.Information)
    {
        Logger.Info($"[Notice] ({type}) {msg}");
        if (IsToastFunc())
        {
            topLevel.TryGetToast()?.Show(msg, type);
        }
        else
        {
            topLevel.TryGetNotification()?.Show(NotificationTitle, msg, new NotificationOptions { Type = type });
        }
    }


    public static void Notice(this TopLevel topLevel, string msg, string title,
        NotificationType type = NotificationType.Information)
    {
        Logger.Info($"[Notice] ({type}) {title}: {msg}");
        if (IsToastFunc())
        {
            topLevel.TryGetToast()?.Show(msg, type);
        }
        else
        {
            topLevel.TryGetNotification()?.Show(title, msg, new NotificationOptions { Type = type });
        }
    }
}