using System.Text.Json;
using IFormatProvider = Iridium.Interfaces.IFormatProvider;
using Iridium.Extension.Minecraft.Layout;
using Iridium.Extension.Minecraft.Parsing;
using Iridium.Minecraft;
using Iridium.Models.Minecraft;

namespace Iridium.Extension.Minecraft.Formats;

/// <summary>
/// Scans MChub installations. Every instance is a fabric-style inherited version manifest
/// stored under <c>instances/&lt;name&gt;/&lt;name&gt;.json</c>; it declares an
/// <c>inheritsFrom</c> id pointing at a vanilla version whose full metadata lives in
/// <c>meta/versions/&lt;id&gt;/&lt;id&gt;.json</c>. This provider walks that chain, merges
/// libraries / main class / arguments just like the standard provider does, then overlays the
/// instance manifest itself.
/// </summary>
public sealed class MChubMcProvider : IFormatProvider {
    private const int MaxScanConcurrency = 2;

    public string Id => "MChubMc";

    public int Priority => 80;

    public bool CanResolve(DirectoryInfo root) {
        var instancesRoot = Path.Combine(root.FullName, "instances");
        if (!Directory.Exists(instancesRoot))
            return false;

        // Distinguish from Prism's instances/: MChub instance dirs hold a manifest
        // named after the instance declaring an inheritsFrom chain.
        foreach (var dir in Directory.EnumerateDirectories(instancesRoot)) {
            var jsonPath = Path.Combine(dir, $"{Path.GetFileName(dir)}.json");
            if (!File.Exists(jsonPath))
                continue;

            try {
                using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (document.RootElement.TryGetProperty("inheritsFrom", out var inherits) &&
                    inherits.GetString() is { Length: > 0 })
                    return true;
            } catch (JsonException) {
            }
        }

        return false;
    }

    public async ValueTask<MinecraftContext?> GetAsync(DirectoryInfo root, string instanceId, CancellationToken ct = default) {
        var instanceDir = Path.Combine(root.FullName, "instances", instanceId);
        if (!Directory.Exists(instanceDir))
            return null;

        var entry = await ParseAsync(instanceDir, root.FullName, ct);
        return entry is null ? null : Wrap(new DirectoryInfo(instanceDir), entry);
    }

    public async ValueTask<IReadOnlyList<MinecraftContext>> GetMinecraftsAsync(DirectoryInfo root, CancellationToken ct = default) {
        var instancesRoot = Path.Combine(root.FullName, "instances");
        if (!Directory.Exists(instancesRoot))
            return [];

        var directories = Directory.EnumerateDirectories(instancesRoot).ToArray();
        var contexts = new MinecraftContext?[directories.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, directories.Length),
            new ParallelOptions { MaxDegreeOfParallelism = MaxScanConcurrency, CancellationToken = ct },
            async (index, cancellationToken) => {
                var dir = directories[index];
                var entry = await ParseAsync(dir, root.FullName, cancellationToken);
                if (entry is not null)
                    contexts[index] = Wrap(new DirectoryInfo(dir), entry);
            });

        return contexts.OfType<MinecraftContext>().ToArray();
    }

    private static MinecraftContext Wrap(DirectoryInfo dir, MinecraftEntry entry) => new() {
        Format = "MChubMc",
        Layout = new MChubMcLayout(),
        Entry = entry,
    };

    private async Task<MinecraftEntry?> ParseAsync(string instanceDir, string launcherRoot, CancellationToken ct) {
        var jsonPath = Path.Combine(instanceDir, $"{Path.GetFileName(instanceDir)}.json");
        if (!File.Exists(jsonPath))
            return null;

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        using var document = JsonDocument.Parse(json);
        var manifest = document.RootElement;

        var inheritsFrom = manifest.TryGetProperty("inheritsFrom", out var inheritsElement)
            ? inheritsElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(inheritsFrom))
            return null;

        // Resolve the inherited chain first so vanilla libraries come first, then overlay
        // the instance manifest itself (loader libraries, main class, ...).
        var versionsRoot = Path.Combine(launcherRoot, "meta", "versions");
        var merger = new VersionManifestMerger(id => Path.Combine(versionsRoot, id, $"{id}.json"));
        var merged = await merger.MergeAsync(inheritsFrom, ct);
        if (merged is null)
            return null;

        VersionManifestMerger.ApplyLayer(merged, manifest, fallbackId: inheritsFrom);

        var id = Path.GetFileName(instanceDir);

        return new MinecraftEntry {
            Id = id,
            Name = id,
            MinecraftVersion = merged.MinecraftVersion,
            InstancePath = instanceDir,
            InheritsFrom = inheritsFrom,
            MainClass = merged.MainClass,
            Arguments = merged.Arguments,
            MinecraftArguments = merged.MinecraftArguments,
            Libraries = merged.Libraries,
            RequiredJavaVersion = merged.RequiredJavaVersion,
            AssetIndex = merged.AssetIndex,
            AssetIndexUrl = merged.AssetIndexUrl,
            ClientDownload = merged.ClientDownload,
            Type = merged.Type,
            ReleaseTime = merged.ReleaseTime,
            Loaders = merged.Loaders
        };
    }
}
