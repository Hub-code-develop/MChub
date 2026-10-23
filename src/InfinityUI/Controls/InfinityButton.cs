using Avalonia;
using Avalonia.Controls;

namespace InfinityUI.Controls;

/// <summary>infinityUI 按钮的语义变体。全库只允许这四种，不新增彩色变体。</summary>
public enum InfinityButtonVariant
{
    /// <summary>主操作：主题色实心（深色主题=白底黑字）。</summary>
    Primary,

    /// <summary>次操作：透明底 + 描边。</summary>
    Secondary,

    /// <summary>纯文字按钮，用于工具栏、行内操作。</summary>
    Ghost,

    /// <summary>破坏性操作：红色实心。全库唯一允许用红的按钮。</summary>
    Danger
}

/// <summary>
/// infinityUI 的基础按钮：直角、无阴影、靠背景与描边区分层级。
/// 从 <see cref="Button" /> 继承以复用 Click / Command / 焦点逻辑，模板整体自绘。
/// </summary>
public class InfinityButton : Button
{
    public static readonly StyledProperty<InfinityButtonVariant> VariantProperty =
        AvaloniaProperty.Register<InfinityButton, InfinityButtonVariant>(nameof(Variant));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<InfinityButton, bool>(nameof(IsCompact));

    public InfinityButton()
    {
    }

    public InfinityButtonVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
