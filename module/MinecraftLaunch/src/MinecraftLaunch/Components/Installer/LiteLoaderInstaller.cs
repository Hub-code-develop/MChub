using System.Text.Json;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;

namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// LiteLoader 安装器（Minecraft 1.5.2 ~ 1.12.2）。
///
/// <para>LiteLoader 既不是 Fabric 系，也不像 BTA 那样替换整包：它在原版之上把主类换成
/// <c>net.minecraft.launchwrapper.Launch</c>，追加一个 <c>--tweakClass</c>，
/// 再把 <c>com.mumfrey:liteloader</c> 与若干依赖挂到 classpath。</para>
///
/// <para>版本数据取自官方 <c>https://dl.liteloader.com/versions/versions.json</c>，
/// 每个构建自带 <c>tweakClass</c> / <c>libraries</c> / <c>file</c>；
/// LiteLoader 自身与部分依赖不在 Maven Central，所以写版本 JSON 时会给缺 <c>url</c> 的库兜底一个仓库地址。</para>
/// </summary>
public sealed class LiteLoaderInstaller : InstallerBase {
    /// <summary>官方版本清单。</summary>
    public const string MetaUrl = "https://dl.liteloader.com/versions/versions.json";

    /// <summary>LiteLoader 自身的 maven 基址（由 dl.liteloader.com 提供）。</summary>
    public const string LiteLoaderMavenBaseUrl = "https://dl.liteloader.com/versions/";

    /// <summary>缺省依赖仓库（asm 等公共库在 Maven Central）。</summary>
    public const string DefaultMavenBaseUrl = "https://repo1.maven.org/maven2/";

    /// <summary>launchwrapper 主类。</summary>
    private const string LaunchWrapperMainClass = "net.minecraft.launchwrapper.Launch";

