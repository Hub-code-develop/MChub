/*
 * 移植自 AngelAuraMC/Amethyst-Android @ v3_openjdk（GPL-3.0-or-later）
 * 源文件：app_pojavlauncher/src/main/java/org/lwjgl/glfw/CallbackBridge.java
 *
 * ============================================================================
 * 这个类是「Android（ART）侧」的实现，不是游戏 JVM 里那个同名的 68 行 stub。
 *
 * 两个 JVM 里各有一份 org.lwjgl.glfw.CallbackBridge，互不相干：
 *   * 游戏 JVM 里的那份来自 assets/runtime/lwjgl/<ver>.zip（移动端补丁版 LWJGL），
 *     负责把 GLFW 的事件转成 nativeSendData(...)；
 *   * 本文件这份运行在 ART 里，libpojavexec.so 的 JNI_OnLoad 会 FindClass 它并取
 *     五个方法 ID（accessAndroidClipboard / onGrabStateChanged / onDirectInputEnable /
 *     getAndroidDPI / notifyLauncher），随后 RegisterNatives 把 8 个输入入口挂到它上面。
 *     没有它，loadLibrary 阶段就会 JNI DETECTED ERROR 直接 SIGABRT。
 *
 * MChub 相对上游的改动（都为了「不引入上游整套 App 框架」）：
 *   1. 去掉 SDL 相关分支（手柄）。上游用 SDLActivity.onNativeMouse/KeyDown 同步一份输入给
 *      SDL；MChub 移动端尚未接入 SDL，这些分支连同 MinecraftGLSurface.sdlEnabled 一起去掉，
 *      输入只走 native 队列（对键盘/触屏足够）。
 *   2. Clipboard / DPI 不再依赖 MainActivity / LauncherPreferences，改为读
 *      hub.code.mchub.runtime.AndroidBridge 里登记的 Application Context。
 *   3. 8 个输入 native **不加 @CriticalNative**：native 侧的 tryCriticalNative() 在找不到
 *      net.kdt.pojavlaunch.CriticalNativeTest 时会回退到 noncritical 那一组（JNIEnv 风格），
 *      此时方法必须是普通 native 才匹配；加了注解反而会对不上调用约定。
 *   4. static 块不再 System.loadLibrary("pojavexec")（本项目的 native 在私有目录，
 *      只能按绝对路径装载，见 JREUtils.loadNativeLayer）；手柄缓冲区改为惰性创建，
 *      免得类一初始化就在 native 还没装载时炸掉。
 * ============================================================================
 */
package org.lwjgl.glfw;

import android.content.ClipData;
import android.content.ClipDescription;
import android.content.ClipboardManager;
import android.content.Context;
import android.util.DisplayMetrics;
import android.view.Choreographer;

import net.kdt.pojavlaunch.GrabListener;
import net.kdt.pojavlaunch.LwjglGlfwKeycode;
import net.kdt.pojavlaunch.Logger;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.ArrayList;

public class CallbackBridge {
    private static final String TAG = "MChubBridge";

    private static boolean isGrabbing = false;
    private static final ArrayList<GrabListener> grabListeners = new ArrayList<>();

    private static Choreographer sChoreographer;

    public static final int CLIPBOARD_COPY = 2000;
    public static final int CLIPBOARD_PASTE = 2001;
    public static final int CLIPBOARD_OPEN = 2002;

    public static volatile int windowWidth, windowHeight;
    public static volatile int physicalWidth, physicalHeight;
    public static float mouseX, mouseY, deltaX, deltaY;
    public static volatile boolean holdingAlt, holdingCapslock, holdingCtrl,
            holdingNumlock, holdingShift;

    private static ByteBuffer sGamepadButtonBuffer;
    private static FloatBuffer sGamepadAxisBuffer;
    public static boolean sGamepadDirectInput = false;
    private static int sMouseButtonState = 0;

    // ==================== 输入：由触屏/键盘 UI 调用，转成 native 队列 ====================

    public static void putMouseEventWithCoords(int button, float x, float y) {
        putMouseEventWithCoords(button, true, x, y);
        postFrameCallbackDelayed(() -> putMouseEventWithCoords(button, false, x, y), 33);
    }

