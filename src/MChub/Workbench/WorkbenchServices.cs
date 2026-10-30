using Avalonia.Controls;
using MChub.Workbench.Commands;
using MChub.Workbench.Documents;
using MChub.Workbench.Services;

namespace MChub.Workbench;

/// <summary>
/// 工作台级服务的访问点。窗口创建时初始化一次；
/// 后续挂上来的功能（实例、下载、创作工具）都从这里拿命令和文档服务，
/// 不需要互相引用窗口类型。
/// </summary>
public static class WorkbenchServices
{
    public static CommandService Commands { get; private set; } = new();

    public static DocumentService Documents { get; private set; } = new();

    /// <summary>输出面板后端。启动、下载、解压都往这里写信。</summary>
    public static OutputService Output { get; } = new();

    /// <summary>快捷键表。菜单里显示的键位和实际生效的键位来自同一份定义。</summary>
    public static KeybindingService Keybindings { get; } = new();

    /// <summary>窗口布局（尺寸、侧栏宽度、选中的活动项）。改动后调用 <see cref="SaveLayout"/>。</summary>
    public static WorkbenchLayoutState Layout { get; private set; } = new();

    /// <summary>主窗口。需要 TopLevel 的能力（通知、对话框、启动游戏）从这里取。</summary>
    public static Window? MainWindow { get; private set; }

    internal static void Initialize(CommandService commands, DocumentService documents,
        WorkbenchLayoutState layout, Window mainWindow)
    {
        Commands = commands;
        Documents = documents;
        Layout = layout;
        MainWindow = mainWindow;
    }

    public static void SaveLayout() => WorkbenchLayoutStore.ScheduleSave(Layout);

    public static void FlushLayout() => WorkbenchLayoutStore.Flush(Layout);
}
