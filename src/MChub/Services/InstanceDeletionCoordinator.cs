using MChub.Core.Minecraft.Classes;
using Tio.Avalonia.Standard.Tab.Interface;

namespace MChub.Services;

public static class InstanceDeletionCoordinator
{
    private static readonly HashSet<string> DeletingPaths = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryBegin(MinecraftInstance instance)
    {
        lock (DeletingPaths)
        {
            return DeletingPaths.Add(NormalizePath(instance.InstanceFolderPath));
        }
    }

    public static void Complete(MinecraftInstance instance)
    {
        lock (DeletingPaths)
        {
            DeletingPaths.Remove(NormalizePath(instance.InstanceFolderPath));
        }
    }

    public static bool IsDeleting(MinecraftInstance instance)
    {
        lock (DeletingPaths)
        {
            return DeletingPaths.Contains(NormalizePath(instance.InstanceFolderPath));
        }
    }

    public static void CloseRelatedPages(MinecraftInstance instance)
    {
        // 旧实现会关掉指向该实例的标签页。新工作台的文档系统会按资源路径回收，
        // 这里等 P2 文档系统接入后再补，目前只保留实例本身的删除逻辑。
    }

    private static bool Matches(MinecraftInstance left, MinecraftInstance right)
    {
        return string.Equals(NormalizePath(left.InstanceFolderPath), NormalizePath(right.InstanceFolderPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}