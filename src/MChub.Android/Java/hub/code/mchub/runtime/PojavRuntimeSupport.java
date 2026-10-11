package hub.code.mchub.runtime;

import android.content.Context;
import android.system.Os;
import android.util.Log;

import net.kdt.pojavlaunch.utils.JREUtils;

import java.io.File;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.List;

/**
 * 按 Pojav / Amethyst 的顺序准备运行环境并装载 native。
 *
 * <p>这一步做不对，后面即使成功创建了 JVM，只要 MC 一碰 java.awt / 网络 / 音频就会缺库或行为异常。
 * 顺序照上游 {@code net.kdt.pojavlaunch.utils.JREUtils} 抄：</p>
 *
 * <pre>
 *   env → LD_LIBRARY_PATH（含 linker 私有接口调用）→ hook → JRE 内部库 dlopen → GL 翻译层 → chdir
 * </pre>
 *
 * <p>其中 {@code LD_LIBRARY_PATH} 必须经 native 的 {@code setLdLibraryPath}（Android linker 的
 * {@code android_update_LD_LIBRARY_PATH}）才真正生效 —— 只设环境变量没用。</p>
 */
public final class PojavRuntimeSupport {
    private static final String TAG = "MChubRuntime";

    private PojavRuntimeSupport() {
    }

    /** 准备结果。{@code failure} 非空即失败，界面要如实展示，不要静默继续。 */
    public static final class Result {
        /** 设进环境变量 LD_LIBRARY_PATH 的值（**不含** lib/server，见 prepare 里的说明）。 */
        public String ldLibraryPath;
        /** 交给 linker（setLdLibraryPath）的值，在 ldLibraryPath 前面加了 lib/server。 */
        public String linkerPath;
        public String renderLibrary;
        public String failure;

        public boolean ok() {
            return failure == null;
        }
    }

    /** 默认渲染后端：MobileGlues（当前打包里最稳的一套 GL 翻译层）。 */
    public static final String DEFAULT_RENDERER = "opengles_mobileglues";

