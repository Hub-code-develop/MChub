using System.IO.Compression;
using System.Text.Json;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;

namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// Rift 安装器（1.13 / 1.13.2，尽力而为）。
///
/// <para>Rift 的官方 GitHub 已归档、Maven 服务器已下线，现存唯一还能拿到的分发是
/// CurseForge 上的 MultiMC 实例包（<c>rift-1.0.4-66.zip</c>）与 Rift 安装器 jar。
/// 因此本安装器不「编」一个 Rift 版本 JSON，而是从这两个真实产物里提取：
/// ① 从 MultiMC 包里找内嵌的装载器 patch（含 mainClass/libraries）；
/// ② 顺带把包里附带的库解出来；
/// ③ 从 CurseForge 拉 Rift jar 落到库目录。
/// 任何一步拿不到真实数据都会明确报错，而不是拿假配置糊过去。</para>
/// </summary>
public sealed class RiftInstaller : InstallerBase {
    /// <summary>CurseForge 上的 MultiMC 实例包直链（file id 2610746）。</summary>
    public const string PackageUrl = "https://media.forgecdn.net/files/2610/746/rift-1.0.4-66.zip";

    /// <summary>CurseForge 上的 Rift 安装器 jar 直链（file id 2610741）。</summary>
    public const string RiftJarUrl = "https://media.forgecdn.net/files/2610/741/Rift-1.0.4-66.jar";

    /// <summary>Rift 库坐标（用于把 jar 落到正确的 libraries 路径）。</summary>
    private const string RiftLibraryCoordinate = "org.dimdev:rift:1.0.4-66";