    public static void putMouseEventWithCoords(int button, boolean isDown, float x, float y) {
        sendCursorPos(x, y);
        sendMouseKeycode(button, getCurrentMods(), isDown);
    }

    public static void sendCursorPos(float x, float y) {
        mouseX = x;
        mouseY = y;
        nativeSendCursorPos(mouseX, mouseY);
    }

    /**
     * 发一次按键。{@code keyCode} 为 0 表示只发字符（聊天/输入框用）。
     *
     * @param keyCode   LWJGL/GLFW 键码（见 {@link LwjglGlfwKeycode}）
     * @param keyChar   字面字符，不受修饰键影响
     * @param scancode  扫描码
     * @param modifiers GLFW 修饰位（见 {@link LwjglGlfwKeycode#GLFW_MOD_SHIFT} 等）
     * @param isDown    true=按下（GLFW_PRESS），false=抬起（GLFW_RELEASE）
     */
    public static void sendKeycode(int keyCode, char keyChar, int scancode, int modifiers, boolean isDown) {
        if (keyCode != 0) {
            nativeSendKey(keyCode, scancode, isDown ? 1 : 0, modifiers);
        }
        // 控制字符不要当字符发，否则旧版本（1.12.2/1.8.9）会把 TAB 当字符打进聊天框而丢掉按键。
        if (isDown && !Character.isISOControl(keyChar)) {
            nativeSendCharMods(keyChar, modifiers);
            nativeSendChar(keyChar);
        }
    }

    public static void sendChar(char keyChar, int modifiers) {
        // 只有输入框走这里，允许 emoji，所以不过滤 ISOControl。
        nativeSendCharMods(keyChar, modifiers);
        nativeSendChar(keyChar);
    }

    public static void sendKeyPress(int keyCode, int modifiers, boolean status) {
        sendKeyPress(keyCode, 0, modifiers, status);
    }

    public static void sendKeyPress(int keyCode, int scancode, int modifiers, boolean status) {
        sendKeyPress(keyCode, '\u0000', scancode, modifiers, status);
    }

    public static void sendKeyPress(int keyCode, char keyChar, int scancode, int modifiers, boolean status) {
        sendKeycode(keyCode, keyChar, scancode, modifiers, status);
    }

    public static void sendKeyPress(int keyCode, char keyChar, int modifiers, boolean status) {
        sendKeyPress(keyCode, keyChar, 0, modifiers, status);
    }

    public static void sendKeyPress(int keyCode) {
        sendKeyPress(keyCode, getCurrentMods(), true);
        sendKeyPress(keyCode, getCurrentMods(), false);
    }

    public static void sendMouseButton(int button, boolean status) {
        sendMouseKeycode(button, getCurrentMods(), status);
    }

    public static void sendMouseKeycode(int button, int modifiers, boolean isDown) {
        nativeSendMouseButton(button, isDown ? 1 : 0, modifiers);
    }

    public static void sendMouseKeycode(int keycode) {
        sendMouseKeycode(keycode, getCurrentMods(), true);
        sendMouseKeycode(keycode, getCurrentMods(), false);
    }

    public static void sendScroll(float xoffset, float yoffset) {
        nativeSendScroll(xoffset, yoffset);
    }

    public static void sendUpdateWindowSize(int w, int h) {
        windowWidth = w;
        windowHeight = h;
        nativeSendScreenSize(w, h);
    }

    public static boolean isGrabbing() {
        // 少走一次 JNI。
        return isGrabbing;
    }

    // ==================== 游戏 JVM 回调进 Android 的入口 ====================

