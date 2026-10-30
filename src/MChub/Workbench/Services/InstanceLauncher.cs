using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;

namespace MChub.Workbench.Services;

/// <summary>
/// 启动游戏。这是「启动器」这三个字的落点，所以单独放一处，
/// 侧边栏、编辑器、命令面板、命令行 IPC 都调它，不再各写一份。
/// </summary>
public static class InstanceLauncher
{
    private static bool _launching;

    public static bool IsLaunching => _launching;

    /// <summary>启动状态变化（用于状态栏 / 按钮禁用）。</summary>
    public static event EventHandler? LaunchStateChanged;

    public static async Task LaunchAsync(MinecraftInstance instance)
    {
        if (_launching)
        {
            WorkbenchServices.Output.Write(OutputService.ChannelLaunch, "已经有一个实例在启动中，请等它跑完。");
            return;
        }

        var output = WorkbenchServices.Output;
        _launching = true;
        LaunchStateChanged?.Invoke(null, EventArgs.Empty);

        output.Write(OutputService.ChannelLaunch,
            $"开始启动「{instance.InstanceName}」 · {instance.ShortDisplay} · {instance.FolderPath}");

        try
        {
            var options = MinecraftLaunchOptionsFactory.Create(instance, session =>
            {
                output.Write(OutputService.ChannelGame,
                    $"已接入「{instance.InstanceName}」的游戏日志");
                session.LogReceived += entry => output.WriteRaw(OutputService.ChannelGame, entry.Text);
            });

            await MinecraftLaunchService.LaunchAsync(instance, WorkbenchServices.MainWindow, options);
        }
        catch (Exception exception)
        {
            output.Write(OutputService.ChannelLaunch, $"启动失败：{exception.Message}");
            Tio.Avalonia.Standard.Modules.DiskIO.Logger.Error("启动实例失败", exception);
        }
        finally
        {
            _launching = false;
            if (Dispatcher.UIThread.CheckAccess())
                LaunchStateChanged?.Invoke(null, EventArgs.Empty);
            else
                Dispatcher.UIThread.Post(() => LaunchStateChanged?.Invoke(null, EventArgs.Empty));
        }
    }
}
