using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MChub.Controls;

/// <summary>
/// 自研 Fluent 取色器：Fluent 按钮 + Flyout 内嵌 Avalonia <see cref="ColorView"/>，
/// 提供标准的三页签（光谱/调色板/组件）取色体验，替代 tio:TioColorPicker。
/// </summary>
public class FluentColorPicker : ContentControl
{
    /// <summary>
    /// 当前选中颜色，双向绑定到 <see cref="ColorView.Color"/>。
    ///
    /// <para><b>必须是 TwoWay</b>：用户选完色要回写到绑定源（如 <c>ConfigEntry.ThemeColor</c>），
    /// 否则主题色既不生效也不会被保存。XAML 写 <c>Color="{Binding X}"</c> 时的绑定方向
    /// 取的就是这里的 defaultBindingMode —— 别改成 OneWay。</para>
    /// </summary>
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<FluentColorPicker, Color>(nameof(Color), Colors.White,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// 当前选中颜色的画刷（颜色驱动的同步只读属性）。
    /// 给个初值，免得还没触发过变更时色块是空的。
    /// </summary>
    public static readonly StyledProperty<IBrush> ColorBrushProperty =
        AvaloniaProperty.Register<FluentColorPicker, IBrush>(nameof(ColorBrush),
            new ImmutableSolidColorBrush(Colors.White));

    /// <summary>
    /// 光谱形状（Box / Ring）。
    /// </summary>
    public static readonly StyledProperty<ColorSpectrumShape> ColorSpectrumShapeProperty =
        AvaloniaProperty.Register<FluentColorPicker, ColorSpectrumShape>(
            nameof(ColorSpectrumShape), ColorSpectrumShape.Box);

    /// <summary>
    /// 默认选中的页签索引：0=光谱，1=调色板，2=组件。
    /// </summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<FluentColorPicker, int>(nameof(SelectedIndex), 1);

    /// <summary>
    /// 调色板数据源。
    /// </summary>
    public static readonly StyledProperty<IColorPalette?> PaletteProperty =
        AvaloniaProperty.Register<FluentColorPicker, IColorPalette?>(nameof(Palette), null);

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public IBrush ColorBrush => GetValue(ColorBrushProperty);

    public ColorSpectrumShape ColorSpectrumShape
    {
        get => GetValue(ColorSpectrumShapeProperty);
        set => SetValue(ColorSpectrumShapeProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public IColorPalette? Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty)
            SetCurrentValue(ColorBrushProperty, new ImmutableSolidColorBrush(Color));
    }
}