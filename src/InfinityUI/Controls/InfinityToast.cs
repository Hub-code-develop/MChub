using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace InfinityUI.Controls;

/// <summary>通知级别。只有这四种，红黄绿仅在此处出现。</summary>
public enum InfinityToastLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Metro 风格通知卡片：直角、左侧 6px 语义色条、标题 + 正文 + 关闭按钮。
/// 不做圆角、不做投影。
/// </summary>
public class InfinityToast : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<InfinityToast, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfinityToast, string?>(nameof(Message));

    public static readonly StyledProperty<InfinityToastLevel> LevelProperty =
        AvaloniaProperty.Register<InfinityToast, InfinityToastLevel>(nameof(Level));

    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<InfinityToast, string?>(nameof(Glyph));

    /// <summary>图标字形所用的字体；与按钮、导航项保持一致，必须是 <see cref="FontFamily" /> 类型。</summary>
    public static readonly StyledProperty<FontFamily?> GlyphFontFamilyProperty =
        AvaloniaProperty.Register<InfinityToast, FontFamily?>(nameof(GlyphFontFamily));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public InfinityToastLevel Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
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

    /// <summary>自动关闭计时器，由宿主创建并在关闭时停掉，避免计时器悬挂。</summary>
    internal DispatcherTimer? AutoDismissTimer { get; set; }

    /// <summary>用户点关闭时触发，宿主负责移除。</summary>
    public event EventHandler? Dismissed;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => RequestDismiss();
    }

    internal void RequestDismiss()
    {
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// 通知宿主：右下角垂直堆叠，自动超时关闭，进出场淡入淡出。
/// 放在窗口内容的 <see cref="Panel" /> 里即可，自身不影响布局。
/// </summary>
public class InfinityToastHost : StackPanel
{
    private const int MaxVisible = 4;

    public InfinityToastHost()
    {
        Orientation = Avalonia.Layout.Orientation.Vertical;
        Spacing = 8;
        Width = 340;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 18, 96);
        IsHitTestVisible = true;
    }

    /// <summary>图标按级别固定，外部不可改，避免出现第五种颜色。</summary>
    private static string ResolveGlyph(InfinityToastLevel level)
    {
        return level switch
        {
            InfinityToastLevel.Success => "✓",
            InfinityToastLevel.Warning => "!",
            InfinityToastLevel.Error => "✕",
            _ => "i"
        };
    }

    public InfinityToast Show(
        string message,
        InfinityToastLevel level = InfinityToastLevel.Info,
        string? title = null,
        TimeSpan? duration = null)
    {
        var toast = new InfinityToast
        {
            Message = message,
            Title = title,
            Level = level,
            Glyph = ResolveGlyph(level),
            Opacity = 0
        };

        toast.Dismissed += (_, _) => Dismiss(toast);
        Children.Add(toast);

        while (Children.Count > MaxVisible && Children[0] is InfinityToast oldest)
            Dismiss(oldest);

        // 入场：淡入 + 轻微上移
        toast.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
            new ThicknessTransition { Property = MarginProperty, Duration = TimeSpan.FromMilliseconds(160) }
        };
        toast.Margin = new Thickness(0, 6, 0, 0);
        Dispatcher.UIThread.Post(() =>
        {
            toast.Opacity = 1;
            toast.Margin = default;
        });

        var timeout = duration ?? TimeSpan.FromSeconds(level == InfinityToastLevel.Error ? 8 : 5);
        if (timeout > TimeSpan.Zero)
        {
            var timer = new DispatcherTimer { Interval = timeout };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Dismiss(toast);
            };
            toast.AutoDismissTimer = timer;
            timer.Start();
        }

        return toast;
    }

    public void Dismiss(InfinityToast toast)
    {
        if (!Children.Contains(toast)) return;

        // 手动关闭后自动超时的计时器必须停掉，否则它还会再触发一次。
        toast.AutoDismissTimer?.Stop();
        toast.AutoDismissTimer = null;

        toast.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) }
        };
        toast.Opacity = 0;

        DispatcherTimer.RunOnce(() =>
        {
            if (Children.Contains(toast)) Children.Remove(toast);
        }, TimeSpan.FromMilliseconds(160));
    }

    public void Clear()
    {
        foreach (var child in Children.OfType<InfinityToast>().ToList())
            Dismiss(child);
    }
}
