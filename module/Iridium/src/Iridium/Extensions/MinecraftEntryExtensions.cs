using System.IO.Compression;
using Iridium.Launch;
using Iridium.Models.Minecraft;

namespace Iridium.Extensions;

public static class MinecraftEntryExtensions {
    /// <summary>
    /// Extracts native libraries (lwjgl/openal etc.) from the native jars into the
    /// natives directory so that they can be loaded via -Djava.library.path.
    /// </summary>
    public static Task ExtractNativesAsync(this MinecraftEntry entry,
        IReadOnlyList<string> nativeJars,
        string? nativesDirectory = null,
        CancellationToken cancellationToken = default) {
        var directory = nativesDirectory ?? entry.InstancePath;

        if (nativeJars.Count == 0)
            return Task.CompletedTask;

        return Task.Run(() => {
            Directory.CreateDirectory(directory);
            foreach (var jar in nativeJars) {
                try {
                    using var archive = ZipFile.OpenRead(jar);
                    foreach (var zipEntry in archive.Entries) {
                        if (zipEntry.FullName.StartsWith("META-INF/", StringComparison.Ordinal))
                            continue;

                        if (zipEntry.Name.Length == 0 || !Path.HasExtension(zipEntry.FullName))
                            continue;

                        if (Path.GetExtension(zipEntry.Name) is ".sha1" or ".git" or ".pom" or ".txt" or ".md")
                            continue;

                        // 必须覆盖：同一实例升级过 LWJGL 之后，目录里留着的是旧版本的
                        // 原生库，而 Java 包已经是新版本 —— 两者不一致会直接让游戏起不来
                        // （LWJGL: Incompatible Java and native library versions detected）。
                        var target = Path.Combine(directory, zipEntry.Name);

                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        zipEntry.ExtractToFile(target, overwrite: true);
                    }
                } catch {
                    // Corrupt native jar; skip.
                }
            }
        }, cancellationToken);
    }
}
