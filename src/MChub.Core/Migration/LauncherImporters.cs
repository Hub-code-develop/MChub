using System.Text.Json;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;

namespace MChub.Core.Migration;

/// <summary>
/// PCL（Plain Craft Launcher）账号导入。
///
/// 来源：Meloong-Git/PCL 的 `Modules/Minecraft/ModLaunch.vb`、`Pages/PageLaunch/PageLoginMs.xaml.vb`：
///   - `LoginMsJson` : `{"玩家名":"OAuthRefreshToken", ...}`  ← 微软账号，**存的是 refresh token**
///   - `LoginLegacyName` : `"玩家名¨UUID"`（离线）
///   - `LoginType` : Ms / Legacy / Auth / Nide ...
/// 配置文件在 PCL 目录下的 `Setup.ini`（启动器级），版本级的在 `versions\&lt;ver&gt;\PCL\Setup.ini`。
/// </summary>
internal static class PclImporter
{
    public static int Import(MigrationSourceInfo info, MigrationResult result)
    {
        var config = FindConfig(info);
        if (config is null)
        {
            result.Warnings.Add("没找到 PCL 的 Setup.ini（PCL 只在 Windows 上有原生版）。");
            return 0;
        }

        var entries = ReadIni(config);
        var imported = 0;

        if (entries.TryGetValue("LoginMsJson", out var msJson) && !string.IsNullOrWhiteSpace(msJson))
        {
            try
            {
                using var document = JsonDocument.Parse(msJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        var name = property.Name?.Trim();
                        var refreshToken = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(refreshToken))
                            continue;
                        if (AccountExists(AccountType.Microsoft, name))
                            continue;

                        Data.ConfigEntry.MinecraftAccounts.Add(new MinecraftAccount(AccountType.Microsoft)
                        {
                            Name = name,
                            RefreshToken = refreshToken,
                            CreateAt = DateTime.Now,
                            LastRefreshTime = null // 交给 AccountRefresher 首次使用时刷新
                        });
                        imported++;
                    }
                }
            }
            catch (Exception e)
            {
                result.Warnings.Add($"PCL 微软账号解析失败：{e.Message}");
            }
        }

        if (entries.TryGetValue("LoginLegacyName", out var legacy) && !string.IsNullOrWhiteSpace(legacy))
        {
            var parts = legacy.Split('¨', StringSplitOptions.RemoveEmptyEntries);
            var name = parts.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !AccountExists(AccountType.Offline, name))
            {
                Data.ConfigEntry.MinecraftAccounts.Add(new MinecraftAccount(AccountType.Offline)
                {
                    Name = name,
                    Uuid = parts.Length > 1 ? TryParseUuid(parts[1]) : null,
                    CreateAt = DateTime.Now,
                    LastLoginTime = DateTime.Now
                });
                imported++;
            }
        }

        result.Warnings.Add($"PCL 账号来源：{config}");
        return imported;
    }

    private static string? FindConfig(MigrationSourceInfo info)
    {
        var candidates = new List<string>
        {
            Path.Combine(info.RootPath, "PCL", "Setup.ini"),
            Path.Combine(info.RootPath, "Setup.ini"),
            Path.Combine(info.RootPath, "PCL.ini")
        };

        // PCL 启动器本体目录（Windows）。macOS/Linux 上一般不存在，探一下无妨。
        foreach (var baseDir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                 })
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                continue;
            candidates.Add(Path.Combine(baseDir, "PCL", "Setup.ini"));
            candidates.Add(Path.Combine(baseDir, "PCL.ini"));
        }

        candidates.Add(Path.Combine(info.RootPath, "..", "PCL", "Setup.ini"));

        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;
                // 必须真的像 PCL 配置：含 Login 相关键才认
                var text = File.ReadAllText(candidate);
                if (text.Contains("LoginMsJson", StringComparison.Ordinal) ||
                    text.Contains("LoginLegacyName", StringComparison.Ordinal) ||
                    text.Contains("LoginType", StringComparison.Ordinal))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // 读不了就跳过，换下一个候选
            }
        }

        return null;
    }

    /// <summary>极简 INI 读取：key=value，忽略 [Section]，键名不区分大小写。</summary>
    private static Dictionary<string, string> ReadIni(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
                continue;

            var index = line.IndexOf('=');
            if (index <= 0)
                continue;

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (key.Length > 0)
                map[key] = value;
        }

        return map;
    }

    private static bool AccountExists(AccountType type, string name)
        => Data.ConfigEntry.MinecraftAccounts.Any(account =>
            account.AccountType == type && string.Equals(account.Name, name, StringComparison.Ordinal));

    private static Guid? TryParseUuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Guid.TryParse(raw.Trim().Replace("-", string.Empty), out var parsed) ? parsed : null;
    }
}

