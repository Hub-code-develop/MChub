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
    // 直接用 Mojang 官方启动器的内容接口（launchercontent 就是官方启动器拉
    // 新闻 / 补丁说明的地方）。在此之前指向的是自建占位域名 mcnews.cangcang.cc ——
    // 那个域名并不存在，导致启动时固定报「获取新闻失败」。
    private const string JavaApiUrl = "https://launchercontent.mojang.com/v2/javaPatchNotes.json";
    private const string BedrockApiUrl = "https://launchercontent.mojang.com/v2/bedrockPatchNotes.json";
    private const string BaseImageUrl = "https://launchercontent.mojang.com";

    public static List<NewsEntry> JavaNews { get; private set; } = [];
    public static List<NewsEntry> BedrockNews { get; private set; } = [];
    public static event EventHandler? NewsUpdated;

    public static void InitializeFromCache()
    {
        Logger.Info(LogLanguageManager.Instance.news_cacheLoadStart.CurrentValue());
        JavaNews = LoadCache(NewsEdition.Java);
        BedrockNews = LoadCache(NewsEdition.Bedrock);
        Logger.Info(string.Format(LogLanguageManager.Instance.news_cacheLoadComplete.CurrentValue(), JavaNews.Count, BedrockNews.Count));
    }

    public static void RaiseNewsUpdated()
    {
        if (JavaNews.Count > 0 || BedrockNews.Count > 0)
            NewsUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static async Task FetchAndRefreshAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(LogLanguageManager.Instance.news_fetchStart.CurrentValue());
        try
        {
            var jTask = FetchAsync(JavaApiUrl, NewsEdition.Java);
            var bTask = FetchAsync(BedrockApiUrl, NewsEdition.Bedrock);

            var java = await jTask;
            var bedrock = await bTask;
            var changed = false;

            if (java?.Count > 0)
            {
                JavaNews = java;
                changed = true;
            }

            if (bedrock?.Count > 0)
            {
                BedrockNews = bedrock;
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

    private static List<NewsEntry> LoadCache(NewsEdition edition)
    {
        try
        {
            return CacheDatabase.ReadNews(edition);
        }
        catch (Exception ex)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.news_cacheLoadFailed.CurrentValue(), edition), ex);
            return [];
        }
    }

    private static async Task<List<NewsEntry>?> FetchAsync(string url, NewsEdition edition)
    {
        try
        {
            var json = await NewsHttp.Client.GetStringAsync(url);
            var entries = ParseJson(json, edition);
            CacheDatabase.WriteNews(edition, entries);
            return entries;
        }
        catch (Exception ex)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.news_fetchEditionFailed.CurrentValue(), edition, url), ex);
            return null;
        }
    }

    private static List<NewsEntry> ParseJson(string json, NewsEdition edition)
    {
        var response = JsonSerializer.Deserialize<PatchNotesResponse>(json, MChubJson.Options);
        return response?.Entries?.Select(e => MapToNewsEntry(e, edition)).ToList() ?? [];
    }

    private static NewsEntry MapToNewsEntry(PatchNoteEntry entry, NewsEdition edition)
    {
        var imageUrl = string.Empty;
        if (!string.IsNullOrEmpty(entry.Image?.Url))
            imageUrl = entry.Image.Url.StartsWith("http") ? entry.Image.Url : BaseImageUrl + entry.Image.Url;

        return new NewsEntry
        {
            Title = entry.Title,
            Version = entry.Version,
            Type = !string.IsNullOrEmpty(entry.Type) ? entry.Type : entry.PatchNoteType,
            ImageUrl = imageUrl,
            ContentPath = entry.ContentPath,
            Id = entry.Id,
            Date = entry.Date.ToLocalTime(),
            ShortText = entry.ShortText,
            NeedsTranslation = entry.NeedsTranslation,
            Edition = edition
        };
    }
}

internal static class NewsHttp
{
    // 不要再关掉证书校验：以前指向自建占位域名（证书不可信）才被迫这么写，
    // 现在直连 launchercontent.mojang.com，走系统信任链即可（实测严格校验下三个
    // 端点都正常）。关掉校验等于给中间人开绿灯 —— 本机还挂着 TUN 代理，风险更实在。
    public static readonly HttpClient Client = new();
}