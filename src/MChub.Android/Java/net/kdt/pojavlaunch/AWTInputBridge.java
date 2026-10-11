/*
 * 移植自 AngelAuraMC/Amethyst-Android @ v3_openjdk（GPL-3.0-or-later）
 * 源文件：app_pojavlauncher/src/main/java/net/kdt/pojavlaunch/AWTInputBridge.java
 *
 * MChub 改动：删掉 static 块里的 System.loadLibrary("pojavexec_awt")。
 * 本项目的 native 库不在 APK 的 lib/ 下（被解压到私有目录以规避 16 KB 段对齐问题），
 * 只能按绝对路径 System.load，统一由 net.kdt.pojavlaunch.utils.JREUtils.loadNativeLayer 负责。
 */
package net.kdt.pojavlaunch;

/** Caciocavallo（Android 上的 AWT 点）与游戏之间的输入桥。 */
public class AWTInputBridge {
    public static final int EVENT_TYPE_CHAR = 1000;
    public static final int EVENT_TYPE_CURSOR_POS = 1003;
    public static final int EVENT_TYPE_KEY = 1005;
    public static final int EVENT_TYPE_MOUSE_BUTTON = 1006;

    public static void sendKey(char keychar, int keycode) {
        nativeSendData(EVENT_TYPE_KEY, keychar, keycode, 1, 0);
        nativeSendData(EVENT_TYPE_KEY, keychar, keycode, 0, 0);
    }

    public static void sendKey(char keychar, int keycode, int state) {
        nativeSendData(EVENT_TYPE_KEY, keychar, keycode, state, 0);
    }

    public static void sendChar(char keychar) {
        nativeSendData(EVENT_TYPE_CHAR, keychar, 0, 0, 0);
    }

    public static void sendMousePress(int awtButtons, boolean isDown) {
        nativeSendData(EVENT_TYPE_MOUSE_BUTTON, awtButtons, isDown ? 1 : 0, 0, 0);
    }

    public static void sendMousePress(int awtButtons) {
        sendMousePress(awtButtons, true);
        sendMousePress(awtButtons, false);
    }

    public static void sendMousePos(int x, int y) {
        nativeSendData(EVENT_TYPE_CURSOR_POS, x, y, 0, 0);
    }

    public static native void nativeSendData(int type, int i1, int i2, int i3, int i4);

    public static native void nativeClipboardReceived(String data, String mimeTypeSub);

    public static native void nativeMoveWindow(int xoff, int yoff);
}
