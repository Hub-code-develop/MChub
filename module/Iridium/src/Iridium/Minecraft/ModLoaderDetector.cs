using System.Collections.Frozen;
using System.Text.Json;
using Iridium.Enums;
using Iridium.Models.Minecraft;

namespace Iridium.Minecraft;

/// <summary>
/// Detects mod loaders from a version manifest's Maven library coordinates or from
/// Prism-style component UIDs.
/// </summary>
public static class ModLoaderDetector {
    private static readonly FrozenDictionary<string, (LoaderType Type, Func<string, string> ParseVersion)> LibraryPatterns =
        new Dictionary<string, (LoaderType, Func<string, string>)>(StringComparer.OrdinalIgnoreCase) {
            { "net.minecraftforge:forge:", (LoaderType.Forge, ForgeVersion) },
            { "net.minecraftforge:fmlloader:", (LoaderType.Forge, ForgeVersion) },
            { "net.minecraftforge:fmlcore:", (LoaderType.Forge, ForgeVersion) },
            { "net.neoforged.fancymodloader:loader:", (LoaderType.NeoForge, static v => v) },
            { "net.neoforged:neoforge:", (LoaderType.NeoForge, static v => v) },
            { "optifine:optifine", (LoaderType.Optifine, AfterFirstUnderscoreUpper) },
            { "net.fabricmc:fabric-loader:", (LoaderType.Fabric, static v => v) },
            { "net.fabricmc:fabric-api:", (LoaderType.Fabric, static v => v) },
            { "net.ornithemc:ornithe-loader:", (LoaderType.Ornithe, static v => v) },
            { "net.legacyfabric:intermediary:", (LoaderType.LegacyFabric, static v => v) },
            { "babric:intermediary-upstream:", (LoaderType.Babric, static v => v) },
            { "org.dimdev:rift:", (LoaderType.Rift, static v => v) },
            { "com.mumfrey:liteloader:", (LoaderType.LiteLoader, static v => v) },
            { "org.quiltmc:quilt-loader:", (LoaderType.Quilt, static v => v) },
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, LoaderType> ComponentUids =
        new Dictionary<string, LoaderType>(StringComparer.OrdinalIgnoreCase) {
            { "net.minecraftforge", LoaderType.Forge },
            { "net.neoforged", LoaderType.NeoForge },
            { "net.fabricmc.fabric-loader", LoaderType.Fabric },
            { "org.quiltmc.quilt-loader", LoaderType.Quilt },
            { "net.optifine", LoaderType.Optifine },
            { "com.mumfrey.liteloader", LoaderType.LiteLoader },
        }.ToFrozenDictionary();

    public static List<MinecraftLoader> DetectFromLibraries(JsonElement libraries) =>
        DetectFromLibraries(libraries, []);

    public static List<MinecraftLoader> DetectFromLibraries(JsonElement libraries, IReadOnlyList<MinecraftLoader> existing) {
        var loaders = new List<MinecraftLoader>(existing);
        var seen = new HashSet<string>(loaders.Select(l => $"{l.Type}:{l.Version}"));
        if (libraries.ValueKind != JsonValueKind.Array)
            return loaders;

        foreach (var library in libraries.EnumerateArray()) {
            if (library.ValueKind != JsonValueKind.Object
                || !library.TryGetProperty("name", out var nameElement)
                || nameElement.GetString() is not { Length: > 0 } libName)
                continue;

            var loader = DetectFromLibraryName(libName);
            if (loader is null || !seen.Add($"{loader.Type}:{loader.Version}"))
                continue;

            loaders.Add(loader);
        }

        return loaders;
    }

    public static bool TryMapComponentUid(string uid, out LoaderType type)
        => ComponentUids.TryGetValue(uid, out type);

    private static MinecraftLoader? DetectFromLibraryName(string libName) {
        foreach (var (pattern, (type, parseVersion)) in LibraryPatterns) {
            if (!libName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = libName.Split(':');
            if (parts.Length < 3 || parts[2].Length == 0)
                return null;

            return new MinecraftLoader { Type = type, Version = parseVersion(parts[2]) };
        }

        return null;
    }

    private static string ForgeVersion(string version) {
        var parts = version.Split('-');
        return parts.Length > 1 ? parts[1] : version;
    }

    private static string AfterFirstUnderscoreUpper(string version) {
        var index = version.IndexOf('_');
        return (index >= 0 ? version[(index + 1)..] : version).ToUpperInvariant();
    }
}
