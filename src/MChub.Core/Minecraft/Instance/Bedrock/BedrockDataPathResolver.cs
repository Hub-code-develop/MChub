using MChub.Bedrock.Standard.Manifest;
using MChub.Core.Minecraft.Classes;

namespace MChub.Core.Minecraft.Instance.Bedrock;

public static class BedrockDataPathResolver
{
    public static string GetDataRoot(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.BuildType == BedrockBuildType.UWP)
            return GetUwpLocalStateRoot(config);

        if (OperatingSystem.IsLinux())
            return GetLinuxProtonDataRoot(config);

        if (OperatingSystem.IsMacOS())
            return GetMacWineDataRoot(config);

        if (config.EnableIndependentInstance)
            return config.EnableLauncherSharedData
                ? GetInstanceSharedDataRoot(config)
                : GetMChubIsolationRoot(config.InstancePath);

        return config.EnableLauncherSharedData ? GetWindowsDataRoot(config) : GetMChubDataRoot();
    }

    private static string GetMChubIsolationRoot(string instancePath)
    {
        return Path.Combine(instancePath, "config", "MChub", "isolation");
    }

    private static string GetInstanceSharedDataRoot(BedrockInstanceConfig config)
    {
        return Path.Combine(config.InstancePath, GetBedrockFolderName(config));
    }

    private static string GetWindowsDataRoot(BedrockInstanceConfig config)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            GetBedrockFolderName(config));
    }

    private static string GetBedrockFolderName(BedrockInstanceConfig config)
    {
        return config.Type == BedrockInstanceReleaseType.Release ? "Minecraft Bedrock" : "Minecraft Bedrock Preview";
    }

    private static string GetLinuxProtonDataRoot(BedrockInstanceConfig config)
    {
        var prefix = Environment.GetEnvironmentVariable("PORTAL_BEDROCK_PREFIX");
        if (string.IsNullOrWhiteSpace(prefix))
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local",
                    "share");
            prefix = Path.Combine(dataHome, "MChub", "Bedrock", "proton-prefix");
        }

        return Path.Combine(Path.GetFullPath(prefix), "pfx", "drive_c", "users", "steamuser", "AppData",
            "Roaming", GetBedrockFolderName(config));
    }

    /// <summary>
    /// macOS 侧的 Wine 前缀布局与 Proton 不同：标准 Wine（GPTK / CrossOver / Whisky）没有
    /// 中间的 <c>pfx</c> 层，用户目录名也随前缀初始化时的用户名而定。
    /// </summary>
    private static string GetMacWineDataRoot(BedrockInstanceConfig config)
    {
        var prefix = Environment.GetEnvironmentVariable("MCHUB_BEDROCK_PREFIX");
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "MChub", "Bedrock", "wine-prefix");

        var users = Path.Combine(Path.GetFullPath(prefix), "drive_c", "users");
        var user = Directory.Exists(users)
            ? Directory.EnumerateDirectories(users)
                .Select(Path.GetFileName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) &&
                                        !string.Equals(name, "Public", StringComparison.OrdinalIgnoreCase))
            : null;
        user ??= Environment.UserName;

        return Path.Combine(users, user, "AppData", "Roaming", GetBedrockFolderName(config));
    }

    public static string GetMojangDataRoot(BedrockInstanceConfig config, string userId = "Shared")
    {
        var root = GetDataRoot(config);
        if (config.BuildType == BedrockBuildType.UWP)
            return Path.Combine(root, "games", "com.mojang");
        return Path.Combine(root, "Users", userId, "games", "com.mojang");
    }

    public static IReadOnlyList<string> GetWorldUserIds(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.BuildType == BedrockBuildType.UWP)
            return ["Shared"];

        var usersRoot = Path.Combine(GetDataRoot(config), "Users");
        if (!Directory.Exists(usersRoot))
            return ["Shared"];

        var userIds = Directory.EnumerateDirectories(usersRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => string.Equals(name, "Shared", StringComparison.OrdinalIgnoreCase))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!userIds.Contains("Shared", StringComparer.OrdinalIgnoreCase))
            userIds.Add("Shared");

        return userIds;
    }

    public static string GetWorldsFolder(BedrockInstanceConfig config, string userId)
    {
        return Path.Combine(GetMojangDataRoot(config, userId), "minecraftWorlds");
    }

    public static void EnsureMChubDataDirectories()
    {
        var mojangRoot = Path.Combine(GetMChubDataRoot(), "Users", "Shared", "games", "com.mojang");
        foreach (var folder in new[]
                 {
                     "behavior_packs", "minecraftpe", "minecraftWorlds", "resource_packs", "Screenshots", "skin_packs"
                 })
            Directory.CreateDirectory(Path.Combine(mojangRoot, folder));
    }

    private static string GetMChubDataRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hub.code.MChub", "Bedrock");
    }

    private static string GetUwpLocalStateRoot(BedrockInstanceConfig config)
    {
        var packagesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        var identityName = GetUwpIdentityName(config);
        if (Directory.Exists(packagesRoot))
        {
            var packageFolder = Directory.EnumerateDirectories(packagesRoot)
                .FirstOrDefault(path => Path.GetFileName(path).StartsWith(identityName + "_",
                    StringComparison.OrdinalIgnoreCase));
            if (packageFolder != null)
                return Path.Combine(packageFolder, "LocalState");
        }

        var packageFamilyName = config.Type == BedrockInstanceReleaseType.Preview
            ? "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe"
            : "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        return Path.Combine(packagesRoot, packageFamilyName, "LocalState");
    }

    private static string GetUwpIdentityName(BedrockInstanceConfig config)
    {
        try
        {
            return BedrockHelper.GetInstanceVersion(config.InstancePath).PackName;
        }
        catch (IOException)
        {
            return config.Type == BedrockInstanceReleaseType.Preview
                ? "Microsoft.MinecraftWindowsBeta"
                : "Microsoft.MinecraftUWP";
        }
        catch (InvalidDataException)
        {
            return config.Type == BedrockInstanceReleaseType.Preview
                ? "Microsoft.MinecraftWindowsBeta"
                : "Microsoft.MinecraftUWP";
        }
    }

    public static string GetFolder(BedrockInstanceConfig config, MinecraftSpecialFolder folder)
    {
        return folder switch
        {
            MinecraftSpecialFolder.InstanceFolder => config.InstancePath,
            MinecraftSpecialFolder.SavesFolder => GetWorldsFolder(config, "Shared"),
            MinecraftSpecialFolder.ResourcePacksFolder => Path.Combine(GetMojangDataRoot(config), "resource_packs"),
            MinecraftSpecialFolder.BehaviorPacksFolder => Path.Combine(GetMojangDataRoot(config), "behavior_packs"),
            MinecraftSpecialFolder.SkinPacksFolder => Path.Combine(GetMojangDataRoot(config), "skin_packs"),
            MinecraftSpecialFolder.WorldTemplatesFolder => Path.Combine(GetMojangDataRoot(config), "world_templates"),
            MinecraftSpecialFolder.DevelopmentResourcePacksFolder => Path.Combine(GetMojangDataRoot(config),
                "development_resource_packs"),
            MinecraftSpecialFolder.DevelopmentBehaviorPacksFolder => Path.Combine(GetMojangDataRoot(config),
                "development_behavior_packs"),
            MinecraftSpecialFolder.ScreenshotsFolder => Path.Combine(GetMojangDataRoot(config), "Screenshots"),
            MinecraftSpecialFolder.ConfigFolder => Path.Combine(config.InstancePath, BedrockHelper.ConfigFolder),
            _ => config.InstancePath
        };
    }
}