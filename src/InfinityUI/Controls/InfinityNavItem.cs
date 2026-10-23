using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InfinityUI.Controls;

/// <summary>
/// 导航项。选中态是"整块实心 + 反色文字"，可选在左侧显示一条语义指示条。
/// 同时供底部主导航与设置页左侧分组导航使用。
/// </summary>
public class InfinityNavItem : Button
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<InfinityNavItem, bool>(nameof(IsActive));

    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<InfinityNavItem, string?>(nameof(Glyph));

    public static readonly StyledProperty<FontFamily?> GlyphFontFamilyProperty =
        AvaloniaProperty.Register<InfinityNavItem, FontFamily?>(nameof(GlyphFontFamily));

    public static readonly StyledProperty<bool> ShowIndicatorProperty =
        AvaloniaProperty.Register<InfinityNavItem, bool>(nameof(ShowIndicator));

    /// <summary>有没有字形，供模板决定显隐。</summary>
    public static readonly DirectProperty<InfinityNavItem, bool> HasGlyphProperty =
        AvaloniaProperty.RegisterDirect<InfinityNavItem, bool>(nameof(HasGlyph), o => o.HasGlyph);

    private bool _hasGlyph;

    static InfinityNavItem()
    {
        GlyphProperty.Changed.AddClassHandler<InfinityNavItem>((item, _) =>
            item.SetAndRaise(HasGlyphProperty, ref item._hasGlyph, !string.IsNullOrEmpty(item.Glyph)));
    }

    public bool HasGlyph
    {
        get => _hasGlyph;
        private set => SetAndRaise(HasGlyphProperty, ref _hasGlyph, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public FontFamily? GlyphFontFamily
    {
        get => GetValue(GlyphFontFamilyProperty);
        set => SetValue(GlyphFontFamilyProperty, value);
    }

    /// <summary>选中时在左侧显示 3px 主题色指示条（侧边导航用）。</summary>
    public bool ShowIndicator
    {
        get => GetValue(ShowIndicatorProperty);
        set => SetValue(ShowIndicatorProperty, value);
    }
}
