using System.Text.Json;
using MChub.Core.Const;

namespace MChub.Core.Creator;

/// <summary>
/// 创作台自己的设置。独立于主配置存盘，避免把只影响这一页的偏好塞进 ConfigEntry。
/// 文件位置：<c>&lt;UserDataRoot&gt;/Creator.json</c>
/// </summary>
public sealed class CreatorSettings
{
    private static readonly string FilePath = Path.Combine(ConfigPath.UserDataRootPath, "Creator.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>模组清单里是否包含已禁用的模组。</summary>
    public bool IncludeDisabledMods { get; set; }

    /// <summary>截图墙一次最多渲染多少张（防止几千张图把内存吃光）。</summary>
    public int ThumbnailLimit { get; set; } = 120;

    /// <summary>对比页默认的贴图路径。</summary>
    public string DefaultTexturePath { get; set; } = "block/stone";

    public static CreatorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<CreatorSettings>(File.ReadAllText(FilePath)) ?? new CreatorSettings();
        }
        catch
        {
            // 配置坏了就用默认值，不要让创作台打不开
        }

        return new CreatorSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // 存盘失败不打断使用
        }
    }
}
