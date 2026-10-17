using System.Text.Json;
using MChub.Core.App.Helpers;
using MChub.Core.Json;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Minecraft;

public static class NewsDetailsService
{
    // 补丁说明正文同样取自 Mojang 官方内容接口：条目的 contentPath 形如
    // "javaPatchNotes/26-4-snapshot-4.json"，拼上这个前缀即可。
    private const string BaseContentUrl = "https://launchercontent.mojang.com/v2/";

    private const string BaseImageUrl = "https://launchercontent.mojang.com";


    private static readonly LruCache<string, NewsDetail?> MemoryCache = new(32, StringComparer.Ordinal);


    private static readonly HashSet<string> RefreshingIds = new(StringComparer.Ordinal);
    private static readonly object RefreshLock = new();

    public static event Action<NewsDetail>? NewsDetailUpdated;

    public static async Task<NewsDetail?> GetAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return null;

        if (MemoryCache.TryGetValue(entry.Id, out var cached))
        {
            TryBackgroundRefreshIfNeedsTranslation(entry, cached);
            return cached;
        }

        var fromDb = CacheDatabase.ReadNewsDetail(entry.Id);
        if (fromDb != null)
        {
            MemoryCache.Set(entry.Id, fromDb);
            TryBackgroundRefreshIfNeedsTranslation(entry, fromDb);
            return fromDb;
        }

        var fetched = await FetchAsync(entry);
        if (fetched != null)
        {
            CacheDatabase.WriteNewsDetail(fetched);
            MemoryCache.Set(entry.Id, fetched);

            TryBackgroundRefreshIfNeedsTranslation(entry, fetched);
        }

        return fetched;
    }

    public static async Task<NewsDetail?> RefreshAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return null;
        var fetched = await FetchAsync(entry);
        if (fetched != null)
        {
            CacheDatabase.WriteNewsDetail(fetched);
            MemoryCache.Set(entry.Id, fetched);

            TryBackgroundRefreshIfNeedsTranslation(entry, fetched);
        }

        return fetched;
    }

    private static void TryBackgroundRefreshIfNeedsTranslation(NewsEntry entry, NewsDetail? cached)
    {
        if (cached?.NeedsTranslation != true) return;

        lock (RefreshLock)
        {
            if (!RefreshingIds.Add(entry.Id)) return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var fetched = await FetchAsync(entry);
                if (fetched == null) return;


                if (fetched.NeedsTranslation == true) return;


                CacheDatabase.WriteNewsDetail(fetched);
                MemoryCache.Set(entry.Id, fetched);
                NewsDetailUpdated?.Invoke(fetched);
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format(LogLanguageManager.Instance.news_detailsBackgroundTranslationFailed.CurrentValue(), entry.ContentPath), ex);
            }
            finally
            {
                lock (RefreshLock)
                {
                    RefreshingIds.Remove(entry.Id);
                }
            }
        });
    }

    private static async Task<NewsDetail?> FetchAsync(NewsEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ContentPath)) return null;
        try
        {
            var url = BaseContentUrl + entry.ContentPath;
            var json = await NewsHttp.Client.GetStringAsync(url);
            var content = JsonSerializer.Deserialize<NewsContentResponse>(json, MChubJson.Options);
            if (content == null) return null;

            var imageUrl = entry.ImageUrl;
            if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(content.Image?.Url))
                imageUrl = content.Image.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? content.Image.Url
                    : BaseImageUrl + content.Image.Url;

            return new NewsDetail
            {
                Id = entry.Id,
                Title = string.IsNullOrEmpty(content.Title) ? entry.Title : content.Title,
                Version = string.IsNullOrEmpty(content.Version) ? entry.Version : content.Version,
                Type = string.IsNullOrEmpty(content.Type) ? entry.Type : entry.Type,
                ImageUrl = imageUrl,
                Date = entry.Date,
                Body = content.Body ?? string.Empty,
                NeedsTranslation = content.NeedsTranslation,
                FetchedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.news_detailsFetchFailed.CurrentValue(), entry.ContentPath), ex);
            return null;
        }
    }
}