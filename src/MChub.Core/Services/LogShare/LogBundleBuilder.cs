using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;

namespace MChub.Core.Services.LogShare;

public sealed record LogShareBundle(
    IReadOnlyList<LogShareFile> Files,
    string PrimaryName,
    string? InstanceName,
    string? MinecraftVersion,
    string? LoaderName,
    string? VersionId)
{
    public LogShareFile Primary => Files[0];

    public int FileCount => Files.Count;
}

/// <summary>
/// 把「当前编辑器里的日志」加上实例目录里的历史日志 / 崩溃报告打包成一次上传。
///
/// LogShare 的一次上传可以带多个文件，Codex 分析也能看到崩溃报告里的上下文，
/// 所以分享时不要只发编辑器里那一份：崩溃报告 + 最近几次日志能把问题说清楚得多。
/// 上限按服务端默认配额（200 个文件 / 20 MiB）留了余量。
/// </summary>
public static class LogBundleBuilder
{
    private const string DefaultPrimaryName = "latest.log";
    private const int MaxFiles = 60;
    private const long MaxTotalBytes = 18 * 1024 * 1024;
    private const long MaxSingleFileBytes = 8 * 1024 * 1024;
    private const int MaxCrashReports = 5;
    private const int MaxExtraLogs = 20;

    public static async Task<LogShareBundle> CreateAsync(
        string primaryContent,
        string? primaryFileName = null,
        MinecraftInstance? instance = null,
        CancellationToken ct = default)
    {
        var primaryName = NormalizeName(string.IsNullOrWhiteSpace(primaryFileName)
            ? DefaultPrimaryName
            : primaryFileName);
        var files = new List<LogShareFile> { new(primaryName, primaryContent) };
        var totalBytes = files[0].ByteCount;

        if (instance is not null)
        {
            (_, totalBytes) = await AddDirectoryAsync(
                instance.GetSpecialFolder(MinecraftSpecialFolder.CrashReportsFolder),
                "crash-reports",
                MaxCrashReports,
                files,
                primaryName,
                totalBytes).ConfigureAwait(false);

            (_, totalBytes) = await AddDirectoryAsync(
                instance.GetSpecialFolder(MinecraftSpecialFolder.LogsFolder),
                "logs",
                MaxExtraLogs,
                files,
                primaryName,
                totalBytes).ConfigureAwait(false);
        }


        return new LogShareBundle(
            DeduplicateByContent(files),
            primaryName,
            instance?.InstanceName,
            instance?.VersionId,
            ResolveLoaderName(instance),
            instance?.MinecraftEntry?.Id);
    }

    /// <summary>
    /// 主文件来自编辑器里的 latest.log，而 logs 目录里往往还有同一份文件，
    /// 换个前缀就会重复上传。按内容哈希去重，保留先出现的那个（主文件优先）。
    /// </summary>
    private static List<LogShareFile> DeduplicateByContent(List<LogShareFile> files)
    {
        if (files.Count <= 1)
            return files;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<LogShareFile>(files.Count);
        foreach (var file in files)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Content)));
            if (seen.Add(hash))
                result.Add(file);
        }

        return result;
    }

    private static async Task<(int Added, long Bytes)> AddDirectoryAsync(
        string? directory,
        string prefix,
        int limit,
        List<LogShareFile> files,
        string primaryName,
        long totalBytes)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return (0, totalBytes);

        var added = 0;
        var bytes = totalBytes;

        foreach (var candidate in Directory.EnumerateFiles(directory)
                     .Select(file => new FileInfo(file))
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(limit * 2))
        {
            if (files.Count >= MaxFiles || added >= limit)
                break;

            if (candidate.Length > MaxSingleFileBytes)
                continue;

            var relative = $"{prefix}/{NormalizeName(candidate.Name)}";
            if (files.Any(file => string.Equals(file.Name, relative, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (string.Equals(relative, primaryName, StringComparison.OrdinalIgnoreCase))
                continue;

            string content;
            try
            {
                content = await ReadLogTextAsync(candidate.FullName).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
                continue;

            var file = new LogShareFile(relative, content);
            if (bytes + file.ByteCount > MaxTotalBytes)
                break;

            files.Add(file);
            bytes += file.ByteCount;
            added++;
        }

        return (added, bytes);
    }

    private static async Task<string> ReadLogTextAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var source = new MemoryStream(bytes);
            await using var gzip = new GZipStream(source, CompressionMode.Decompress);
            await using var buffer = new MemoryStream();
            await gzip.CopyToAsync(buffer).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    private static string NormalizeName(string name)
    {
        var normalized = name.Replace('\\', '/').Trim();
        if (normalized.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];
        return normalized;
    }

    private static string? ResolveLoaderName(MinecraftInstance? instance)
    {
        var loaders = instance?.MinecraftEntry?.Loaders;
        if (loaders is null || loaders.Count == 0)
            return null;
        return string.Join('+', loaders.Select(loader => loader.Type.ToString()));
    }
}
