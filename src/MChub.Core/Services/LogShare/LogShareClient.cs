using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MChub.Core.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Services.LogShare;

/// <summary>
/// LogShare v1 API 客户端（https://logshare.cn/api-docs）。
///
/// 设计取舍：
///   · 不引入 SDK 包 —— 只有六个端点，自带 System.Text.Json 足够，且能跟随启动器的代理设置。
///   · 请求体超过阈值时用 gzip 压缩（服务端支持 gzip / br / deflate，最多 5 层）。
///   · 上传前先读 /v1/limits，避免把明显超限的内容发出去。
///   · 429 / 503 会让服务端要求重试，这里按 Retry-After 自动重试两次。
/// </summary>
public sealed class LogShareClient
{
    /// <summary>
    /// 默认指向官方部署；自建实例可通过 MCHUB_LOG_SHARE_ENDPOINT 覆盖（值是 v1 根地址，含 /v1）。
    /// </summary>
    private static readonly string Endpoint = ResolveEndpoint();

    private const int CompressThreshold = 256 * 1024;
    private const int MaxRetries = 2;

    private static readonly Lazy<LogShareClient> LazyInstance = new(static () => new LogShareClient());
    private static readonly TimeSpan LimitsCacheDuration = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _limitsGate = new(1, 1);
    private readonly HttpClient _http;

    private LogShareLimits? _cachedLimits;
    private DateTimeOffset _limitsFetchedAt;

