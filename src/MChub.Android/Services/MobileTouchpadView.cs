using Android.Content;
using Android.OS;
using Android.Views;

using Org.Lwjgl.Glfw;

namespace MChub.Mobile.Services;

/// <summary>
/// 游戏触摸板：把手势翻译成 GLFW 鼠标事件。
///
/// <para><b>为什么必须自己翻译</b>：MC 的输入全部走 GLFW。在移动端，GLFW 是补丁版：
/// ART 侧调用 <c>org.lwjgl.glfw.CallbackBridge</c> 上的 native 方法把事件推进
/// <c>libpojavexec.so</c> 里的环形队列，游戏线程上的 <c>pojavPumpEvents</c> 再把它们取出来
/// 回调给游戏。这个类就是那条链路最前面的「输入端」—— Android 的触摸事件本身
/// 和 MC 认识的鼠标事件之间没有任何自动翻译。</para>
///
/// <para><b>两套手势，按游戏是否抓取了鼠标切换</b>（<see cref="CallbackBridge.IsGrabbing"/>，
/// 游戏调 <c>glfwSetInputMode(GLFW_CURSOR, GLFW_CURSOR_DISABLED)</c> 时会同步过来）：</para>
/// <list type="table">
///   <item>
///     <term>菜单里（未抓取）</term>
///     <description>手指位置就是光标位置（绝对定位）；轻点 = 左键单击；
///       按住拖动 = 按住左键（背包里拖东西、拖滑条）；双指拖动 = 滚轮；双指轻点 = 右键。</description>
///   </item>
///   <item>
///     <term>游戏里（已抓取）</term>
///     <description>手指移动量当作视角转动量（相对定位）；轻点 = 左键（攻击/使用一下）；
///       按住不动 = 按住左键（持续挖方块）；按住后再拖 = 一边挖一边转视角；
///       双指拖动 = 滚轮（切快捷栏）；双指轻点 = 右键（放置方块 / 使用物品）。</description>
///   </item>
/// </list>
///
/// <para>阈值与时序照 Pojav/Amethyst 取（<c>FINGER_STILL_THRESHOLD</c> = 9dp、
/// <c>FINGER_SCROLL_THRESHOLD</c> = 6dp），行为上只做了一处有意的差别：
/// 上游是「轻点 = 右键、长按 = 左键」，这里把轻点改成左键单击 —— 触屏玩家按下去的第一反应
/// 是「点一下打一下」，右键放给双指轻点，避免上来就误放置方块。
/// 另外去掉了上游「触摸板小部件」那条相对定位分支（移动端用满屏触摸板更自然）。</para>
/// </summary>
internal sealed class MobileTouchpadView : View
{
    /// <summary>GLFW 鼠标键号（org.lwjgl.glfw.GLFW.GLFW_MOUSE_BUTTON_*）。</summary>
    private const int MouseButtonLeft = 0;

    private const int MouseButtonRight = 1;

    /// <summary>超过这个位移（dp）就不再算「轻点」，改判为拖动。</summary>
    private const float TapSlopDp = 9f;

    /// <summary>双指滚动走过多少 dp 算一格。</summary>
    private const float ScrollStepDp = 6f;

    /// <summary>按住不动多久算「长按 → 按住左键」（毫秒）。</summary>
    private const int LongPressHoldMs = 300;

    /// <summary>手指位移换算成视角转动量的倍率。</summary>
    private const float DefaultSensitivity = 2.0f;

    private readonly float _sensitivity;
    private readonly float _tapSlop;
    private readonly float _scrollStep;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Java.Lang.Runnable _longPressRunnable;

    /// <summary>手势起点（视图坐标）。</summary>
    private float _downX, _downY;

    /// <summary>上一个事件的位置（算相对位移用）。</summary>
    private float _lastX, _lastY;

    /// <summary>单指是否已经超过轻点容差。</summary>
    private bool _moved;

    /// <summary>双指手势是否真的滚过（滚过就不再当「双指轻点」）。</summary>
    private bool _scrolled;

    /// <summary>当前是否按住左键（长按/拖动产生）。</summary>
    private bool _leftPressed;

    /// <summary>本次手势是否已被取消（手指划出视图等）。</summary>
    private bool _cancelled;