    /** 剪贴板 / 打开链接；由游戏 JVM 经 native 调进来。 */
    public static String accessAndroidClipboard(int type, String copy) {
        Context context = hub.code.mchub.runtime.AndroidBridge.context();
        if (context == null) {
            return null;
        }

        switch (type) {
            case CLIPBOARD_COPY: {
                ClipboardManager manager = (ClipboardManager) context.getSystemService(Context.CLIPBOARD_SERVICE);
                if (manager != null) {
                    manager.setPrimaryClip(ClipData.newPlainText("Copy", copy));
                }
                return null;
            }
            case CLIPBOARD_PASTE: {
                ClipboardManager manager = (ClipboardManager) context.getSystemService(Context.CLIPBOARD_SERVICE);
                if (manager != null && manager.hasPrimaryClip()
                        && manager.getPrimaryClipDescription() != null
                        && manager.getPrimaryClipDescription().hasMimeType(ClipDescription.MIMETYPE_TEXT_PLAIN)) {
                    ClipData clip = manager.getPrimaryClip();
                    if (clip != null && clip.getItemCount() > 0 && clip.getItemAt(0).getText() != null) {
                        return clip.getItemAt(0).getText().toString();
                    }
                }
                return "";
            }
            case CLIPBOARD_OPEN:
                // 打开链接属于「上游 App 的界面能力」，MChub 暂时不做（避免凭空造一个没验证过的 Intent）。
                Logger.appendToLog("MChubBridge: CLIPBOARD_OPEN 暂未实现：" + copy);
                return null;
            default:
                return null;
        }
    }

    /** 通知类型（只保留 SDL 一档，与上游一致）。 */
    public static final int NOTIF_TYPE_SDL = 0;

    public static final int ACTION_INIT_LAUNCHER_INTEGRATION = 0;
    public static final int ACTION_SEND_TEXTBOX_RECT = 1;
    public static final int ACTION_INIT_CONTROLLER = 2;
    public static final int ACTION_DEINIT_CONTROLLER = 3;

    /**
     * 游戏 JVM 侧发过来的通知。
     *
     * <p>MChub 尚未接入 SDL，所以 ACTION_INIT_LAUNCHER_INTEGRATION 会如实返回 false
     * （游戏侧据此退回非 SDL 路径），不假装成功。</p>
     */
    public static boolean notifyLauncher(int type, int... action) {
        if (type != NOTIF_TYPE_SDL || action == null || action.length == 0) {
            return false;
        }

        switch (action[0]) {
            case ACTION_INIT_LAUNCHER_INTEGRATION:
                Logger.appendToLog("MChubBridge: 游戏请求启用 SDL 集成，但移动端尚未接入 SDL，退回 GLFW 路径");
                return false;
            case ACTION_SEND_TEXTBOX_RECT:
                // 输入框位置同步，MChub 的输入 UI 还没做。
                return false;
            case ACTION_INIT_CONTROLLER:
                sGamepadDirectInput = true;
                return true;
            case ACTION_DEINIT_CONTROLLER:
                sGamepadDirectInput = false;
                return true;
            default:
                return false;
        }
    }

    public static int getCurrentMods() {
        int currMods = 0;
        if (holdingAlt) currMods |= LwjglGlfwKeycode.GLFW_MOD_ALT;
        if (holdingCapslock) currMods |= LwjglGlfwKeycode.GLFW_MOD_CAPS_LOCK;
        if (holdingCtrl) currMods |= LwjglGlfwKeycode.GLFW_MOD_CONTROL;
        if (holdingNumlock) currMods |= LwjglGlfwKeycode.GLFW_MOD_NUM_LOCK;
        if (holdingShift) currMods |= LwjglGlfwKeycode.GLFW_MOD_SHIFT;
        return currMods;
    }

    public static void setModifiers(int keyCode, boolean isDown) {
        switch (keyCode) {
            case LwjglGlfwKeycode.GLFW_KEY_LEFT_SHIFT:
                holdingShift = isDown;
                return;
            case LwjglGlfwKeycode.GLFW_KEY_LEFT_CONTROL:
                holdingCtrl = isDown;
                return;
            case LwjglGlfwKeycode.GLFW_KEY_LEFT_ALT:
                holdingAlt = isDown;
                return;
            case LwjglGlfwKeycode.GLFW_KEY_CAPS_LOCK:
                holdingCapslock = isDown;
                return;
            case LwjglGlfwKeycode.GLFW_KEY_NUM_LOCK:
                holdingNumlock = isDown;
                return;
            default:
        }
    }

