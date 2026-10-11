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

    /**
     * 把 Android 的 {@code Surface} 交给 native 的窗口桥 —— **渲染的关键一步**。
     *
     * <p>游戏 JVM 里的 GLFW stub 调 {@code glfwCreateWindow/glfwMakeContextCurrent} 时，
     * native 侧用的就是这里传进来的 Surface 去建 EGLSurface（GL 上下文由 libglxshim +
     * GL 翻译层在 JVM 内建）。所以必须在 {@code VMLauncher.launchJVM} **之前**、
     * SurfaceHolder 已经 surfaceCreated 之后调用。</p>
     *
     * @param surface {@code android.view.Surface}
     */
    public static native void setupBridgeWindow(Object surface);

    /** 释放窗口桥（游戏退出 / Surface 销毁时调用，不看住会留悬挂引用）。 */
    public static native void releaseBridgeWindow();

    /** AWT（Caciocavallo）路径下的窗口桥，与 {@link #setupBridgeWindow} 配套。 */
    public static native void setupBridgeSurfaceAWT(Object surface);

    /** 以子进程方式执行一个可执行文件（Forge 系安装处理器会用到）。 */
    public static native int executeBinary(String[] args);

    /** native 层是否已装载（{@link #loadNativeLayer} 幂等）。 */
    private static boolean nativeLayerLoaded;

    /**
     * 从指定目录装载 native 层（幂等，必须在触碰本类其它方法之前调用一次）。
     *
     * <p><b>为什么用 {@code System.load(绝对路径)} 而不是 {@code System.loadLibrary(名字)}</b>：
     * 这些库不再是 APK 的 {@code lib/<abi>/} 成员，而是运行时解压到应用私有目录的普通文件。
     * 不能放 {@code lib/} 的原因见 {@code MobileRuntimePaths.BundledNativesArchivePath}：
     * 上游预编译产物的 ELF 段对齐是 4 KB，平台会判定不符合 16 KB 要求并强制「页面大小兼容模式」。</p>
     *
     * <p><b>顺序必须满足 DT_NEEDED 的偏序</b>（实测每个库的 NEEDED，见下）。
     * 私有目录不在 linker 的搜索路径里，所以「按名字找依赖」必然失败：
     * {@code dlopen failed: library "libpojavexec.so" not found: needed by libexithook.so}。
     * 唯一可行的是**先把被依赖的那个按绝对路径装载**，之后 linker 会在已装载表里按 SONAME 复用。</p>
     *
     * <pre>
     *   libc++_shared.so       → 仅系统库
     *   libbytehook.so         → 仅系统库
     *   libandroidnsbypass.so  → libc++_shared
     *   libpojavexec.so        → libandroidnsbypass
     *   libexithook.so         → libbytehook + libpojavexec
     *   libpojavexec_awt.so    → 仅系统库
     * </pre>
     *
     * <p>注意 {@code setLdLibraryPath}（属于 libpojavexec）只能等它装载之后才能调，
     * 所以这里没法靠改 linker 搜索路径来偷懒 —— 顺序是唯一解。</p>
     *
     * @param nativeLibraryDirectory 运行时原生库所在目录（应用私有目录）
     */
    public static synchronized void loadNativeLayer(String nativeLibraryDirectory) {
        if (nativeLayerLoaded) {
            return;
        }

        load(sharedLibrary(nativeLibraryDirectory, "libc++_shared.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libbytehook.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libandroidnsbypass.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libpojavexec.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libexithook.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libpojavexec_awt.so"));

        nativeLayerLoaded = true;
    }

    private static java.io.File sharedLibrary(String directory, String name) {
        java.io.File file = new java.io.File(directory, name);
        if (!file.isFile()) {
            throw new UnsatisfiedLinkError("原生库缺失：" + file.getAbsolutePath()
                    + "（运行组件未解压或解压不完整）");
        }
        return file;
    }

    private static void load(java.io.File library) {
        System.load(library.getAbsolutePath());
    }
}
