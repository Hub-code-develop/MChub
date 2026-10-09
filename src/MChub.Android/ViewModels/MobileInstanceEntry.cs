using System.ComponentModel;
using Avalonia.Media.Imaging;
using MChub.Core.Minecraft.Classes;

namespace MChub.Mobile.ViewModels;

/// <summary>
/// 主页实例卡片的视图模型。
///
/// <para>为什么要包一层：卡片需要「是否为当前实例」这个**界面态**，而
/// <see cref="MinecraftInstance"/> 是 Core 的业务模型（桌面端共用），不该为移动端加界面字段。</para>
///
/// <para>不依赖 MVVM 源生成器：CommunityToolkit.Mvvm 的生成器不会传递到本项目
/// （PackageReference 的分析器默认不随传递引用流动），所以这里手写 <see cref="INotifyPropertyChanged"/>。</para>
/// </summary>
public sealed class MobileInstanceEntry(MinecraftInstance instance) : INotifyPropertyChanged
{
    private bool _isCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>被包装的实例本体。</summary>
    public MinecraftInstance Instance { get; } = instance;

    public string InstanceName => Instance.InstanceName;

    /// <summary>形如 <c>Fabric·1.20.1</c>。</summary>
    public string ShortDisplay => Instance.ShortDisplay;

    public Bitmap Icon => Instance.Icon;

    /// <summary>是否为「当前实例」，卡片选中态与它绑定。</summary>
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