    /** 游戏请求开启「直接输入」（原生鼠标/手柄），由游戏 JVM 经 native 调进来。 */
    public static void onDirectInputEnable() {
        sGamepadDirectInput = true;
    }

    /** 游戏告知「鼠标锁定（grab）」状态变化，由游戏 JVM 经 native 调进来。 */
    public static void onGrabStateChanged(final boolean grabbing) {
        isGrabbing = grabbing;
        postFrameCallbackDelayed(() -> {
            // 期间状态又变了就跳过这次通知。
            if (isGrabbing != grabbing) {
                return;
            }
            synchronized (grabListeners) {
                for (GrabListener listener : grabListeners) {
                    listener.onGrabState(grabbing);
                }
            }
        }, 16);
    }

    public static void addGrabListener(GrabListener listener) {
        synchronized (grabListeners) {
            listener.onGrabState(isGrabbing);
            grabListeners.add(listener);
        }
    }

    public static void removeGrabListener(GrabListener listener) {
        synchronized (grabListeners) {
            grabListeners.remove(listener);
        }
    }

    /** GLFW 窗口内容缩放用的 DPI（density）。 */
    public static float getAndroidDPI() {
        Context context = hub.code.mchub.runtime.AndroidBridge.context();
        if (context == null) {
            return 1f;
        }
        DisplayMetrics metrics = context.getResources().getDisplayMetrics();
        return metrics.density;
    }

    /** 手柄轴缓冲区（惰性创建：类初始化时 native 可能还没装载）。 */
    public static FloatBuffer createGamepadAxisBuffer() {
        ByteBuffer axisByteBuffer = nativeCreateGamepadAxisBuffer();
        return axisByteBuffer.order(ByteOrder.LITTLE_ENDIAN).asFloatBuffer();
    }

    /** 预创建的手柄按钮/轴缓冲区；未装载 native 时为 null（调用方需判空）。 */
    public static synchronized ByteBuffer gamepadButtonBuffer() {
        if (sGamepadButtonBuffer == null) {
            sGamepadButtonBuffer = nativeCreateGamepadButtonBuffer();
        }
        return sGamepadButtonBuffer;
    }

    public static synchronized FloatBuffer gamepadAxisBuffer() {
        if (sGamepadAxisBuffer == null) {
            sGamepadAxisBuffer = createGamepadAxisBuffer();
        }
        return sGamepadAxisBuffer;
    }

    // ==================== native ====================

    // 以下 8 个由 libpojavexec.so 的 JNI_OnLoad → RegisterNatives 绑定（noncritical 一组）。
    public static native void nativeSetUseInputStackQueue(boolean useInputStackQueue);

    private static native boolean nativeSendChar(char codepoint);

    private static native boolean nativeSendCharMods(char codepoint, int mods);

    private static native void nativeSendKey(int key, int scancode, int action, int mods);

    private static native void nativeSendCursorPos(float x, float y);

    private static native void nativeSendMouseButton(int button, int action, int mods);

    private static native void nativeSendScroll(double xoffset, double yoffset);

    private static native void nativeSendScreenSize(int width, int height);

    public static native void nativeSetWindowAttrib(int attrib, int value);

    private static native ByteBuffer nativeCreateGamepadButtonBuffer();

    private static native ByteBuffer nativeCreateGamepadAxisBuffer();

    // ==================== 内部小工具 ====================

    /**
     * 在下一帧（或约 {@code delayMs} 后）执行。
     *
     * <p>Choreographer 需要 Looper，而本类也可能被 native 在非 UI 线程首次加载，
     * 所以惰性获取；拿不到就直接在当前线程跑 —— 通知晚一点/早一点都好过抛异常。</p>
     */
    private static void postFrameCallbackDelayed(Runnable action, long delayMs) {
        if (sChoreographer == null) {
            try {
                sChoreographer = Choreographer.getInstance();
            } catch (Throwable ignored) {
                action.run();
                return;
            }
        }
        sChoreographer.postFrameCallbackDelayed(time -> action.run(), delayMs);
    }
}
