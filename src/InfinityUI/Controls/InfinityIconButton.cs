using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InfinityUI.Controls;

/// <summary>方形图标按钮，用于工具栏与行内操作。尺寸固定，图标走字形。</summary>
public class InfinityIconButton : Button
{
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<InfinityIconButton, double>(nameof(IconSize), 16);

    public static readonly StyledProperty<double> BoxSizeProperty =
        AvaloniaProperty.Register<InfinityIconButton, double>(nameof(BoxSize), 30);

    /// <summary>图标字形（配合宿主传入的图标字体）。</summary>
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<InfinityIconButton, string?>(nameof(Glyph));

    public static readonly StyledProperty<FontFamily?> GlyphFontFamilyProperty =
        AvaloniaProperty.Register<InfinityIconButton, FontFamily?>(nameof(GlyphFontFamily));

    /// <summary>有没有字形，供模板决定显隐（避免在 XAML 里用转换器）。</summary>
    public static readonly DirectProperty<InfinityIconButton, bool> HasGlyphProperty =
        AvaloniaProperty.RegisterDirect<InfinityIconButton, bool>(nameof(HasGlyph), o => o.HasGlyph);

    private bool _hasGlyph;

    static InfinityIconButton()
    {
        GlyphProperty.Changed.AddClassHandler<InfinityIconButton>((button, _) =>
            button.SetAndRaise(HasGlyphProperty, ref button._hasGlyph,
                !string.IsNullOrEmpty(button.Glyph)));
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double BoxSize
    {
        get => GetValue(BoxSizeProperty);
        set => SetValue(BoxSizeProperty, value);
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

    public bool HasGlyph
    {
        get => _hasGlyph;
        private set => SetAndRaise(HasGlyphProperty, ref _hasGlyph, value);
    }
}
