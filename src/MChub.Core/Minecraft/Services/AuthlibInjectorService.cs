using System.Text.Json;
using MChub.Core.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Minecraft.Services;

/// <summary>
/// authlib-injector 的获取与缓存。
/// 第三方（Yggdrasil / LittleSkin 等）账户光在启动器里登录还不够：
/// 游戏本体必须通过 authlib-injector 改写认证请求，皮肤与登录态才会在游戏内生效。
/// </summary>
public static class AuthlibInjectorService
{
    private const string MetaUrl = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";
    private const string GithubLatest = "https://authlib-injector.yushi.moe/artifact/latest.json";
    private const long MinimumJarSize = 200_000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static string JarPath => Path.Combine(ConfigPath.UserDataRootPath, "Runtimes", "authlib-injector.jar");

    /// <summary>返回可用的 authlib-injector.jar 路径；失败返回 null。</summary>
    public static async Task<string?> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var path = JarPath;
        if (File.Exists(path) && new FileInfo(path).Length >= MinimumJarSize)
            return path;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        foreach (var metaUrl in new[] { MetaUrl, GithubLatest })
        {
            try
            {
                var downloadUrl = await ResolveDownloadUrlAsync(metaUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(downloadUrl)) continue;

                var bytes = await Http.GetByteArrayAsync(downloadUrl, cancellationToken);
                if (bytes.Length < MinimumJarSize) continue;

                var temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                File.Move(temp, path, true);
                return path;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Warning($"[AuthlibInjector] 从 {metaUrl} 获取失败：{exception.Message}");
            }
        }

        return null;
    }

    private static async Task<string?> ResolveDownloadUrlAsync(string metaUrl, CancellationToken cancellationToken)
    {
        var json = await Http.GetStringAsync(metaUrl, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        foreach (var key in new[] { "download_url", "bmclapi_download_url", "url" })
        {
            if (!root.TryGetProperty(key, out var value)) continue;
            var url = value.GetString();
            if (!string.IsNullOrWhiteSpace(url)) return url;
        }

        return null;
    }

    /// <summary>拼出 authlib-injector 需要的两条 JVM 参数。</summary>
    public static IEnumerable<string> BuildJvmArguments(string jarPath, string serverUrl)
    {
        yield return "-Dauthlibinjector.side=client";
        yield return $"-javaagent:{jarPath}={serverUrl.TrimEnd('/')}";
    }
}
