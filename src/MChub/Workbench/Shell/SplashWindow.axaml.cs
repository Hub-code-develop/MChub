using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using MChub.Core.Services;

namespace MChub.Workbench.Shell;

/// <summary>
/// 启动画面。只做「展示 + 汇报进度」，生命周期由 <see cref="SplashService"/> 管。
/// 界面元素在这里用代码补（阶段行），因为阶段数量固定、且状态机比 XAML 触发器好写。
/// </summary>
public partial class SplashWindow : Window
{
    private const int StepCount = 4;

    private static readonly IBrush DotPending = new SolidColorBrush(Color.Parse("#3A3A3F"));
    private static readonly IBrush DotActive = new SolidColorBrush(Color.Parse("#007ACC"));
    private static readonly IBrush DotDone = new SolidColorBrush(Color.Parse("#63636B"));
    private static readonly IBrush FgPending = new SolidColorBrush(Color.Parse("#55555C"));
    private static readonly IBrush FgActive = new SolidColorBrush(Color.Parse("#E8E8EA"));
    private static readonly IBrush FgDone = new SolidColorBrush(Color.Parse("#7A7A82"));

    private readonly Border[] _dots = new Border[StepCount];
    private readonly TextBlock[] _labels = new TextBlock[StepCount];

    private double _progress;

    public SplashWindow()
    {
        InitializeComponent();

        BuildSteps();
        VersionText.Text = DescribeVersion();

        ProgressTrack.SizeChanged += (_, _) => ApplyProgressWidth();

        Opened += (_, _) => SetupScreenshotHook();
    }

    private static string DescribeVersion()
    {
        try
        {
            var version = AppVersionService.Instance.Version;
            var type = string.IsNullOrWhiteSpace(version.Type) ? "dev" : version.Type;
            var title = string.IsNullOrWhiteSpace(version.VersionTitle) ? "local-build" : version.VersionTitle;
            return $"{type.ToUpperInvariant()} · {title}";
        }
        catch
        {
            return "MChub";
        }
    }

    private void BuildSteps()
    {
        for (var i = 0; i < StepCount; i++)
        {
            var dot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = DotPending,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Margin = new Thickness(11, 0, 0, 0),
                FontSize = 12,
                Foreground = FgPending,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var row = new Grid
            {
                Height = 22,
                ColumnDefinitions = new ColumnDefinitions("6,*")
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(dot);
            row.Children.Add(label);
            StepsHost.Children.Add(row);

            _dots[i] = dot;
            _labels[i] = label;
        }
    }

    /// <summary>设置四个阶段的文案。等本地化服务就绪后再调用。</summary>
    public void SetStepLabels(string[] labels)
    {
        for (var i = 0; i < StepCount && i < labels.Length; i++)
            _labels[i].Text = labels[i];
    }

    /// <summary>
    /// 推进进度。<paramref name="activeStep"/> 之前的阶段算完成，等于的算进行中，
    /// 之后的还是待办；传 <see cref="StepCount"/> 表示全部完成。
    /// </summary>
    public void SetStage(int activeStep, double progress, string? detail = null)
    {
        activeStep = Math.Clamp(activeStep, 0, StepCount);
        _progress = Math.Clamp(progress, 0, 1);

        for (var i = 0; i < StepCount; i++)
        {
            var state = i < activeStep ? 1 : i == activeStep ? 2 : 0;
            _dots[i].Background = state switch
            {
                1 => DotDone,
                2 => DotActive,
                _ => DotPending
            };
            _labels[i].Foreground = state switch
            {
                1 => FgDone,
                2 => FgActive,
                _ => FgPending
            };
            _labels[i].FontWeight = state == 2 ? FontWeight.SemiBold : FontWeight.Normal;
        }

        StagePercent.Text = $"{Math.Round(_progress * 100):0}%";
        if (detail is not null)
            StageDetail.Text = detail;

        ApplyProgressWidth();
    }

    public void SetTitle(string title) => StageTitle.Text = title;

    public void SetDetail(string detail) => StageDetail.Text = detail;

    public void MarkFailed(string detail)
    {
        StageDetail.Foreground = new SolidColorBrush(Color.Parse("#F48771"));
        StageDetail.Text = detail;
        ProgressFill.Background = new SolidColorBrush(Color.Parse("#C93635"));
    }

    private void ApplyProgressWidth()
    {
        var track = ProgressTrack.Bounds.Width;
        if (track <= 0)
            return;
        ProgressFill.Width = Math.Max(0, track * _progress);
    }

    /// <summary>淡出后再真正关闭，避免窗口「啪」地消失。</summary>
    public async Task FadeOutAsync()
    {
        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(170),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters = { new Setter(OpacityProperty, 0d) }
                    }
                }
            };
            await animation.RunAsync(this);
        }
        catch
        {
            // 动画失败不影响关闭
        }
    }

    /// <summary>
    /// 开发期自查：<c>MCHUB_SPLASH_SHOT=/path/x.png</c> 时把画面渲染成 PNG。
    /// 和主窗口是同一套办法，用于没有屏幕录制权限时核对版式。
    /// </summary>
    private void SetupScreenshotHook()
    {
        var path = Environment.GetEnvironmentVariable("MCHUB_SPLASH_SHOT");
        if (string.IsNullOrWhiteSpace(path))
            return;

        DispatcherTimer.RunOnce(() => Capture(path), TimeSpan.FromMilliseconds(600));
    }

    private void Capture(string path)
    {
        try
        {
            if (Content is not Visual root)
                return;

            var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
            // 注意：位图尺寸和 dpi 都不要再乘一次缩放，否则内容会被放大两遍（缩放²），
            // 截出来只有左上角一块。用逻辑尺寸 + 96dpi 是稳的。
            var size = new PixelSize(
                Math.Max(1, (int)Math.Round(Bounds.Width)),
                Math.Max(1, (int)Math.Round(Bounds.Height)));

            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(root);
            bitmap.Save(path);
            Console.WriteLine($"[SPLASH-SHOT] {path} {size.Width}x{size.Height} @{scaling}x");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SPLASH-SHOT] 失败: " + ex);
        }
    }
}
