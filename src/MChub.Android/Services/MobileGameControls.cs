using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

using Avalonia.Threading;

using Net.Kdt.Pojavlaunch;

using MChub.Localization;

using Org.Lwjgl.Glfw;

namespace MChub.Mobile.Services;

/// <summary>
/// 游戏内的操作层：一块满屏触摸板 + 一组屏幕按键 + 一个用来调起软键盘的隐形输入框。
///
/// <para><b>为什么不用「直接给 Activity 加一堆 Button」</b>：触摸板必须铺满整屏（游戏里
/// 任何地方都能拖视角），按键又必须压在触摸板之上。所以用一层
/// <see cref="FrameLayout"/>：先放触摸板，再放按键，后加的在上层、优先进事件。</para>
///
/// <para><b>按键怎么变成游戏输入</b>：全部经 <see cref="CallbackBridge"/> 的 native 方法
/// 进 native 队列（见 <see cref="MobileTouchpadView"/> 的说明）。按键分两种：
/// 普通键按下发 keyDown、抬起发 keyUp；<b>切换键</b>（潜行/疾跑）点一下开、再点一下关 ——
/// 手机上同时按住「跑」和「W」再拖屏幕很难受，做成开关更实际。</para>
///
/// <para><b>聊天</b>：按 T（或 /）先发按键让 MC 打开聊天栏，再把焦点给一个隐形的
/// <see cref="EditText"/> 并调起输入法。输入法敲进去的字符按「与上一次的差集」发给游戏
/// （多退少补），所以光标移动、退格、输入法替换候选都还能对上。</para>
/// </summary>
internal static class MobileGameControls
{
    /// <summary>屏幕按键的键码（GLFW/org.lwjgl.glfw.GLFW）。</summary>
    private const int KeySpace = LwjglGlfwKeycode.GlfwKeySpace;
    // 说明：下面这些常量在 Java 侧是 short，转成 int 用。
    private const int KeyEscape = LwjglGlfwKeycode.GlfwKeyEscape;
    private const int KeyLeftShift = LwjglGlfwKeycode.GlfwKeyLeftShift;
    private const int KeyLeftControl = LwjglGlfwKeycode.GlfwKeyLeftControl;
    private const int KeyEnter = LwjglGlfwKeycode.GlfwKeyEnter;
    private const int KeyBackspace = LwjglGlfwKeycode.GlfwKeyBackspace;
    private const int KeyTab = LwjglGlfwKeycode.GlfwKeyTab;
    private const int KeyT = LwjglGlfwKeycode.GlfwKeyT;
    private const int KeySlash = LwjglGlfwKeycode.GlfwKeySlash;
    private const int KeyQ = LwjglGlfwKeycode.GlfwKeyQ;

    /// <summary>屏幕按键的三种底色（半透明，免得挡住游戏画面）。</summary>
    private const int KeyIdleColor = unchecked((int)0x88202020);

    private const int KeyPressedColor = unchecked((int)0xCC3C8527);

    private const int KeyActiveColor = unchecked((int)0xCC3C8527);

    private const int KeyStrokeColor = unchecked((int)0x60FFFFFF);

    private static FrameLayout? _overlay;
    private static MobileTouchpadView? _touchpad;
    private static EditText? _imeInput;

    /// <summary>输入法上次提交后的文本内容（用来算「这次新增/删掉了哪些字符」）。</summary>
    private static string _imeText = string.Empty;

    /// <summary>正在按程序改 EditText 的文本 → 忽略这次回调，别把它当成用户输入。</summary>
    private static bool _imeSuppressed;

    /// <summary>挂上操作层（必须在游戏 JVM 创建之前调用：一进世界游戏就会抓鼠标）。</summary>
    public static async Task AttachAsync()
    {
        var activity = MainActivity.Current;
        if (activity is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_overlay is not null)
            {
                return;
            }

            var context = (Context)activity;
            var overlay = new FrameLayout(context);

            _touchpad = new MobileTouchpadView(context);
            overlay.AddView(_touchpad, MatchParent());

            AddTopRightKeys(context, overlay);
            AddEscapeKey(context, overlay);
            AddMovementKeys(context, overlay);
            AddActionKeys(context, overlay);
            AddHotbarKeys(context, overlay);
            AddImeInput(activity, overlay);

            activity.AddContentView(overlay,
                new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent));

