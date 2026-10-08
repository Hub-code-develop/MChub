using System.IO.Compression;
using System.Net.Http;

namespace MChub.Mobile.Services;

/// <summary>安装/下载进度（<see cref="Fraction"/> 为 0~1，用于进度条）。</summary>
internal sealed record MobileInstallProgress(string Stage, double Fraction);

/// <summary>
/// 从公开直链下载并安装移动端运行组件（JRE + 原生库 + LWJGL jar）。
///
/// <p>分工：本类只管"取文件 + 落盘"；JRE 归档的解压安装复用
/// <see cref="MobileJavaRuntime.ImportArchiveAsync"/>，避免两套解压逻辑。</p>
/// </summary>
internal static class MobileRuntimeInstaller
{
    private static readonly HttpClient Client = CreateClient();

    /// <summary>是否已有满足该主版本要求的 JRE（主版本 ≥ 要求即可）。</summary>
    public static bool IsRuntimeInstalled(int requiredMajorVersion)
        => MobileJavaRuntime.FindRuntimeRoot(requiredMajorVersion) is not null;

    /// <summary>运行组件（LWJGL / OpenAL / GL 翻译层 + 移动端 LWJGL jar）是否齐了。</summary>
    public static bool AreNativesInstalled
    {
        get
        {
            var libraries = MobileRuntimePaths.NativesLibraryDirectory;
            var jars = MobileRuntimePaths.NativesJarDirectory;

            return File.Exists(Path.Combine(libraries, "liblwjgl.so"))
                   && File.Exists(Path.Combine(libraries, "libopenal.so"))
                   && File.Exists(Path.Combine(libraries, "libgl4es_114.so"))
                   && File.Exists(Path.Combine(jars, $"lwjgl-{MobileRuntimeCatalog.LwjglVersion}-merged-modules.jar"));
        }
    }

    /// <summary>
    /// 安装 JRE：按实例要求的 Java 版本自动选一个可下载的版本（8/17/21/25，arm64）。
    /// </summary>
    public static async Task<string> InstallRuntimeAsync(int requiredMajorVersion,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var majorVersion = MobileRuntimeCatalog.NormalizeRuntimeMajor(requiredMajorVersion);
        var archiveName = $"jre{majorVersion}-android-{MobileRuntimeCatalog.Abi}.tar.xz";

        progress?.Report(new MobileInstallProgress($"下载 Java {majorVersion} 运行时…", 0));

        var archivePath = await DownloadFirstAvailableAsync(
            MobileRuntimeCatalog.GetRuntimeUrls(majorVersion), archiveName,
            progress, $"下载 Java {majorVersion} 运行时", cancellationToken);

        try
        {
            progress?.Report(new MobileInstallProgress("解压 Java 运行时…", 0.9));
            return await MobileJavaRuntime.ImportArchiveAsync(archivePath, majorVersion, null, cancellationToken);
        }
        finally
        {
            TryDelete(archivePath);
        }
    }

    /// <summary>
    /// 安装运行组件：原生库归档（AAR）+ 散装 GL 翻译层 + 移动端 LWJGL jar。
    /// </summary>
    public static async Task InstallNativesAsync(IProgress<MobileInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var libraries = MobileRuntimePaths.NativesLibraryDirectory;
        var jars = MobileRuntimePaths.NativesJarDirectory;
        Directory.CreateDirectory(libraries);
        Directory.CreateDirectory(jars);

        var archives = MobileRuntimeCatalog.GetNativeArchives();
        var jarNames = MobileRuntimeCatalog.GetLwjglJarNames();
        var graphicsNames = MobileRuntimeCatalog.GetGraphicsLibraryNames();
        var supplementary = MobileRuntimeCatalog.GetSupplementaryLibraries();
        var total = archives.Count + jarNames.Count + graphicsNames.Count + supplementary.Count + 1;
        var done = 0;

        // 1) 原生库归档（AAR）：按 abi 挑出 .so。
        foreach (var archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MobileInstallProgress($"下载 {archive.Name}…", Progress(done, total)));

            var archivePath = await DownloadFirstAvailableAsync(archive.Urls,
                $"{archive.Name}-{MobileRuntimeCatalog.Abi}.aar", progress, $"下载 {archive.Name}", cancellationToken);
            try
            {
                var extracted = ExtractNativeLibraries(archivePath, libraries);
                if (extracted == 0)
                    throw new InvalidOperationException(
                        $"{archive.Name} 归档里没有 {MobileRuntimeCatalog.Abi} 的原生库，上游结构可能变了");
            }
            finally
            {
                TryDelete(archivePath);
            }

            done++;
        }

