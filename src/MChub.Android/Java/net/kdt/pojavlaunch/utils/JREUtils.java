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
     * <p><b>加载顺序</b>：共同依赖（libc++_shared）→ libbytehook（libpojavexec 与
     * libandroidnsbypass 都依赖它）→ libexithook → libpojavexec → libpojavexec_awt。
     * linker 按 SONAME 复用已载入的库，所以先把依赖载进来，后面 dlopen 绝对路径才找得到。</p>
     *
     * @param nativeLibraryDirectory 运行时原生库所在目录（应用私有目录）
     */
    public static synchronized void loadNativeLayer(String nativeLibraryDirectory) {
        if (nativeLayerLoaded) {
            return;
        }

        load(sharedLibrary(nativeLibraryDirectory, "libc++_shared.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libbytehook.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libexithook.so"));
        load(sharedLibrary(nativeLibraryDirectory, "libpojavexec.so"));
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
