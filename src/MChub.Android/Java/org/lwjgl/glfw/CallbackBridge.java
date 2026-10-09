package org.lwjgl.glfw;

import java.nio.ByteBuffer;

/**
 * ART（系统运行时）侧的占位实现。
 *
 * <p><b>为什么需要它</b>：随 APK 打进 {@code lib/arm64-v8a/} 的 {@code libpojavexec.so} 里的
 * {@code JNI_OnLoad}（源文件 {@code input_bridge_v3.c}）会要求本库「在 Android-land 里至少被加载一次」，
 * 并在这个时机做：</p>
 *
 * <pre>
 *   FindClass("org/lwjgl/glfw/CallbackBridge")
 *   GetStaticMethodID(..., "accessAndroidClipboard", "(ILjava/lang/String;)Ljava/lang/String;")
 *   GetStaticMethodID(..., "onGrabStateChanged",     "(Z)V")
 *   GetStaticMethodID(..., "onDirectInputEnable",    "()V")
 *   GetStaticMethodID(..., "getAndroidDPI",          "()F")
 *   GetStaticMethodID(..., "notifyLauncher",         "(I[I)Z")
 * </pre>
 *
 * <p>而 .NET Android 在启动阶段会把 {@code lib/} 下的原生库全部 {@code loadLibrary} 一遍 ——
 * 此时若找不到这个类，就会 {@code JNI DETECTED ERROR: java_class == null} 直接 SIGABRT（实测过）。</p>
 *
 * <p><b>它不会被真正调用</b>：JNI_OnLoad 只解析方法 ID；真实的输入/剪贴板逻辑跑在第二个 JVM 里
 * （由 {@code assets/runtime/lwjgl/<ver>.zip} 解出的移动端补丁版 LWJGL jar 提供完整实现）。
 * 两个 JVM 互相独立，同名类不冲突。所以这里的方法体只需要签名精确匹配。</p>
 *
 * <p><b>签名必须与上游完全一致</b>（{@code GetStaticMethodID} 是精确匹配，差一个字符就返回 null
 * 并同样导致崩溃）。</p>
 */
public final class CallbackBridge {
    private CallbackBridge() {
    }

    // ---- JNI_OnLoad 需要解析的五个方法（签名严格对齐） ----

    /** 签名：{@code (ILjava/lang/String;)Ljava/lang/String;} */
    public static String accessAndroidClipboard(int type, String copy) {
        return null;
    }

    /** 签名：{@code (Z)V} */
    public static void onGrabStateChanged(boolean grabbing) {
    }

    /** 签名：{@code ()V} */
    public static void onDirectInputEnable() {
    }

    /** 签名：{@code ()F} */
    public static float getAndroidDPI() {
        return 0f;
    }

    /** 签名：{@code (I[I)Z} */
    public static boolean notifyLauncher(int type, int[] action) {
        return false;
    }

    // ---- native 声明：真身由 libpojavexec.so 提供，此处保留以使类结构完整 ----

    public static native void nativeSendData(boolean isAndroid, int type, String data);

    public static native boolean nativeSetInputReady(boolean ready);

    public static native String nativeClipboard(int action, byte[] copy);

    public static native void nativeSetGrabbing(boolean grab);

    public static native ByteBuffer nativeCreateGamepadButtonBuffer();

    public static native ByteBuffer nativeCreateGamepadAxisBuffer();

    public static native float nativeGetAndroidDPI();

    public static native boolean nativeNotifyLauncher(int type, int... action);
}
