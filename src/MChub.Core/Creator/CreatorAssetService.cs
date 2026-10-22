using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MChub.Core.Const;

namespace MChub.Core.Creator;

/// <summary>一张截图。</summary>
public sealed record ScreenshotEntry(string FilePath, DateTime TakenAt, long SizeBytes);

/// <summary>一个模组（从 jar 里读出来的元数据）。</summary>
public sealed record ModEntry(string Name, string Version, string FileName, bool Enabled);

/// <summary>一个实例的素材概况。</summary>
public sealed class InstanceAssets
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public List<ModEntry> Mods { get; } = [];
    public int ResourcePackCount { get; set; }
    public int ShaderPackCount { get; set; }
    public int ScreenshotCount { get; set; }

    public int EnabledModCount => Mods.Count(mod => mod.Enabled);
}

/// <summary>
/// 创作台的数据来源：截图库、模组清单。
///
/// 全部只读本地文件，不联网。设计目标是「拍完/装完就能直接产出视频简介」，
/// 所以模组清单会直接吐 Markdown，粘到简介里即可。
/// </summary>
public static class CreatorAssetService
{
    /// <summary>扫描所有游戏目录里的截图。</summary>
    public static IReadOnlyList<ScreenshotEntry> FindScreenshots()
    {
        var results = new List<ScreenshotEntry>();
        foreach (var folder in SafeFolders())
        {
            CollectScreenshots(Path.Combine(folder, "screenshots"), results);
            foreach (var instance in EnumerateInstanceDirectories(folder))
                CollectScreenshots(Path.Combine(instance, "screenshots"), results);
        }

        return results
            .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(entry => entry.TakenAt)
            .ToList();
    }

    /// <summary>列出可分析的实例（含 mods / resourcepacks / shaderpacks / screenshots 的目录）。</summary>
    public static IReadOnlyList<string> FindInstanceDirectories()
    {
        var results = new List<string>();
        foreach (var folder in SafeFolders())
        {
            if (HasAssets(folder))
                results.Add(folder);
            results.AddRange(EnumerateInstanceDirectories(folder).Where(HasAssets));
        }

        return results
            .Select(SafeFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>读取一个实例的素材概况（模组名/版本、资源包与光影数量）。</summary>
    public static InstanceAssets ReadInstance(string instancePath)
    {
        var assets = new InstanceAssets
        {
            Name = new DirectoryInfo(instancePath).Name,
            Path = instancePath
        };

        foreach (var jar in EnumerateModJars(Path.Combine(instancePath, "mods")))
        {
            var enabled = jar.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            assets.Mods.Add(ReadMod(jar, enabled));
        }

        assets.Mods.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        assets.ResourcePackCount = CountEntries(Path.Combine(instancePath, "resourcepacks"));
        assets.ShaderPackCount = CountEntries(Path.Combine(instancePath, "shaderpacks"));
        assets.ScreenshotCount = SafeScreenshotCount(Path.Combine(instancePath, "screenshots"));
        return assets;
    }

    /// <summary>生成可直接贴进视频简介的 Markdown。</summary>
    public static string BuildMarkdown(InstanceAssets assets, bool includeDisabled = false)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## {assets.Name}");
        builder.AppendLine();
        builder.AppendLine($"- 模组：{assets.EnabledModCount}");
        if (assets.ResourcePackCount > 0) builder.AppendLine($"- 资源包：{assets.ResourcePackCount}");
        if (assets.ShaderPackCount > 0) builder.AppendLine($"- 光影：{assets.ShaderPackCount}");
        if (assets.ScreenshotCount > 0) builder.AppendLine($"- 截图：{assets.ScreenshotCount}");
        builder.AppendLine();

        var mods = assets.Mods.Where(mod => includeDisabled || mod.Enabled).ToList();
        if (mods.Count > 0)
        {
            builder.AppendLine("### 模组列表");
            builder.AppendLine();
            foreach (var mod in mods)
            {
                var version = string.IsNullOrWhiteSpace(mod.Version) ? string.Empty : $" `{mod.Version}`";
                var suffix = mod.Enabled ? string.Empty : "（已禁用）";
                builder.AppendLine($"- {mod.Name}{version}{suffix}");
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static ModEntry ReadMod(string path, bool enabled)
    {
        var fileName = Path.GetFileName(path);
        try
        {
            using var archive = ZipFile.OpenRead(path);

            var fabric = archive.GetEntry("fabric.mod.json");
            if (fabric is not null)
            {
                using var stream = fabric.Open();
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                var name = ReadString(root, "name") ?? ReadString(root, "id");
                var version = ReadString(root, "version");
                if (!string.IsNullOrWhiteSpace(name))
                    return new ModEntry(name!, version ?? string.Empty, fileName, enabled);
            }

            var quilt = archive.GetEntry("quilt.mod.json");
            if (quilt is not null)
            {
                using var stream = quilt.Open();
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.TryGetProperty("quilt_loader", out var loader))
                {
                    var name = ReadString(loader, "id");
                    var version = ReadString(loader, "version");
                    if (!string.IsNullOrWhiteSpace(name))
                        return new ModEntry(name!, version ?? string.Empty, fileName, enabled);
                }
            }

            foreach (var tomlName in new[] { "META-INF/neoforge.mods.toml", "META-INF/mods.toml" })
            {
                var toml = archive.GetEntry(tomlName);
                if (toml is null) continue;

                using var reader = new StreamReader(toml.Open());
                var text = reader.ReadToEnd();
                var displayName = MatchToml(text, "displayName");
                var modId = MatchToml(text, "modId");
                var version = MatchToml(text, "version");
                var name = displayName ?? modId;
                if (!string.IsNullOrWhiteSpace(name))
                    return new ModEntry(name!, version ?? string.Empty, fileName, enabled);
            }
        }
        catch
        {
            // 坏包/非 zip 直接退回文件名
        }

        return new ModEntry(Path.GetFileNameWithoutExtension(fileName), string.Empty, fileName, enabled);
    }

    private static string? MatchToml(string text, string key)
    {
        var match = Regex.Match(text, $@"^\s*{key}\s*=\s*""([^""]+)""", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<string> SafeFolders()
        => Data.ConfigEntry.MinecraftFolders
            .Select(folder => folder.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(SafeFullPath);

    private static IEnumerable<string> EnumerateInstanceDirectories(string folder)
    {
        foreach (var kind in new[] { "instances", "versions" })
        {
            var root = Path.Combine(folder, kind);
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
                yield return directory;
        }
    }

    private static bool HasAssets(string path)
        => Directory.Exists(Path.Combine(path, "mods"))
           || Directory.Exists(Path.Combine(path, "resourcepacks"))
           || Directory.Exists(Path.Combine(path, "shaderpacks"))
           || Directory.Exists(Path.Combine(path, "screenshots"));

    private static IEnumerable<string> EnumerateModJars(string modsPath)
    {
        if (!Directory.Exists(modsPath)) yield break;
        foreach (var file in Directory.EnumerateFiles(modsPath))
        {
            if (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }
    }

    private static void CollectScreenshots(string path, List<ScreenshotEntry> results)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path))
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var info = new FileInfo(file);
                results.Add(new ScreenshotEntry(info.FullName, info.LastWriteTime, info.Length));
            }
            catch
            {
                // 文件被占用就跳过
            }
        }
    }

    private static int CountEntries(string path)
        => Directory.Exists(path)
            ? Directory.EnumerateFiles(path).Count() + Directory.EnumerateDirectories(path).Count()
            : 0;

    private static int SafeScreenshotCount(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.png").Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
