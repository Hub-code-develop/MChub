using MinecraftLaunch.Base.Models.Network;

namespace MChub.Mobile.ViewModels;

/// <summary>
/// 「新建实例」页里版本列表的一行。
///
/// <p><see cref="VersionManifestEntry"/> 是 Core / MinecraftLaunch 的网络模型，
/// 不为移动端界面改它；这里只做展示层包装（类型标签的文字需要本地化，由页面填进来）。</p>
/// </summary>
public sealed class MobileVersionOption
{
    public required VersionManifestEntry Entry { get; init; }

    /// <summary>本地化后的类型标签（正式版 / 快照 / 旧版）。</summary>
    public required string TypeLabel { get; init; }

    public string Id => Entry.Id;

    public string ReleaseText => Entry.ReleaseTime == default
        ? string.Empty
        : Entry.ReleaseTime.ToLocalTime().ToString("yyyy-MM-dd");
}
