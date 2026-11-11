using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using MChub.Core.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Minecraft;

/// <summary>
/// authlib-injector 的获取与启动参数构造。
/// <para>
/// 第三方 Yggdrasil（LittleSkin、各类皮肤站/私服认证）登录只解决「拿到 accessToken」，
/// 客户端本身仍会向 Mojang 校验会话、拉取皮肤。必须在 JVM 层挂上 authlib-injector，
/// 把 <c>api.minecraftservices.com</c> / <c>sessionserver.mojang.com</c> 等请求改写到
/// 对应 Yggdrasil 服务端，皮肤与正版验证才会生效。
/// </para>
/// </summary>
public static class AuthlibInjectorService
{
    // BMCLAPI 镜像优先（国内直连稳定），失败再回退官方源。
    private const string MirrorMetadataUrl =
        "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

    private const string OfficialMetadataUrl =
        "https://authlib-injector.yushi.moe/artifact/latest.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string? _resolvedPath;

    /// <summary>jar 缓存目录：<c>{用户数据}/AuthlibInjector</c>。</summary>
    public static string CacheDirectory => Path.Combine(ConfigPath.UserDataRootPath, "AuthlibInjector");

    /// <summary>
    /// 取本地 authlib-injector.jar，缺失时下载。
    /// 任何失败都返回 <c>null</c>：注入是增强项，不能因为镜像不可用就让第三方账号启动不了。
    /// </summary>
    public static async Task<string?> EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (_resolvedPath is { } cached && File.Exists(cached))
            return cached;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_resolvedPath is { } again && File.Exists(again))
                return again;

            Directory.CreateDirectory(CacheDirectory);

            AuthlibMetadata? metadata = null;
            try
            {
                metadata = await TryReadMetadataAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning($"[AuthlibInjector] 读取版本信息失败，将回退到本地缓存：{exception.Message}");
            }

            var existing = FindExistingJar();
            if (metadata is null)
            {
                _resolvedPath = existing;
                return existing;
            }

            var target = Path.Combine(CacheDirectory,
                $"authlib-injector-{SanitizeFileName(metadata.Version)}.jar");

            if (File.Exists(target) &&
                (string.IsNullOrWhiteSpace(metadata.Sha256) ||
                 await VerifySha256Async(target, metadata.Sha256, cancellationToken)))
            {
                _resolvedPath = target;
                return target;
            }

            try
            {
                var bytes = await Http.GetByteArrayAsync(metadata.DownloadUrl, cancellationToken);
                if (bytes.Length == 0)
                    throw new InvalidDataException("下载结果为空");

                if (!string.IsNullOrWhiteSpace(metadata.Sha256))
                {
                    var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(actual, metadata.Sha256.ToLowerInvariant(), StringComparison.Ordinal))
                        throw new InvalidDataException($"SHA-256 校验失败（期望 {metadata.Sha256}，实际 {actual}）");
                }

                var temp = target + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                File.Move(temp, target, true);
                Logger.Info($"[AuthlibInjector] 已下载 {metadata.Version} → {target}");
                _resolvedPath = target;
                return target;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning($"[AuthlibInjector] 下载 {metadata.Version} 失败，回退本地缓存：{exception.Message}");
                _resolvedPath = existing;
                return existing;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>构造 <c>-javaagent:&lt;jar&gt;=&lt;服务端根地址&gt;</c>。</summary>
    public static string BuildJavaAgentArgument(string jarPath, string yggdrasilServerUrl) =>
        $"-javaagent:{jarPath}={NormalizeServerRoot(yggdrasilServerUrl)}";

    /// <summary>
    /// authlib-injector 要求传入 API 根地址，且不能带尾斜杠；
    /// 未写协议的地址按 https 补全，避免用户从启动器拖入时格式不一。
    /// </summary>
    public static string NormalizeServerRoot(string yggdrasilServerUrl)
    {
        var value = (yggdrasilServerUrl ?? string.Empty).Trim();
        if (value.Length == 0)
            return value;

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;

        return value.TrimEnd('/');
    }

    private static async Task<AuthlibMetadata?> TryReadMetadataAsync(CancellationToken cancellationToken)
    {
        foreach (var url in new[] { MirrorMetadataUrl, OfficialMetadataUrl })
        {
            try
            {
                var json = await Http.GetStringAsync(url, cancellationToken);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var version = root.TryGetProperty("version", out var versionNode)
                    ? versionNode.GetString()
                    : null;
                var downloadUrl = root.TryGetProperty("download_url", out var downloadNode)
                    ? downloadNode.GetString()
                    : null;

                string? sha256 = null;
                if (root.TryGetProperty("checksums", out var checksums) &&
                    checksums.TryGetProperty("sha256", out var shaNode))
                    sha256 = shaNode.GetString();

                if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(downloadUrl))
                    continue;

                return new AuthlibMetadata(version, downloadUrl, sha256);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning($"[AuthlibInjector] 元数据源不可用 {url}：{exception.Message}");
            }
        }

        return null;
    }

    private static string? FindExistingJar()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory))
                return null;

            var candidate = Directory
                .EnumerateFiles(CacheDirectory, "authlib-injector-*.jar")
                .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            return candidate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Warning($"[AuthlibInjector] 扫描本地缓存失败：{exception.Message}");
            return null;
        }
    }

    private static async Task<bool> VerifySha256Async(string path, string expected,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            return string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'));

    private sealed record AuthlibMetadata(string Version, string DownloadUrl, string? Sha256);
}
