using System.Diagnostics;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using LiteSkinViewer3D.OpenGL;
using LiteSkinViewer3D.Shared.Enums;
using SkiaSharp;

namespace LiteSkinViewer3D.Avalonia.Controls;

public sealed class SkinViewer3D : OpenGlControlBase, ICustomHitTest
{
    private static readonly Vector2 DefaultModelPosition = new(0f, 0.5f);

    public static readonly StyledProperty<string> SkinProperty =
        AvaloniaProperty.Register<SkinViewer3D, string>(nameof(Skin));

    public static readonly StyledProperty<string> CapeProperty =
        AvaloniaProperty.Register<SkinViewer3D, string>(nameof(Cape));

    public static readonly StyledProperty<bool> IsEnableAnimationProperty =
        AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(IsEnableAnimation), true);

    public static readonly StyledProperty<bool> IsCapeVisibleProperty =
        AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(IsCapeVisible), true);

    public static readonly StyledProperty<bool> IsUpperLayerVisibleProperty =
        AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(IsUpperLayerVisible), true);

    public static readonly StyledProperty<bool> IsTopLayer3DProperty =
        AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(IsTopLayer3D), false);

    public static readonly StyledProperty<bool> IsLightingEnabledProperty =
        AvaloniaProperty.Register<SkinViewer3D, bool>(nameof(IsLightingEnabled), true);

    public static readonly StyledProperty<Vector2> MaxPanOffsetProperty =
        AvaloniaProperty.Register<SkinViewer3D, Vector2>(nameof(MaxPanOffset), new Vector2(3f, 3f));

    public static readonly StyledProperty<SkinRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<SkinViewer3D, SkinRenderMode>(nameof(RenderMode), SkinRenderMode.FXAA);

    public static readonly StyledProperty<Vector3> HeadRotationProperty =
        AvaloniaProperty.Register<SkinViewer3D, Vector3>(nameof(HeadRotation));

    public static readonly StyledProperty<Vector2> ArmRotationProperty =
        AvaloniaProperty.Register<SkinViewer3D, Vector2>(nameof(ArmRotation));

    public static readonly StyledProperty<Vector2> LegRotationProperty =
        AvaloniaProperty.Register<SkinViewer3D, Vector2>(nameof(LegRotation));

    public static readonly StyledProperty<float> ModelDistanceProperty =
        AvaloniaProperty.Register<SkinViewer3D, float>(nameof(ModelDistance), 1f);

    public static readonly StyledProperty<Vector2> ModelRotationProperty =
        AvaloniaProperty.Register<SkinViewer3D, Vector2>(nameof(ModelRotation));

    public static readonly StyledProperty<Vector2> ModelPositionProperty =
        AvaloniaProperty.Register<SkinViewer3D, Vector2>(nameof(ModelPosition), DefaultModelPosition);

    private float _modelDistance;
    private bool _renderingFailed;
    private OpenGLSkinViewerBase? _skin;

    private DateTime _time;

    public SkinViewer3D()
    {
        _modelDistance = ModelDistance;
    }

    /// <summary>
    ///     皮肤渲染失败
    /// </summary>
    public event Action<object?, Exception>? RenderingFailed;

    public string Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public string Cape
    {
        get => GetValue(CapeProperty);
        set => SetValue(CapeProperty, value);
    }

    public bool IsCapeVisible
    {
        get => GetValue(IsCapeVisibleProperty);
        set => SetValue(IsCapeVisibleProperty, value);
    }

    public bool IsEnableAnimation
    {
        get => GetValue(IsEnableAnimationProperty);
        set => SetValue(IsEnableAnimationProperty, value);
    }

    public bool IsUpperLayerVisible
    {
        get => GetValue(IsUpperLayerVisibleProperty);
        set => SetValue(IsUpperLayerVisibleProperty, value);
    }

    public bool IsTopLayer3D
    {
        get => GetValue(IsTopLayer3DProperty);
        set => SetValue(IsTopLayer3DProperty, value);
    }

    public bool IsLightingEnabled
    {
        get => GetValue(IsLightingEnabledProperty);
        set => SetValue(IsLightingEnabledProperty, value);
    }

    public Vector2 MaxPanOffset
    {
        get => GetValue(MaxPanOffsetProperty);
        set => SetValue(MaxPanOffsetProperty, value);
    }

    public float ModelDistance
    {
        get => GetValue(ModelDistanceProperty);
        set => SetValue(ModelDistanceProperty, value);
    }

    public SkinRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public Vector3 HeadRotation
    {
        get => GetValue(HeadRotationProperty);
        set => SetValue(HeadRotationProperty, value);
    }

    public Vector2 ArmRotation
    {
        get => GetValue(ArmRotationProperty);
        set => SetValue(ArmRotationProperty, value);
    }

    public Vector2 LegRotation
    {
        get => GetValue(LegRotationProperty);
        set => SetValue(LegRotationProperty, value);
    }

    public Vector2 ModelPosition
    {
        get => GetValue(ModelPositionProperty);
        set => SetValue(ModelPositionProperty, value);
    }

    public Vector2 ModelRotation
    {
        get => GetValue(ModelRotationProperty);
        set => SetValue(ModelRotationProperty, value);
    }

    public bool HitTest(Point point)
    {
        return Bounds.Contains(point);
    }

    public void Reset()
    {
        _skin?.ResetPos();
        _modelDistance = 1f;
        RequestNextFrameRendering();
    }

    public void AddDis(float x)
    {
        _skin?.AddDis(x);
    }

    public void ChangeSkin(string skin, string cape = default!)
    {
        // 未完成 OpenGL 初始化时还没有 _skin；直接返回即可——OnOpenGlInit 会用当前的
        // Skin / Cape 属性值重新加载一次，不会丢设置。
        if (_skin == null)
            return;

        if (string.IsNullOrEmpty(skin) && string.IsNullOrEmpty(cape))
            return;

        if (!string.IsNullOrEmpty(skin))
            _skin.SetSkin(skin);

        if (!string.IsNullOrEmpty(cape))
        {
            if (File.Exists(cape))
                _skin.SetCapeTex(SKBitmap.Decode(cape));
        }

        RequestNextFrameRendering();
    }

    public void UpdatePointerMoved(PointerType type, Vector2 point)
    {
        if (_skin != null && _skin.HaveSkin)
        {
            _skin.PointerMoved(type, point);
            RequestNextFrameRendering();
        }
    }

    public void UpdatePointerPressed(PointerType type, Vector2 point)
    {
        if (_skin != null && _skin.HaveSkin)
        {
            _skin.PointerPressed(type, point);
            RequestNextFrameRendering();
        }
    }

    public void UpdatePointerReleased(PointerType type, Vector2 point)
    {
        if (_skin != null && _skin.HaveSkin)
        {
            _skin.PointerReleased(type, point);
            RequestNextFrameRendering();
        }
    }

    public void UpdatePointerWheelChanged(bool isPost)
    {
        if (_skin != null && _skin.HaveSkin)
        {
            _skin.PointerWheelChanged(isPost);
            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        CheckError(gl);
        _skin = new OpenGLSkinViewerBase(new AvaloniaApi(gl), GlVersion.Type is GlProfileType.OpenGLES)
        {
            BackColor = new Vector4(0f),
            RenderMode = RenderMode,
            EnableCape = IsCapeVisible,
            Animation = IsEnableAnimation,
            EnableTop = IsUpperLayerVisible,
            EnableTopLayer3D = IsTopLayer3D,
            EnableLighting = IsLightingEnabled,
            MaxPanOffset = MaxPanOffset
        };

        _skin.Error += (_, type) =>
            Debug.WriteLine(type.ToString());

        ChangeSkin(Skin, Cape);
        _skin.OpenGlInit();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderingFailed)
            return;

        try
        {
            RenderSkin(gl, fb);
        }
        catch (Exception ex)
        {
            _renderingFailed = true;
            RenderingFailed?.Invoke(this, ex);
        }
    }

    private void RenderSkin(GlInterface gl, int fb)
    {
        var width = (int)Bounds.Width;
        var height = (int)Bounds.Height;
        if (_skin != null)
        {
            if (VisualRoot is TopLevel { RenderScaling: var renderScaling })
            {
                width = (int)(Bounds.Width * renderScaling);
                height = (int)(Bounds.Height * renderScaling);
            }

            _skin.Width = width;
            _skin.Height = height;
            if (_time.Year == 1) _time = DateTime.Now;
            var now = DateTime.Now;
            var timeSpan = now - _time;
            _time = now;
            _skin.Tick(timeSpan.TotalSeconds);
            _skin.OpenGlRender(fb);
            CheckError(gl);
        }

        if (IsEnableAnimation && IsVisible && Opacity > 0 && !string.IsNullOrEmpty(Skin))
            RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _skin?.OpenGlDeinit();
        _skin = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SkinProperty || change.Property == CapeProperty)
        {
            _renderingFailed = false;
            if (_skin != null)
                ChangeSkin(Skin ?? "", Cape ?? "");
        }

        if (change.Property == RenderModeProperty)
        {
            // _skin 只在本控件完成 OpenGL 初始化（OnOpenGlInit）后才存在，而使用方可能在更早的
            // Loaded 里就设置 RenderMode；此处判空。所设的值不会丢——OnOpenGlInit 会用当前属性值
            // 统一初始化 _skin（见该方法里的对象初始化器）。
            if (_skin != null)
                _skin.RenderMode = RenderMode;

            RequestNextFrameRendering();
        }

        if (change.Property == IsEnableAnimationProperty)
            if (_skin != null)
            {
                _skin.Animation = IsEnableAnimation;
                RequestNextFrameRendering();
            }

        if (change.Property == IsCapeVisibleProperty)
        {
            if (_skin != null)
                _skin.EnableCape = IsCapeVisible;

            RequestNextFrameRendering();
        }

        if (change.Property == IsUpperLayerVisibleProperty)
        {
            if (_skin != null)
                _skin.EnableTop = IsUpperLayerVisible;

            RequestNextFrameRendering();
        }

        if (change.Property == IsTopLayer3DProperty)
        {
            if (_skin != null)
                _skin.EnableTopLayer3D = IsTopLayer3D;

            RequestNextFrameRendering();
        }

        if (change.Property == IsLightingEnabledProperty)
        {
            if (_skin != null)
                _skin.EnableLighting = IsLightingEnabled;

            RequestNextFrameRendering();
        }

        if (change.Property == MaxPanOffsetProperty && _skin != null)
            _skin.MaxPanOffset = MaxPanOffset;

        if (change.Property == HeadRotationProperty) RequestNextFrameRendering();

        if (change.Property == ArmRotationProperty)
            if (_skin != null)
            {
                _skin.ArmRotate = new Vector3(ArmRotation.X, ArmRotation.Y, 0);
                RequestNextFrameRendering();
            }

        if (change.Property == LegRotationProperty) RequestNextFrameRendering();

        if (change.Property == ModelDistanceProperty)
        {
            _skin?.AddDis(ModelDistance - _modelDistance);
            _modelDistance = ModelDistance;
        }
    }

    private static void CheckError(GlInterface gl)
    {
        int error;

        while ((error = gl.GetError()) != 0)
            Debug.WriteLine(error.ToString());
    }
}
