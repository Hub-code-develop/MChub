using Avalonia;
using Avalonia.Controls;

namespace InfinityUI.Controls;

/// <summary>
/// 直角卡片容器。只做"边框 + 底色 + 内边距"，不引入阴影与圆角，
/// 层级一律靠 1px 描边和灰阶区分。
/// </summary>
public class InfinityCard : ContentControl
{
    public static readonly StyledProperty<bool> IsRaisedProperty =
        AvaloniaProperty.Register<InfinityCard, bool>(nameof(IsRaised));

    public static readonly StyledProperty<bool> ShowBorderProperty =
        AvaloniaProperty.Register<InfinityCard, bool>(nameof(ShowBorder), true);

    public static readonly StyledProperty<Thickness> ContentPaddingProperty =
        AvaloniaProperty.Register<InfinityCard, Thickness>(nameof(ContentPadding), new Thickness(14));

    /// <summary>抬高一级的底色（用于浮层里的卡片）。</summary>
    public bool IsRaised
    {
        get => GetValue(IsRaisedProperty);
        set => SetValue(IsRaisedProperty, value);
    }

    public bool ShowBorder
    {
        get => GetValue(ShowBorderProperty);
        set => SetValue(ShowBorderProperty, value);
    }

    public Thickness ContentPadding
    {
        get => GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }
}
