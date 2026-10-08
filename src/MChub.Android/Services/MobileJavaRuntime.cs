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
    /// <summary>运行时根目录（各主版本以子目录并存于此）。</summary>
    public static string RootDirectory => MobileRuntimePaths.JavaRuntimesRootDirectory;

    /// <summary>libjvm.so 的候选相对路径（与 Java / native 侧的探测顺序一致）。</summary>
    private static readonly string[] JvmLibraryCandidates =
    [
        Path.Combine("lib", "server", "libjvm.so"),
        Path.Combine("lib", "aarch64", "server", "libjvm.so"),
        Path.Combine("lib", "arm64", "server", "libjvm.so")
    ];

    private const string ReleaseFileName = "release";

    /// <summary>已安装的运行时（Java 主版本 → 目录）。</summary>
    public static IReadOnlyDictionary<int, string> GetInstalledRuntimes()
    {
        var result = new Dictionary<int, string>();
        if (!Directory.Exists(RootDirectory))
            return result;

        foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
        {
            if (!IsRuntimeRoot(directory))
                continue;

            if (int.TryParse(Path.GetFileName(directory), out var majorVersion) && majorVersion > 0)
                result[majorVersion] = directory;
        }

        return result;
    }

    /// <summary>
    /// 取满足要求的运行时：主版本 ≥ 要求里最小的那个。
    ///
    /// <p>不满足要求时返回 null（让上层去装对应版本），**不拿低版本硬跑** ——
    /// 拿 JRE 17 跑要求 21 的 MC 会在类加载阶段直接崩。</p>
    /// </summary>
    public static string? FindRuntimeRoot(int requiredMajorVersion)
    {
        var installed = GetInstalledRuntimes();
        var candidate = installed.Keys
            .Where(majorVersion => majorVersion >= requiredMajorVersion)
            .OrderBy(majorVersion => majorVersion)
            .Cast<int?>()
            .FirstOrDefault();

        return candidate is { } major ? installed[major] : null;
    }

    /// <summary>随便取一个已安装的运行时（仅用于状态展示，不用于启动）。</summary>
    public static string? FindAnyRuntimeRoot()
        => GetInstalledRuntimes()
            .OrderByDescending(pair => pair.Key)
            .Select(pair => pair.Value)
            .FirstOrDefault();

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
    /// <p>归档先解压到临时目录，再整体搬到 <c>Runtimes/Java/&lt;主版本&gt;</c>，
    /// 避免中途失败留下半个运行时被误判成"已安装"。</p>
    /// </summary>
    /// <param name="majorVersion">
    /// 安装到哪个主版本目录；传 <c>0</c>（或负数）表示按归档里的 <c>release</c> 自动判定。
    /// </param>
    public static async Task<string> ImportArchiveAsync(string archivePath, int majorVersion,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("归档不存在", archivePath);

        var staging = Path.Combine(Path.GetTempPath(), "mchub-runtime-" + Guid.NewGuid().ToString("N"));

        try
        {
            progress?.Report("正在解压归档…");
            Directory.CreateDirectory(staging);
            await ExtractArchiveAsync(archivePath, staging, cancellationToken);

            // 解压结果可能多一层顶层目录，先定位真正的运行时根。
            var stagedRoot = FindRuntimeRootIn(staging)
                             ?? throw new InvalidOperationException("归档里找不到 release 与 libjvm.so，可能不是可用的移动端运行时");

            // 手动导入的归档不一定会告诉我们版本，按 release 里的 JAVA_VERSION 判定更可靠。
            var targetMajor = majorVersion > 0 ? majorVersion : DetectMajorVersion(stagedRoot, archivePath);
            var root = MobileRuntimePaths.JavaRuntimeDirectoryFor(targetMajor);

            progress?.Report($"正在安装 Java {targetMajor} 运行时…");
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

    /// <summary>判定归档里的 Java 主版本：优先 release，其次文件名里的数字，最后兜底 21。</summary>
    private static int DetectMajorVersion(string stagedRoot, string archivePath)
    {
        if (TryReadJavaVersion(stagedRoot, out _, out var majorVersion) && majorVersion > 0)
            return majorVersion;

        var digits = new string(Path.GetFileName(archivePath).TakeWhile(char.IsLetterOrDigit).ToArray());
        var match = System.Text.RegularExpressions.Regex.Match(digits, @"(1[0-9]|2[0-9])");
        if (match.Success && int.TryParse(match.Value, out var fromName) && fromName > 0)
            return fromName;

        return 21;
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