    public string CustomId { get; init; }
    public RiftInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }

    public static RiftInstaller Create(string mcFolder, RiftInstallEntry installEntry, string customId = default) {
        return new RiftInstaller {
            CustomId = customId,
            Entry = installEntry,
            MinecraftFolder = mcFolder,
        };
    }

    /// <summary>Rift 只支持 1.13 / 1.13.2。</summary>
    public static bool IsCompatibleMcVersion(string mcVersion) =>
        mcVersion is "1.13" or "1.13.2";

    public static Task<IEnumerable<RiftInstallEntry>> EnumerableRiftAsync(string mcVersion,
        CancellationToken cancellationToken = default) {
        if (!IsCompatibleMcVersion(mcVersion))
            return Task.FromResult<IEnumerable<RiftInstallEntry>>([]);

        IEnumerable<RiftInstallEntry> entries =
        [
            new RiftInstallEntry {
                LoaderVersion = "1.0.4-66",
                PackageUrl = PackageUrl,
                TargetMcVersion = mcVersion,
            }
        ];
        return Task.FromResult(entries);
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default) {
        ModifiedMinecraftEntry entry = default;
        ReportProgress(InstallStep.Started, 0d, TaskStatus.WaitingToRun, 1, 1);

        try {
            var inheritedEntry = ParseMinecraft(cancellationToken);
            var profile = await ExtractProfileAsync(cancellationToken);
            var jsonFile = await WriteVersionAsync(inheritedEntry, profile, cancellationToken);
            entry = ParseModifiedMinecraft(jsonFile, inheritedEntry, cancellationToken);
            await CompleteDependenciesAsync(entry, cancellationToken);
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

    private string ResolveInstanceId() => CustomId ?? $"{Entry.TargetMcVersion}-rift-{Entry.LoaderVersion}";

    private MinecraftEntry ParseMinecraft(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.05d, TaskStatus.Running, 1, 0);

        if (InheritedMinecraft is not null) return InheritedMinecraft;

        var inherited = new MinecraftParser(MinecraftFolder).GetMinecrafts()
            .FirstOrDefault(x => x.Version.VersionId.Equals(Entry.TargetMcVersion, StringComparison.OrdinalIgnoreCase));

        ReportProgress(InstallStep.ParseMinecraft, 0.10d, TaskStatus.Running, 1, 1);
        return inherited ?? throw new InvalidOperationException(
            $"Rift 需要先安装原版 {Entry.TargetMcVersion} 作为父版本，但没有找到。");
    }

    /// <summary>
    /// 从 CurseForge 的 MultiMC 包里提取真正的装载器 profile，并把包内附带的库解到本地。
    /// 找不到真实 profile 就报错（Rift 的分发已停，宁可失败也不编配置）。
    /// </summary>
    private async Task<RiftProfile> ExtractProfileAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.15d, TaskStatus.Running, 1, 0);

        var tempPath = Path.Combine(Path.GetTempPath(), $"mchub-rift-{Guid.NewGuid():N}.zip");
        try {
            await using (var remote = await HttpUtil.Client.GetStreamAsync(Entry.PackageUrl, cancellationToken))
            await using (var local = File.Create(tempPath))
            {
                await remote.CopyToAsync(local, cancellationToken);
            }

            using var archive = ZipFile.OpenRead(tempPath);

            RiftProfile? profile = null;
            foreach (var zipEntry in archive.Entries) {
                if (!zipEntry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = TryReadProfile(zipEntry);
                if (candidate is null) continue;

                // 优先取 patches/ 下或名字带 rift 的 patch。
                var path = zipEntry.FullName;
                if (path.Contains("patches/", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("rift", StringComparison.OrdinalIgnoreCase)) {
                    profile = candidate;
                    break;
                }

                profile ??= candidate;
            }

            if (profile is null)
                throw new InvalidOperationException(
                    "Rift 包（rift-1.0.4-66.zip）里没有找到可用的版本配置。Rift 上游分发已停止，请手动导入该实例包。");

            ExtractBundledLibraries(archive);
            return profile;
        } finally {
            TryDelete(tempPath);
        }
    }

    private static RiftProfile? TryReadProfile(ZipArchiveEntry zipEntry) {
        try {
            using var stream = zipEntry.Open();
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("mainClass"u8, out var mainClassNode)) return null;
            var mainClass = mainClassNode.GetString();
            if (string.IsNullOrWhiteSpace(mainClass)) return null;

            var libraries = root.TryGetProperty("libraries"u8, out var libs) ? libs.GetRawText() : "[]";
            var arguments = root.TryGetProperty("arguments"u8, out var args) ? args.GetRawText() : null;
            var minecraftArguments = root.TryGetProperty("minecraftArguments"u8, out var mcArgs)
                ? mcArgs.GetString()
                : null;

            return new RiftProfile(mainClass!, libraries, arguments, minecraftArguments);
        } catch (JsonException) {
            return null;
        }
    }

    private void ExtractBundledLibraries(ZipArchive archive) {
        foreach (var zipEntry in archive.Entries) {
            if (zipEntry.FullName.EndsWith('/')) continue;
            var marker = zipEntry.FullName.IndexOf("libraries/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;

            var relative = zipEntry.FullName[marker..];
            var destination = Path.Combine(MinecraftFolder, relative.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            zipEntry.ExtractToFile(destination, true);
        }
    }

    private async Task<FileInfo> WriteVersionAsync(MinecraftEntry inheritedEntry, RiftProfile profile,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.55d, TaskStatus.Running, 1, 0);

        var instanceId = ResolveInstanceId();
        var directory = Path.Combine(MinecraftFolder, "versions", instanceId);
        Directory.CreateDirectory(directory);

        // 把 Rift jar 落到库目录，避免依赖已下线的 dimdev maven。
        await EnsureRiftJarAsync(cancellationToken);

        var jsonPath = Path.Combine(directory, $"{instanceId}.json");
        var json = $"{{\"id\":{JsonSerializer.Serialize(instanceId)}," +
                   $"\"inheritsFrom\":{JsonSerializer.Serialize(inheritedEntry.Id)}," +
                   $"\"mainClass\":{JsonSerializer.Serialize(profile.MainClass)}," +
                   $"\"libraries\":{profile.Libraries}," +
                   (profile.Arguments is not null ? $"\"arguments\":{profile.Arguments}," : string.Empty) +
                   (profile.MinecraftArguments is not null
                       ? $"\"minecraftArguments\":{JsonSerializer.Serialize(profile.MinecraftArguments)},"
                       : string.Empty) +
                   "\"assetIndex\":{\"id\":\"1.13\"}}";
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);

        ReportProgress(InstallStep.DownloadVersionJson, 0.70d, TaskStatus.Running, 1, 1);
        return new FileInfo(jsonPath);
    }

    private async Task EnsureRiftJarAsync(CancellationToken cancellationToken) {
        var parts = RiftLibraryCoordinate.Split(':');
        if (parts.Length != 3) return;

        var relative = Path.Combine("libraries",
            parts[0].Replace('.', Path.DirectorySeparatorChar),
            parts[1], parts[2], $"{parts[1]}-{parts[2]}.jar");
        var destination = Path.Combine(MinecraftFolder, relative);
        if (File.Exists(destination) && new FileInfo(destination).Length > 0) return;

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await using var remote = await HttpUtil.Client.GetStreamAsync(RiftJarUrl, cancellationToken);
        await using var local = File.Create(destination);
        await remote.CopyToAsync(local, cancellationToken);
    }

    private async Task CompleteDependenciesAsync(MinecraftEntry minecraft, CancellationToken cancellationToken) {
        ReportProgress(InstallStep.DownloadLibraries, 0.80d, TaskStatus.Running, 0, 0);

        var downloader = new MinecraftResourceDownloader(minecraft) {
            SourceRootDirectories = SourceRootDirectories
        };
        downloader.ProgressChanged += (_, x)
            => ReportProgress(InstallStep.DownloadLibraries, x.ToPercentage().ToPercentage(0.80d, 0.98d),
                TaskStatus.Running, downloader.TotalCount, x.CompletedCount, x.Speed, true);

        await downloader.VerifyAndDownloadDependenciesAsync(cancellationToken: cancellationToken);
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) File.Delete(path);
        } catch (Exception) {
            // 临时文件删不掉不影响安装。
        }
    }

    private static ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, MinecraftEntry inheritedEntry,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, [inheritedEntry], out _) as ModifiedMinecraftEntry;
        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    private sealed record RiftProfile(string MainClass, string Libraries, string? Arguments, string? MinecraftArguments);

    #endregion
}
