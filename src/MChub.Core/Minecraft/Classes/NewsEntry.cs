using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using MChub.Localization;

namespace MChub.Core.Minecraft.Classes;

public class NewsEntry : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    /// <summary>分类。新闻条目里对应官方 <c>category</c>（如 Minecraft for Windows）。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>官方 <c>tag</c>（news / New / Java Realms …），仅作展示用。</summary>
    public string Tag { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>补丁说明正文路径。新闻条目没有正文文件，此字段为空。</summary>
    public string ContentPath { get; set; } = string.Empty;

    /// <summary>新闻原文链接（官方 <c>readMoreLink</c>），点「阅读原文」时打开。</summary>
    public string ReadMoreLink { get; set; } = string.Empty;

    /// <summary>该条目属于 Java 版（newsType 含 "Java"）。</summary>
    public bool IsJava { get; set; }

    /// <summary>该条目属于基岩版（newsType 含 "Bedrock"）。两者可同时为真。</summary>
    public bool IsBedrock { get; set; }

    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ShortText { get; set; } = string.Empty;
    public bool? NeedsTranslation { get; set; }
    public NewsEdition Edition { get; set; }
    public string RelativeDate => GetRelativeTime(Date);

    public bool HasReadMoreLink => !string.IsNullOrWhiteSpace(ReadMoreLink);

    /// <summary>版别展示文本。同时属于两个版本时显示 "Java / 基岩"。</summary>
    public string EditionDisplay => IsJava && IsBedrock
        ? $"Java / {CommonLanguageManager.Instance.news_editionBedrock.CurrentValue()}"
        : IsBedrock
            ? CommonLanguageManager.Instance.news_editionBedrock.CurrentValue()
            : "Java";

    private static string GetRelativeTime(DateTime date)
    {
        var diff = DateTime.Now - date;
        if (diff.TotalMinutes < 1) return CommonLanguageManager.Instance.news_justNow.CurrentValue();
        if (diff.TotalHours < 1) return string.Format(CommonLanguageManager.Instance.news_minutesAgo.CurrentValue(), (int)diff.TotalMinutes);
        if (diff.TotalDays < 1) return string.Format(CommonLanguageManager.Instance.news_hoursAgo.CurrentValue(), (int)diff.TotalHours);
        if (diff.TotalDays < 2) return CommonLanguageManager.Instance.news_yesterday.CurrentValue();
        if (diff.TotalDays < 7) return string.Format(CommonLanguageManager.Instance.news_daysAgo.CurrentValue(), (int)diff.TotalDays);
        if (diff.TotalDays < 30) return string.Format(CommonLanguageManager.Instance.news_weeksAgo.CurrentValue(), (int)(diff.TotalDays / 7));
        if (diff.TotalDays < 365) return string.Format(CommonLanguageManager.Instance.news_monthsAgo.CurrentValue(), (int)(diff.TotalDays / 30));
        return string.Format(CommonLanguageManager.Instance.news_yearsAgo.CurrentValue(), (int)(diff.TotalDays / 365));
    }
}

public enum NewsEdition
{
    Java,
    Bedrock
}

/// <summary>
/// 官方新闻流 (<c>launchercontent.mojang.com/v2/news.json</c>)。与补丁说明不是一套模型：
/// 新闻只有摘要 text + 图片 + readMoreLink，正文在 minecraft.net 网页上。
/// </summary>
public class NewsFeedResponse
{
    public int Version { get; set; }
    public List<NewsFeedEntry> Entries { get; set; } = [];
}

public class NewsFeedEntry
{
    public string Title { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Text { get; set; } = string.Empty;
    public NewsFeedImage? PlayPageImage { get; set; }
    public NewsFeedImage? NewsPageImage { get; set; }
    public string ReadMoreLink { get; set; } = string.Empty;
    public string ArticleBody { get; set; } = string.Empty;

    /// <summary>JSON 字段名是单数 <c>newsType</c>，且是字符串数组。</summary>
    [JsonPropertyName("newsType")]
    public List<string> NewsTypes { get; set; } = [];

    public string Id { get; set; } = string.Empty;
    public bool? NeedsTranslation { get; set; }
}

public class NewsFeedImage
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class PatchNoteImage
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class NewsDetail
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>新闻摘要（正文文件不存在时用它填充详情页）。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>新闻原文链接；非空时详情页显示「阅读原文」。</summary>
    public string ReadMoreLink { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; }
    public bool? NeedsTranslation { get; set; }
}

public class NewsContentResponse
{
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public PatchNoteImage? Image { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool? NeedsTranslation { get; set; }
}