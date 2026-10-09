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
        public String ldLibraryPath;
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

            // 2) LD_LIBRARY_PATH：JRE 的 lib + jli + server，再补上应用原生库目录
            String ldLibraryPath = jreLibDir + ":"
                    + jreLibDir + "/jli:"
                    + jreLibDir + "/server:"
                    + nativeLibDir;
            result.ldLibraryPath = ldLibraryPath;
            Os.setenv("LD_LIBRARY_PATH", ldLibraryPath, true);

            // 3) 让 linker 真的认这条路径（只有 native 侧能调这个私有接口）
            JREUtils.setLdLibraryPath(ldLibraryPath);

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

            Log.i(TAG, "运行环境就绪：ld=" + ldLibraryPath + " renderer=" + result.renderLibrary);
        } catch (Throwable error) {
            result.failure = "准备运行环境失败：" + error;
        }
        return result;
    }

    /**
     * 装载 GL 翻译层。返回实际装载的库名；都失败时返回 null 并写日志（不冒充成功）。
     *
     * <p>MobileGlues 需要在 dlopen <b>之前</b> 设好 {@code POJAVEXEC_EGL} 与 {@code MG_DIR_PATH}。</p>
     */
    private static String loadGraphicsLibrary(Context context, String nativeLibDir, String renderer) {
        String selected = renderer == null || renderer.isEmpty() ? DEFAULT_RENDERER : renderer;

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
