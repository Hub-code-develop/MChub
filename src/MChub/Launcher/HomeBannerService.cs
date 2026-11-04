using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace MChub.Launcher;

/// <summary>
/// 主界面左侧的大图。图片由远端配置下发（和微软官方启动器的新闻位一个思路），
/// 拿不到就返回 null，由界面把那块区域收起来 —— 绝不拿假数据占位。
///
/// 接口约定（先留空，等配置地址给过来直接填）：
///   GET  {endpoint}  -&gt;  [{ "title": "...", "subtitle": "...", "image": "https://..." }]
/// 或者  { "items": [ ... ] }
/// 环境变量 <c>MCHUB_BANNER_URL</c> / <c>MCHUB_BANNER_IMAGE</c> 可以临时覆盖，方便联调。
/// </summary>
public static class HomeBannerService
{
    /// <summary>远端配置地址。拿到正式地址后填这里即可。</summary>
    private const string Endpoint = "";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MChub", "banner");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public sealed record Banner(string Title, string Subtitle, Bitmap Image);

    public static async Task<Banner?> GetAsync()
    {
        // 本地调试图优先：方便没有接口时先看版式
        var local = Environment.GetEnvironmentVariable("MCHUB_BANNER_IMAGE");
        if (!string.IsNullOrWhiteSpace(local) && File.Exists(local))
        {
            var bitmap = TryLoad(local);
            if (bitmap is not null)
                return new Banner("Minecraft", "本地预览图", bitmap);
        }

        var endpoint = Environment.GetEnvironmentVariable("MCHUB_BANNER_URL");
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            return await FromCacheAsync();

        try
        {
            var json = await Http.GetStringAsync(endpoint).ConfigureAwait(false);
            File.WriteAllText(Path.Combine(CacheDir, "feed.json"), json);

            var item = ParseFirst(json);
            if (item is null)
                return await FromCacheAsync();

            var imageBytes = await Http.GetByteArrayAsync(item.ImageUrl).ConfigureAwait(false);
            var path = Path.Combine(CacheDir, "banner.img");
            await File.WriteAllBytesAsync(path, imageBytes).ConfigureAwait(false);

            using var stream = new MemoryStream(imageBytes);
            return new Banner(item.Title, item.Subtitle, new Bitmap(stream));
        }
        catch
        {
            // 网络/接口有问题就退回缓存，再不行就交给界面降级
            return await FromCacheAsync();
        }
    }

    private sealed record FeedItem(string Title, string Subtitle, string ImageUrl);

    private static FeedItem? ParseFirst(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
            root = items;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        var first = root[0];
        var title = Str(first, "title") ?? "Minecraft";
        var subtitle = Str(first, "subtitle") ?? string.Empty;
        var image = Str(first, "image") ?? Str(first, "imageUrl");
        if (string.IsNullOrWhiteSpace(image))
            return null;

        return new FeedItem(title, subtitle, image);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<Banner?> FromCacheAsync()
    {
        var jsonPath = Path.Combine(CacheDir, "feed.json");
        var imagePath = Path.Combine(CacheDir, "banner.img");
        if (!File.Exists(jsonPath) || !File.Exists(imagePath))
            return null;

        try
        {
            var item = ParseFirst(await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false));
            var bitmap = TryLoad(imagePath);
            return item is null || bitmap is null
                ? null
                : new Banner(item.Title, item.Subtitle, bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoad(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}
