using System.Text.Json;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;

namespace MChub.Core.Migration;

/// <summary>可迁移的启动器来源。</summary>
public enum MigrationSource
{
    /// <summary>官方启动器 / 原版 .minecraft 目录。</summary>
    OfficialLauncher,

    /// <summary>PCL（Plain Craft Launcher）。</summary>
    Pcl,

    /// <summary>SJMCL。</summary>
    Sjmcl
}

/// <summary>扫到的一个候选来源。</summary>
public sealed class MigrationSourceInfo
{
    public required MigrationSource Source { get; init; }
    public required string DisplayName { get; init; }
    public required string RootPath { get; init; }

    public bool Exists => Directory.Exists(RootPath);

    public string VersionsPath => Path.Combine(RootPath, "versions");
    public string SavesPath => Path.Combine(RootPath, "saves");
    public string ResourcePacksPath => Path.Combine(RootPath, "resourcepacks");
    public string ShaderPacksPath => Path.Combine(RootPath, "shaderpacks");
    public string OptionsPath => Path.Combine(RootPath, "options.txt");

    public int VersionCount => CountDirectories(VersionsPath);
    public int SaveCount => CountDirectories(SavesPath);
    public int ResourcePackCount => CountFiles(ResourcePacksPath, "*.zip");
    public bool HasOptions => File.Exists(OptionsPath);

    /// <summary>有没有可以直接读懂并导入的账号文件（目前只有官方启动器）。</summary>
    public bool HasImportableAccounts => File.Exists(OfficialAccountsPath);

    internal string OfficialAccountsPath => Path.Combine(RootPath, "launcher_accounts.json");

    private static int CountDirectories(string path)
        => Directory.Exists(path) ? Directory.GetDirectories(path).Length : 0;

    private static int CountFiles(string path, string pattern)
        => Directory.Exists(path) ? Directory.GetFiles(path, pattern).Length : 0;
}

