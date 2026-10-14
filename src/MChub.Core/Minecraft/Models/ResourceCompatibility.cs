using Iridium.Enums;
using MChub.Core.Minecraft.Classes;
using MChub.Localization;

namespace MChub.Core.Minecraft.Models;

public static class ResourceCompatibility
{
    public static IReadOnlyList<string> GetInstalledLoaders(MinecraftInstance instance)
    {
        if (instance.Type != MinecraftInstanceType.Java ||
            instance.MinecraftEntry is not { } entry)
            return [];

        return entry.Loaders.Select(loader => LoaderName(loader.Type))
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsCompatible(ResourceVersionFileItem file, MinecraftInstance instance, ResourceKind kind)
    {
        if (!file.MinecraftVersions.Contains(instance.VersionId, StringComparer.OrdinalIgnoreCase))
            return false;
        if (kind != ResourceKind.Mod)
            return true;

        var fileLoaders = file.GroupKeys.Select(key => key.Loader)
            .Where(loader => loader != LinguaSentinels.UniversalLoader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fileLoaders.Count == 0)
            return true;

        return GetInstalledLoaders(instance).Any(loader => fileLoaders.Contains(loader));
    }

    private static string? LoaderName(LoaderType loader)
    {
        return loader switch
        {
            LoaderType.NeoForge => "neoforge",
            LoaderType.Forge => "forge",
            LoaderType.Fabric => "fabric",
            LoaderType.Quilt => "quilt",
            // Modrinth 上确有这些装载器的分类（Babric / BTA 没有，返回 null 即视为不参与匹配）。
            LoaderType.Ornithe => "ornithe",
            LoaderType.LegacyFabric => "legacy-fabric",
            LoaderType.Rift => "rift",
            _ => null
        };
    }
}
