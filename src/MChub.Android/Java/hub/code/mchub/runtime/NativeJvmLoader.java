package hub.code.mchub.runtime;

import java.util.concurrent.atomic.AtomicBoolean;

/**
 * native 启动层（libmchubjvm.so）的加载与运行状态。
 *
 * <p><b>为什么创建 JVM 必须在 native 侧</b>：本进程已经运行在 Android Runtime(ART) 之上，
 * 而 Minecraft 需要的是一个完整的 OpenJDK 移植版运行时；同一进程内的第二个 JVM 只能由
 * native 代码通过 {@code JNI_CreateJavaVM} 创建（Amethyst(AngelAuraMC) / PojavLauncher 走的也是这条路）。
 *
 * <p><b>库缺失时不抛异常</b>：Android 端界面本身必须始终可用。缺库只标记为"原生层未接入"，
 * 由 {@link LauncherBridge#launch} 在真正启动时给出明确原因，而不是让界面直接崩掉。
 */
final class NativeJvmLoader {

    /** 对应 APK 内 lib/arm64-v8a/libmchubjvm.so。 */
    private static final String LIBRARY_NAME = "mchubjvm";

    private static final AtomicBoolean libraryLoaded = new AtomicBoolean(false);
    private static final AtomicBoolean gameRunning = new AtomicBoolean(false);
    private static final AtomicBoolean abortRequested = new AtomicBoolean(false);

    /** 原生层回传的失败原因（见 {@link #setNativeError}）。 */
    private static volatile String nativeError;

    static {
        loadLibrary();
    }

    private NativeJvmLoader() {
    }

    /** native 启动层是否已随 APK 打包并可加载。 */
    static boolean isLibraryLoaded() {
        return libraryLoaded.get();
    }

    static boolean isGameRunning() {
        return gameRunning.get();
    }

    /** 请求中止：native 侧在渲染/事件循环中轮询 {@link #isAbortRequested()}。 */
    static void requestAbort() {
        abortRequested.set(true);
    }

    /** 供 native 侧（JNI 回调）查询是否需要中止。 */
    static boolean isAbortRequested() {
        return abortRequested.get();
    }

    /** 供 native 侧回调：游戏已开始。 */
    static void notifyGameStarted() {
        abortRequested.set(false);
        gameRunning.set(true);
    }

    /** 供 native 侧回调：游戏已结束。 */
    static void notifyGameStopped() {
        gameRunning.set(false);
    }

    /**
     * 供 native 侧回调：记录原生层的失败原因。
     *
     * <p>原生层直接拿到的是 Java 异常文本（找不到主类 / 主类抛异常等），比"退出码 1"
     * 有用得多，因此单独回传一条，由 {@link LauncherBridge#launch} 优先展示。</p>
     */
    static void setNativeError(String message) {
        nativeError = message;
    }

    /** 取走原生层的失败原因（取后清空）。 */
    static String takeNativeError() {
        String message = nativeError;
        nativeError = null;
        return message;
    }

    private static void loadLibrary() {
        try {
            System.loadLibrary(LIBRARY_NAME);
            libraryLoaded.set(true);
        } catch (UnsatisfiedLinkError error) {
            // 原生层尚未接入：保持进程可用，启动时再由 LauncherBridge 如实报错。
            libraryLoaded.set(false);
        }
    }
}
