using System.Text;

namespace MChub.Core.Services.LogShare;

/// <summary>
/// 一次上传里的单个文件。name 支持相对路径（例如 crash-reports/crash-01.txt），
/// 服务端按名称精确匹配，后续读取 / 分析都要用同一个 name。
/// </summary>
public sealed record LogShareFile(string Name, string Content)
{
    public long ByteCount => Encoding.UTF8.GetByteCount(Content);
}

/// <summary>公开元数据条目，visible=false 时不出现在 GET /v1/log/{id} 的响应里。</summary>
public sealed record LogShareMetadataEntry(string Key, string Value, string? Label = null, bool Visible = true);

public sealed record LogShareFileInfo(string Name, long Size, int Lines, string? Raw, string? Insights);

/// <summary>上传（或读取元信息）后的日志记录。</summary>
public sealed record LogShareEntry(
    string Id,
    string? Url,
    string? Raw,
    string? Token,
    bool Deduplicated,
    int FileCount,
    bool RequiresFileSelection,
    IReadOnlyList<LogShareFileInfo> Files)
{
    public LogShareFileInfo? Find(string name)
    {
        return Files.FirstOrDefault(file => string.Equals(file.Name, name, StringComparison.Ordinal));
    }
}

public sealed record LogShareMeta(
    string Id,
    int FileCount,
    bool RequiresFileSelection,
    long Size,
    long Lines,
    long Created,
    long Expires,
    string? Source,
    IReadOnlyList<LogShareFileInfo> Files,
    IReadOnlyList<LogShareMetadataEntry> Metadata);

public sealed record LogShareProblem(string Message, int Counter, IReadOnlyList<string> Solutions);

public sealed record LogShareInformation(string Message, int Counter, string? Label, string? Value);

/// <summary>Codex 对单个文件的结构化分析结果。</summary>
public sealed record LogShareInsights(
    string Id,
    string Name,
    string Type,
    string? Version,
    string? Title,
    IReadOnlyList<LogShareProblem> Problems,
    IReadOnlyList<LogShareInformation> Information);

public sealed record LogShareLimits(
    long StorageTime,
    int MaxFiles,
    long MaxTotalBytes,
    long MaxRequestBodyBytes);

public sealed record LogShareDeleteResult(bool Success, IReadOnlyList<string> Deleted, IReadOnlyList<string> Failed);

public sealed class LogShareException(int statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;

    public bool IsRateLimited => StatusCode is 429 or 503;
}
