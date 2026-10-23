using Avalonia;
using Avalonia.Controls.Primitives;

namespace InfinityUI.Controls;

/// <summary>
/// 直角开关。轨道是一个矩形，滑块是方块，选中时填充主题色并右移。
/// 继承 <see cref="ToggleButton" /> 以保留 IsChecked 与 Click 语义。
/// </summary>
public class InfinityToggleSwitch : ToggleButton
{
    public static readonly StyledProperty<double> TrackWidthProperty =
        AvaloniaProperty.Register<InfinityToggleSwitch, double>(nameof(TrackWidth), 42);

    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<InfinityToggleSwitch, double>(nameof(TrackHeight), 20);

    /// <summary>开启时显示在开关右侧的内容。</summary>
    public static readonly StyledProperty<object?> OnContentProperty =
        AvaloniaProperty.Register<InfinityToggleSwitch, object?>(nameof(OnContent));

    /// <summary>关闭时显示在开关右侧的内容。</summary>
    public static readonly StyledProperty<object?> OffContentProperty =
        AvaloniaProperty.Register<InfinityToggleSwitch, object?>(nameof(OffContent));

    public object? OnContent
    {
        get => GetValue(OnContentProperty);
        set => SetValue(OnContentProperty, value);
    }

    public object? OffContent
    {
        get => GetValue(OffContentProperty);
        set => SetValue(OffContentProperty, value);
    }

    public double TrackWidth
    {
        get => GetValue(TrackWidthProperty);
        set => SetValue(TrackWidthProperty, value);
    }

    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }
}