        // 2) 散装 GL 翻译层与辅助原生库。
        foreach (var libraryName in graphicsNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MobileInstallProgress($"下载 {libraryName}…", Progress(done, total)));
            await DownloadFirstAvailableToFileAsync(MobileRuntimeCatalog.GetGraphicsLibraryUrls(libraryName),
                Path.Combine(libraries, libraryName), cancellationToken);
            done++;
        }

        // 3) 上游没有公开产物的补充库（当前为空，留好扩展位）。
        foreach (var file in supplementary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MobileInstallProgress($"下载 {file.Name}…", Progress(done, total)));
            await DownloadFirstAvailableToFileAsync(file.Urls, Path.Combine(libraries, file.Name),
                cancellationToken);
            done++;
        }

        // 4) 移动端 LWJGL jar（与原生库版本必须一致）。
        foreach (var jarName in jarNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MobileInstallProgress($"下载 {jarName}…", Progress(done, total)));
            await DownloadFirstAvailableToFileAsync(MobileRuntimeCatalog.GetLwjglJarUrls(jarName),
                Path.Combine(jars, jarName), cancellationToken);
            done++;
        }

        progress?.Report(new MobileInstallProgress("运行组件已就绪", 1));
    }

    /// <summary>
    /// 把 AAR 里本 ABI 的 .so 解到目标目录。
    ///
    /// <p>上游 AAR 的实际布局是 <c>assets/components/&lt;name&gt;/&lt;abi&gt;/*.so</c>，
    /// 不是标准的 <c>jni/&lt;abi&gt;/</c>，所以这里按"路径里出现 <c>/&lt;abi&gt;/</c>"匹配，
    /// 两种布局都能吃。</p>
    /// </summary>
    private static int ExtractNativeLibraries(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var marker = $"/{MobileRuntimeCatalog.Abi}/";
        var extracted = 0;

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".so", StringComparison.Ordinal) ||
                entry.FullName.IndexOf(marker, StringComparison.Ordinal) < 0)
                continue;

            entry.ExtractToFile(Path.Combine(destination, Path.GetFileName(entry.FullName)), overwrite: true);
            extracted++;
        }

        return extracted;
    }

    private static double Progress(int done, int total) => total == 0 ? 0 : Math.Min(0.95, (double)done / total);

    /// <summary>按顺序尝试候选地址，下载到临时文件后返回其路径。</summary>
    private static async Task<string> DownloadFirstAvailableAsync(IReadOnlyList<string> urls, string fileName,
        IProgress<MobileInstallProgress>? progress, string stage, CancellationToken cancellationToken)
    {
        var target = Path.Combine(Path.GetTempPath(), fileName);
        await DownloadFirstAvailableToFileAsync(urls, target, cancellationToken, progress, stage);
        return target;
    }

    private static async Task DownloadFirstAvailableToFileAsync(IReadOnlyList<string> urls, string target,
        CancellationToken cancellationToken, IProgress<MobileInstallProgress>? progress = null,
        string? stage = null)
    {
        Exception? lastError = null;

        foreach (var url in urls)
        {
            try
            {
                await DownloadAsync(url, target, progress, stage, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
            }
        }

        throw new InvalidOperationException(
            $"所有候选地址都下载失败（{string.Join(" | ", urls)}）：{lastError?.Message}", lastError);
    }

    private static async Task DownloadAsync(string url, string target,
        IProgress<MobileInstallProgress>? progress, string? stage, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = File.Create(target))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;

                if (totalBytes > 0 && progress is not null && stage is not null)
                    progress.Report(new MobileInstallProgress(stage, Math.Min(0.95, (double)written / totalBytes)));
            }

            if (written == 0)
                throw new InvalidOperationException($"下载到 0 字节：{url}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub 的 raw / release 直链对 UA 有要求，缺 UA 会被拒。
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MChub-Android/1.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