    public string CustomId { get; init; }
    public LiteLoaderInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }

    public static LiteLoaderInstaller Create(string mcFolder, LiteLoaderInstallEntry installEntry,
        string customId = default) {
        return new LiteLoaderInstaller {
            CustomId = customId,
            Entry = installEntry,
            MinecraftFolder = mcFolder,
        };
    }

    /// <summary>拉取该原版版本下可用的 LiteLoader 构建（官方未收录的版本返回空列表）。</summary>
    public static async Task<IEnumerable<LiteLoaderInstallEntry>> EnumerableLiteLoaderAsync(string mcVersion,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(mcVersion)) return [];

        await using var json = await HttpUtil.FlurlClient.Request(MetaUrl)
            .GetStreamAsync(cancellationToken: cancellationToken);

        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("versions"u8, out var versions) ||
            versions.ValueKind != JsonValueKind.Object ||
            !versions.TryGetProperty(mcVersion, out var versionNode))
            return [];

        // RELEASE 流放 artefacts，SNAPSHOT 流放 snapshots；同一版本只会有一个。
        foreach (var bucketName in new[] { "artefacts", "snapshots" }) {
            if (!versionNode.TryGetProperty(bucketName, out var bucket) ||
                bucket.ValueKind != JsonValueKind.Object)
                continue;

            if (bucket.TryGetProperty("com.mumfrey:liteloader"u8, out var builds) &&
                builds.ValueKind == JsonValueKind.Object)
                return ReadBuilds(builds, mcVersion);
        }

        return [];
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default) {
        ModifiedMinecraftEntry entry = default;
        ReportProgress(InstallStep.Started, 0d, TaskStatus.WaitingToRun, 1, 1);

        try {
            var inheritedEntry = ParseMinecraft(cancellationToken);
            var jsonFile = await WriteVersionJsonAsync(inheritedEntry, cancellationToken);
            entry = ParseModifiedMinecraft(jsonFile, inheritedEntry, cancellationToken);
            await CompleteLibrariesAsync(entry, cancellationToken);
        } catch (Exception exception) {
            ReportProgress(InstallStep.Interrupted, 1d, TaskStatus.Faulted, 1, 1);
            ReportCompleted(false, exception);
            throw;
        }

        ReportProgress(InstallStep.RanToCompletion, 1d, TaskStatus.RanToCompletion, 1, 1);
        ReportCompleted(true);
        return entry;
    }

    public Task PreloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    #region Privates

    private string ResolveInstanceId() => CustomId ?? $"{Entry.TargetMcVersion}-LiteLoader{Entry.LoaderVersion}";

    private static IEnumerable<LiteLoaderInstallEntry> ReadBuilds(JsonElement builds, string mcVersion) {
        var entries = new List<LiteLoaderInstallEntry>();
        foreach (var build in builds.EnumerateObject()) {
            // "latest" 只是最新构建的别名，跳过以免重复。
            if (build.Name.Equals("latest", StringComparison.OrdinalIgnoreCase)) continue;
            if (build.Value.ValueKind != JsonValueKind.Object) continue;

            var node = build.Value;
            var version = node.TryGetProperty("version"u8, out var versionNode) ? versionNode.GetString() : null;
            var tweakClass = node.TryGetProperty("tweakClass"u8, out var tweakNode) ? tweakNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(tweakClass)) continue;

            var libraries = node.TryGetProperty("libraries"u8, out var libs) && libs.ValueKind == JsonValueKind.Array
                ? NormalizeLibraries(libs)
                : "[]";
            var buildId = node.TryGetProperty("build"u8, out var buildNode) ? buildNode.GetString() : null;

            entries.Add(new LiteLoaderInstallEntry {
                TargetMcVersion = mcVersion,
                LoaderVersion = version!,
                TweakClass = tweakClass!,
                LibrariesJson = libraries,
                Build = buildId ?? string.Empty,
                MavenBaseUrl = LiteLoaderMavenBaseUrl,
            });
        }

        return entries;
    }

    /// <summary>保留库自带的 <c>url</c>（如 Sponge mixin），只给缺 <c>url</c> 的库补兜底仓库。</summary>
    private static string NormalizeLibraries(JsonElement libraries) {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            writer.WriteStartArray();
            foreach (var library in libraries.EnumerateArray()) {
                writer.WriteStartObject();
                var hasUrl = false;
                foreach (var property in library.EnumerateObject()) {
                    if (property.NameEquals("url"u8)) hasUrl = true;
                    property.WriteTo(writer);
                }

                if (!hasUrl) writer.WriteString("url", DefaultMavenBaseUrl);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private MinecraftEntry ParseMinecraft(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.10d, TaskStatus.Running, 1, 0);

        if (InheritedMinecraft is not null) return InheritedMinecraft;

        var inherited = new MinecraftParser(MinecraftFolder).GetMinecrafts()
            .FirstOrDefault(x => x.Version.VersionId.Equals(Entry.McVersion, StringComparison.OrdinalIgnoreCase));

        ReportProgress(InstallStep.ParseMinecraft, 0.15d, TaskStatus.Running, 1, 1);
        return inherited ?? throw new InvalidOperationException(
            $"LiteLoader 需要先安装原版 {Entry.McVersion} 作为父版本，但没有找到。");
    }

    private async Task<FileInfo> WriteVersionJsonAsync(MinecraftEntry inheritedEntry,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.20d, TaskStatus.Running, 1, 0);

        var instanceId = ResolveInstanceId();
        var directory = Path.Combine(MinecraftFolder, "versions", instanceId);
        Directory.CreateDirectory(directory);

        var mavenBase = string.IsNullOrWhiteSpace(Entry.MavenBaseUrl) ? LiteLoaderMavenBaseUrl : Entry.MavenBaseUrl;
        var loaderLibrary = $"{{\"name\":{JsonSerializer.Serialize($"com.mumfrey:liteloader:{Entry.LoaderVersion}")}," +
                            $"\"url\":{JsonSerializer.Serialize(mavenBase)}}}";

        var jsonPath = Path.Combine(directory, $"{instanceId}.json");
        var json = $"{{\"id\":{JsonSerializer.Serialize(instanceId)}," +
                   $"\"inheritsFrom\":{JsonSerializer.Serialize(inheritedEntry.Id)}," +
                   $"\"mainClass\":{JsonSerializer.Serialize(LaunchWrapperMainClass)}," +
                   $"\"libraries\":[{JoinLibraries(Entry.LibrariesJson, loaderLibrary)}]," +
                   // LiteLoader 只跑 1.5.2~1.12.2，用旧版 minecraftArguments 形式追加 tweak。
                   $"\"minecraftArguments\":{JsonSerializer.Serialize($"--tweakClass {Entry.TweakClass}")}}}";
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);

        ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
        return new FileInfo(jsonPath);
    }

    private static string JoinLibraries(string librariesJson, string extraLibrary) {
        var trimmed = string.IsNullOrWhiteSpace(librariesJson) ? "[]" : librariesJson.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']')) return $"{extraLibrary}";
        var inner = trimmed[1..^1].Trim();
        return inner.Length == 0 ? extraLibrary : $"{inner},{extraLibrary}";
    }

    private async Task CompleteLibrariesAsync(MinecraftEntry minecraft, CancellationToken cancellationToken) {
        ReportProgress(InstallStep.DownloadLibraries, 0.5d, TaskStatus.Running, 0, 0);

        var downloader = new MinecraftResourceDownloader(minecraft) {
            SourceRootDirectories = SourceRootDirectories
        };
        downloader.CopyProgressChanged += (_, x)
            => ReportProgress(InstallStep.CopyLibraries, x.Percentage.ToPercentage(0.5d, 0.75d),
                TaskStatus.Running, x.TotalCount, x.CompletedCount);
        downloader.ProgressChanged += (_, x)
            => ReportProgress(InstallStep.DownloadLibraries, x.ToPercentage().ToPercentage(0.75d, 0.95d),
                TaskStatus.Running, downloader.TotalCount, x.CompletedCount, x.Speed, true);

        await downloader.VerifyAndDownloadDependenciesAsync(cancellationToken: cancellationToken);
    }

    private static ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, MinecraftEntry inheritedEntry,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, [inheritedEntry], out _) as ModifiedMinecraftEntry;
        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    #endregion
}
