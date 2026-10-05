using Iridium.Enums;
using Iridium.Java;
using Iridium.Models.Minecraft;
using Iridium.Models.Java;

namespace Iridium.Extensions;

public static class JavaExtensions {
    public static async Task<JavaEntry?> FindJavaForMinecraftAsync(this MinecraftEntry entry,
        IReadOnlyList<JavaEntry> javas,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(javas);

        if (javas.Count == 0)
            return null;

        var requiredVersion = entry.RequiredJavaVersion ?? GetRequiredJavaVersion(entry.MinecraftVersion);
        var exactVersion = false;

        foreach (var loader in entry.Loaders)
            if (loader.Type is LoaderType.Forge or LoaderType.NeoForge) {
                exactVersion = true;
                break;
            }

        var candidates = OrderCandidates(javas, requiredVersion, exactVersion);
        foreach (var java in candidates) {
            cancellationToken.ThrowIfCancellationRequested();

            if (await JavaVerifier.IsUsableAsync(java.JavaPath, java.MajorVersion, java.Version, cancellationToken)
                    .ConfigureAwait(false))
                return java;
        }

        return null;
    }

    private static List<JavaEntry> OrderCandidates(IReadOnlyList<JavaEntry> javas, int requiredVersion, bool exactVersion) {
        var has64Bit = javas.Any(java => java.Is64Bit);
        var candidates = new List<JavaEntry>(javas.Count);
        candidates.AddRange(javas.Where(java => !has64Bit || java.Is64Bit));

        candidates.Sort((x, y) => {
            var xCompatible = requiredVersion is 0 or -1 ||
                (exactVersion
                    ? x.MajorVersion == requiredVersion
                    : x.MajorVersion >= requiredVersion);

            var yCompatible = requiredVersion is 0 or -1 ||
                (exactVersion
                    ? y.MajorVersion == requiredVersion
                    : y.MajorVersion >= requiredVersion);

            if (xCompatible != yCompatible)
                return xCompatible ? -1 : 1;

            return xCompatible
                ? x.MajorVersion.CompareTo(y.MajorVersion)
                : y.MajorVersion.CompareTo(x.MajorVersion);
        });

        return candidates;
    }

    /// <summary>
    /// Gets the minimum Java major version required by the Minecraft release.
    /// </summary>
    private static int GetRequiredJavaVersion(string version) {
        if (string.IsNullOrWhiteSpace(version))
            return 8;

        if (!version.StartsWith("1.", StringComparison.Ordinal)) {
            var separator = version.IndexOf('.');
            var major = separator < 0
                ? version.AsSpan()
                : version.AsSpan(0, separator);

            if (!int.TryParse(major, out var value1))
                return 8;

            // 新版版本号方案：26.x 起要求 Java 25；22~25 归为 Java 21。
            if (value1 >= 26)
                return 25;
            return value1 >= 22 ? 21 : 8;
        }

        var minorEnd = version.IndexOf('.', 2);
        var minorSpan = minorEnd < 0
            ? version.AsSpan(2)
            : version.AsSpan(2, minorEnd - 2);

        if (!int.TryParse(minorSpan, out var minor))
            return 8;

        switch (minor) {
            case >= 21:
                return 21;
            case >= 18:
                return 17;
            case 17:
                return 16;
        }

        if (minorEnd < 0)
            return 8;

        var patch = version.AsSpan(minorEnd + 1);
        return int.TryParse(patch, out var value2) && value2 >= 5 ? 21 : 17;
    }
}
