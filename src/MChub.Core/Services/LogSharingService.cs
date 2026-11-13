using System.Text;
using System.Text.Json;
using MChub.Core.Const;
using MChub.Core.Services.LogShare;
using MChub.Localization;

namespace MChub.Core.Services;

public sealed record LogShareResult(string Platform, string? Url, string? Error)
{
    public bool IsSuccess => Url is not null && Error is null;

    public string? Id { get; init; }

    public string? Token { get; init; }

    public int FileCount { get; init; }

    public bool Deduplicated { get; init; }
}

/// <summary>
/// 日志分享 / 分析的编排层。
///
/// LogShare.CN 走 v1 多文件接口（崩溃报告 + 最近几次日志一起发），
/// mclo.gs 作为备用单文件镜像；分析用 LogShare 的 Codex 接口，返回结构化结论后
/// 排版成可读文本；只有在用户自己配置了 AI 服务时才会退回聊天补全接口。
/// </summary>
public static class LogSharingService
{
    private static readonly HttpClient McLogsClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static async Task<LogShareResult[]> ShareAllAsync(LogShareBundle bundle, CancellationToken ct)
    {
        var logShareTask = ShareToLogShareCnAsync(bundle, ct);
        var mcloTask = ShareToMcLogsAsync(bundle.Primary, ct);
        await Task.WhenAll(logShareTask, mcloTask);
        return [logShareTask.Result, mcloTask.Result];
    }

    /// <summary>上传后用 Codex 分析主文件，返回排版好的文本结果。</summary>
    public static async Task<string> AnalyseAsync(LogShareBundle bundle, CancellationToken ct)
    {
        try
        {
            var entry = await LogShareClient.Instance
                .UploadAsync(bundle.Files, BuildSource(), BuildMetadata(bundle), ct).ConfigureAwait(false);

            var file = entry.Find(bundle.PrimaryName) ?? entry.Files.FirstOrDefault();
            if (file is null)
                throw new LogShareException(404, CommonLanguageManager.Instance.logSharing_noAnalyzableFile.CurrentValue());

            var insights = await LogShareClient.Instance.GetInsightsAsync(entry.Id, file.Name, ct)
                .ConfigureAwait(false);
            return FormatInsights(insights);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            // 服务端分析不可用时，如果用户配了自己的 AI，就退回去用它
            if (HasCustomAiServer())
                return await AnalyseAiAsync(bundle.Primary.Content, null, ct).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<LogShareResult> ShareToLogShareCnAsync(LogShareBundle bundle, CancellationToken ct)
    {
        try
        {
            var entry = await LogShareClient.Instance
                .UploadAsync(bundle.Files, BuildSource(), BuildMetadata(bundle), ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(entry.Url))
                return new LogShareResult("LogShare.CN", null,
                    CommonLanguageManager.Instance.logSharing_noShareLink.CurrentValue());

            return new LogShareResult("LogShare.CN", entry.Url, null)
            {
                Id = entry.Id,
                Token = entry.Token,
                FileCount = entry.FileCount,
                Deduplicated = entry.Deduplicated
            };
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return new LogShareResult("LogShare.CN", null, ex.Message);
        }
    }

    private static async Task<LogShareResult> ShareToMcLogsAsync(LogShareFile file, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mclo.gs/1/log");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { content = LimitContent(file.Content, 10_485_760, 25_000), source = "MChub" }),
                Encoding.UTF8, "application/json");
            using var response = await McLogsClient.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new LogShareResult("mclo.gs", null,
                    string.Format(CommonLanguageManager.Instance.logSharing_httpError.CurrentValue(), (int)response.StatusCode,
                        ExtractError(body) ?? CommonLanguageManager.Instance.logSharing_serverError.CurrentValue()));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var url = root.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                return new LogShareResult("mclo.gs", null, CommonLanguageManager.Instance.logSharing_noShareLink.CurrentValue());
            return new LogShareResult("mclo.gs", url, null);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            return new LogShareResult("mclo.gs", null, ex.Message);
        }
    }

