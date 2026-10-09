package com.bytedance.android.bytehook;

/**
 * ByteHook 的 ART 侧占位类（与 {@code org/lwjgl/glfw/CallbackBridge} 同一套路）。
 *
 * <p><b>为什么需要它</b>：随 APK 打进 {@code lib/arm64-v8a/} 的 {@code libbytehook.so}
 * 是上游 Pojav 系 native 的依赖（{@code libpojavexec.so} 用它做 inline hook，
 * {@code libandroidnsbypass.so} 用它 hook {@code android_dlopen_ext}）。
 * 它的 {@code JNI_OnLoad} 会：</p>
 *
 * <pre>
 *   FindClass("com/bytedance/android/bytehook/ByteHook")
 *   NewGlobalRef(...)
 *   RegisterNatives(...)          // 9 个 native 方法，名字见下
 * </pre>
 *
 * <p>而 .NET Android 在启动阶段会把 {@code lib/} 下的原生库全部 {@code loadLibrary} 一遍 ——
 * 类不存在时 {@code FindClass} 失败留下 pending exception，紧接着 {@code NewGlobalRef}
 * 就触发 {@code JNI DETECTED ERROR: NewGlobalRef called with pending exception
 * ... ClassNotFoundException: Didn't find class "com.bytedance.android.bytehook.ByteHook"} 并 SIGABRT（实测过）。</p>
 *
 * <p><b>签名必须与上游完全一致</b>：{@code RegisterNatives} 按「方法名 + 签名」精确匹配，
 * 少一个或签名差一个字符都会 {@code NoSuchMethodError}（实测过：漏了 {@code nativeGetArch}
 * 就报 {@code no static or non-static method "Lcom/bytedance/android/bytehook/ByteHook;.nativeGetArch()Ljava/lang/String;"}）。</p>
 *
 * <p><b>怎么穷举出一个 native 库到底要哪些方法</b>（比对着 AAR 猜可靠）：</p>
 *
 * <pre>
 *   # 该库是否自己声明了 JNI_OnLoad（= 装进 lib/ 后启动时一定会执行）
 *   llvm-nm -D --defined-only libbytehook.so | grep JNI_OnLoad
 *   # 它 RegisterNatives 时要用到的方法名（补类的清单就是它）
 *   strings -a libbytehook.so | grep -E '^native[A-Z][A-Za-z0-9_]*$' | sort -u
 *   # 它 FindClass 的类名
 *   strings -a libbytehook.so | grep -E '^[a-z][a-z0-9_]*(/[A-Za-z0-9_$]+){1,6}$' | grep -Ev '^(android|java|javax|com/toolchain)/'
 * </pre>
 *
 * <p><b>它现在为什么还需要</b>：先前启动必崩的真凶是「.NET Android 在启动阶段把 {@code lib/} 下
 * 全部原生库 {@code loadLibrary} 一遍 → 本 .so 的 JNI_OnLoad 找不到这个类 →
 * {@code ClassNotFoundException} 留成 pending exception → 紧接着的 JNI 调用被 CheckJNI 判死」。
 * 主修法是 {@code MChub.Android.csproj} 里把预置运行时排除出启动期预加载
 * （{@code AndroidNativeLibraryNoJniPreload}），让它在运行时按需加载。</p>
 *
 * <p>本类作为**兜底**保留：只要 {@code libbytehook.so} 被 {@code System.loadLibrary}
 * 或作为 {@code libpojavexec.so} 的依赖被 JNI 加载路径带上，其 JNI_OnLoad 就会执行 ——
 * 有占位类时它能正常注册；没有则又变成上面那条崩溃。</p>
 *
 * <p><b>签名必须与上游完全一致</b>：{@code RegisterNatives} 按「方法名 + 签名」精确匹配，
 * 少一个或签名差一个字符都会 {@code NoSuchMethodError}（实测过：漏了 {@code nativeGetArch}
 * 就报 {@code no static or non-static method "Lcom/bytedance/android/bytehook/ByteHook;.nativeGetArch()Ljava/lang/String;"}）。</p>
 *
 * <p><b>怎么穷举出一个 native 库到底要哪些方法</b>（比对着 AAR 猜可靠）：</p>
 *
 * <pre>
 *   # 该库是否自己声明了 JNI_OnLoad（= 被 JNI 加载路径带上时一定会执行）
 *   llvm-nm -D --defined-only libbytehook.so | grep JNI_OnLoad
 *   # 它 RegisterNatives 时要用到的方法名（补类的清单就是它）
 *   strings -a libbytehook.so | grep -E '^native[A-Z][A-Za-z0-9_]*$' | sort -u
 *   # 它 FindClass 的类名
 *   strings -a libbytehook.so | grep -E '^[a-z][a-z0-9_]*(/[A-Za-z0-9_$]+){1,6}$' | grep -Ev '^(android|java|javax|com/toolchain)/'
 * </pre>
 *
 * <p>本文件的签名取自 Maven Central 上 {@code com.bytedance:bytehook} 的 {@code classes.jar}
 * （{@code javap -p} 反查）＋ 上面 strings 的并集：AAR 里 {@code nativeGetArch} 还是 Java 实现，
 * 而这个 .so 已改成 native，所以只信 strings。</p>
 *
 * <p><b>方法体不会被调用</b>：真正的 ByteHook 初始化由第二个 JVM 里的 LWJGL/Pojav 侧完成，
 * 我们只调用 {@code JREUtils.dlopen} / {@code initializeHooks} 这些不需要 ByteHook Java API 的入口。</p>
 */
public class ByteHook {
    private static native String nativeGetVersion();

    private static native String nativeGetArch();

    private static native int nativeInit(int mode, boolean debug);

    private static native int nativeAddIgnore(String caller);

    private static native int nativeGetMode();

    private static native boolean nativeGetDebug();

    private static native void nativeSetDebug(boolean debug);

    private static native boolean nativeGetRecordable();

    private static native void nativeSetRecordable(boolean recordable);

    private static native String nativeGetRecords(int flags);
}
