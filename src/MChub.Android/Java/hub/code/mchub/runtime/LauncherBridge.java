package hub.code.mchub.runtime;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

import net.kdt.pojavlaunch.utils.JREUtils;

/**
 * Avalonia(C#) 侧通过 JNI 调用本类的静态方法，完成「在 Android 上启动 Minecraft Java 版」的编排。
 *
 * <p>分层约定（移动端设计）：
 * <ul>
 *   <li><b>C# 侧 MChub.Core</b>：下载 / 解压 JRE（tar.xz）/ 实例与账户管理 —— 桌面端已有能力，移动端直接复用；</li>
 *   <li><b>本类（Java 后端）</b>：运行时校验、启动编排、状态与错误上报；</li>
 *   <li><b>native 层</b>：真正创建 JVM（libjvm.so）并驱动 LWJGL / 图形翻译层（GL4ES、Zink、ANGLE）。
 *       该层尚未接入时，{@link #launch} 会抛出带有明确原因的异常，绝不静默失败。</li>
 * </ul>
 *
 * <p>注意：本类运行在 Android Runtime（ART）之上，**不能**在这里创建第二个 JVM ——
 * 创建 JVM 必须发生在 native 层（进程已有一个受管运行时）。因此 {@link #launch} 委托给
 * native 入口 {@link #nativeLaunch}。
 */
public final class LauncherBridge {

    /**
     * 运行时中 JVM 动态库的候选相对路径。
     *
     * <p>面向 Amethyst(AngelAuraMC) / PojavLauncher 系所用的 android-openjdk-build-multiarch 产物：
     * OpenJDK 8 的移动端移植把库放在 {@code {arch}/server} 下，OpenJDK 17/21 沿用标准 {@code server}
     * 目录。这里按顺序探测，避免因布局差异把可用的运行时误判成"不可用"。
     */
    private static final String[] JVM_LIBRARY_CANDIDATES = {
            "lib/server/libjvm.so",
            "lib/aarch64/server/libjvm.so",
            "lib/arm64/server/libjvm.so"
    };

    private static final String RELEASE_FILE_NAME = "release";

    private static volatile String lastError;

    private LauncherBridge() {
    }

    /**
     * 运行时目录是否已具备启动条件：存在 release 文件与 JVM 动态库。
     *
     * @param runtimeDir 运行时根目录（由 C# 侧解压后提供）
     */
    public static boolean isRuntimeReady(String runtimeDir) {
        if (isBlank(runtimeDir)) {
            return false;
        }

        File root = new File(runtimeDir);
        if (!new File(root, RELEASE_FILE_NAME).isFile()) {
            return false;
        }

        for (String candidate : JVM_LIBRARY_CANDIDATES) {
            if (new File(root, candidate).isFile()) {
                return true;
            }
        }

        return false;
    }

    /**
     * 读取运行时版本（release 文件中的 JAVA_VERSION）。
     *
     * @return 形如 {@code 17.0.9} 的版本串；读取失败返回 {@code null}（由调用方展示"未知"）。
     */
    public static String readRuntimeVersion(String runtimeDir) {
        if (isBlank(runtimeDir)) {
            return null;
        }

        File release = new File(runtimeDir, RELEASE_FILE_NAME);
        if (!release.isFile()) {
            return null;
        }

        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(new FileInputStream(release), StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                String trimmed = line.trim();
                if (trimmed.startsWith("JAVA_VERSION=")) {
                    return stripQuotes(trimmed.substring("JAVA_VERSION=".length()));
                }
            }
        } catch (Exception exception) {
            lastError = "读取运行时版本失败：" + exception.getMessage();
        }