    private LogShareClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        ApplyProxy(handler);

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(180) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ResolveUserAgent());
    }

    public static LogShareClient Instance => LazyInstance.Value;

    /// <summary>上传一个或多个文件，返回日志 ID、分享地址、删除 token 和文件列表。</summary>
    public async Task<LogShareEntry> UploadAsync(
        IReadOnlyList<LogShareFile> files,
        string? source = null,
        IReadOnlyList<LogShareMetadataEntry>? metadata = null,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            throw new LogShareException(400, "至少需要一个日志文件。");

        var limits = await GetLimitsAsync(ct).ConfigureAwait(false);
        if (files.Count > limits.MaxFiles)
            throw new LogShareException(413, $"文件数量 {files.Count} 超过服务端上限 {limits.MaxFiles}。");

        var totalBytes = files.Sum(file => file.ByteCount);
        if (totalBytes > limits.MaxTotalBytes)
            throw new LogShareException(413,
                $"文件总大小 {FormatBytes(totalBytes)} 超过服务端上限 {FormatBytes(limits.MaxTotalBytes)}。");

        var payload = BuildUploadPayload(files, source, metadata);
        var compressed = payload.Length > CompressThreshold;
        if (compressed)
            payload = Compress(payload);

        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/log");
            var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            if (compressed)
                content.Headers.ContentEncoding.Add("gzip");
            request.Content = content;
            return request;
        }, ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new LogShareException(500, "服务端没有返回日志 ID。");

        var deduplicated = GetBoolean(root, "deduplicated");

        // 去重命中时服务端返回的是同一份记录的 id，但响应里的 token 并不是这条记录的删除凭证
        // （实测 DELETE 会回 "Invalid token"），因此不要把它当成可用的删除令牌往外传。
        var token = deduplicated ? null : GetString(root, "token");

        return new LogShareEntry(
            id!,
            GetString(root, "url"),
            GetString(root, "raw"),
            token,
            deduplicated,
            GetInt32(root, "fileCount", 1),
            GetBoolean(root, "requiresFileSelection"),
            ReadFiles(root));
    }

    /// <summary>读取元信息与文件列表。该接口不会续期日志。</summary>
    public async Task<LogShareMeta> GetMetaAsync(string id, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/log/{Uri.EscapeDataString(id)}"), ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new LogShareMeta(
            GetString(root, "id") ?? id,
            GetInt32(root, "fileCount", 1),
            GetBoolean(root, "requiresFileSelection"),
            GetInt64(root, "size", 0),
            GetInt64(root, "lines", 0),
            GetInt64(root, "created", 0),
            GetInt64(root, "expires", 0),
            GetString(root, "source"),
            ReadFiles(root),
            ReadMetadata(root));
    }

    /// <summary>读取指定文件的原文。多文件日志必须带 fileName。</summary>
    public async Task<string> GetRawAsync(string id, string? fileName, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildFileUrl("raw", id, fileName)), ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, body);
        return body;
    }

    /// <summary>对指定文件执行 Codex 分析。上传时不会预跑，首次请求才会真正分析。</summary>
    public async Task<LogShareInsights> GetInsightsAsync(string id, string fileName, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildFileUrl("insights", id, fileName)), ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var problems = new List<LogShareProblem>();
        var information = new List<LogShareInformation>();

        if (root.TryGetProperty("analysis", out var analysis) && analysis.ValueKind == JsonValueKind.Object)
        {
            if (analysis.TryGetProperty("problems", out var problemsElement) &&
                problemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var problem in problemsElement.EnumerateArray())
                {
                    var solutions = new List<string>();
                    if (problem.TryGetProperty("solutions", out var solutionsElement) &&
                        solutionsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var solution in solutionsElement.EnumerateArray())
                        {
                            var message = solution.ValueKind == JsonValueKind.String
                                ? solution.GetString()
                                : GetString(solution, "message");
                            if (!string.IsNullOrWhiteSpace(message))
                                solutions.Add(message!);
                        }
                    }

                    problems.Add(new LogShareProblem(
                        GetString(problem, "message") ?? string.Empty,
                        GetInt32(problem, "counter", 1),
                        solutions));
                }
            }

            if (analysis.TryGetProperty("information", out var informationElement) &&
                informationElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var info in informationElement.EnumerateArray())
                {
                    information.Add(new LogShareInformation(
                        GetString(info, "message") ?? string.Empty,
                        GetInt32(info, "counter", 1),
                        GetString(info, "label"),
                        GetString(info, "value")));
                }
            }
        }

        return new LogShareInsights(
            GetString(root, "id") ?? string.Empty,
            GetString(root, "name") ?? string.Empty,
            GetString(root, "type") ?? string.Empty,
            GetString(root, "version"),
            GetString(root, "title"),
            problems,
            information);
    }

    /// <summary>删除日志。token 只在首次上传的响应里返回一次，服务端只存哈希。</summary>
    public async Task<LogShareDeleteResult> DeleteAsync(string id, string token, CancellationToken ct = default)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{Endpoint}/log/{Uri.EscapeDataString(id)}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            return request;
        }, ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        EnsureSuccess(response, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var deleted = new List<string>();
        var failed = new List<string>();

        if (root.TryGetProperty("deleted", out var deletedElement) && deletedElement.ValueKind == JsonValueKind.Array)
            deleted.AddRange(deletedElement.EnumerateArray().Select(item => item.GetString()).OfType<string>());

        if (root.TryGetProperty("failed", out var failedElement) && failedElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in failedElement.EnumerateArray())
            {
                var failedId = GetString(item, "id");
                var message = GetString(item, "message");
                failed.Add(string.IsNullOrWhiteSpace(message)
                    ? failedId ?? "未知日志"
                    : $"{failedId}：{message}");
            }
        }

        return new LogShareDeleteResult(GetBoolean(root, "success"), deleted, failed);
    }

    /// <summary>上传限制（文件数 / 总大小 / 请求体 / 保留时间），有 10 分钟缓存。</summary>
    public async Task<LogShareLimits> GetLimitsAsync(CancellationToken ct = default)
    {
        var cached = _cachedLimits;
        if (cached is not null && DateTimeOffset.UtcNow - _limitsFetchedAt < LimitsCacheDuration)
            return cached;

        await _limitsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cached = _cachedLimits;
            if (cached is not null && DateTimeOffset.UtcNow - _limitsFetchedAt < LimitsCacheDuration)
                return cached;

            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/limits"), ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            EnsureSuccess(response, body);

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var limits = new LogShareLimits(
                GetInt64(root, "storageTime", 604_800),
                GetInt32(root, "maxFiles", 200),
                GetInt64(root, "maxTotalBytes", 20 * 1024 * 1024),
                GetInt64(root, "maxRequestBodyBytes", 32 * 1024 * 1024));

            _cachedLimits = limits;
            _limitsFetchedAt = DateTimeOffset.UtcNow;
            return limits;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
                                   ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.Warning($"读取 LogShare 上传限制失败，使用默认值：{ex.Message}");
            return new LogShareLimits(604_800, 200, 20 * 1024 * 1024, 32 * 1024 * 1024);
        }
        finally
        {
            _limitsGate.Release();
        }
    }

    private static byte[] BuildUploadPayload(
        IReadOnlyList<LogShareFile> files,
        string? source,
        IReadOnlyList<LogShareMetadataEntry>? metadata)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (var file in files)
            {
                writer.WriteStartObject();
                writer.WriteString("name", file.Name);
                writer.WriteString("content", file.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (!string.IsNullOrWhiteSpace(source))
                writer.WriteString("source", source.Trim());

            if (metadata is { Count: > 0 })
            {
                writer.WritePropertyName("metadata");
                writer.WriteStartArray();
                foreach (var entry in metadata.Take(100))
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", entry.Key);
                    writer.WriteString("value", entry.Value);
                    if (!string.IsNullOrWhiteSpace(entry.Label))
                        writer.WriteString("label", entry.Label);
                    writer.WriteBoolean("visible", entry.Visible);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(payload, 0, payload.Length);
        return output.ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var request = factory();
            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            request.Dispose();
            if (attempt >= MaxRetries ||
                response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
                return response;

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1));
            if (delay > TimeSpan.FromSeconds(30))
                delay = TimeSpan.FromSeconds(30);
            var status = (int)response.StatusCode;
            response.Dispose();
            Logger.Warning($"LogShare 限流（HTTP {status}），{delay.TotalSeconds:0} 秒后重试。");
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static string BuildFileUrl(string kind, string id, string? fileName)
    {
        var url = $"{Endpoint}/{kind}/{Uri.EscapeDataString(id)}";
        if (string.IsNullOrWhiteSpace(fileName))
            return url;
        // 子路径要逐段编码，斜杠本身保留
        return url + "/" + string.Join('/', fileName.Split('/').Select(Uri.EscapeDataString));
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw new LogShareException((int)response.StatusCode, ReadError(body) ?? DescribeStatus(response.StatusCode));
    }

    private static string? ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var error = GetString(root, "error") ?? GetString(root, "message");
                if (!string.IsNullOrWhiteSpace(error))
                    return error;
            }
        }
        catch (JsonException)
        {
            // 不是 JSON：多半是网关的错误页，下面兜底
        }

        var trimmed = body.Trim();
        if (trimmed.StartsWith('<') || trimmed.Length > 200)
            return null;
        return trimmed;
    }

    private static string DescribeStatus(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.BadRequest => "请求参数错误。",
            HttpStatusCode.Unauthorized => "缺少删除凭证（Authorization: Bearer token）。",
            HttpStatusCode.Forbidden => "删除凭证不匹配，或当前 IP 被封禁。",
            HttpStatusCode.NotFound => "日志或文件不存在。",
            HttpStatusCode.RequestEntityTooLarge => "内容超过服务端限制。",
            HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后重试。",
            HttpStatusCode.ServiceUnavailable => "服务暂时不可用，请稍后重试。",
            _ => $"服务端返回 HTTP {(int)status}。"
        };
    }

    private static List<LogShareFileInfo> ReadFiles(JsonElement root)
    {
        var files = new List<LogShareFileInfo>();
        if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
            return files;

        foreach (var file in filesElement.EnumerateArray())
        {
            var name = GetString(file, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            files.Add(new LogShareFileInfo(
                name!,
                GetInt64(file, "size", 0),
                GetInt32(file, "lines", 0),
                GetString(file, "raw"),
                GetString(file, "insights")));
        }

        return files;
    }

    private static List<LogShareMetadataEntry> ReadMetadata(JsonElement root)
    {
        var entries = new List<LogShareMetadataEntry>();
        if (!root.TryGetProperty("metadata", out var metadataElement) || metadataElement.ValueKind != JsonValueKind.Array)
            return entries;

        foreach (var item in metadataElement.EnumerateArray())
        {
            var key = GetString(item, "key");
            if (string.IsNullOrWhiteSpace(key))
                continue;
            entries.Add(new LogShareMetadataEntry(
                key!,
                GetString(item, "value") ?? string.Empty,
                GetString(item, "label")));
        }

        return entries;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool GetBoolean(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static int GetInt32(JsonElement element, string property, int fallback)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static long GetInt64(JsonElement element, string property, long fallback)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var result)
            ? result
            : fallback;
    }

    private static string ResolveEndpoint()
    {
        const string fallback = "https://api.logshare.cn/v1";
        var custom = Environment.GetEnvironmentVariable("MCHUB_LOG_SHARE_ENDPOINT");
        return string.IsNullOrWhiteSpace(custom) ? fallback : custom.Trim().TrimEnd('/');
    }

    private static void ApplyProxy(SocketsHttpHandler handler)
    {
        try
        {
            var config = Data.ConfigEntry;
            if (config.EnableProxyServer && !string.IsNullOrWhiteSpace(config.ProxyServer))
            {
                var address = config.ProxyServer.Trim();
                if (!address.Contains("://", StringComparison.Ordinal))
                    address = "http://" + address;
                handler.Proxy = new WebProxy(address);
                handler.UseProxy = true;
                return;
            }

            if (config.DisableSystemProxy)
                handler.UseProxy = false;
        }
        catch (InvalidOperationException)
        {
            // 配置未初始化（CLI / 单元测试），保持系统默认
        }
    }

    private static string ResolveUserAgent()
    {
        try
        {
            return Data.Instance.UserAgent;
        }
        catch (InvalidOperationException)
        {
            return "MChub";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
