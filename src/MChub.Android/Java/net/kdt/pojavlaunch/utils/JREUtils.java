package net.kdt.pojavlaunch.utils;

/**
 * 仅为声明 Pojav 系 native 入口的「壳」类 —— 不复制上游实现。
 *
 * <p><b>为什么包名与类名必须与上游完全一致</b>：JNI 是按「类的全限定名（把 . 换成 _）+ 方法名」
 * 来解析符号的。这些方法的实现就在我们随 APK 打包的
 * {@code libpojavexec.so} / {@code libexithook.so} 里（符号形如
 * {@code Java_net_kdt_pojavlaunch_utils_JREUtils_dlopen}）。换个包名就找不到符号。</p>
 *
 * <p>另一个要点：{@code setLdLibraryPath} 内部走的是 Android linker 的私有接口
 * {@code android_update_LD_LIBRARY_PATH}（{@code libdl.so}）—— 这是运行时修改
 * {@code LD_LIBRARY_PATH} 的唯一有效途径（linker 并不读环境变量），只有 native 侧能做。
 * 所以 JRE 内部库（libjli/libjvm/libawt/...）必须先经它把搜索路径补上，再 dlopen。</p>
 */
public final class JREUtils {
    private JREUtils() {
    }

    /** 以 RTLD_GLOBAL 打开动态库；失败时会尝试 namespace 逃逸（androidnsbypass）。 */
    public static native boolean dlopen(String libPath);

    /** 更新 linker 的库搜索路径（Android 私有接口）。 */
    public static native void setLdLibraryPath(String ldLibraryPath);

    /** 切换进程工作目录 —— 游戏必须在实例目录下启动。 */
    public static native int chdir(String path);

    /** 装载 exit / dlopen / chmod 等 hook（实现在 libexithook.so）。 */
    public static native void initializeHooks();

    /** 让 exit hook 拿到应用 Context（用于在游戏退出时做收尾而不是直接杀进程）。 */
    public static native void setupExitMethod(android.content.Context context);

    static {
        // 顺序与 Pojav 一致：exit hook 先上，再是主运行支撑与其 AWT 桥。
        System.loadLibrary("exithook");
        System.loadLibrary("pojavexec");
        System.loadLibrary("pojavexec_awt");
    }
}
