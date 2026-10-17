using System.Diagnostics;
using System.Text.Json;
using MChub.Core.Const;
using MChub.Core.Json;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Minecraft;

public static class NewsService
{
    // 官方启动器「新闻」页用的就是这份 feed，而且**按语言分文件**：
    //   https://launchercontent.mojang.com/v2/news/zh-CN.json
    //   https://launchercontent.mojang.com/v2/news/en-US.json
    //   …（de-DE / fr-FR / ru-RU 等也都有）
    // 不存在的语言回落到无语言后缀的 news.json（英文）。
    // 这比自己接机器翻译可靠：译文是官方给的，不含第三方服务与配额。
    private const string NewsApiBase = "https://launchercontent.mojang.com/v2/news";
    private const string DefaultNewsApiUrl = "https://launchercontent.mojang.com/v2/news.json";
    private const string BaseImageUrl = "https://launchercontent.mojang.com";
    private const string FallbackLocale = "en-US";

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

    /// <summary>当前界面语言（zh-CN / en-US …）。配置未就绪时回落英文。</summary>
    private static string CurrentLocale
    {
        get
        {
            try
            {
                var language = Data.ConfigEntry.Language;
                return string.IsNullOrWhiteSpace(language) ? FallbackLocale : language.Trim();
            }
            catch (InvalidOperationException)
            {
                return FallbackLocale;
            }
        }
    }

    public static void InitializeFromCache()
    {
        Logger.Info(LogLanguageManager.Instance.news_cacheLoadStart.CurrentValue());
        var locale = CurrentLocale;
        var java = LoadCache(NewsEdition.Java, locale);
        var bedrock = LoadCache(NewsEdition.Bedrock, locale);
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
            var locale = CurrentLocale;
            var localizedUrl = $"{NewsApiBase}/{locale}.json";

            // 先试当前语言的 feed；该语言没有对应文件时（404）静默回落英文，
            // 所以第一次失败不记日志，避免给用户刷一条「获取 News 新闻失败」。
            var entries = await FetchAsync(localizedUrl, logFailure: false);
            if (entries == null && !string.Equals(locale, FallbackLocale, StringComparison.OrdinalIgnoreCase))
                entries = await FetchAsync(DefaultNewsApiUrl, logFailure: true);

            var changed = false;

            if (entries is { Count: > 0 })
            {
                var java = entries.Where(e => e.IsJava).ToList();
                var bedrock = entries.Where(e => e.IsBedrock).ToList();

                CacheDatabase.WriteNews(NewsEdition.Java, locale, java);
                CacheDatabase.WriteNews(NewsEdition.Bedrock, locale, bedrock);

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

    private static async Task<List<NewsEntry>?> FetchAsync(string url, bool logFailure)
    {
        try
        {
            var json = await NewsHttp.Client.GetStringAsync(url);
            return ParseJson(json);
        }
        catch (Exception ex)
        {
            if (logFailure)
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
