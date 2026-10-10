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

    /// <summary>
    /// 运行组件（native 库 + 移动端 LWJGL jar）是否齐了。
    /// </summary>
    public static bool AreNativesInstalled =>
        AreNativeLibrariesInstalled && AreLwjglJarsInstalled;

    /// <summary>
    /// native 库（Pojav 系 / OpenAL / GL 翻译层）是否已落到**私有目录**。
    ///
    /// <p>这批库不再放进 APK 的 <c>lib/&lt;abi&gt;/</c>：上游预编译产物的 ELF 段对齐是 4 KB，
    /// 平台会判定不符合 16 KB 要求，让 App 跑在「页面大小兼容模式」并在启动时弹警告
    /// （实测过，去掉它们弹窗就消失）。改成随 APK 以 <c>assets/runtime/natives.zip</c> 预置、
    /// 首次启动解到私有目录，再由 JREUtils / PojavRuntimeSupport 按绝对路径装载。</p>
    /// </summary>
    public static bool AreNativeLibrariesInstalled
    {
        get
        {
            string[] required = ["libpojavexec.so", "libopenal.so", "libmobileglues.so"];
            return required.All(name =>
                File.Exists(Path.Combine(MobileRuntimePaths.NativesLibraryDirectory, name)));
        }
    }

    /// <summary>
    /// 移动端 LWJGL jar 是否已落到私有目录。
    ///
    /// <p>jar 必须解成真实文件：它要进 <c>-Djava.class.path</c>，无法直接用 asset 流。
    /// 注意必须用**移动端补丁版**（GLFW 由 Java 侧 CallbackBridge 实现），
    /// 拿桌面原版会在加载原生库时失败。</p>
    /// </summary>
    public static bool AreLwjglJarsInstalled =>
        File.Exists(Path.Combine(MobileRuntimePaths.NativesJarDirectory,
            $"lwjgl-{MobileRuntimeCatalog.LwjglVersion}-merged-modules.jar"));

    /// <summary>APK 内是否预置了该主版本的 JRE 归档。</summary>
    public static bool HasBundledRuntime(int majorVersion)
        => AssetExists(MobileRuntimePaths.BundledJreAssetPath(
            MobileRuntimeCatalog.NormalizeRuntimeMajor(majorVersion)));

    /// <summary>APK 内是否预置了移动端 LWJGL jar（以 zip 形式预置）。</summary>
    public static bool HasBundledJars
        => AssetExists(MobileRuntimePaths.BundledJwjglArchivePath(MobileRuntimeCatalog.LwjglVersion));

    /// <summary>APK 内是否预置了原生库归档（assets/runtime/natives.zip）。</summary>
    public static bool HasBundledNatives
        => AssetExists(MobileRuntimePaths.BundledNativesArchivePath);

    /// <summary>
    /// 把 APK 内预置的原生库解到私有目录（**不需要联网**）。
    ///
    /// <p>为什么必须解成真实文件：这些库要按**绝对路径** <c>System.load</c> / <c>dlopen</c>，
    /// linker 只认可读的真实文件（asset 流不行）；而且 Mesa / ANGLE 这类库还要靠
    /// 「驱动就在自己旁边」（$ORIGIN）去找同目录的其它 .so。</p>
    /// </summary>
    public static async Task InstallBundledNativesAsync(
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var assetPath = MobileRuntimePaths.BundledNativesArchivePath;
        if (!AssetExists(assetPath))
            throw new InvalidOperationException(
                $"APK 内没有预置原生库归档（assets/{assetPath}），运行组件不完整");

        var destination = MobileRuntimePaths.NativesLibraryDirectory;
        Directory.CreateDirectory(destination);

        progress?.Report(new MobileInstallProgress("解出原生库…", 0));

        // asset 流未必可随机访问，ZipArchive 顺序读更稳：先落到临时文件。
        var temp = Path.Combine(Path.GetTempPath(), "mchub-natives.zip");
        try
        {
            await CopyAssetToFileAsync(assetPath, temp, cancellationToken);

            using var archive = ZipFile.OpenRead(temp);
            var entries = archive.Entries
                .Where(entry => entry.Name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (entries.Length == 0)
                throw new InvalidOperationException($"原生库归档里没有 .so：{assetPath}");

            var done = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new MobileInstallProgress(
                    $"解出 {entry.Name}…", Progress(done, entries.Length)));
                entry.ExtractToFile(Path.Combine(destination, entry.Name), overwrite: true);
                done++;
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// 从 APK 内预置资产安装 JRE（**不需要联网**）：把归档解出到私有目录。
    /// </summary>
    public static async Task<string> InstallBundledRuntimeAsync(int requiredMajorVersion,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var majorVersion = MobileRuntimeCatalog.NormalizeRuntimeMajor(requiredMajorVersion);
        var assetPath = MobileRuntimePaths.BundledJreAssetPath(majorVersion);

        progress?.Report(new MobileInstallProgress($"解出 Java {majorVersion} 运行时…", 0));

        // 解压器需要可随机访问的文件，先把 asset 落到私有临时文件再解。
        var temp = Path.Combine(Path.GetTempPath(),
            $"jre{majorVersion}-{MobileRuntimeCatalog.JreAbi}.tar.xz");
        try
        {
            await CopyAssetToFileAsync(assetPath, temp, cancellationToken);
            progress?.Report(new MobileInstallProgress("解压 Java 运行时…", 0.4));
            return await MobileJavaRuntime.ImportArchiveAsync(temp, majorVersion, null, cancellationToken);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// 把 APK 内预置的移动端 LWJGL jar 解到私有目录 —— classpath 需要真实文件路径，
    /// 不能直接用 asset 流。
    /// </summary>
    public static async Task InstallBundledJarsAsync(string lwjglVersion,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var assetPath = MobileRuntimePaths.BundledJwjglArchivePath(lwjglVersion);
        if (!AssetExists(assetPath))
            throw new InvalidOperationException(
                $"APK 内没有预置 LWJGL jar 归档（assets/{assetPath}），运行组件不完整");

        var destination = MobileRuntimePaths.NativesJarDirectory;
        Directory.CreateDirectory(destination);

        progress?.Report(new MobileInstallProgress($"解出 LWJGL {lwjglVersion} jar…", 0));

        // asset 流未必可随机访问，ZipArchive 顺序读更稳：先落到临时文件。
        var temp = Path.Combine(Path.GetTempPath(), $"lwjgl-{lwjglVersion}.zip");
        try
        {
            await CopyAssetToFileAsync(assetPath, temp, cancellationToken);

            using var archive = ZipFile.OpenRead(temp);
            var entries = archive.Entries
                .Where(entry => entry.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (entries.Length == 0)
                throw new InvalidOperationException($"LWJGL 归档里没有 jar：{assetPath}");

            var done = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new MobileInstallProgress(
                    $"解出 {entry.Name}…", Progress(done, entries.Length)));

                await using var output = File.Create(Path.Combine(destination, entry.Name));
                await using var input = entry.Open();
                await input.CopyToAsync(output, cancellationToken);
                done++;
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>APK 内预置了哪些 LWJGL 原生库版本（从 assets 下的 <c>&lt;ver&gt;.zip</c> 推断）。</summary>
    public static IReadOnlyList<string> BundledLwjglNativesVersions =>
        ListAssets(MobileRuntimePaths.BundledLwjglNativesAssetRoot)
            .Where(name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(name => name[..^".zip".Length])
            .Where(name => name.Length > 0)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>APK 内是否预置了该版本的 LWJGL 原生库。</summary>
    public static bool HasBundledLwjglNatives(string lwjglVersion)
        => AssetExists(MobileRuntimePaths.BundledLwjglNativesArchivePath(lwjglVersion));

    /// <summary>
    /// 把 APK 内预置的**各版本** LWJGL 原生库解到私有目录的对应版本子目录，返回成功解出的版本。
    ///
    /// <p>必须分版本解：不同 MC 版本用不同 LWJGL，而 3.3.3 与 3.4.1 有同名 .so，
    /// 放同一目录会互相覆盖，所以各自一个目录，启动时按实例需要的版本前置到 library path。</p>
    /// </summary>
    public static async Task<IReadOnlyList<string>> InstallBundledLwjglNativesAsync(
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var versions = BundledLwjglNativesVersions;
        var installed = new List<string>();
        var done = 0;

        foreach (var version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var assetPath = MobileRuntimePaths.BundledLwjglNativesArchivePath(version);
            if (!AssetExists(assetPath))
                continue;

            var destination = MobileRuntimePaths.LwjglNativesDirectoryFor(version);
            Directory.CreateDirectory(destination);

            // asset 流未必可随机访问，先落临时文件让 ZipArchive 顺序读。
            var temp = Path.Combine(Path.GetTempPath(), $"lwjgl-natives-{version}.zip");
            try
            {
                await CopyAssetToFileAsync(assetPath, temp, cancellationToken);

                using var archive = ZipFile.OpenRead(temp);
                var entries = archive.Entries
                    .Where(entry => entry.Name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (entries.Length == 0)
                    continue;

                var extracted = 0;
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = Path.Combine(destination, entry.Name);
                    if (File.Exists(target))
                        continue; // 已解过，重复调用不必再写

                    await using var output = File.Create(target);
                    await using var input = entry.Open();
                    await input.CopyToAsync(output, cancellationToken);
                    extracted++;
                }

                installed.Add(version);
                progress?.Report(new MobileInstallProgress(
                    $"解出 LWJGL {version} 原生库（{extracted}/{entries.Length}）…",
                    Progress(done, versions.Count)));
                done++;
            }
            finally
            {
                TryDelete(temp);
            }
        }

        return installed;
    }

    private static Android.Content.Res.AssetManager Assets =>
        Android.App.Application.Context.Assets!;

    private static bool AssetExists(string assetPath)
    {
        try
        {
            using var stream = Assets.Open(assetPath);
            return true;
        }
        catch (Java.IO.FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string[] ListAssets(string assetDirectory)
    {
        try
        {
            return Assets.List(assetDirectory) ?? [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static async Task CopyAssetToFileAsync(string assetPath, string target,
        CancellationToken cancellationToken)
    {
        using var source = Assets.Open(assetPath);
        await using var destination = File.Create(target);
        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// 安装 JRE：按实例要求的 Java 版本自动选一个可下载的版本（8/17/21/25，arm64）。
    /// （仅作预置方案缺失时的兜底，正常路径见 <see cref="InstallBundledRuntimeAsync"/>。）
    /// </summary>
    public static async Task<string> InstallRuntimeAsync(int requiredMajorVersion,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var majorVersion = MobileRuntimeCatalog.NormalizeRuntimeMajor(requiredMajorVersion);
        var archiveName = $"jre{majorVersion}-android-{MobileRuntimeCatalog.JreAbi}.tar.xz";

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
