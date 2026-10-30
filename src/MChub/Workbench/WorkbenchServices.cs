using MChub.Workbench.Commands;
using MChub.Workbench.Documents;

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

    internal static void Initialize(CommandService commands, DocumentService documents)
    {
        Commands = commands;
        Documents = documents;
    }
}
