using System.Buffers;
using System.Text.RegularExpressions;
using Iridium.Enums;
using Iridium.Java;
using Iridium.Launch;
using Iridium.Models.Launch;
using Iridium.Models.Minecraft;
using Iridium.Interfaces;
using Iridium.Models.Java;

namespace Iridium.Minecraft;

/// <summary>
/// Builds launch arguments from a resolved <see cref="MinecraftContext"/>. The parser is
/// format-agnostic: directory mapping comes from <c>context.Layout</c> and the whole
/// argument set is derived from the parsed entry model via private helpers — no
/// contributor/strategy extension point is needed.
/// </summary>
public sealed partial class ArgumentParser : IArgumentParser {
    private const string DefaultMainClass = "net.minecraft.client.main.Main";
    private static readonly DateTime QuickPlayFeatureCutoff = new(2023, 4, 4);

    public LaunchArguments Build(MinecraftContext context, LaunchConfig config) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(config);

        if (config.Account is null)
            throw new InvalidOperationException("Account is required for launch");

        if (config.JavaPath is null)
            throw new InvalidOperationException("Java runtime path is required");

        if (config.MaxMemorySize <= 0)
            throw new InvalidOperationException("Max memory size must be greater than 0");

        var entry = context.Entry;
        var layout = context.Layout;
        var paths = LaunchDirectories.Resolve(layout, entry, config);
        var features = BuildFeatures(config);
        var libraryPaths = ResolveLibraryPaths(entry, paths, features);
        var classpath = BuildClasspath(libraryPaths, paths.VersionJarPath);
        var nativePaths = ResolveNativePaths(entry, paths, features);

        var vmReplacements = BuildVmReplacements(config, paths, entry, classpath);
        var gameReplacements = BuildGameReplacements(config, paths, entry);

        var jvmArguments = BuildJvmArguments(config, entry, paths, features, vmReplacements);
        var gameArguments = BuildGameArguments(config, entry, features, gameReplacements);

        var mainClass = string.IsNullOrWhiteSpace(entry.MainClass)
            ? DefaultMainClass
            : entry.MainClass;

