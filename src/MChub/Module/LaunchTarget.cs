using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;

namespace MChub.Module;

/// <summary>
/// 外壳层面"这次要启动哪个实例"的共享状态。
///
/// LauncherX 布局里，底部导航的「开始游戏」、首页左下的大标题、账户卡旁边的启动动作
/// 必须是同一个目标；用户在大标题上选了别的版本，底栏再点开始游戏就要启动它。
/// 没有显式选择时退回到最近游玩的实例。
/// </summary>
public static class LaunchTarget
{
    private static MinecraftInstance? _current;

    /// <summary>用户显式选中的实例；null 表示跟随"最近游玩"。</summary>
    public static MinecraftInstance? Pinned => _current;

    /// <summary>当前生效的启动目标。</summary>
    public static MinecraftInstance? Current => _current ?? RecentInstance();

    public static event EventHandler? Changed;

    public static void Set(MinecraftInstance? instance)
    {
        if (ReferenceEquals(_current, instance)) return;
        _current = instance;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static MinecraftInstance? RecentInstance()
    {
        return InstanceManager.Instance.Instances
            .Where(instance => !instance.IsBlocked && instance.Config is not null)
            .OrderByDescending(instance => instance.LastPlayTime)
            .FirstOrDefault();
    }
}
