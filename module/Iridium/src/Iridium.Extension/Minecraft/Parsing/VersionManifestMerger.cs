using System.Text.Json;
using Iridium.Enums;
using Iridium.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Extension.Minecraft.Parsing;

/// <summary>
/// Merged view of a Mojang-style version manifest after walking its
/// <c>inheritsFrom</c> chain. Child layers override scalar fields (main class,
/// arguments, asset index, ...) while libraries and loaders accumulate, so vanilla
/// metadata comes first and loader metadata is layered on top.
/// </summary>
public sealed class MergedVersionManifest {
    public string MinecraftVersion { get; set; } = string.Empty;
    public string? MainClass { get; set; }
    public string? MinecraftArguments { get; set; }
    public MinecraftArguments Arguments { get; set; } = new();
    public List<MinecraftLibrary> Libraries { get; } = [];
    public int? RequiredJavaVersion { get; set; }
    public AssetIndex? AssetIndex { get; set; }
    public string? AssetIndexUrl { get; set; }
    public MinecraftFileDownload? ClientDownload { get; set; }
    public string? InheritsFrom { get; set; }
    public MinecraftVersionType Type { get; set; }
    public DateTime? ReleaseTime { get; set; }
    public IReadOnlyList<MinecraftLoader> Loaders { get; set; } = [];
}

/// <summary>
/// Walks an <c>inheritsFrom</c> chain of Mojang-style version manifests and merges each
/// layer into a single <see cref="MergedVersionManifest"/>. Used by launcher providers
/// whose metadata stores fully merged or inherited vanilla manifests (MChub, Modrinth,
/// CurseForge, ...).
/// </summary>
public sealed class VersionManifestMerger {
    private const int MaxDepth = 8;

    private readonly Func<string, string?> _resolveVersionJsonPath;

    /// <param name="resolveVersionJsonPath">
    /// Maps a version id to the absolute path of its manifest JSON (or <c>null</c> when the
    /// launcher does not ship metadata for that id).
    /// </param>
    public VersionManifestMerger(Func<string, string?> resolveVersionJsonPath) {
        _resolveVersionJsonPath = resolveVersionJsonPath
            ?? throw new ArgumentNullException(nameof(resolveVersionJsonPath));
    }

    public async Task<MergedVersionManifest?> MergeAsync(
        string versionId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        return await MergeAsync(versionId, null, cancellationToken, 0);
    }

    private async Task<MergedVersionManifest?> MergeAsync(
        string versionId,
        MergedVersionManifest? parent,
        CancellationToken cancellationToken,
        int depth) {
        if (depth > MaxDepth)
            return parent;

        var jsonPath = _resolveVersionJsonPath(versionId);
        if (jsonPath is null || !File.Exists(jsonPath))
            return parent;

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var parentId = root.TryGetProperty("inheritsFrom", out var parentElement)
            ? parentElement.GetString()
            : null;

        var merged = parent;
        if (!string.IsNullOrWhiteSpace(parentId) && parentId != versionId) {
            var inherited = await MergeAsync(parentId, parent, cancellationToken, depth + 1);
            if (inherited is not null) {
                merged = inherited;
                merged.InheritsFrom = parentId;
            }
        }

        merged ??= new MergedVersionManifest();
        ApplyLayer(merged, root, versionId);
        return merged;
    }

    /// <summary>
    /// Merges a single manifest layer into an accumulated result. Used both while walking the
    /// inheritance chain and by providers that overlay a standalone manifest (e.g. a MChub
    /// instance file) on top of an already-merged chain.
    /// </summary>
    public static void ApplyLayer(MergedVersionManifest merged, JsonElement root, string? fallbackId) {
        if (merged.MinecraftVersion.Length == 0)
            merged.MinecraftVersion = root.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } versionValue
                ? versionValue
                : fallbackId ?? string.Empty;

        if (root.TryGetProperty("mainClass", out var mainClass) && mainClass.GetString() is { Length: > 0 } mainClassValue)
            merged.MainClass = mainClassValue;

        if (root.TryGetProperty("minecraftArguments", out var mcArgs) && mcArgs.GetString() is { Length: > 0 } mcArgsValue)
            merged.MinecraftArguments = mcArgsValue;

        if (root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object) {
            var game = VersionJsonParser.MapArguments(args, "game");
            var jvm = VersionJsonParser.MapArguments(args, "jvm");
            merged.Arguments = merged.Arguments with {
                Game = game.Count > 0
                    ? [.. merged.Arguments.Game, .. game]
                    : merged.Arguments.Game,
                Jvm = jvm.Count > 0
                    ? [.. merged.Arguments.Jvm, .. jvm]
                    : merged.Arguments.Jvm
            };
        }

        if (root.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array) {
            foreach (var library in VersionJsonParser.MapLibraries(libraries))
                VersionJsonParser.AddLibrary(merged.Libraries, library);

            merged.Loaders = ModLoaderDetector.DetectFromLibraries(libraries, merged.Loaders);
        }

        if (root.TryGetProperty("assetIndex", out var assetIndex) && assetIndex.ValueKind == JsonValueKind.Object) {
            if (assetIndex.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } assetId)
                merged.AssetIndex = new AssetIndex(assetId);
            if (assetIndex.TryGetProperty("url", out var urlElement))
                merged.AssetIndexUrl = urlElement.GetString();
        }

        if (VersionJsonParser.MapClientDownload(root) is { } clientDownload)
            merged.ClientDownload = clientDownload;

        if (VersionJsonParser.MapJavaVersion(root) is { } requiredJava)
            merged.RequiredJavaVersion = requiredJava;

        merged.Type = VersionJsonParser.MapType(root);
        merged.ReleaseTime = VersionJsonParser.MapReleaseTime(root);
    }
}