    /// <summary>删除分享出去的日志（只有首次上传返回的 token 有效）。</summary>
    public static async Task<LogShareDeleteResult> DeleteAsync(string id, string token, CancellationToken ct)
    {
        return await LogShareClient.Instance.DeleteAsync(id, token, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 兼容自定义 AI 服务（OpenAI 兼容的 /chat/completions 流式接口）。
    /// LogShare 的 Codex 分析不可用时才会用到。
    /// </summary>
    public static async Task<string> AnalyseAiAsync(string content, Action<string>? onChunk, CancellationToken ct)
    {
        var config = Data.ConfigEntry;
        if (string.IsNullOrWhiteSpace(config.AiServerBaseUrl))
            throw new InvalidOperationException("尚未配置 AI 服务地址。");

        var baseUrl = config.AiServerBaseUrl.Trim().TrimEnd('/') + "/chat/completions";
        var key = config.AiApiKey?.Trim();
        var model = string.IsNullOrWhiteSpace(config.AiModel) ? null : config.AiModel.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = model ?? "gpt-4o",
                stream = true,
                messages = new[]
                {
                    new { role = "user", content = LimitContent(content, 1_048_576, 50_000) }
                }
            }),
            Encoding.UTF8, "application/json");
        using var response = await McLogsClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.logSharing_httpError.CurrentValue(),
                (int)response.StatusCode, ExtractError(error) ?? CommonLanguageManager.Instance.logSharing_serverError.CurrentValue()));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var builder = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                break;
            if (data.Length == 0)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.String)
                {
                    var chunk = contentElement.GetString() ?? string.Empty;
                    if (chunk.Length > 0)
                    {
                        builder.Append(chunk);
                        onChunk?.Invoke(chunk);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        if (builder.Length == 0)
            throw new InvalidOperationException(CommonLanguageManager.Instance.logSharing_aiNoResult.CurrentValue());
        return builder.ToString();
    }

    private static string FormatInsights(LogShareInsights insights)
    {
        var language = CommonLanguageManager.Instance;
        var builder = new StringBuilder();

        var title = insights.Title;
        if (string.IsNullOrWhiteSpace(title))
            title = string.Join(' ', new[] { insights.Name, insights.Version, insights.Type }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(title))
            builder.AppendLine(title.Trim());

        builder.AppendLine();
        if (insights.Problems.Count == 0)
        {
            builder.AppendLine(language.logSharing_insightsNoProblems.CurrentValue());
        }
        else
        {
            builder.AppendLine(string.Format(language.logSharing_insightsProblems.CurrentValue(), insights.Problems.Count));
            for (var index = 0; index < insights.Problems.Count; index++)
            {
                var problem = insights.Problems[index];
                builder.Append(index + 1).Append(". ").Append(problem.Message);
                if (problem.Counter > 1)
                    builder.Append(" (").Append(string.Format(language.logSharing_insightsOccurrence.CurrentValue(), problem.Counter)).Append(')');
                builder.AppendLine();

                foreach (var solution in problem.Solutions)
                    builder.Append("   · ").AppendLine(solution);
            }
        }

        if (insights.Information.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(language.logSharing_insightsInformation.CurrentValue());
            foreach (var information in insights.Information)
            {
                var label = !string.IsNullOrWhiteSpace(information.Label) ? information.Label : information.Message;
                var value = !string.IsNullOrWhiteSpace(information.Value) ? information.Value : information.Message;
                builder.Append("· ").Append(label).Append(": ").AppendLine(value);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static List<LogShareMetadataEntry> BuildMetadata(LogShareBundle bundle)
    {
        var language = CommonLanguageManager.Instance;
        var metadata = new List<LogShareMetadataEntry>();

        if (!string.IsNullOrWhiteSpace(bundle.MinecraftVersion))
            metadata.Add(new LogShareMetadataEntry("version", bundle.MinecraftVersion!,
                language.logSharing_metadataVersion.CurrentValue()));

        if (!string.IsNullOrWhiteSpace(bundle.LoaderName))
            metadata.Add(new LogShareMetadataEntry("loader", bundle.LoaderName!,
                language.logSharing_metadataLoader.CurrentValue()));

        if (!string.IsNullOrWhiteSpace(bundle.InstanceName))
            metadata.Add(new LogShareMetadataEntry("instance", bundle.InstanceName!,
                language.logSharing_metadataInstance.CurrentValue(), Visible: false));

        var version = TryGetVersionTitle();
        if (!string.IsNullOrWhiteSpace(version))
            metadata.Add(new LogShareMetadataEntry("launcher", version!,
                language.logSharing_metadataLauncher.CurrentValue()));

        return metadata;
    }

    private static string BuildSource()
    {
        var version = TryGetVersionTitle();
        return string.IsNullOrWhiteSpace(version) ? "MChub" : $"MChub/{version}";
    }

    private static string? TryGetVersionTitle()
    {
        try
        {
            return Data.Instance.Version.VersionTitle;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool HasCustomAiServer()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(Data.ConfigEntry.AiServerBaseUrl);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is LogShareException or HttpRequestException or TaskCanceledException or JsonException
            or IOException;
    }

    private static string LimitContent(string content, int maxBytes, int maxLines)
    {
        var bytes = Encoding.UTF8.GetByteCount(content);
        var lines = content.Split('\n');
        if (bytes <= maxBytes && lines.Length <= maxLines)
            return content;

        if (lines.Length > maxLines)
            content = string.Join('\n', lines[^maxLines..]);

        if (Encoding.UTF8.GetByteCount(content) <= maxBytes)
            return content;

        // 按行裁完还超，就直接截尾部字节（日志的关键信息在末尾）
        var buffer = Encoding.UTF8.GetBytes(content);
        var slice = buffer[^maxBytes..];
        var text = Encoding.UTF8.GetString(slice);
        var start = 0;
        while (start < text.Length && text[start] == '\uFFFD')
            start++;
        return text[start..];
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