    /**
     * @param runtimeDir  运行时根目录
     * @param nativeLibDir 应用原生库目录（APK 的 lib/&lt;abi&gt;/ 在安装时解压到这里）
     * @param gameDir     实例工作目录（会 chdir 过去）
     * @param renderer    GL 翻译层标识；null 用 {@link #DEFAULT_RENDERER}
     */
    public static Result prepare(String runtimeDir, String nativeLibDir, String gameDir, String renderer) {
        Result result = new Result();
        try {
            String jreLibDir = findJreLibDir(runtimeDir);
            if (jreLibDir == null) {
                result.failure = "运行时里找不到 lib 目录（既没有 lib/，也没有 lib/<arch>/）：" + runtimeDir;
                return result;
            }

            Context context = currentApplicationContext();

            // 0) 把进程的 stdout / stderr 接到一份日志文件上（见 beginGameLog 的说明）。
            beginGameLog(context);

            // 1) 环境变量：JVM 侧与 native 侧都会读
            Os.setenv("POJAV_NATIVEDIR", nativeLibDir, true);
            Os.setenv("JAVA_HOME", runtimeDir, true);
            Os.setenv("HOME", gameDir, true);
            if (context != null) {
                Os.setenv("TMPDIR", context.getCacheDir().getAbsolutePath(), true);
            }
            // GL 翻译层的通用调参（与上游一致）
            Os.setenv("LIBGL_MIPMAP", "3", true);
            Os.setenv("LIBGL_NOERROR", "1", true);
            Os.setenv("LIBGL_NOINTOVLHACK", "1", true);
            Os.setenv("LIBGL_NORMALIZE", "1", true);
            Os.setenv("LIBGL_ES", "3", true);
            // FORCE_VSYNC 同样被裸 strcmp：egl_bridge.c 的 pojavInitOpenGL() 里
            //     if (strcmp(getenv("FORCE_VSYNC"), "true") == 0)
            // 不设就是 strcmp(NULL, "true") → 又一次 SIGSEGV（就在 AMETHYST_RENDERER 之后的下一步）。
            Os.setenv("FORCE_VSYNC", "true", true);
            // Mesa（zink 后端）的着色器缓存目录，换后端时不必再回来改代码。
            if (context != null) {
                Os.setenv("MESA_GLSL_CACHE_DIR", context.getCacheDir().getAbsolutePath(), true);
            }

            // 2) LD_LIBRARY_PATH。
            //
            // ⚠ 这里有两个路径，**绝不能混为一谈**（踩过一次，代价极大）：
            //
            //   * 环境变量 LD_LIBRARY_PATH —— 给「别人看的」，比如 JLI 自己；
            //   * linker 内部搜索路径（setLdLibraryPath）—— 给 dlopen 用的。
            //
            // OpenJDK 的 java_md.c 里 RequiresSetenv() 会读**环境变量**判断要不要「重新 exec 自己」：
            //   LD_LIBRARY_PATH 里只要出现含 lib/server（或 lib/client）且那里真有 libjvm.so 的路径，
            //   且它又不是以 libjvm 所在目录开头，JLI 就会 putenv 后 execve($JAVA_HOME/bin/java)。
            // Android 上应用私有目录**禁止 execve**，于是直接：
            //     Permission denied
            //     Error: trying to exec /data/user/0/<pkg>/files/Runtimes/Java/17/bin/java.
            //   游戏连主类都没跑到就退出了。
            //
            // 所以：环境变量里**不放 server 目录**（与上游 Amethyst 的 relocateLibPath 一致），
            // 只在 linker 搜索路径里放 —— dlopen("libjvm.so") 能命中，JLI 又不会想重开自己。
            String ldLibraryPath = jreLibDir + "/jli:"
                    + jreLibDir + ":"
                    + nativeLibDir;
            Os.setenv("LD_LIBRARY_PATH", ldLibraryPath, true);
            result.ldLibraryPath = ldLibraryPath;

            // 3) 给 linker 的路径：server 目录在最前（libjvm.so / libjsig.so 在这里）
            String linkerPath = jreLibDir + "/server:" + ldLibraryPath;
            result.linkerPath = linkerPath;
            JREUtils.setLdLibraryPath(linkerPath);

            // 4) hook：exit 拦截（否则游戏退出会直接带走进程）、dlopen/chmod 补丁
            if (context != null) {
                JREUtils.setupExitMethod(context.getApplicationContext());
            } else {
                Log.w(TAG, "拿不到 Application Context，跳过 setupExitMethod（退出行为会退化为默认）");
            }
            JREUtils.initializeHooks();

            // 5) JRE 内部库。顺序敏感：jli → jvm → java/awt 一族。
            String[] ordered = {
                    "libjli.so", "libjvm.so", "libverify.so", "libjava.so",
                    "libnet.so", "libnio.so", "libawt.so", "libawt_headless.so",
                    "libfreetype.so", "libfontmanager.so"
            };
            for (String name : ordered) {
                File located = findInDirectory(jreLibDir, name);
                boolean loaded = located != null
                        ? JREUtils.dlopen(located.getAbsolutePath())
                        : JREUtils.dlopen(name);
                if (!loaded) {
                    Log.w(TAG, "dlopen 未成功：" + name + "（可能该运行时不含此库）");
                }
            }
            // 兜底：把 lib 下其余的 .so 也带上（上游就是这么扫的）
            for (File library : locateLibraries(new File(jreLibDir))) {
                JREUtils.dlopen(library.getAbsolutePath());
            }
            JREUtils.dlopen(nativeLibDir + "/libopenal.so");

            // 6) GL 翻译层
            result.renderLibrary = loadGraphicsLibrary(context, nativeLibDir, renderer);

            // 7) 工作目录 —— 游戏必须在实例目录下启动
            JREUtils.chdir(gameDir);

            Log.i(TAG, "运行环境就绪：env ld=" + ldLibraryPath + " | linker=" + result.linkerPath
                    + " | renderer=" + result.renderLibrary);
        } catch (Throwable error) {
            result.failure = "准备运行环境失败：" + error;
        }
        return result;
    }

    /**
     * 把进程的 stdout / stderr 接到一份日志文件上。
     *
     * <p><b>为什么不用上游的 {@code Logger.begin}</b>：它内部是「dup2 到管道 + 一个 native 线程读管道
     * 再写文件」。进程被 {@code abort()} 掉的那一刻，管道里最后一段（恰恰是崩溃原因）来不及被
     * 搬到文件里就丢了。我们要的就是崩溃现场，所以直接 dup2 到文件，写入是同步的。</p>
     *
     * <p>{@code JLI_Launch}、JVM 的初始化报错、游戏主类、GL 翻译层的输出全走 stdout/stderr，
     * 不接这一下就只能看到「退出码 1」。</p>
     */
    private static void beginGameLog(Context context) {
        if (context == null) {
            Log.w(TAG, "拿不到 Context，跳过日志重定向（游戏输出只会进 logcat）");
            return;
        }

        try {
            File logs = new File(context.getFilesDir(), "Logs");
            if (!logs.isDirectory() && !logs.mkdirs()) {
                Log.w(TAG, "日志目录建不出来：" + logs);
                return;
            }

            File latest = new File(logs, "minecraft-latest.log");
            java.io.FileDescriptor descriptor = Os.open(
                    latest.getAbsolutePath(),
                    android.system.OsConstants.O_WRONLY
                            | android.system.OsConstants.O_CREAT
                            | android.system.OsConstants.O_TRUNC,
                    0600);

            // 1 = stdout，2 = stderr；此后进程里所有原生 fprintf 与 System.out/err 都落到这个文件。
            Os.dup2(descriptor, 1);
            Os.dup2(descriptor, 2);
            Os.close(descriptor);

            Log.i(TAG, "游戏输出重定向到：" + latest.getAbsolutePath());
        } catch (Throwable error) {
            Log.w(TAG, "启用游戏日志失败：" + error);
        }
    }

