using System.Diagnostics;
using System.Text.Json;
using MChub.Core.Json;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Minecraft;

public static class NewsService
{
    // 官方启动器「新闻」页用的就是这份 feed：
    //   https://launchercontent.mojang.com/v2/news.json
    //
    // 官方另有按语言分文件的版本（/v2/news/zh-CN.json 等），但**译文并不完整**：
    // 实测 zh-CN 版本 100 条里只有 8 条是中文，其余仍是英文 —— 直接用会出现
    // 中英混排的列表。既然拿不到 100% 中文，就统一用英文源，不做逐条混搭。
    // （缓存里仍带 locale 列，值为 ContentLocale，将来若出现完整译文可直接切换。）
    private const string NewsApiUrl = "https://launchercontent.mojang.com/v2/news.json";
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    /// <summary>新闻正文内容的语言。当前固定英文源。</summary>
    private const string ContentLocale = "en-US";

    // newsType 里的分类标记：只有带 "News page" 的条目才进新闻页；
    // Java / Bedrock 决定版别，两个都有时同属两个版本。
    private const string NewsPageType = "News page";
    private const string JavaType = "Java";
    private const string BedrockType = "Bedrock";

    public static List<NewsEntry> JavaNews { get; private set; } = [];
    public static List<NewsEntry> BedrockNews { get; private set; } = [];

    /// <summary>全量新闻（按 Id 去重后的并集）。页面「全部」用它，避免同属两版的条目重复出现。</summary>
    public static List<NewsEntry> AllNews { get; private set; } = [];

    public static event EventHandler? NewsUpdated;

    public static void InitializeFromCache()
    {
        Logger.Info(LogLanguageManager.Instance.news_cacheLoadStart.CurrentValue());
        var java = LoadCache(NewsEdition.Java, ContentLocale);
        var bedrock = LoadCache(NewsEdition.Bedrock, ContentLocale);
        SetCollections(java, bedrock);
        Logger.Info(string.Format(LogLanguageManager.Instance.news_cacheLoadComplete.CurrentValue(), java.Count, bedrock.Count));
    }

    public static void RaiseNewsUpdated()
    {
        if (AllNews.Count > 0)
            NewsUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static async Task FetchAndRefreshAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(LogLanguageManager.Instance.news_fetchStart.CurrentValue());
        try
        {
            var entries = await FetchAsync(NewsApiUrl);

            var changed = false;

            if (entries is { Count: > 0 })
            {
                var java = entries.Where(e => e.IsJava).ToList();
                var bedrock = entries.Where(e => e.IsBedrock).ToList();

                CacheDatabase.WriteNews(NewsEdition.Java, ContentLocale, java);
                CacheDatabase.WriteNews(NewsEdition.Bedrock, ContentLocale, bedrock);

                SetCollections(java, bedrock);
                changed = true;
            }

            if (changed) NewsUpdated?.Invoke(null, EventArgs.Empty);
            Logger.Info(string.Format(LogLanguageManager.Instance.news_fetchComplete.CurrentValue(), changed, stopwatch.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            Logger.Error(LogLanguageManager.Instance.news_fetchFailed.CurrentValue(), ex);
        }
    }

    /// <summary>
    /// Java 列表 + 基岩列表 → 去重后的全量列表。
    /// 同属两版的条目会同时出现在两个缓存分片里，并集时必须按 Id 去重，否则「全部」会重复。
    /// </summary>
    private static void SetCollections(List<NewsEntry> java, List<NewsEntry> bedrock)
    {
        JavaNews = java;
        BedrockNews = bedrock;
        AllNews = java.Concat(bedrock)
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(e => e.Date)
            .ToList();
    }

    private static List<NewsEntry> LoadCache(NewsEdition edition, string locale)
    {
        try
        {
            return CacheDatabase.ReadNews(edition, locale);
        }
        catch (Exception ex)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.news_cacheLoadFailed.CurrentValue(), edition), ex);
            return [];
        }
    }

    private static async Task<List<NewsEntry>?> FetchAsync(string url)
    {
        try
        {
            var json = await NewsHttp.Client.GetStringAsync(url);
            return ParseJson(json);
        }
        catch (Exception ex)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.news_fetchEditionFailed.CurrentValue(), "News", url), ex);
            return null;
        }
    }

    private static List<NewsEntry> ParseJson(string json)
    {
        var response = JsonSerializer.Deserialize<NewsFeedResponse>(json, MChubJson.Options);
        if (response?.Entries is not { Count: > 0 }) return [];

        var result = new List<NewsEntry>(response.Entries.Count);
        foreach (var entry in response.Entries)
        {
            var types = entry.NewsTypes ?? [];

            // 只要新闻页条目；Dungeons / Legends 等其它产品线不进这个页面。
            if (!Contains(types, NewsPageType)) continue;

            var isJava = Contains(types, JavaType);
            var isBedrock = Contains(types, BedrockType);

            // 少数条目只有 Dungeons / DungeonsII 标记，既非 Java 也非基岩 —— 页面没有对应筛选，跳过。
            if (!isJava && !isBedrock) continue;

            result.Add(MapToNewsEntry(entry, isJava, isBedrock));
        }

        return result.OrderByDescending(e => e.Date).ToList();
    }

    private static bool Contains(List<string> values, string target)
    {
        foreach (var value in values)
            if (string.Equals(value, target, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static NewsEntry MapToNewsEntry(NewsFeedEntry entry, bool isJava, bool isBedrock)
    {
        // 卡片是 772×350 的横向图，优先用 newsPageImage；playPageImage(700×466) 作兜底。
        var image = entry.NewsPageImage?.Url;
        if (string.IsNullOrWhiteSpace(image)) image = entry.PlayPageImage?.Url;

        var imageUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(image))
            imageUrl = image.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? image : BaseImageUrl + image;

        return new NewsEntry
        {
            Title = entry.Title,
            // 新闻没有版本号，留空让详情页隐藏版本角标。
            Version = string.Empty,
            Type = entry.Category,
            Tag = entry.Tag,
            ImageUrl = imageUrl,
            ReadMoreLink = entry.ReadMoreLink,
            IsJava = isJava,
            IsBedrock = isBedrock,
            Id = entry.Id,
            Date = entry.Date == default ? DateTime.Now : entry.Date.ToLocalTime(),
            ShortText = entry.Text,
            NeedsTranslation = entry.NeedsTranslation,
            // 只属于基岩的条目记成 Bedrock；其余（含两版共有）记 Java，真实版别由 IsJava/IsBedrock 表达。
            Edition = isBedrock && !isJava ? NewsEdition.Bedrock : NewsEdition.Java
        };
    }
}

internal static class NewsHttp
{
    // 不要再关掉证书校验：以前指向自建占位域名（证书不可信）才被迫这么写，
    // 现在直连 launchercontent.mojang.com，走系统信任链即可（实测严格校验下端点正常）。
    // 关掉校验等于给中间人开绿灯 —— 本机还挂着 TUN 代理，风险更实在。
    public static readonly HttpClient Client = new();
}