        return new LaunchArguments {
            JvmArguments = jvmArguments,
            MainClass = mainClass,
            GameArguments = gameArguments,
            Natives = nativePaths
        };
    }

    private static List<string> BuildJvmArguments(
        LaunchConfig config,
        MinecraftEntry entry,
        LaunchDirectories paths,
        Dictionary<string, bool> features,
        Dictionary<string, string> vmReplacements) {
        var java = config.JavaPath!;
        var jvm = new List<string>(config.JvmArguments.Count() + 32);

        if (config.MinMemorySize > 0)
            jvm.Add($"-Xms{config.MinMemorySize}m");

        jvm.Add($"-Xmx{config.MaxMemorySize}m");

        jvm.AddRange(config.JvmArguments);

        AppendEncodingArguments(jvm, java);
        AppendSecurityArguments(jvm);

        var log4JConfiguration = Path.Combine(entry.InstancePath, "log4j2.xml");
        if (File.Exists(log4JConfiguration))
            jvm.Add($"-Dlog4j.configurationFile={log4JConfiguration}");

        AppendGeneratedArguments(jvm, entry, paths, java);

        jvm.AddRange(JvmArgumentParser
            .Parse(entry, features)
            // Forge / NeoForge 的 -DignoreList 用 ${version_name}.jar 来忽略「原版客户端 jar」。
            // 但该 jar 是按【基础游戏版本】命名的（如 1.20.1.jar），并非实例/修改版 id；
            // 若按 entry.Id 替换（例如 “Zombie Invade 100 Days.jar”），真正的 1.20.1.jar 就没被忽略，
            // 会被当作自动模块（1.20.1.jar → 模块 _1._20_1），与 Forge 的 minecraft 模块同时导出
            // net.minecraft.client，触发 java.lang.module.ResolutionException 而启动失败。
            // 这里把 ${version_name}.jar 归一到实际客户端 jar 文件名（primary_jar_name）。
            .Select(argument => ReplacePlaceholders(
                argument.Replace("${version_name}.jar", "${primary_jar_name}", StringComparison.Ordinal),
                vmReplacements)));

        return jvm;
    }

    private static List<string> BuildGameArguments(
        LaunchConfig config,
        MinecraftEntry entry,
        Dictionary<string, bool> features,
        Dictionary<string, string> gameReplacements) {
        var game = new List<string>(entry.Tweakers.Count + 16);
        var hasLegacyArguments = entry.MinecraftArguments is { Length: > 0 };

        game.AddRange(GameArgumentParser
            .Parse(entry, features)
            .Select(argument => ReplacePlaceholders(argument, gameReplacements)));

        // Legacy minecraftArguments (e.g. OptiFine) often only contain --tweakClass
        // and omit the standard arguments that LaunchWrapper/tweakers expect. Inject
        // them here if the parsed set does not already include --gameDir.
        if (hasLegacyArguments && !game.Contains("--gameDir") && !game.Contains("--game_directory")) {
            // Inject the placeholders first, then replace them in place so the
            // replacement logic is consistent with the rest of the argument list.
            var inject = new (string Flag, string Placeholder)[] {
                ("--username", "${auth_player_name}"),
                ("--version", "${version_name}"),
                ("--gameDir", "${game_directory}"),
                ("--assetsDir", "${game_assets}"),
                ("--assetIndex", "${assets_index_name}"),
                ("--uuid", "${auth_uuid}"),
                ("--accessToken", "${auth_access_token}"),
                ("--userType", "${user_type}"),
                ("--userProperties", "${user_properties}")
            };
            // Insert before the first --tweakClass so LaunchWrapper can parse gameDir
            // before any tweaker's acceptOptions is called.
            var idx = game.FindIndex(a => a == "--tweakClass");
            if (idx < 0) idx = 0;
            for (var i = inject.Length - 1; i >= 0; i--) {
                var (flag, placeholder) = inject[i];
                game.Insert(idx, ReplacePlaceholders(flag, gameReplacements));
                game.Insert(idx + 1, ReplacePlaceholders(placeholder, gameReplacements));
            }
        }

        // Modern versions carry their own --width/--height rules in arguments.game;
        // the launcher only supplies them for legacy (minecraftArguments) versions.
        if (hasLegacyArguments && features.GetValueOrDefault("has_custom_resolution")) {
            game.Add("--width");
            game.Add(ReplacePlaceholders("${resolution_width}", gameReplacements));
            game.Add("--height");
            game.Add(ReplacePlaceholders("${resolution_height}", gameReplacements));
        }

        if (config.IsFullscreen)
            game.Add("--fullscreen");

        // OneSix / Prism component launchwrapper tweakers.
        foreach (var tweaker in entry.Tweakers) {
            game.Add("--tweakClass");
            game.Add(tweaker);
        }

        var isHighVersion = entry.ReleaseTime is { } releaseTime && releaseTime > QuickPlayFeatureCutoff;
        if (!string.IsNullOrWhiteSpace(config.SaveName) && isHighVersion) {
            game.Add("--quickPlaySingleplayer");
            game.Add(config.SaveName);
        }

        if (config.ServerInfo is not null) {
            if (isHighVersion) {
                game.Add("--quickPlayMultiplayer");
                game.Add(config.ServerInfo.Address);
            } else {
                game.Add("--server");
                game.Add(config.ServerInfo.Address);
                game.Add("--port");
                game.Add(config.ServerInfo.Port.ToString());
            }
        }

        return game;
    }

    private static void AppendEncodingArguments(List<string> jvm, JavaEntry java) {
        const string encoding = "UTF-8";

        jvm.Add($"-Dfile.encoding={encoding}");

        if (java.MajorVersion < 19) {
            jvm.Add($"-Dsun.stdout.encoding={encoding}");
            jvm.Add($"-Dsun.stderr.encoding={encoding}");
        } else {
            jvm.Add($"-Dstdout.encoding={encoding}");
            jvm.Add($"-Dstderr.encoding={encoding}");
        }
    }

    private static void AppendSecurityArguments(List<string> jvm) {
        jvm.Add("-Djava.rmi.server.useCodebaseOnly=true");
        jvm.Add("-Dcom.sun.jndi.rmi.object.trustURLCodebase=false");
        jvm.Add("-Dcom.sun.jndi.cosnaming.object.trustURLCodebase=false");
        jvm.Add("-Dlog4j2.formatMsgNoLookups=true");
    }

    private static void AppendGeneratedArguments(
        List<string> jvm,
        MinecraftEntry entry,
        LaunchDirectories paths,
        JavaEntry java) {
        jvm.Add($"-Dminecraft.client.jar={paths.VersionJarPath}");

        if (!OperatingSystem.IsWindows())
            jvm.Add($"-Duser.home={Path.GetDirectoryName(paths.InstanceRoot) ?? paths.InstanceRoot}");

        if (OperatingSystem.IsMacOS()) {
            jvm.Add($"-Xdock:name=Minecraft {entry.Id}");
            // Ensure -XstartOnFirstThread is always present on macOS; GLFW requires
            // it and version JSON rule matching can fail if the OS name differs.
            if (!jvm.Contains("-XstartOnFirstThread"))
                jvm.Add("-XstartOnFirstThread");
        }

        jvm.Add("-Djava.net.useSystemProxies=true");

        AppendGeneratedGcArguments(jvm, java, jvm);
        AppendGeneratedJitArguments(jvm, java);

        if (java.MajorVersion == 16)
            jvm.Add("--illegal-access=permit");

        jvm.Add("-Dfml.ignoreInvalidMinecraftCertificates=true");
        jvm.Add("-Dfml.ignorePatchDiscrepancies=true");
    }

    private static void AppendGeneratedGcArguments(List<string> jvm, JavaEntry java, List<string> arguments) {
        if (java.MajorVersion < 8)
            return;

        if (arguments.Any(argument => argument == "-XX:-UseG1GC" ||
                                      (argument.StartsWith("-XX:+Use", StringComparison.Ordinal) &&
                                       argument.EndsWith("GC", StringComparison.Ordinal))))
            return;

        jvm.Add("-XX:+UnlockExperimentalVMOptions");
        jvm.Add("-XX:+UnlockDiagnosticVMOptions");
        jvm.Add("-XX:+UseG1GC");
        jvm.Add("-XX:G1MixedGCCountTarget=5");
        jvm.Add("-XX:G1NewSizePercent=20");
        jvm.Add("-XX:G1ReservePercent=20");
        jvm.Add("-XX:MaxGCPauseMillis=50");
        jvm.Add("-XX:G1HeapRegionSize=32m");
        jvm.Add("-XX:-OmitStackTraceInFastThrow");

        if (!java.Is64Bit)
            jvm.Add("-Xss1m");
    }

    private static void AppendGeneratedJitArguments(List<string> jvm, JavaEntry java) {
        if (java.MajorVersion < 8 || !java.Is64Bit)
            return;

        if (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes <= 4L * 1024 * 1024 * 1024)
            return;

        jvm.Add("-XX:-DontCompileHugeMethods");
        jvm.Add("-XX:MaxNodeLimit=240000");
        jvm.Add("-XX:NodeLimitFudgeFactor=8000");
        jvm.Add("-XX:TieredCompileTaskTimeout=10000");
        jvm.Add("-XX:ReservedCodeCacheSize=400M");

        if (java.MajorVersion >= 9) {
            jvm.Add("-XX:NonNMethodCodeHeapSize=12M");
            jvm.Add("-XX:ProfiledCodeHeapSize=194M");
        }

        jvm.Add("-XX:NmethodSweepActivity=1");
    }

    private static Dictionary<string, string> BuildVmReplacements(
        LaunchConfig config,
        LaunchDirectories paths,
        MinecraftEntry entry,
        string classpath) =>
        new(8, StringComparer.Ordinal) {
            ["launcher_name"] = config.LauncherName ?? "Iridium",
            ["launcher_version"] = string.Empty,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = paths.LibrariesRoot,
            ["libraries_directory"] = paths.LibrariesRoot,
            ["classpath"] = classpath,
            ["primary_jar"] = paths.VersionJarPath,
            ["primary_jar_name"] = Path.GetFileName(paths.VersionJarPath),
            // NeoForge's generated manifests use ${version_name}.jar in ignoreList to
            // exclude the instance jar. That file is named after the entry id, while
            // MinecraftVersion is the underlying game version (for example 1.21.1).
            ["version_name"] = entry.Id,
            ["natives_directory"] = paths.NativesDirectory
        };

    private static Dictionary<string, string> BuildGameReplacements(LaunchConfig config, LaunchDirectories paths, MinecraftEntry entry) {
        var account = config.Account!;

        return new Dictionary<string, string>(16, StringComparer.Ordinal) {
            ["auth_player_name"] = account.Name,
            ["auth_access_token"] = account.AccessToken,
            ["access_token"] = account.AccessToken,
            ["auth_session"] = account.AccessToken,
            ["auth_uuid"] = account.Uuid.ToString("N"),
            ["clientid"] = string.Empty,
            ["auth_xuid"] = string.Empty,
            ["user_type"] = account.Type == AccountType.Microsoft ? "msa" : "mojang",
            ["user_properties"] = "{}",
            ["version_name"] = entry.Id,
            ["version_type"] = config.LauncherName ?? GetVersionType(entry),
            ["game_assets"] = paths.AssetsRoot,
            ["assets_root"] = paths.AssetsRoot,
            ["game_directory"] = paths.GameDirectory,
            ["assets_index_name"] = entry.AssetIndex?.Id ?? entry.Id,
            ["resolution_width"] = config.Width.ToString(),
            ["resolution_height"] = config.Height.ToString()
        };
    }

    private static string GetVersionType(MinecraftEntry entry) => entry.Type switch {
        MinecraftVersionType.Snapshot => "snapshot",
        MinecraftVersionType.OldBeta => "old_beta",
        MinecraftVersionType.OldAlpha => "old_alpha",
        _ => "release"
    };

    private static Dictionary<string, bool> BuildFeatures(LaunchConfig config) => new(1, StringComparer.Ordinal) {
        ["has_custom_resolution"] = config.Width != 0 && config.Height != 0
    };

    private static List<string> ResolveLibraryPaths(
        MinecraftEntry entry,
        LaunchDirectories paths,
        Dictionary<string, bool> features) {
        var result = new List<string>(entry.Libraries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in entry.Libraries) {
            if (!VersionArgumentRuleParser.IsActive(library.Rules, features))
                continue;

            if (ResolveLibraryPath(paths.LibrariesRoot, library.Name) is { } path && seen.Add(path))
                result.Add(path);
        }

        return result;
    }

    private static bool IsNativeClassifier(string name) {
        var parts = name.Split(':');
        return parts.Length >= 4 && parts[3].StartsWith("natives-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a library's jar path. When the declared artifact file is missing (e.g. a
    /// launcher shipped a newer build of the same artifact), falls back to an installed jar
    /// under the artifact's Maven directory that shares the requested classifier, so the
    /// launch survives metadata drift.
    /// </summary>
    private static string? ResolveLibraryPath(string librariesRoot, string name) {
        if (MavenPathParser.Resolve(librariesRoot, name) is { } declared && File.Exists(declared))
            return declared;

        var classifier = GetClassifier(name);
        var artifact = GetArtifact(name);
        var groupPath = GetArtifactRoot(librariesRoot, name);
        if (groupPath is null || !Directory.Exists(groupPath))
            return null;

        var all = Directory.EnumerateFiles(groupPath, "*.jar", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("-sources.jar", StringComparison.OrdinalIgnoreCase) &&
                           !file.EndsWith("-javadoc.jar", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Prefer a jar carrying the same classifier, matching on the exact trailing
        // segment so "-natives-windows" does not match "-natives-windows-x86".
        // Among variants, choose the highest version rather than newest write time.
        // Fallbacks are restricted to the same major version: a missing authlib 4.0.43
        // must not be substituted with the authlib 6.x used by another Minecraft release.
        var declaredMajor = GetMajorVersion(ExtractVersionFromName(name));
        var candidates = all
            .Where(file => MatchesClassifier(file, classifier))
            .Where(file => declaredMajor is null || GetMajorVersion(ExtractVersionFromFileName(file, artifact)) == declaredMajor)
            .OrderByDescending(file => ExtractVersionFromFileName(file, artifact), Comparer<string>.Create(CompareVersions))
            .ToArray();

        return candidates.Length > 0 ? candidates[0] : null;
    }

    private static string ExtractVersionFromName(string name) {
        // group:artifact:version[:classifier] -> 3rd segment.
        var parts = name.Split(':');
        return parts.Length >= 3 ? parts[2] : string.Empty;
    }

    private static string GetArtifact(string name) {
        // group:artifact:version[:classifier] -> 2nd segment.
        var parts = name.Split(':');
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    private static int? GetMajorVersion(string version) {
        var dot = version.IndexOfAny(['.', '-', '_']);
        var segment = dot >= 0 ? version[..dot] : version;
        return int.TryParse(segment, out var value) ? value : null;
    }

    private static string ExtractVersionFromFileName(string filePath, string artifact) {
        // <artifact>-<version>[-classifier].jar -> strip the artifact prefix to reach
        // the version segment (artifact names may themselves contain hyphens).
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (artifact.Length > 0 && fileName.StartsWith($"{artifact}-", StringComparison.OrdinalIgnoreCase))
            return fileName[(artifact.Length + 1)..];
        var index = fileName.IndexOf('-');
        return index >= 0 ? fileName[(index + 1)..] : fileName;
    }

    private static int CompareVersions(string a, string b) {
        var aParts = a.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var bParts = b.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var count = Math.Max(aParts.Length, bParts.Length);

        for (var i = 0; i < count; i++) {
            var x = i < aParts.Length ? aParts[i] : string.Empty;
            var y = i < bParts.Length ? bParts[i] : string.Empty;
            if (x == y)
                continue;

            if (int.TryParse(x, out var xi) && int.TryParse(y, out var yi)) {
                var numeric = xi.CompareTo(yi);
                if (numeric != 0)
                    return numeric;
            } else {
                var ordinal = string.CompareOrdinal(x, y);
                if (ordinal != 0)
                    return ordinal;
            }
        }

        return 0;
    }

    private static bool MatchesClassifier(string filePath, string? classifier) {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(classifier))
            return !HasClassifier(fileName);

        // lwjgl-tinyfd-3.3.3-natives-windows.jar -> check it ends with -<classifier>
        return fileName.EndsWith($"-{classifier}", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetClassifier(string name) {
        // group:artifact[:version][:classifier] -> 4th segment, if present.
        var parts = name.Split(':');
        return parts.Length >= 4 ? parts[3] : string.Empty;
    }

    private static bool HasClassifier(string fileName) {
        // e.g. lwjgl-tinyfd-3.3.1-natives-windows.jar -> strip trailing -<ver> and check.
        var index = fileName.LastIndexOf("-natives-", StringComparison.OrdinalIgnoreCase);
        return index >= 0;
    }

    private static string? GetArtifactRoot(string librariesRoot, string name) {
        var relative = MavenPathParser.GetRelativePath(name);
        if (relative is null)
            return null;

        // relative = group/dirs/artifact/version/file.jar -> strip file, keep group/artifact.
        var directory = Path.GetDirectoryName(Path.Combine(librariesRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (directory is null)
            return null;
        return Path.GetDirectoryName(directory);
    }

    private static List<string> ResolveNativePaths(
        MinecraftEntry entry,
        LaunchDirectories paths,
        Dictionary<string, bool> features) {
        var result = new List<string>();
        foreach (var library in entry.Libraries) {
            if (!VersionArgumentRuleParser.IsActive(library.Rules, features))
                continue;

            string? classifierName = null;
            if (library.Natives is { Count: > 0 }) {
                if (VersionArgumentRuleParser.GetNativeClassifier(library.Natives) is not { } classifier)
                    continue;
                classifierName = $"{library.Name}:{classifier}";
            } else if (IsNativeClassifier(library.Name)) {
                classifierName = library.Name;
            } else {
                continue;
            }

            if (ResolveLibraryPath(paths.LibrariesRoot, classifierName) is { } jarPath)
                result.Add(jarPath);
        }

        return result;
    }

    private static string BuildClasspath(List<string> libraryPaths, string clientJarPath) {
        var hasClientJar = !string.IsNullOrEmpty(clientJarPath);

        if (libraryPaths.Count == 0 && !hasClientJar)
            return string.Empty;

        var libs = ArrayPool<string>.Shared.Rent(libraryPaths.Count);
        var totalSize = libraryPaths.Count - 1;

        if (hasClientJar) {
            totalSize += clientJarPath.Length;
            totalSize += 1;
        }

        for (var i = 0; i < libraryPaths.Count; i++) {
            libs[i] = libraryPaths[i];
            totalSize += libraryPaths[i].Length;
        }

        var buffer = ArrayPool<char>.Shared.Rent(totalSize);
        var offset = 0;
        for (var i = 0; i < libraryPaths.Count; i++) {
            libs[i].CopyTo(buffer.AsSpan(offset..));
            offset += libs[i].Length;
            buffer[offset] = Path.PathSeparator;
            offset += 1;
        }

        if (hasClientJar) {
            clientJarPath.CopyTo(buffer.AsSpan(offset..));
            offset += clientJarPath.Length;
        } else
            offset -= 1;

        var result = new string(buffer.AsSpan(..offset));
        ArrayPool<char>.Shared.Return(buffer);
        ArrayPool<string>.Shared.Return(libs);
        return result;
    }

    private static string ReplacePlaceholders(string value, Dictionary<string, string> replacements) {
        if (value.IndexOf("${", StringComparison.Ordinal) < 0)
            return value;

        return PlaceholderRegex().Replace(value,
            match => replacements.TryGetValue(match.Groups[1].Value, out var replacement)
                ? replacement
                : match.Value);
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex PlaceholderRegex();
}

internal static class GameArgumentParser {
    /// <summary>
    /// Parses the game arguments declared by the version JSON. The merged
    /// <see cref="MinecraftEntry.MinecraftArguments"/> string is authoritative for the
    /// whole profile: loader components append their own args there (e.g. Forge's
    /// --launchTarget / --fml.*), and structured <c>arguments.game</c> would drop them.
    /// Structured arguments are only used when no merged string exists (modern vanilla).
    /// </summary>
    public static IEnumerable<string> Parse(MinecraftEntry entry, Dictionary<string, bool> features) {
        if (entry.MinecraftArguments is { Length: > 0 } legacyArguments)
            foreach (var value in legacyArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                yield return value;
        else if (entry.Arguments?.Game is { } versionGame)
            foreach (var argument in versionGame) {
                if (!VersionArgumentRuleParser.IsActive(argument.Rules, features))
                    continue;

                foreach (var value in argument.Values)
                    yield return value;
            }
    }
}

internal static class JvmArgumentParser {
    /// <summary>
    /// Parses the JVM arguments declared by the version JSON, or falls back to the
    /// launcher-provided defaults for legacy versions without an arguments.jvm block.
    /// </summary>
    public static IEnumerable<string> Parse(MinecraftEntry entry, Dictionary<string, bool> features) {
        if (entry.Arguments?.Jvm is { } versionJvm) {
            var hasClasspath = false;
            foreach (var argument in versionJvm) {
                if (!VersionArgumentRuleParser.IsActive(argument.Rules, features))
                    continue;

                foreach (var value in argument.Values) {
                    if (value is "-cp" or "-classpath")
                        hasClasspath = true;

                    yield return value;
                }
            }

            // Some loader versions omit the -cp option; add it to avoid a failed launch.
            if (!hasClasspath) {
                yield return "-cp";
                yield return "${classpath}";
            }
        } else {
            if (OperatingSystem.IsWindows()) {
                yield return "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump";

                if (Environment.OSVersion.Version.Major == 10) {
                    yield return "-Dos.name=Windows 10";
                    yield return "-Dos.version=10.0";
                }
            }

            yield return "-Djava.library.path=${natives_directory}";
            yield return "-Dminecraft.launcher.brand=${launcher_name}";
            yield return "-Dminecraft.launcher.version=${launcher_version}";
            yield return "-cp";
            yield return "${classpath}";
        }
    }
}