/// <summary>导入结果。</summary>
public sealed class MigrationResult
{
    public required MigrationSourceInfo Source { get; init; }
    public bool FolderRegistered { get; set; }
    public int AccountsImported { get; set; }
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// 从其它启动器迁移。
///
/// 设计取向：**不搬运大文件**。PCL / SJMCL / 官方启动器用的都是同一套 .minecraft 布局，
/// 所以「版本与实例、存档、资源、设置」通过把已有目录注册成 MChub 游戏目录即可全部带上；
/// 只有账号需要单独读文件导入（而且微软账号拿不到 refresh token，必须重新登录一次）。
/// </summary>
public static class MigrationService
{
    /// <summary>扫描本机常见的三类来源。</summary>
    public static IReadOnlyList<MigrationSourceInfo> Scan()
    {
        var list = new List<MigrationSourceInfo>();

        foreach (var candidate in EnumerateOfficialRoots())
            list.Add(new MigrationSourceInfo
            {
                Source = MigrationSource.OfficialLauncher,
                DisplayName = "官方启动器",
                RootPath = candidate
            });

        foreach (var candidate in EnumeratePclRoots())
            list.Add(new MigrationSourceInfo
            {
                Source = MigrationSource.Pcl,
                DisplayName = "PCL",
                RootPath = candidate
            });

        foreach (var candidate in EnumerateSjmclRoots())
            list.Add(new MigrationSourceInfo
            {
                Source = MigrationSource.Sjmcl,
                DisplayName = "SJMCL",
                RootPath = candidate
            });

        // 只保留真实存在、并且确实像 Minecraft 目录的（至少有 versions 或 options.txt）
        return list
            .Where(info => info.Exists &&
                           (Directory.Exists(info.VersionsPath) || info.HasOptions))
            .GroupBy(info => Path.GetFullPath(info.RootPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>
    /// 导入：把来源目录注册成 MChub 游戏目录，并尽力导入账号。
    /// </summary>
    public static async Task<MigrationResult> ImportAsync(MigrationSourceInfo info,
        CancellationToken cancellationToken = default)
    {
        var result = new MigrationResult { Source = info };

        if (!info.Exists)
        {
            result.Warnings.Add($"目录不存在：{info.RootPath}");
            return result;
        }

        var fullPath = Path.GetFullPath(info.RootPath);
        var already = Data.ConfigEntry.MinecraftFolders
            .Any(folder => string.Equals(SafeFullPath(folder.FolderPath), fullPath, StringComparison.OrdinalIgnoreCase));

        if (!already)
        {
            Data.ConfigEntry.MinecraftFolders.Add(new MinecraftFolderEntry
            {
                FolderPath = fullPath,
                FolderName = $"{info.DisplayName}（迁移）"
            });
            result.FolderRegistered = true;
        }

        result.AccountsImported = info.Source switch
        {
            MigrationSource.Pcl => PclImporter.Import(info, result),
            MigrationSource.Sjmcl => SjmclImporter.Import(result),
            _ => await ImportOfficialAccountsAsync(info, result, cancellationToken)
        };

        return result;
    }

    /// <summary>读官方启动器的 launcher_accounts.json 并导入可用的账号。</summary>
    private static async Task<int> ImportOfficialAccountsAsync(MigrationSourceInfo info, MigrationResult result,
        CancellationToken cancellationToken)
    {
        if (!info.HasImportableAccounts)
            return 0;

        var imported = 0;
        try
        {
            await using var stream = File.OpenRead(info.OfficialAccountsPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("accounts", out var accounts) ||
                accounts.ValueKind != JsonValueKind.Object)
                return 0;

            foreach (var property in accounts.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = property.Value;
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                var accessToken = GetString(entry, "accessToken");
                var name = GetString(entry, "username");
                var type = GetString(entry, "type");

                if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(name))
                    continue;

                // 官方启动器只放过 access token，没有 refresh token。这种账号过几小时就失效，
                // 也不满足启动闸门（闸门要求可刷新），所以不导入，避免造出用不了的账号。
                if (!string.Equals(type, "Offline", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"微软账号 {name} 需要重新登录一次（官方启动器不保存可刷新凭证）。");
                    continue;
                }

                var alreadyExists = Data.ConfigEntry.MinecraftAccounts.Any(account =>
                    account.AccountType == AccountType.Offline &&
                    string.Equals(account.Name, name, StringComparison.Ordinal));

                if (alreadyExists)
                    continue;

                Data.ConfigEntry.MinecraftAccounts.Add(new MinecraftAccount(AccountType.Offline)
                {
                    Name = name,
                    Uuid = TryParseUuid(GetString(entry, "uuid")),
                    AccessToken = accessToken,
                    CreateAt = DateTime.Now,
                    LastLoginTime = DateTime.Now
                });
                imported++;
            }
        }
        catch (Exception e)
        {
            result.Warnings.Add($"读取账号文件失败：{e.Message}");
        }

        return imported;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? TryParseUuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Guid.TryParse(raw.Replace("-", string.Empty), out var parsed) ? parsed : null;
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

    private static IEnumerable<string> EnumerateOfficialRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                yield return Path.Combine(appData, ".minecraft");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "minecraft");
        }
        else
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");
        }
    }

    private static IEnumerable<string> EnumeratePclRoots()
    {
        // PCL 本身沿用 .minecraft；PCL2 还会把自己的配置放在 PCL 目录下。
        // 这里两种都探一下，真正的游戏目录由 Scan() 的过滤器筛。
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrEmpty(appData))
            {
                yield return Path.Combine(appData, ".minecraft");
                yield return Path.Combine(appData, "PCL", ".minecraft");
            }

            if (!string.IsNullOrEmpty(userProfile))
                yield return Path.Combine(userProfile, ".minecraft");
        }
    }

    private static IEnumerable<string> EnumerateSjmclRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                yield return Path.Combine(appData, "SJMCL", ".minecraft");
                yield return Path.Combine(appData, "SJMCL");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(userProfile, "Library", "Application Support", "SJMCL", ".minecraft");
            yield return Path.Combine(userProfile, "Library", "Application Support", "SJMCL");
        }
        else
        {
            yield return Path.Combine(userProfile, ".local", "share", "SJMCL", ".minecraft");
            yield return Path.Combine(userProfile, ".config", "SJMCL");
        }
    }
}
