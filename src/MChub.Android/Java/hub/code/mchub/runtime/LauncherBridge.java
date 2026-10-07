package hub.code.mchub.runtime;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

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
     * @param mainClass  启动主类（由 C# 侧依据版本 JSON 解析，例如 {@code cpw.mods.bootstraplauncher.BootstrapLauncher}）
     * @param jvmArgs    JVM 参数（含 {@code -Xmx}、{@code -Djava.library.path} 等，由 C# 侧组装）
     * @param gameArgs   游戏参数（{@code --username} 等）
     * @param gameDir    实例工作目录
     * @return {@code 0} 表示游戏正常退出；非 0 由调用方展示 {@link #takeLastError()}。
     */
    public static int launch(String runtimeDir, String mainClass, String[] jvmArgs, String[] gameArgs,
                             String gameDir) {
        if (NativeJvmLoader.isGameRunning()) {
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

        List<String> command = new ArrayList<>();
        command.add(mainClass);
        if (jvmArgs != null) {
            for (String argument : jvmArgs) {
                if (!isBlank(argument)) {
                    command.add(argument);
                }
            }
        }
        if (gameArgs != null) {
            for (String argument : gameArgs) {
                if (argument != null) {
                    command.add(argument);
                }
            }
        }

        try {
            int exitCode = nativeLaunch(runtimeDir,
                    command.toArray(new String[0]), new File(gameDir).getAbsolutePath());
            if (exitCode != 0) {
                lastError = "游戏进程以退出码 " + exitCode + " 结束";
            }
            return exitCode;
        } catch (UnsatisfiedLinkError error) {
            return fail("原生启动层尚未接入（缺少 mchubjvm 动态库）：" + error.getMessage());
        } catch (Exception exception) {
            return fail("启动游戏失败：" + exception);
        }
    }

    /** 是否有游戏进程在运行。 */
    public static boolean isRunning() {
        return NativeJvmLoader.isGameRunning();
    }

    /** 请求停止当前游戏进程。 */
    public static void abort() {
        NativeJvmLoader.requestAbort();
    }

    /** 取走最近一次错误信息（取后清空，避免旧错误被重复展示）。 */
    public static String takeLastError() {
        String message = lastError;
        lastError = null;
        return message;
    }

    /**
     * native 入口：加载 libjvm.so、创建 JVM、驱动 LWJGL 与图形翻译层运行游戏。
     *
     * <p>{@code args[0]} 为主类，其余为 JVM 参数与游戏参数（顺序与 {@code JVM_CreateJavaVM} 的
     * {@code JavaVMOption} 约定一致，便于 native 侧直接消费）。
     *
     * @return 进程退出码
     */
    private static native int nativeLaunch(String runtimeDir, String[] args, String gameDir);

    private static int fail(String message) {
        lastError = message;
        return -1;
    }

    private static boolean isBlank(String value) {
        return value == null || value.trim().isEmpty();
    }

    private static String stripQuotes(String value) {
        String trimmed = value.trim();
        if (trimmed.length() >= 2 && trimmed.startsWith("\"") && trimmed.endsWith("\"")) {
            return trimmed.substring(1, trimmed.length() - 1);
        }
        return trimmed;
    }
}
