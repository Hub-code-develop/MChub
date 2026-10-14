using System.IO.Compression;
using System.Text.Json;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Utilities;

namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// BTA（Better Than Adventure!）安装器。
///
/// <para>BTA 与 Fabric 系完全不同：它是基于 Minecraft <c>b1.7.3</c> 的独立客户端，
/// 直接把原版客户端 jar 换掉。所以安装分两步——
/// ① 原版 b1.7.3 由调用方先装好（走 VanillaInstaller）；
/// ② 本安装器从 GitHub release 拉 BTA 客户端 jar，写一个继承 b1.7.3 的版本 JSON
/// （<c>jar</c> 指向我们放进来的 BTA jar，主类从 jar 清单里读）。</para>
///
/// <para>版本列表来自 GitHub Releases API，资产名各版本不统一，所以按
/// 「名字以 client 结尾的 .jar」来挑，而不是硬编码文件名。</para>
/// </summary>
public sealed class BtaInstaller : InstallerBase {
    public string CustomId { get; init; }
    public BtaInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }

    public static BtaInstaller Create(string mcFolder, BtaInstallEntry installEntry, string customId = default) {
        return new BtaInstaller {
            CustomId = customId,
            Entry = installEntry,
            MinecraftFolder = mcFolder,
        };
    }

    /// <summary>BTA 只支持叠加在 b1.7.3 上；其它版本一律返回空列表（由 UI 显示「暂无可用版本」）。</summary>
    public static bool IsCompatibleMcVersion(string mcVersion) {
        if (string.IsNullOrWhiteSpace(mcVersion)) return false;
        var trimmed = mcVersion.Trim();
        return trimmed.Equals(BtaInstallerDefaults.BaseMcVersion, StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("1.7.3", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<IEnumerable<BtaInstallEntry>> EnumerableBtaAsync(string mcVersion,
        CancellationToken cancellationToken = default) {
        if (!IsCompatibleMcVersion(mcVersion)) return [];

        var url = $"https://api.github.com/repos/{BtaInstallerDefaults.ReleasesRepository}/releases?per_page=60";
        await using var json = await HttpUtil.FlurlClient.Request(url)
            .GetStreamAsync(cancellationToken: cancellationToken);

        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        var entries = new List<BtaInstallEntry>();
        foreach (var release in document.RootElement.EnumerateArray()) {
            if (release.TryGetProperty("draft"u8, out var draft) && draft.GetBoolean()) continue;

            var tag = release.TryGetProperty("tag_name"u8, out var tagNode) ? tagNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) continue;

            var version = tag.TrimStart('v', 'V');
            var jarUrl = FindClientJarUrl(release);
            if (jarUrl is null) continue;

            entries.Add(new BtaInstallEntry {
                TagName = tag,
                LoaderVersion = version,
                ClientJarUrl = jarUrl,
            });
        }

        return entries.OrderByDescending(x => x.LoaderVersion, BtaVersionComparer.Instance);
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default) {
        ModifiedMinecraftEntry entry = default;
        ReportProgress(InstallStep.Started, 0d, TaskStatus.WaitingToRun, 1, 1);

        try {
            var inheritedEntry = ParseMinecraft(cancellationToken);
            var jsonFile = await WriteVersionAsync(inheritedEntry, cancellationToken);
            entry = ParseModifiedMinecraft(jsonFile, inheritedEntry, cancellationToken);
        } catch (Exception exception) {
            ReportProgress(InstallStep.Interrupted, 1d, TaskStatus.Faulted, 1, 1);
            ReportCompleted(false, exception);
            throw;
        }

        ReportProgress(InstallStep.RanToCompletion, 1d, TaskStatus.RanToCompletion, 1, 1);
        ReportCompleted(true);
        return entry;
    }

    /// <summary>把下载客户端 jar 提前做掉，和原版下载并行。</summary>
    public async Task PreloadAsync(CancellationToken cancellationToken = default) {
        var instanceId = ResolveInstanceId();
        var jarPath = Path.Combine(MinecraftFolder, "versions", instanceId, $"{instanceId}.jar");
        await DownloadClientJarAsync(jarPath, cancellationToken);
    }

    #region Privates

    private string ResolveInstanceId() => CustomId ?? $"bta-{Entry.LoaderVersion}";

    private MinecraftEntry ParseMinecraft(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.10d, TaskStatus.Running, 1, 0);

        if (InheritedMinecraft is not null) return InheritedMinecraft;

        var inherited = new MinecraftParser(MinecraftFolder).GetMinecrafts()
            .FirstOrDefault(x => x.Version.VersionId.Equals(Entry.McVersion, StringComparison.OrdinalIgnoreCase));

        ReportProgress(InstallStep.ParseMinecraft, 0.15d, TaskStatus.Running, 1, 1);
        return inherited ?? throw new InvalidOperationException(
            $"BTA 需要先安装原版 {Entry.McVersion} 作为父版本，但没有找到。");
    }

    private async Task<FileInfo> WriteVersionAsync(MinecraftEntry inheritedEntry, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.20d, TaskStatus.Running, 1, 0);

        var instanceId = ResolveInstanceId();
        var directory = Path.Combine(MinecraftFolder, "versions", instanceId);
        Directory.CreateDirectory(directory);

        var jarPath = Path.Combine(directory, $"{instanceId}.jar");
        await DownloadClientJarAsync(jarPath, cancellationToken);

        ReportProgress(InstallStep.DownloadLibraries, 0.80d, TaskStatus.Running, 1, 0);

        // 主类优先从 BTA jar 自己的清单里读（比硬编码靠谱）；读不到再退回 b1.7.3 的主类。
        var mainClass = ReadMainClass(jarPath) ?? "net.minecraft.client.Minecraft";

        var jsonPath = Path.Combine(directory, $"{instanceId}.json");
        await using (var stream = File.Create(jsonPath)) {
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteString("id", instanceId);
            writer.WriteString("inheritsFrom", inheritedEntry.Id);
            writer.WriteString("mainClass", mainClass);
            // jar 指向我们放进来的 BTA 客户端 jar，而不是原版 b1.7.3 的 jar。
            writer.WriteString("jar", instanceId);
            writer.WritePropertyName("libraries");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        ReportProgress(InstallStep.DownloadLibraries, 0.95d, TaskStatus.Running, 1, 1);
        return new FileInfo(jsonPath);
    }

    private async Task DownloadClientJarAsync(string jarPath, CancellationToken cancellationToken) {
        if (File.Exists(jarPath) && new FileInfo(jarPath).Length > 0) return;

        var directory = Path.GetDirectoryName(jarPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await using var remote = await HttpUtil.Client.GetStreamAsync(Entry.ClientJarUrl, cancellationToken);
        await using var local = File.Create(jarPath);
        await remote.CopyToAsync(local, cancellationToken);
    }

    private static string? FindClientJarUrl(JsonElement release) {
        if (!release.TryGetProperty("assets"u8, out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray()) {
            var name = asset.TryGetProperty("name"u8, out var nameNode) ? nameNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("server", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.Contains("client", StringComparison.OrdinalIgnoreCase)) continue;

            return asset.TryGetProperty("browser_download_url"u8, out var urlNode) ? urlNode.GetString() : null;
        }

        return null;
    }

    private static string? ReadMainClass(string jarPath) {
        try {
            using var archive = ZipFile.OpenRead(jarPath);
            var manifest = archive.GetEntry("META-INF/MANIFEST.MF");
            if (manifest is null) return null;

            using var reader = new StreamReader(manifest.Open());
            while (reader.ReadLine() is { } line) {
                if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                    return line["Main-Class:".Length..].Trim();
            }
        } catch (Exception) {
            // jar 不是 zip / 读不了都无所谓，退回默认主类。
        }

        return null;
    }

    private static ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, MinecraftEntry inheritedEntry,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, [inheritedEntry], out _) as ModifiedMinecraftEntry;
        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    #endregion

    /// <summary>按「点/下划线分段、逐段比数字」排序，兼容 7.3 / 7.3_04 / 8.0 这类 tag。</summary>
    private sealed class BtaVersionComparer : IComparer<string> {
        public static readonly BtaVersionComparer Instance = new();

        public int Compare(string? x, string? y) {
            var left = Parts(x ?? string.Empty);
            var right = Parts(y ?? string.Empty);
            var length = Math.Max(left.Length, right.Length);
            for (var i = 0; i < length; i++) {
                var a = i < left.Length ? left[i] : 0;
                var b = i < right.Length ? right[i] : 0;
                if (a != b) return a.CompareTo(b);
            }

            return 0;
        }

        private static int[] Parts(string value) => value
            .Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .ToArray();
    }
}
