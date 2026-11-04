using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MChub.Core.Minecraft.Classes;

namespace MChub.Core.Minecraft.Instance;

/// <summary>
/// 自动发现 Minecraft 目录。目标是「用户什么都不用配」：
/// 先看各平台的约定位置，再在用户主目录里做一次有深度上限的遍历，
/// 找到能识别成 Minecraft 布局的目录（标准 .minecraft、Prism/MultiMC、
/// Modrinth、CurseForge、MChub 自己的目录）就收进来。
///
/// 不做全盘递归（那会把整块盘读穿），深度上限 4 层并跳过缓存/媒体/开发目录，
/// 覆盖到 99% 的安装方式，同时把耗时压在可接受范围内。
/// </summary>
public static class MinecraftFolderDiscovery
{
    private const int MaxDepth = 4;

    /// <summary>遍历时直接跳过的目录名，避免读缓存和媒体库。</summary>
    private static readonly HashSet<string> DeniedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Library", "node_modules", ".git", ".svn", ".hg", ".Trash", ".cache", ".npm", ".gradle",
        ".m2", ".vscode", ".idea", ".conda", ".nuget", ".cargo", ".rustup", "Pictures", "Movies",
        "Music", "Photos Library.photoslibrary", "Caches", "Containers", "Group Containers",
        "AppData", "Application Data", "Temp", "$RECYCLE.BIN", "System Volume Information"
    };

    public sealed record Found(string FolderPath, string DisplayName);

    public sealed record Progress(int Index, int Total, string CurrentRoot, int Found);

    /// <summary>各平台约定位置。命中就不必再遍历了。</summary>
    public static IReadOnlyList<string> GetWellKnownRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<string>();

        void Add(params string[] parts)
        {
            var path = Path.Combine(parts);
            if (Directory.Exists(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase))
                list.Add(path);
        }

        if (OperatingSystem.IsMacOS())
        {
            var support = Path.Combine(home, "Library", "Application Support");
            Add(Path.Combine(support, "minecraft"));
            Add(Path.Combine(home, ".minecraft"));
            Add(Path.Combine(support, "PrismLauncher"));
            Add(Path.Combine(support, "ModrinthApp"));
            Add(Path.Combine(support, "com.modrinth.theseus"));
            Add(Path.Combine(support, "CurseForge"));
            Add(Path.Combine(support, "multimc"));
            Add(Path.Combine(support, "MultiMC"));
            Add(Path.Combine(support, "PCL"));
            Add(Path.Combine(support, "HMCL"));
        }
        else if (OperatingSystem.IsWindows())
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Add(Path.Combine(roaming, ".minecraft"));
            Add(Path.Combine(roaming, "PrismLauncher"));
            Add(Path.Combine(roaming, "ModrinthApp"));
            Add(Path.Combine(roaming, "com.modrinth.theseus"));
            Add(Path.Combine(roaming, "PCL"));
            Add(Path.Combine(roaming, "HMCL"));
            Add(Path.Combine(roaming, ".minecraft"));
            Add(Path.Combine(home, "curseforge", "minecraft"));
            Add(Path.Combine(local, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                "LocalState", "games", "com.mojang"));
        }
        else
        {
            var share = Path.Combine(home, ".local", "share");
            Add(Path.Combine(home, ".minecraft"));
            Add(Path.Combine(share, "PrismLauncher"));
            Add(Path.Combine(share, "multimc"));
            Add(Path.Combine(share, "ModrinthApp"));
            Add(Path.Combine(share, "com.modrinth.theseus"));
            Add(Path.Combine(share, "CurseForge"));
        }

        return list;
    }

    /// <summary>
    /// 发现所有可用的 Minecraft 目录。会去重并剔除互相嵌套的条目
    /// （比如已经收下了 .minecraft，就不会再收它下面的 versions）。
    /// </summary>
    public static Task<List<Found>> DiscoverAsync(IProgress<Progress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Discover(progress, cancellationToken), cancellationToken);

    private static List<Found> Discover(IProgress<Progress>? progress, CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();

        // 1) 约定位置
        candidates.AddRange(GetWellKnownRoots());

        // 2) 主目录遍历
        var walkHits = new List<string>();
        try
        {
            Walk(home, 0, walkHits, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 某个子树读不动就跳过，不影响整体
        }

        candidates.AddRange(walkHits);

        // 3) 逐个确认，能在 Detect 里识别出布局的才算数
        var results = new List<Found>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = Math.Max(1, candidates.Count);
        var index = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            string full;
            try
            {
                full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            if (!seen.Add(full))
                continue;

            var layout = MinecraftFolderLayout.Detect(full);
            if (layout.Kind == MinecraftFolderKind.Unknown)
                continue;

            // 已经被更上层的目录覆盖了就跳过（根目录优先）
            if (results.Any(item => full.StartsWith(item.FolderPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new Found(full, DescribeName(full)));
            progress?.Report(new Progress(index, total, full, results.Count));
        }

        progress?.Report(new Progress(total, total, string.Empty, results.Count));
        return results;
    }

    /// <summary>给目录起一个用户看得懂的名字，重名的补上父级。</summary>
    private static string DescribeName(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
            return path;

        if (name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
            return "Minecraft（默认目录）";

        return name;
    }

    private static void Walk(string dir, int depth, List<string> hits, CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(dir);
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name;
            try
            {
                name = Path.GetFileName(child);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(name) || name.StartsWith('.') && name != ".minecraft")
                continue;
            if (DeniedNames.Contains(name))
                continue;

            if (LooksLikeMinecraft(child))
            {
                hits.Add(child);
                continue; // 命中就不再往下钻，避免把 versions 也当成一个根
            }

            Walk(child, depth + 1, hits, cancellationToken);
        }
    }

    private static bool LooksLikeMinecraft(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            if (name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Directory.Exists(Path.Combine(path, "versions")))
                return true;

            // 多实例启动器：根目录下有一个 instances，里面才是各实例
            if (Directory.Exists(Path.Combine(path, "instances")))
                return MinecraftFolderLayout.Detect(path).Kind != MinecraftFolderKind.Unknown;

            return MinecraftFolderLayout.Detect(path).Kind is MinecraftFolderKind.MChubMc;
        }
        catch
        {
            return false;
        }
    }
}
