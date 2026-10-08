using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Compressors.Xz;

namespace MChub.Mobile.Services;

/// <summary>
/// 移动端 Java 运行时的落盘与查找。
///
/// <p><b>为什么是"导入"而不是"下载"</b>：Android 能用的不是桌面 JDK，而是
/// Amethyst(AngelAuraMC) / PojavLauncher 系的 android-openjdk-build-multiarch 产物
/// （携带 arm64 的 libjvm.so）。这些运行时只随它们的 APK 分发或在 GitHub Actions 产物里
/// （需登录），**没有稳定的公开下载地址**，因此这里不做"假装能自动下载"：
/// 支持导入用户手里的 <c>.tar.xz</c> / <c>.zip</c> 归档，或直接放入已解压目录。</p>
/// </summary>
internal static class MobileJavaRuntime
{
    /// <summary>运行时根目录（应用私有目录）。</summary>
    public static string RootDirectory => MobileRuntimePaths.JavaRuntimeDirectory;

    /// <summary>libjvm.so 的候选相对路径（与 Java / native 侧的探测顺序一致）。</summary>
    private static readonly string[] JvmLibraryCandidates =
    [
        Path.Combine("lib", "server", "libjvm.so"),
        Path.Combine("lib", "aarch64", "server", "libjvm.so"),
        Path.Combine("lib", "arm64", "server", "libjvm.so")
    ];

    private const string ReleaseFileName = "release";

    /// <summary>
    /// 找到可用的运行时根目录（含 <c>release</c> 与 <c>libjvm.so</c>）。
    ///
    /// <p>归档解压后往往多一层顶层目录（如 <c>jre17/</c>），所以先看根目录本身，
    /// 再往下找一层。</p>
    /// </summary>
    public static string? FindRuntimeRoot()
    {
        var root = RootDirectory;
        if (IsRuntimeRoot(root))
            return root;

        if (!Directory.Exists(root))
            return null;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (IsRuntimeRoot(directory))
                return directory;
        }

        return null;
    }

    /// <summary>某个目录是否是运行时根（有 release 且有 libjvm.so）。</summary>
    public static bool IsRuntimeRoot(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        if (!File.Exists(Path.Combine(directory, ReleaseFileName)))
            return false;

        return JvmLibraryCandidates.Any(candidate => File.Exists(Path.Combine(directory, candidate)));
    }

    /// <summary>java 可执行文件路径（存在就返回它，否则给出约定路径）。</summary>
    public static string ResolveJavaExecutable(string runtimeRoot)
    {
        var candidate = Path.Combine(runtimeRoot, "bin", "java");
        return File.Exists(candidate) ? candidate : candidate;
    }

    /// <summary>
    /// 从 <c>release</c> 文件里读 JAVA_VERSION 并解析主版本号。
    ///
    /// <p>启动参数的组装需要主版本号（例如是否要加 <c>-Dfile.encoding</c> 之类的编码参数），
    /// 但移动端不执行 <c>java -version</c>，所以直接读文本，不启进程。</p>
    /// </summary>
    public static bool TryReadJavaVersion(string runtimeRoot, out string version, out int majorVersion)
    {
        version = string.Empty;
        majorVersion = 0;

        var release = Path.Combine(runtimeRoot, ReleaseFileName);
        if (!File.Exists(release))
            return false;

        foreach (var rawLine in File.ReadLines(release))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal))
                continue;

            version = line["JAVA_VERSION=".Length..].Trim().Trim('"');
            majorVersion = ParseMajorVersion(version);
            return majorVersion > 0;
        }

        return false;
    }

    /// <summary>把 "17.0.9" / "1.8.0_392" 解析成主版本号（17 / 8）。</summary>
    public static int ParseMajorVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return 0;

        var parts = version.Split('.', '-', '_', '+');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
            return 0;

        // 老式版本号 1.8.0_392 → 主版本是第二段。
        if (first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second))
            return second;

        return first;
    }

    /// <summary>
    /// 导入运行时归档：<c>.tar.xz</c> / <c>.xz</c> / <c>.tar</c> / <c>.zip</c>。
    ///
    /// <p>归档先解压到临时目录，再整体搬到运行时根目录，避免中途失败留下半个运行时
    /// 被误判成"已安装"。</p>
    /// </summary>
    public static async Task<string> ImportArchiveAsync(string archivePath, IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("归档不存在", archivePath);

        var root = RootDirectory;
        var staging = Path.Combine(Path.GetTempPath(), "mchub-runtime-" + Guid.NewGuid().ToString("N"));

        try
        {
            progress?.Report("正在解压归档…");
            Directory.CreateDirectory(staging);
            await ExtractArchiveAsync(archivePath, staging, cancellationToken);

            // 解压结果可能多一层顶层目录，先定位真正的运行时根。
            var stagedRoot = FindRuntimeRootIn(staging)
                             ?? throw new InvalidOperationException("归档里找不到 release 与 libjvm.so，可能不是可用的移动端运行时");

            progress?.Report("正在安装运行时…");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            CopyDirectory(stagedRoot, root, cancellationToken);

            if (!IsRuntimeRoot(root))
                throw new InvalidOperationException("安装后的运行时校验失败（缺少 release 或 libjvm.so）");

            return root;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响导入结果。
            }
        }
    }

    private static string? FindRuntimeRootIn(string directory)
    {
        if (IsRuntimeRoot(directory))
            return directory;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (IsRuntimeRoot(child))
                return child;
        }

        return null;
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destination,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(archivePath).ToLowerInvariant();

        if (extension == ".zip")
        {
            ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
            return;
        }

        await using var file = File.OpenRead(archivePath);
        if (extension == ".xz")
        {
            // Android 端的运行时归档是 .tar.xz：先 XZ 解压成 tar，再按 tar 展开。
            await using var xz = new XZStream(file);
            TarFile.ExtractToDirectory(xz, destination, overwriteFiles: true);
            return;
        }

        if (extension is ".tar")
        {
            TarFile.ExtractToDirectory(file, destination, overwriteFiles: true);
            return;
        }

        throw new NotSupportedException($"不支持的归档格式：{extension}（请用 .tar.xz 或 .zip）");
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