    /**
     * 装载 GL 翻译层。返回实际装载的库名；都失败时返回 null 并写日志（不冒充成功）。
     *
     * <p>MobileGlues 需要在 dlopen <b>之前</b> 设好 {@code POJAVEXEC_EGL} 与 {@code MG_DIR_PATH}。</p>
     */
    private static String loadGraphicsLibrary(Context context, String nativeLibDir, String renderer) {
        String selected = renderer == null || renderer.isEmpty() ? DEFAULT_RENDERER : renderer;

        // ⚠ AMETHYST_RENDERER 必须设，而且 native 侧**不做空指针判断**：
        //   egl_bridge.c 的 pojavSetWindowHint() 里是
        //       const char *renderer = getenv("AMETHYST_RENDERER");
        //       if (strncmp(renderer, "opengles", 8) == 0) ...
        //   glfwInit 一进来就 strncmp(NULL, ...) → SIGSEGV（si_addr=0），
        //   堆栈是 pojavInit → glfwInit → GLX._initGlfw，极难从 Java 侧看出来。
        try {
            Os.setenv("AMETHYST_RENDERER", selected, true);
        } catch (Throwable error) {
            Log.w(TAG, "设置 AMETHYST_RENDERER 失败：" + error);
        }

        if ("opengles_mobileglues".equals(selected)) {
            try {
                if (context != null) {
                    Os.setenv("MG_DIR_PATH", context.getDataDir().getAbsolutePath() + "/MobileGlues", true);
                }
                Os.setenv("POJAVEXEC_EGL", "libmobileglues.so", true);
            } catch (Throwable error) {
                Log.w(TAG, "设置 MobileGlues 环境失败：" + error);
            }
            if (tryLoad(nativeLibDir, "libmobileglues.so")) {
                return "libmobileglues.so";
            }
        }

        // 依次回退到其它后端
        String[] fallbacks = {"libglxshim.so", "libEGL_angle.so"};
        for (String name : fallbacks) {
            if (tryLoad(nativeLibDir, name)) {
                return name;
            }
        }

        Log.e(TAG, "没有任何可用的 GL 翻译层（请求的是 " + selected + "）");
        return null;
    }

    private static boolean tryLoad(String nativeLibDir, String name) {
        if (JREUtils.dlopen(nativeLibDir + "/" + name)) {
            return true;
        }
        return JREUtils.dlopen(name);
    }

    /**
     * 取 Application Context。
     *
     * <p>宿主是 .NET Android，Java 侧没有 Activity 引用，所以经
     * {@code ActivityThread.currentApplication()} 反射获取；拿不到就返回 null 并由调用方降级处理
     * （不致命：只影响 exit hook 的收尾与缓存目录）。</p>
     */
    private static Context currentApplicationContext() {
        try {
            Class<?> activityThread = Class.forName("android.app.ActivityThread");
            Method method = activityThread.getMethod("currentApplication");
            Object application = method.invoke(null);
            return application instanceof Context ? (Context) application : null;
        } catch (Throwable error) {
            Log.w(TAG, "反射获取 Application 失败：" + error);
            return null;
        }
    }

    /** 找到运行时的库目录：优先 lib/&lt;arch&gt;，再 lib。 */
    private static String findJreLibDir(String runtimeDir) {
        File root = new File(runtimeDir);
        String[] candidates = {"lib", "lib/aarch64", "lib/arm64", "lib/arm64-v8a"};
        for (String candidate : candidates) {
            File directory = new File(root, candidate);
            if (directory.isDirectory()) {
                return directory.getAbsolutePath();
            }
        }
        return null;
    }

    private static File findInDirectory(String directory, String name) {
        File file = new File(directory, name);
        return file.isFile() ? file : null;
    }

    private static List<File> locateLibraries(File directory) {
        List<File> collected = new ArrayList<>();
        File[] children = directory.listFiles();
        if (children == null) {
            return collected;
        }
        for (File child : children) {
            if (child.isFile() && child.getName().endsWith(".so")) {
                collected.add(child);
            } else if (child.isDirectory()) {
                collected.addAll(locateLibraries(child));
            }
        }
        return collected;
    }
}
