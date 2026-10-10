using MChub.Core.Classes.Config;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Module.Initialize;

namespace MChub.Mobile.Services;

/// <summary>
/// 移动端本机的游戏数据根目录。
///
/// <p><b>移动端不依赖桌面端</b>：装机后第一次打开就在设备上建好一套 MChub 布局
/// （<c>meta/</c> + <c>instances/</c>，即 <see cref="MinecraftFolderKind.MChubMc"/>），
/// 之后「新建实例」全程在本机下载安装，用户不需要有电脑。</p>
///
/// <p>目录优先落在 <c>Android/data/&lt;包名&gt;/files/Minecraft</c>：这是应用专属的外部目录，
/// 不需要任何存储权限，而且插上数据线就能看到，用户可以自己往里丢存档 / mod / 资源包。
/// 外部目录不可用时（极少数机型）退回到应用内部私有目录。</p>
/// </summary>
internal static class MobileGameStorage
{
    /// <summary>游戏数据根目录（绝对路径，已确保存在）。</summary>
    public static string RootDirectory { get; } = ResolveRootDirectory();

    /// <summary>实例根目录。</summary>
    public static string InstancesDirectory => Path.Combine(RootDirectory, "instances");

    private static string ResolveRootDirectory()
    {
        var context = Android.App.Application.Context;
        var baseDirectory = context.GetExternalFilesDir(null)?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = MobileRuntimePaths.AppDataDirectory;

        return Path.Combine(baseDirectory, "Minecraft");
    }

    /// <summary>
    /// 建好本机目录并登记到配置里（幂等）。返回可供安装使用的目录项。
    ///
    /// <p>目录必须同时存在 <c>meta/</c> 与 <c>instances/</c> 才会被识别成 MChubMc 布局 ——
    /// 少了 <c>meta/</c>，安装器会把版本装进 <c>instances/</c> 里当传统布局处理，实例扫描也认不出来。</p>
    /// </summary>
    public static MinecraftFolderEntry EnsureRegistered()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(Path.Combine(RootDirectory, "meta"));
        Directory.CreateDirectory(InstancesDirectory);

        var folders = Data.ConfigEntry.MinecraftFolders;
        var normalized = Normalize(RootDirectory);
        var existing = folders.FirstOrDefault(folder => Normalize(folder.FolderPath) == normalized);
        if (existing is not null)
            return existing;

        var entry = new MinecraftFolderEntry
        {
            FolderName = "MChub",
            FolderPath = RootDirectory,
            // 显式声明布局：目录刚建出来时可能是空的，自动探测会判成 Unknown。
            FolderKind = MinecraftFolderKind.MChubMc
        };
        folders.Add(entry);
        ConfigSaver.SaveConfig();
        return entry;
    }

    /// <summary>配置里已登记的目录项；没登记过（或路径变了）时返回 null。</summary>
    public static MinecraftFolderEntry? FindRegistered()
    {
        var normalized = Normalize(RootDirectory);
        return Data.ConfigEntry.MinecraftFolders
            .FirstOrDefault(folder => Normalize(folder.FolderPath) == normalized);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