    private long _downAtMs;

    public MobileTouchpadView(Context context)
        : base(context)
    {
        var density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        _sensitivity = DefaultSensitivity;
        _tapSlop = TapSlopDp * density;
        _scrollStep = ScrollStepDp * density;
        _longPressRunnable = new Java.Lang.Runnable(OnLongPressElapsed);

        // 触摸板要吃掉整个屏幕的手指事件（上层按键会先拿到自己的那份），
        // 但**不能**拿焦点 —— 否则软键盘/按键的输入会被它截走。
        Clickable = true;
        Focusable = false;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        var grabbing = CallbackBridge.IsGrabbing;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                BeginGesture(e, grabbing);
                break;

            case MotionEventActions.PointerDown:
                // 第二根手指落下：手势改判为「双指」，撤销单指那一套（长按、按住左键）。
                CancelPendingLongPress();
                ReleaseLeftIfPressed();
                break;

            case MotionEventActions.Move:
                if (e.PointerCount >= 2)
                {
                    HandleScroll(e);
                }
                else if (grabbing)
                {
                    HandleGameMove(e);
                }
                else
                {
                    HandleGuiMove(e);
                }

                break;

            case MotionEventActions.PointerUp:
                // 双指轻点（两根手指一起落下、没滚过就抬起一根）→ 右键。
                if (e.PointerCount == 2 && !_moved && !_scrolled && !_leftPressed)
                {
                    ClickAt(MouseButtonRight, CallbackBridge.MouseX, CallbackBridge.MouseY);
                }

                break;

            case MotionEventActions.Up:
                FinishGesture(click: true, grabbing);
                break;

            case MotionEventActions.Cancel:
                FinishGesture(click: false, grabbing);
                break;

            default:
                break;
        }

