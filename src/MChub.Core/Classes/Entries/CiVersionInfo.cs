using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MChub.Core.Classes.Entries;

public partial class CiVersionInfo : ObservableObject
{
    [JsonPropertyName("type")]
    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [JsonPropertyName("build-time")]
    [ObservableProperty]
    public partial DateTime BuildTime { get; set; }

    [JsonPropertyName("action")]
    [ObservableProperty]
    public partial string Action { get; set; } = string.Empty;

    [JsonPropertyName("commit")]
    [ObservableProperty]
    public partial string Commit { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [ObservableProperty]
    public partial string Version { get; set; } = string.Empty;

    [JsonPropertyName("version_title")]
    [ObservableProperty]
    public partial string VersionTitle { get; set; } = string.Empty;

    /// <summary>
    /// 构建变体。同一个 release 里会并列存放两套构建：
    /// <c>standard</c> 对应普通版的 <c>MChub.*</c> 资产，
    /// <c>newui</c> 对应 InfinityUI 重写版的 <c>MChub.NewUI.*</c> 资产。
    /// 旧版本发布产物没有这个字段，缺省即 <c>standard</c>，行为与改动前一致。
    /// </summary>
    [JsonPropertyName("edition")]
    [ObservableProperty]
    public partial string Edition { get; set; } = "standard";
}