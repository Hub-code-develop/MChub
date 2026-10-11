package hub.code.mchub.runtime;

import java.util.concurrent.atomic.AtomicBoolean;

/**
 * 第二个 JVM（游戏）的运行状态。
 *
 * <p><b>为什么创建 JVM 不是在 MChub 自己的 native 里做</b>：本进程已经跑在 Android Runtime(ART)
 * 之上，Minecraft 需要的是一个完整的 OpenJDK 移植版运行时。同一进程里的第二个 JVM 由
 * libpojavexec.so 里的 {@code JLI_Launch} 创建（AngelAuraMC/Amethyst 也是这条路），
 * 也就是 {@code com.oracle.dalvik.VMLauncher.launchJVM(argv)}。
 * 早先那套自研的 {@code JNI_CreateJavaVM}（libmchubjvm.so）已被取代并删除 ——
 * JLI_Launch 会把 java.home、classpath 通配符、AWT 库、信号处理这些琐事按 OpenJDK 自己的规则做掉。</p>
 *
 * <p>这里只保存状态，不加载任何库；界面据此显示「运行中 / 空闲」，不伪造进度。</p>
 */
final class GameRuntimeState {

    private static final AtomicBoolean gameRunning = new AtomicBoolean(false);
    private static final AtomicBoolean nativeLoaded = new AtomicBoolean(false);
    private static final AtomicBoolean abortRequested = new AtomicBoolean(false);

    private GameRuntimeState() {
    }

    static boolean isNativeLayerLoaded() {
        return nativeLoaded.get();
    }

    /** 由 {@link LauncherBridge#launch} 在 native 层装载成功后调用。 */
    static void notifyNativeLayerLoaded() {
        nativeLoaded.set(true);
    }

    static boolean isGameRunning() {
        return gameRunning.get();
    }

    /** 进入启动流程（还没真正跑起来，但已经不能再启动第二个）。 */
    static void notifyGameStarting() {
        abortRequested.set(false);
        gameRunning.set(true);
    }

    /** 游戏主类开始执行（native 侧回调，比 notifyGameStarting 更晚也更准）。 */
    static void notifyGameStarted() {
        abortRequested.set(false);
        gameRunning.set(true);
    }

    static void notifyGameStopped() {
        gameRunning.set(false);
    }

    static void requestAbort() {
        abortRequested.set(true);
    }

    static boolean isAbortRequested() {
        return abortRequested.get();
    }
}
