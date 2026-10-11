using System.ComponentModel;
using Avalonia.Media.Imaging;
using MChub.Core.Minecraft.Classes;

namespace MChub.Mobile.ViewModels;

/// <summary>
/// 账户页里的一张账户卡片。
///
/// <para>为什么要包一层：卡片需要「是否为当前账户」这个**界面态**，而
/// <see cref="MinecraftAccount"/> 是 Core 的业务模型（桌面端共用），不该为移动端加界面字段。</para>
///
/// <para>皮肤图直接借 Core 已经算好的 <see cref="MinecraftAccount.Head"/>（离线账户默认给 Steve），
/// 不用移动端再引一套皮肤渲染。</para>
/// </summary>
public sealed class MobileAccountEntry(MinecraftAccount account) : INotifyPropertyChanged
{
    private bool _isCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>被包装的账户本体。</summary>
    public MinecraftAccount Account { get; } = account;

    public string Name => Account.Name;

    /// <summary>形如 <c>离线·Steve</c>。</summary>
    public string ShortDisplay => Account.ShortDisplay;

    /// <summary>账户类型短标签（离线 / 微软 / 第三方登录）。</summary>
    public string TypeLabel => Account.ShortType;

    /// <summary>备注；没备注时回落成类型说明，卡片上不留空行。</summary>
    public string Note => string.IsNullOrWhiteSpace(Account.AccountNote)
        ? Account.DisplayAccountNote
        : $"{Account.DisplayAccountNote} · {Account.AccountNote}";

    /// <summary>头像（Core 从皮肤里裁出来的 36px 头）。</summary>
    public Bitmap Head => Account.Head;

    /// <summary>离线账户才允许改名字（在线账户的名字由认证服务端说了算）。</summary>
    public bool IsOffline => Account.IsOffline;

    /// <summary>是否为「当前账户」，卡片选中态与它绑定。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
                return;

            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }
}