            _overlay = overlay;
        });
    }

    /// <summary>摘掉操作层（游戏退出时）。</summary>
    public static async Task DetachAsync()
    {
        var overlay = _overlay;
        if (overlay is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                // 先收键盘：HideKeyboard 还要用 _imeInput，不能提前把它置空。
                HideKeyboard();
                if (overlay.Parent is ViewGroup parent)
                {
                    parent.RemoveView(overlay);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"摘除操作层失败：{exception.Message}");
            }
        });

        _overlay = null;
        _touchpad = null;
        _imeInput = null;
        _imeText = string.Empty;
    }

    // ==================== 布局 ====================

    /// <summary>右上角：背包 / 聊天 / 命令 / 玩家列表 / 丢弃 / 回车 / 退格。</summary>
    private static void AddTopRightKeys(Context context, FrameLayout overlay)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };

        row.AddView(CreateKey(context, "E", KeyE));
        row.AddView(CreateKey(context, "T", KeyT, onPress: ShowKeyboard));
        row.AddView(CreateKey(context, "/", KeySlash, onPress: ShowKeyboard));
        row.AddView(CreateKey(context, "TAB", KeyTab));
        row.AddView(CreateKey(context, "Q", KeyQ));
        row.AddView(CreateKey(context, "⏎", KeyEnter, onPress: HideKeyboard));
        row.AddView(CreateKey(context, "⌫", KeyBackspace));

        overlay.AddView(row, Place(GravityFlags.Top | GravityFlags.Right, 0, 6, 8, 0));
    }

    private static void AddEscapeKey(Context context, FrameLayout overlay)
        => overlay.AddView(CreateKey(context, "ESC", KeyEscape, widthDp: 60),
            Place(GravityFlags.Top | GravityFlags.Left, 8, 6, 0, 0));

    /// <summary>左下角：WASD 十字。</summary>
    private static void AddMovementKeys(Context context, FrameLayout overlay)
    {
        const int size = 52;
        const int step = 52;

        overlay.AddView(CreateKey(context, "W", LwjglGlfwKeycode.GlfwKeyW, widthDp: size, heightDp: size),
            Place(GravityFlags.Bottom | GravityFlags.Left, step, 0, 0, step + 8));

        overlay.AddView(CreateKey(context, "A", LwjglGlfwKeycode.GlfwKeyA, widthDp: size, heightDp: size),
            Place(GravityFlags.Bottom | GravityFlags.Left, 8, 0, 0, 8));

        overlay.AddView(CreateKey(context, "S", LwjglGlfwKeycode.GlfwKeyS, widthDp: size, heightDp: size),
            Place(GravityFlags.Bottom | GravityFlags.Left, step, 0, 0, 8));

        overlay.AddView(CreateKey(context, "D", LwjglGlfwKeycode.GlfwKeyD, widthDp: size, heightDp: size),
            Place(GravityFlags.Bottom | GravityFlags.Left, step * 2, 0, 0, 8));
    }

    /// <summary>右下角：跳跃、潜行（切换）、疾跑（切换）。</summary>
    private static void AddActionKeys(Context context, FrameLayout overlay)
    {
        overlay.AddView(
            CreateKey(context, LabelJump(), KeySpace, widthDp: 76, heightDp: 68),
            Place(GravityFlags.Bottom | GravityFlags.Right, 0, 0, 8, 8));

        overlay.AddView(
            CreateKey(context, LabelSneak(), KeyLeftShift, widthDp: 62, heightDp: 56, toggle: true),
            Place(GravityFlags.Bottom | GravityFlags.Right, 0, 0, 92, 8));

        overlay.AddView(
            CreateKey(context, LabelSprint(), KeyLeftControl, widthDp: 62, heightDp: 56, toggle: true),
            Place(GravityFlags.Bottom | GravityFlags.Right, 0, 0, 8, 84));
    }

    /// <summary>底部中间：1~9 快捷栏。</summary>
    private static void AddHotbarKeys(Context context, FrameLayout overlay)
    {
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };

        for (var slot = 1; slot <= 9; slot++)
        {
            var parameters = new LinearLayout.LayoutParams(Px(context, 36), Px(context, 36));
            if (slot > 1)
            {
                parameters.LeftMargin = Px(context, 4);
            }

            row.AddView(CreateKey(context, slot.ToString(), KeyDigit(slot), widthDp: 36, heightDp: 36),
                parameters);
        }

        overlay.AddView(row, Place(GravityFlags.Bottom | GravityFlags.Center, 0, 0, 0, 8));
    }

    /// <summary>隐形输入框：只用来调起软键盘并把字符转发给游戏。</summary>
    private static void AddImeInput(Activity activity, FrameLayout overlay)
    {
        var input = new EditText(activity)
        {
            Alpha = 0f,
            Focusable = true,
            FocusableInTouchMode = true,

            // VisiblePassword：输入法不走「候选串组合」，每个字符立即提交 ——
            // 我们按字符转发给游戏，中间态（拼音）没有意义。
            InputType = InputTypes.ClassText
                        | InputTypes.TextVariationVisiblePassword
                        | InputTypes.TextFlagNoSuggestions
        };

        input.AddTextChangedListener(new ImeWatcher());

        overlay.AddView(input, new FrameLayout.LayoutParams(1, 1)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left
        });

        _imeInput = input;
    }

    // ==================== 按键 ====================

    private static Button CreateKey(Context context, string label, int keyCode,
        int widthDp = 46, int heightDp = 42, bool toggle = false,
        Action? onPress = null)
    {
        var button = new Button(context)
        {
            Text = label,

            // 不能拿焦点：否则会把软键盘/按键的输入截走，游戏反而收不到。
            Focusable = false,
            FocusableInTouchMode = false,
            Clickable = true,
            TextSize = 13f
        };

        button.SetTextColor(Android.Graphics.Color.White);
        button.SetPadding(0, 0, 0, 0);

        var shape = CreateKeyBackground();
        button.Background = shape;
        button.LayoutParameters = new LinearLayout.LayoutParams(Px(context, widthDp), Px(context, heightDp));

        var screenKey = new ScreenKey(button, shape, keyCode, toggle);

        button.Touch += (_, args) =>
        {
            if (args.Event is { } motion)
            {
                if (motion.ActionMasked == MotionEventActions.Down && onPress is not null)
                {
                    onPress();
                }

                screenKey.Handle(motion.ActionMasked);
            }

            args.Handled = true;
        };

        return button;
    }

    private static GradientDrawable CreateKeyBackground()
    {
        var shape = new GradientDrawable();
        shape.SetShape(ShapeType.Rectangle);
        shape.SetColor(KeyIdleColor);
        shape.SetCornerRadius(10f);
        // setStroke 的 (int, int) 重载在这套绑定里被映射成了 ColorStateList，
        // 直接传 ColorStateList 最稳（避免踩重载解析）。
        shape.SetStroke(2, Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(KeyStrokeColor)));
        return shape;
    }

    /// <summary>屏幕按键：按下发 keyDown、抬起发 keyUp；切换模式点一下开、再点一下关。</summary>
    private sealed class ScreenKey
    {
        private readonly Button _button;
        private readonly GradientDrawable _shape;
        private readonly int _keyCode;
        private readonly bool _toggle;
        private bool _on;

        public ScreenKey(Button button, GradientDrawable shape, int keyCode, bool toggle)
        {
            _button = button;
            _shape = shape;
            _keyCode = keyCode;
            _toggle = toggle;
        }

        public void Handle(MotionEventActions action)
        {
            if (_toggle)
            {
                if (action != MotionEventActions.Down)
                {
                    return;
                }

                _on = !_on;
                SendKey(_keyCode, _on);
                _shape.SetColor(_on ? KeyActiveColor : KeyIdleColor);
                _button.Invalidate();
                return;
            }

            if (action == MotionEventActions.Down)
            {
                _shape.SetColor(KeyPressedColor);
                _button.Invalidate();
                SendKey(_keyCode, true);
            }
            else if (action is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                _shape.SetColor(KeyIdleColor);
                _button.Invalidate();
                SendKey(_keyCode, false);
            }
        }
    }

    /// <summary>
    /// 发一次按键。
    ///
    /// <para>字符一律传 0：<c>CallbackBridge.sendKeycode</c> 只在字符不是 ISO 控制字符时
    /// 才顺带发 char 事件，传 0 就只发按键本身 —— 这正是屏幕按键要的
    /// （文本输入交给输入法那条路，见 <see cref="ImeWatcher"/>）。</para>
    /// </summary>
    private static void SendKey(int keyCode, bool down)
    {
        try
        {
            CallbackBridge.SendKeyPress(keyCode, (char)0, 0, down);
            Android.Util.Log.Info("MChubInput", $"key {keyCode} down={down}");
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn("MChubInput", $"发按键失败（{keyCode}）：{exception.Message}");
        }
    }

    // ==================== 聊天输入 ====================

    /// <summary>
    /// 打开软键盘。
    ///
    /// <para>按键本身由 <see cref="ScreenKey"/> 发（按下/抬起各一发），这里只负责把焦点给隐形输入框、
    /// 调起输入法 —— 不能在这里再补发一次按键，否则 MC 会收到两次（聊天栏会瞬间开了又关）。</para>
    /// </summary>
    private static void ShowKeyboard()
    {
        var input = _imeInput;
        var activity = MainActivity.Current;
        if (input is null || activity is null)
        {
            return;
        }

        // 开聊天时把隐形框清空，让它与「MC 聊天栏当前内容」重新对齐。
        ResetImeText(input);

        input.RequestFocus();
        var manager = activity.GetSystemService(Context.InputMethodService) as InputMethodManager;
        manager?.ShowSoftInput(input, ShowFlags.Implicit);
    }

    private static void HideKeyboard()
    {
        var input = _imeInput;
        var activity = MainActivity.Current;
        if (input is null || activity is null)
        {
            return;
        }

        var manager = activity.GetSystemService(Context.InputMethodService) as InputMethodManager;
        manager?.HideSoftInputFromWindow(input.WindowToken, HideSoftInputFlags.None);
        input.ClearFocus();
        ResetImeText(input);
    }

    private static void ResetImeText(EditText input)
    {
        _imeSuppressed = true;
        try
        {
            input.Text = string.Empty;
        }
        finally
        {
            _imeText = string.Empty;
            _imeSuppressed = false;
        }
    }

    /// <summary>输入法文本变化：把「与上次的差异」补给游戏（多退少补）。</summary>
    private static void OnImeTextChanged(string text)
    {
        if (_imeSuppressed)
        {
            return;
        }

        var previous = _imeText;
        _imeText = text;

        // 公共前缀之后的都是变化部分：先把多出来的删掉，再把新增的打上。
        var common = 0;
        while (common < previous.Length && common < text.Length && previous[common] == text[common])
        {
            common++;
        }

        for (var index = common; index < previous.Length; index++)
        {
            SendKey(KeyBackspace, true);
            SendKey(KeyBackspace, false);
        }

        for (var index = common; index < text.Length; index++)
        {
            SendChar(text[index]);
        }
    }

    /// <summary>
    /// 发一个字面字符（聊天/告示牌/命令输入）。
    ///
    /// <para>走 <c>CallbackBridge.sendChar</c>，它只发 char 事件、不发按键 ——
    /// 与真实键盘上「按下 A 键」会同时产生 key 与 char 的做法不同，
    /// 但对输入框来说 char 就够了，多一个 key 事件反而会误触发快捷键。</para>
    /// </summary>
    private static void SendChar(char value)
    {
        try
        {
            CallbackBridge.SendChar(value, 0);
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn("MChubInput", $"发字符失败（{value}）：{exception.Message}");
        }
    }

    private sealed class ImeWatcher : Java.Lang.Object, ITextWatcher
    {
        public void AfterTextChanged(IEditable? text) => OnImeTextChanged(text?.ToString() ?? string.Empty);

        public void BeforeTextChanged(Java.Lang.ICharSequence? text, int start, int count, int after)
        {
        }

        public void OnTextChanged(Java.Lang.ICharSequence? text, int start, int before, int count)
        {
        }
    }

    // ==================== 小工具 ====================

    private static int KeyDigit(int slot) => slot switch
    {
        1 => LwjglGlfwKeycode.GlfwKey1,
        2 => LwjglGlfwKeycode.GlfwKey2,
        3 => LwjglGlfwKeycode.GlfwKey3,
        4 => LwjglGlfwKeycode.GlfwKey4,
        5 => LwjglGlfwKeycode.GlfwKey5,
        6 => LwjglGlfwKeycode.GlfwKey6,
        7 => LwjglGlfwKeycode.GlfwKey7,
        8 => LwjglGlfwKeycode.GlfwKey8,
        _ => LwjglGlfwKeycode.GlfwKey9
    };

    private static int KeyE => LwjglGlfwKeycode.GlfwKeyE;

    private static string LabelJump() => Label(language => language.mobile_gameKeyJump.CurrentValue(), "跳");

    private static string LabelSneak() => Label(language => language.mobile_gameKeySneak.CurrentValue(), "潜行");

    private static string LabelSprint() => Label(language => language.mobile_gameKeySprint.CurrentValue(), "疾跑");

    /// <summary>取本地化文案；语言管理器还没就绪时退回中文，别让按钮变成空块。</summary>
    private static string Label(Func<MChub.Localization.MobileLanguageManager, string> pick, string fallback)
    {
        try
        {
            return pick(MChub.Localization.MobileLanguageManager.Instance);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static FrameLayout.LayoutParams MatchParent()
        => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

    private static FrameLayout.LayoutParams Place(GravityFlags gravity,
        int leftDp, int topDp, int rightDp, int bottomDp)
    {
        var context = (Context)MainActivity.Current!;
        var parameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = gravity,
            LeftMargin = Px(context, leftDp),
            TopMargin = Px(context, topDp),
            RightMargin = Px(context, rightDp),
            BottomMargin = Px(context, bottomDp)
        };
        return parameters;
    }

    private static int Px(Context context, int dp)
        => (int)MathF.Round(dp * (context.Resources?.DisplayMetrics?.Density ?? 1f));
}