/// <summary>
/// SJMCL 账号导入。
///
/// 来源：UNIkeEN/SJMCL（Tauri）`src-tauri/src/account/models.rs` + `constants.rs`：
///   账号文件 `sjmcl.account.json`，位于 Tauri 的 app data 目录（identifier = "SJMCL"）：
///     macOS   ~/Library/Application Support/SJMCL
///     Windows %APPDATA%\SJMCL
///     Linux   ~/.local/share/SJMCL
///   结构：<c>{ "players": [ { id, name, uuid, playerType, accessToken, refreshToken, authServerUrl, ... } ] }</c>
///   playerType: "microsoft" | "offline" | "3rdparty"
/// </summary>
internal static class SjmclImporter
{
    public static int Import(MigrationResult result)
    {
        var file = FindAccountsFile();
        if (file is null)
        {
            result.Warnings.Add("没找到 SJMCL 的 sjmcl.account.json。");
            return 0;
        }

        var imported = 0;
        try
        {
            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);

            if (!TryGetPlayers(document.RootElement, out var players))
            {
                result.Warnings.Add($"SJMCL 账号文件结构不认识：{file}");
                return 0;
            }

            foreach (var player in players.EnumerateArray())
            {
                if (player.ValueKind != JsonValueKind.Object)
                    continue;

                var name = GetString(player, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var playerType = GetString(player, "playerType") ?? "offline";
                var refreshToken = GetString(player, "refreshToken");
                var accessToken = GetString(player, "accessToken");
                var uuid = TryParseUuid(GetString(player, "uuid"));

                switch (playerType.ToLowerInvariant())
                {
                    case "microsoft":
                        if (string.IsNullOrWhiteSpace(refreshToken) || AccountExists(AccountType.Microsoft, name))
                            continue;
                        Data.ConfigEntry.MinecraftAccounts.Add(new MinecraftAccount(AccountType.Microsoft)
                        {
                            Name = name,
                            Uuid = uuid,
                            RefreshToken = refreshToken,
                            AccessToken = accessToken,
                            CreateAt = DateTime.Now,
                            LastRefreshTime = null
                        });
                        imported++;
                        break;

                    case "3rdparty":
                        if (AccountExists(AccountType.Yggdrasil, name))
                            continue;
                        Data.ConfigEntry.MinecraftAccounts.Add(new MinecraftAccount(AccountType.Yggdrasil)
                        {
                            Name = name,
                            Uuid = uuid,
                            AccessToken = accessToken,
                            YggdrasilServerUrl = GetString(player, "authServerUrl"),
                            AccountNote = GetString(player, "authServerUrl"),
                            CreateAt = DateTime.Now,
                            LastRefreshTime = null
                        });
                        imported++;
                        break;

                    default:
                        if (AccountExists(AccountType.Offline, name))
                            continue;
                        Data.ConfigEntry.MinecraftAccounts.Add(new MinecraftAccount(AccountType.Offline)
                        {
                            Name = name,
                            Uuid = uuid,
                            AccessToken = accessToken,
                            CreateAt = DateTime.Now,
                            LastLoginTime = DateTime.Now
                        });
                        imported++;
                        break;
                }
            }
        }
        catch (Exception e)
        {
            result.Warnings.Add($"SJMCL 账号解析失败：{e.Message}");
        }

        result.Warnings.Add($"SJMCL 账号来源：{file}");
        return imported;
    }

    /// <summary>兼容两种包法：根对象直接有 players，或包在 data/value 里。</summary>
    private static bool TryGetPlayers(JsonElement root, out JsonElement players)
    {
        players = default;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("players", out players) && players.ValueKind == JsonValueKind.Array)
            return true;

        foreach (var wrapper in new[] { "data", "value", "account" })
        {
            if (root.TryGetProperty(wrapper, out var inner) &&
                inner.ValueKind == JsonValueKind.Object &&
                inner.TryGetProperty("players", out players) &&
                players.ValueKind == JsonValueKind.Array)
                return true;
        }

        return false;
    }

    private static string? FindAccountsFile()
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
                roots.Add(Path.Combine(appData, "SJMCL"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            roots.Add(Path.Combine(home, "Library", "Application Support", "SJMCL"));
            roots.Add(Path.Combine(home, "Library", "Application Support", "sjmcl"));
        }
        else
        {
            roots.Add(Path.Combine(home, ".local", "share", "SJMCL"));
            roots.Add(Path.Combine(home, ".config", "SJMCL"));
            roots.Add(Path.Combine(home, ".local", "share", "sjmcl"));
        }

        foreach (var root in roots)
        {
            var file = Path.Combine(root, "sjmcl.account.json");
            if (File.Exists(file))
                return Path.GetFullPath(file);
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool AccountExists(AccountType type, string name)
        => Data.ConfigEntry.MinecraftAccounts.Any(account =>
            account.AccountType == type && string.Equals(account.Name, name, StringComparison.Ordinal));

    private static Guid? TryParseUuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Guid.TryParse(raw.Trim().Replace("-", string.Empty), out var parsed) ? parsed : null;
    }
}