        return null;
    }

    /**
     * 启动游戏。
     *
     * @param runtimeDir 运行时根目录
     * @param mainClass  启动主类（由 C# 侧依据版本 JSON 解析，例如 {@code net.minecraft.client.main.Main}）
     * @param jvmArgs    JVM 参数（含 {@code -Xmx}、{@code -Djava.library.path}、{@code -cp} 等，由 C# 侧组装）
     * @param gameArgs   游戏参数（{@code --username} 等）
     * @param gameDir    实例工作目录
     * @param nativeLibDir 运行时原生库目录（应用私有目录），native 侧要靠它找
     *                    libpojavexec / GL 翻译层 / libopenal
     * @param surface    {@code android.view.Surface}（必须是已经 surfaceCreated 的那个），
     *                   交 native 建 EGLSurface 用；传 null 会启动失败（没有渲染目标）
     * @return {@code 0} 表示游戏正常退出；非 0 由调用方展示 {@link #takeLastError()}。
     */
    public static int launch(String runtimeDir, String mainClass, String[] jvmArgs, String[] gameArgs,
                             String gameDir, String nativeLibDir, Object surface) {
        if (GameRuntimeState.isGameRunning()) {
            return fail("已有游戏进程在运行");
        }

        if (!isRuntimeReady(runtimeDir)) {
            return fail("Java 运行时不可用（缺少 release 或 libjvm.so）：" + runtimeDir);
        }

        if (isBlank(mainClass)) {
            return fail("缺少启动主类（mainClass）");
        }

        if (isBlank(gameDir)) {
            return fail("缺少实例工作目录（gameDir）");
        }

        if (isBlank(nativeLibDir)) {
            return fail("缺少原生库目录（nativeLibDir）");
        }

        if (surface == null) {
            return fail("没有可用的渲染 Surface（界面还没准备好就把启动按钮按下去了）");
        }

        List<String> jvm = new ArrayList<>();
        if (jvmArgs != null) {
            for (String argument : jvmArgs) {
                if (!isBlank(argument)) {
                    jvm.add(argument);
                }
            }
        }

        List<String> game = new ArrayList<>();
        if (gameArgs != null) {
            for (String argument : gameArgs) {
                if (argument != null) {
                    game.add(argument);
                }
            }
        }

        String absoluteGameDir = new File(gameDir).getAbsolutePath();

        GameRuntimeState.notifyGameStarting();

        try {
            // 0) 先把 native 层从私有目录装载进来（这些库不再进 APK 的 lib/，见 JREUtils.loadNativeLayer）。
            //    这一步会跑 libpojavexec.so 的 JNI_OnLoad：它 FindClass org/lwjgl/glfw/CallbackBridge
            //    并 RegisterNatives 输入入口 —— 类缺了或方法签名不对，这里就会 SIGABRT。
            JREUtils.loadNativeLayer(nativeLibDir);
            GameRuntimeState.notifyNativeLayerLoaded();

            // 1) 环境：env → LD_LIBRARY_PATH（含 linker 私有接口）→ hook → JRE 内部库 dlopen → GL 层 → chdir。
            PojavRuntimeSupport.Result prepared = PojavRuntimeSupport.prepare(
                    runtimeDir, nativeLibDir, absoluteGameDir, null);
            if (!prepared.ok()) {
                return fail(prepared.failure);
            }

            // 2) 把 Surface 交给 native 窗口桥，顺序上紧贴 JVM 创建：游戏第一次
            //    glfwCreateWindow 就要用它建 EGLSurface（GL 翻译层的环境变量上一步已设好）。
            JREUtils.setupBridgeWindow(surface);

            // 3) 告诉 LWJGL「桌面 GL 库」其实是我们的 GL 翻译层。
            //
            //    否则 org.lwjgl.opengl.GL 的类初始化会去 dlopen 桌面 Linux 的 libGLX.so.0，直接
            //      java.lang.UnsatisfiedLinkError: Failed to locate library: libGLX.so.0
            //      at org.lwjgl.opengl.GL.create(GL.java:101)
            //      at org.lwjgl.glfw.GLFW.glfwCreateWindow(...)
            //    —— 崩在创建窗口那一步，看着像 GLFW 的问题，其实是 GL 库名字不对。
            //    上游 Amethyst 也是在 launchJavaVM 里加这一条（-Dorg.lwjgl.opengl.libname=<renderer>）。
            //    这里用 prepare() 实际装载成功的那一个，保证「说加载谁」和「真加载谁」一致。
            List<String> effectiveJvm = new ArrayList<>(jvm);
            if (prepared.renderLibrary != null
                    && !containsProperty(effectiveJvm, "-Dorg.lwjgl.opengl.libname=")) {
                effectiveJvm.add("-Dorg.lwjgl.opengl.libname=" + prepared.renderLibrary);
            }

            // 4) 组装成与 `java <args>` 完全一样的 argv，交给 libpojavexec 的 JLI_Launch 真正建 JVM。
            //    argv[0] 是程序名；-Djava.home 必须显式给（JLI 靠它找 lib/server/libjvm.so，
            //    在 Android 上它自己那套「从可执行文件路径推断 java.home」推断不出来）。
            List<String> argv = new ArrayList<>(effectiveJvm.size() + game.size() + 3);
            argv.add("java");
            if (!containsJavaHome(effectiveJvm)) {
                argv.add("-Djava.home=" + runtimeDir);
            }
            argv.addAll(effectiveJvm);
            argv.add(mainClass);
            argv.addAll(game);

            int exitCode = com.oracle.dalvik.VMLauncher.launchJVM(argv.toArray(new String[0]));
            if (exitCode != 0) {
                lastError = lastError != null
                        ? lastError
                        : "游戏进程以退出码 " + exitCode + " 结束";
            }
            return exitCode;
        } catch (UnsatisfiedLinkError error) {
            return fail("装载移动端原生库失败：" + error.getMessage());
        } catch (Throwable error) {
            return fail("启动游戏失败：" + error);
        } finally {
            // 无论怎么退出都要把窗口桥摘掉，否则下次启动会拿着一个已经销毁的 Surface。
            // 只在 native 层确实装载成功时调：否则这个 native 方法本身就会抛 UnsatisfiedLinkError。
            if (GameRuntimeState.isNativeLayerLoaded()) {
                try {
                    JREUtils.releaseBridgeWindow();
                } catch (Throwable error) {
                    android.util.Log.w("MChubRuntime", "释放窗口桥失败: " + error);
                }
            }
            GameRuntimeState.notifyGameStopped();
        }
    }

    /** 参数表里是否已经带了某个 -D 属性（带 = 前缀精确比较属性名）。 */
    private static boolean containsProperty(List<String> jvmArgs, String propertyPrefix) {
        for (String argument : jvmArgs) {
            if (argument.startsWith(propertyPrefix)) {
                return true;
            }
        }
        return false;
    }

    private static boolean containsJavaHome(List<String> jvmArgs) {
        for (String argument : jvmArgs) {
            if (argument.startsWith("-Djava.home=")) {
                return true;
            }
        }
        return false;
    }

    /** 移动端原生层（libpojavexec 等运行时库）是否已装载成功。 */
    public static boolean isNativeLayerLoaded() {
        return GameRuntimeState.isNativeLayerLoaded();
    }

    /** 是否有游戏进程在运行。 */
    public static boolean isRunning() {
        return GameRuntimeState.isGameRunning();
    }

    /** 请求停止当前游戏进程。 */
    public static void abort() {
        // 第二个 JVM 在 native 里跑着 JLI_Launch，停它只能靠 System.exit —— 由 exit hook 兜住，
        // 让请求变成「结束 JVM」而不是「杀掉我们的进程」。
        GameRuntimeState.requestAbort();
        try {
            Class<?> systemClass = Class.forName("java.lang.System");
            systemClass.getMethod("exit", int.class).invoke(null, 0);
        } catch (Throwable error) {
            // 第二个 JVM 可能还没建起来（或已经退出了）：只置位即可，调用方按 IsRunning 观察。
            // 这里的日志也不能硬用 Logger（它也是 native 方法，native 层没装载时同样会抛）。
            android.util.Log.w("MChubRuntime", "请求停止时未能调用 System.exit: " + error);
        }
    }

    /** 取走最近一次错误信息（取后清空，避免旧错误被重复展示）。 */
    public static String takeLastError() {
        String message = lastError;
        lastError = null;
        return message;
    }

    /** native 侧（exit hook / 启动失败路径）回调：记录失败原因。 */
    public static void reportError(String message) {
        lastError = message;
    }

    /** native 侧回调：JVM 已经起来、游戏主类开始跑。 */
    public static void notifyGameStarted() {
        GameRuntimeState.notifyGameStarted();
    }

    private static int fail(String message) {
        lastError = message;
        return -1;
    }

    private static boolean isBlank(String value) {
        return value == null || value.trim().isEmpty();
    }

    /** 去掉 release 文件里的首尾引号（JAVA_VERSION="17.0.9"）。 */
    private static String stripQuotes(String value) {
        String trimmed = value.trim();
        if (trimmed.length() >= 2 && trimmed.startsWith("\"") && trimmed.endsWith("\"")) {
            return trimmed.substring(1, trimmed.length() - 1);
        }
        return trimmed;
    }
}
