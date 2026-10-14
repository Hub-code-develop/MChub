using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Text.Json;

namespace MinecraftLaunch.Components.Installer;

public sealed class FabricInstaller : InstallerBase {
    /// <summary>官方 Fabric 元数据根（v2），也是其它 Fabric 系分支的默认值。</summary>
    public const string DefaultMetaBaseUrl = "https://meta.fabricmc.net/v2";

    public string CustomId { get; init; }
    public FabricInstallEntry Entry { get; init; }
    /// <summary>
    /// 元数据根地址（形如 <c>https://meta.fabricmc.net/v2</c>）。
    /// Ornithe / LegacyFabric / Babric 等 Fabric 系装载器只是换一个兼容根地址。
    /// </summary>
    public string MetaBaseUrl { get; init; } = DefaultMetaBaseUrl;
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }

    public static FabricInstaller Create(string mcFolder, FabricInstallEntry installEntry, string customId = default,
        string metaBaseUrl = default) {
        return new FabricInstaller {
            CustomId = customId,
            Entry = installEntry,
            MetaBaseUrl = string.IsNullOrWhiteSpace(metaBaseUrl) ? DefaultMetaBaseUrl : metaBaseUrl.TrimEnd('/'),
            MinecraftFolder = mcFolder,
        };
    }

    public static async Task<IEnumerable<FabricInstallEntry>> EnumerableFabricAsync(string mcVersion,
        CancellationToken cancellationToken = default, string metaBaseUrl = default) {
        var baseUrl = string.IsNullOrWhiteSpace(metaBaseUrl) ? DefaultMetaBaseUrl : metaBaseUrl.TrimEnd('/');
        await using var json = await HttpUtil.FlurlClient.Request($"{baseUrl}/versions/loader/{mcVersion}")
            .GetStreamAsync(cancellationToken: cancellationToken);

        var entries = (await JsonSerializer.DeserializeAsync(json,
                FabricInstallEntryContext.Default.IEnumerableFabricInstallEntry, cancellationToken))
            .Select(entry => entry with { RequestedMcVersion = mcVersion })
            // 部分分支（如 Ornithe）不返回 separator，这里给个兜底，否则 Replace(null, ".") 会抛。
            .OrderByDescending(x => ToComparableVersion(x.Loader.Version, x.Loader.Separator));

        return entries;
    }

    private static Version ToComparableVersion(string version, string? separator) {
        var normalized = string.IsNullOrEmpty(separator) ? version : version.Replace(separator, ".");
        return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0);
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default) {
        ModifiedMinecraftEntry entry = default;
        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try {
            var inheritedEntry = ParseMinecraft(cancellationToken);

            var jsonFile = await DownloadVersionJsonAsync(inheritedEntry, cancellationToken);
            entry = ParseModifiedMinecraft(jsonFile, inheritedEntry, cancellationToken);
            await CompleteFabricLibrariesAsync(entry, cancellationToken);
        } catch (Exception ex) {
            ReportProgress(InstallStep.Interrupted, 1.0d, TaskStatus.Faulted, 1, 1);
            ReportCompleted(false, ex);
            throw;
        }

        ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
        ReportCompleted(true);
        return entry;
    }

    /// <summary>提前下载加载器版本配置，使其可与原版资源下载并行。</summary>
    public Task PreloadAsync(CancellationToken cancellationToken = default) =>
        DownloadProfileAsync(CustomId ?? $"fabric-loader-{Entry.Loader.Version}_{Entry.McVersion}", cancellationToken);

    #region Privates

    private MinecraftEntry ParseMinecraft(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.10d, TaskStatus.Running, 1, 0);

        if (InheritedMinecraft is not null) {
            return InheritedMinecraft;
        }

        var inheritedMinecraft = new MinecraftParser(MinecraftFolder).GetMinecrafts()
            .FirstOrDefault(x => x.Version.VersionId == Entry.McVersion);

        ReportProgress(InstallStep.ParseMinecraft, 0.15d, TaskStatus.Running, 1, 1);
        return inheritedMinecraft ?? throw new InvalidOperationException("The corresponding version's parent was not found."); ;
    }

    private async Task<FileInfo> DownloadVersionJsonAsync(MinecraftEntry entry, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.20d, TaskStatus.Running, 1, 0);

        if (CustomId is { } customId)
        {
            var cachedProfile = new FileInfo(Path.Combine(MinecraftFolder, "versions", customId, $"{customId}.json"));
            if (cachedProfile.Exists)
            {
                ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
                return cachedProfile;
            }
        }

        string requestUrl = $"{MetaBaseUrl}/versions/loader/{Entry.McVersion}/{Entry.BuildVersion}/profile/json";
        requestUrl = DownloadManager.BmclApi.TryFindUrl(requestUrl);

        await using var jsonStream = await HttpUtil.FlurlClient.Request(requestUrl)
            .GetStreamAsync(HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(jsonStream,cancellationToken:cancellationToken).ConfigureAwait(false);
        
        string instanceId = CustomId ??
                            doc.RootElement.GetPropertyNullable("id"u8)?.GetString() ??
                            $"fabric-loader-{Entry.Loader.Version}_{entry.Id}";

        var jsonFile = new FileInfo(Path
            .Combine(MinecraftFolder, "versions", instanceId, $"{instanceId}.json"));

        if (jsonFile.Exists) {
            ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
            return jsonFile;
        }

        if (!jsonFile.Directory!.Exists)
            jsonFile.Directory.Create();

        await using var output = File.OpenWrite(jsonFile.FullName);
        await JsonSerializer.SerializeAsync(output, doc, JsonDocumentSerializeContext.Default.JsonDocument,
            cancellationToken);
        
        ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
        return jsonFile;
    }

    private async Task<FileInfo> DownloadProfileAsync(string instanceId, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.20d, TaskStatus.Running, 1, 0);

        var jsonFile = new FileInfo(Path.Combine(MinecraftFolder, "versions", instanceId, $"{instanceId}.json"));
        if (jsonFile.Exists) {
            ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
            return jsonFile;
        }

        var requestUrl = DownloadManager.BmclApi.TryFindUrl(
            $"{MetaBaseUrl}/versions/loader/{Entry.McVersion}/{Entry.BuildVersion}/profile/json");
        await using var jsonStream = await HttpUtil.FlurlClient.Request(requestUrl)
            .GetStreamAsync(HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!jsonFile.Directory!.Exists) jsonFile.Directory.Create();
        await using var output = File.OpenWrite(jsonFile.FullName);
        await jsonStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
        return jsonFile;
    }

    private async Task CompleteFabricLibrariesAsync(MinecraftEntry minecraft, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadLibraries, 0.5d, TaskStatus.Running, 0, 0);

        var resourceDownloader = new MinecraftResourceDownloader(minecraft) {
            SourceRootDirectories = SourceRootDirectories
        };
        resourceDownloader.CopyProgressChanged += (_, x)
            => ReportProgress(InstallStep.CopyLibraries, x.Percentage.ToPercentage(0.5d, 0.75d),
                TaskStatus.Running, x.TotalCount, x.CompletedCount);
        resourceDownloader.ProgressChanged += (_, x)
            => ReportProgress(InstallStep.DownloadLibraries, x.ToPercentage().ToPercentage(0.75d, 0.95d),
                TaskStatus.Running, resourceDownloader.TotalCount,
                    x.CompletedCount, x.Speed, true);

        var result = await resourceDownloader.VerifyAndDownloadDependenciesAsync(cancellationToken: cancellationToken);
        if (result.Failed.Any())
            throw new IOException($"Failed to download {result.Failed.Count()} Fabric dependencies.");
    }

    private static ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, MinecraftEntry inheritedEntry,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, [inheritedEntry], out _) as ModifiedMinecraftEntry;

        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    #endregion
}