        return true;
    }

    // ==================== 手势 ====================

    private void BeginGesture(MotionEvent e, bool grabbing)
    {
        _downX = _lastX = e.GetX();
        _downY = _lastY = e.GetY();
        _moved = false;
        _scrolled = false;
        _cancelled = false;
        _downAtMs = SystemClock.UptimeMillis();
        Android.Util.Log.Info("MChubInput", $"手势开始 grabbing={grabbing}");

        if (grabbing)
        {
            // 游戏里：先起一个「按住不动 = 按住左键」的计时器，动起来就撤掉。
            _handler.PostDelayed(_longPressRunnable, LongPressHoldMs);
        }
        else
        {
            // 菜单里：手指落到哪，光标就到哪（绝对定位）。
            SendCursor(_downX, _downY);
        }
    }

    /// <summary>长按计时到点：手指没动过就把左键按下去（并一直按住到松手）。</summary>
    private void OnLongPressElapsed()
    {
        if (_cancelled || _moved || !CallbackBridge.IsGrabbing)
        {
            return;
        }

        SendMouseButton(MouseButtonLeft, true);
        _leftPressed = true;
    }

    /// <summary>游戏里（鼠标被抓取）：位移 → 视角转动。MC 自己按光标位置差算转动量。</summary>
    private void HandleGameMove(MotionEvent e)
    {
        var x = e.GetX();
        var y = e.GetY();

        var deltaX = (x - _lastX) * _sensitivity;
        var deltaY = (y - _lastY) * _sensitivity;
        _lastX = x;
        _lastY = y;

        MarkMovedIfBeyondSlop(x, y);

        if (deltaX == 0f && deltaY == 0f)
        {
            return;
        }

        // 累积到 ART 侧的光标位置上：MC 读的是「光标位置的变化」，所以累加 + 回传是等价做法
        // （上游 InGameEventProcessor 也是这么写的）。
        CallbackBridge.MouseX += deltaX;
        CallbackBridge.MouseY += deltaY;
        CallbackBridge.DeltaX = deltaX;
        CallbackBridge.DeltaY = deltaY;
        CallbackBridge.SendCursorPos(CallbackBridge.MouseX, CallbackBridge.MouseY);
    }

    /// <summary>菜单里（未抓取）：手指位置就是光标位置；拖动超过容差后按住左键（支持拖拽）。</summary>
    private void HandleGuiMove(MotionEvent e)
    {
        var x = e.GetX();
        var y = e.GetY();
        SendCursor(x, y);

        if (!_moved && Distance(x, y, _downX, _downY) > _tapSlop)
        {
            _moved = true;

            // 拖着走 = 按住左键：背包里拖物品、拖动滑条都要这个行为。
            // 注意不能一按下就按住 —— 那样在物品栏里挪一下鼠标就会把整摞东西拖起来。
            SendMouseButton(MouseButtonLeft, true);
            _leftPressed = true;
        }
    }

    /// <summary>双指拖动 → 滚轮。位移原样发出去，MC 自己按格累积（见 MouseHandler.onScroll）。</summary>
    private void HandleScroll(MotionEvent e)
    {
        // 用主指针算位移（第二根手指的坐标在手指抬起时会跳）。
        var y = e.GetY(0);
        var delta = (y - _lastY) / _scrollStep;
        _lastX = e.GetX(0);
        _lastY = y;

        if (delta == 0f)
        {
            return;
        }

        // 手指往上划 = 向上滚（切到上一个快捷栏槽位），与桌面滚轮方向一致。
        CallbackBridge.SendScroll(0f, -delta);
        _scrolled = true;
    }

    private void FinishGesture(bool click, bool grabbing)
    {
        _cancelled = !click;
        CancelPendingLongPress();

        var stillHeld = _leftPressed;
        ReleaseLeftIfPressed();

        if (stillHeld)
        {
            // 长按/拖动本身已经产生了「按下 → 松开」的语义，不再补一次点击。
            return;
        }

        if (!click || _moved || _scrolled)
        {
            return;
        }

        // 手指仍然只有一根才算轻点（双指轻点在 PointerUp 里已经处理过）。
        if (grabbing && SystemClock.UptimeMillis() - _downAtMs > LongPressHoldMs + 100)
        {
            // 按了很久却既没触发长按也没移动：多半是被中断/状态刚切换，不发点击以免误触。
            return;
        }

        ClickAt(MouseButtonLeft, CallbackBridge.MouseX, CallbackBridge.MouseY);
    }

    /// <summary>
    /// 完整点一次（按下 → 隔一帧抬起）。
    ///
    /// <para>走 <c>CallbackBridge.putMouseEventWithCoords</c>：它先把光标定到给定位置、按下左/右键，
    /// 再用 Choreographer 在约 33 ms 后补上抬起。<b>不能在同一帧里按下+抬起</b> ——
    /// 游戏的 GUI 在「按下」时只记录被按住的控件、真正触发动作是在「抬起」，
    /// 两个事件挤在同一批里处理时中间没有经过一次界面更新，按钮就可能整组被忽略。</para>
    /// </summary>
    private static void ClickAt(int button, float x, float y)
    {
        try
        {
            CallbackBridge.PutMouseEventWithCoords(button, x, y);
            Android.Util.Log.Info("MChubInput", $"click button={button} at ({x:F0},{y:F0})");
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn("MChubInput", $"点击失败：{exception.Message}");
        }
    }

    private void MarkMovedIfBeyondSlop(float x, float y)
    {
        if (_moved || Distance(x, y, _downX, _downY) <= _tapSlop)
        {
            return;
        }

        _moved = true;
        // 已经动起来了，就别再触发「按住不动 = 挖方块」。
        CancelPendingLongPress();
    }

    // ==================== 小工具 ====================

    private void SendCursor(float x, float y)
    {
        CallbackBridge.MouseX = x;
        CallbackBridge.MouseY = y;
        CallbackBridge.SendCursorPos(x, y);
    }

    private void SendMouseButton(int button, bool down)
    {
        // 事件进 native 队列；native 层没装载时队列里也没人取，不会抛。
        CallbackBridge.SendMouseButton(button, down);

        Android.Util.Log.Info("MChubInput",
            $"mouse button={button} down={down} at ({CallbackBridge.MouseX:F0},{CallbackBridge.MouseY:F0})"
            + $" grabbing={CallbackBridge.IsGrabbing}");
    }

    private void CancelPendingLongPress() => _handler.RemoveCallbacks(_longPressRunnable);

    private void ReleaseLeftIfPressed()
    {
        if (!_leftPressed)
        {
            return;
        }

        _leftPressed = false;
        CallbackBridge.SendMouseButton(MouseButtonLeft, false);
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}
