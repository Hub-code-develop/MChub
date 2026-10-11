/*
 * 移植自 AngelAuraMC/Amethyst-Android @ v3_openjdk（GPL-3.0-or-later）
 * 源文件：app_pojavlauncher/src/main/java/net/kdt/pojavlaunch/Logger.java
 *
 * MChub 改动：去掉 androidx.annotation.Keep（本项目不引入 androidx 注解也要能编），
 * 其余保持原样。native 实现就在随 APK 打包的 libpojavexec.so 里，
 * 所以包名 net.kdt.pojavlaunch 与类名 Logger 都不能改。
 */
package net.kdt.pojavlaunch;

import java.util.concurrent.CopyOnWriteArrayList;

/** 把日志写到文件；同一份日志也允许 Java 侧挂监听（游戏日志面板用）。 */
public class Logger {
    private static final CopyOnWriteArrayList<eventLogListener> logListeners = new CopyOnWriteArrayList<>();

    /** 写一行日志（native 侧有过滤）。 */
    public static native void appendToLog(String text);

    /** 重新开始一份日志文件。 */
    public static native void begin(String logFilePath);

    public static void addLogListener(eventLogListener listener) {
        boolean wasEmpty = Logger.logListeners.isEmpty();
        Logger.logListeners.add(listener);
        if (wasEmpty) setLogListener(Logger::onEventLogged);
    }

    public static void removeLogListener(eventLogListener listener) {
        Logger.logListeners.remove(listener);
        if (Logger.logListeners.isEmpty()) {
            // 没有监听者时把 native 侧的回调摘掉，省掉每行日志一次的 JNI 往返。
            setLogListener(null);
        }
    }

    private static void onEventLogged(String text) {
        for (eventLogListener logListener : Logger.logListeners) {
            logListener.onEventLogged(text);
        }
    }

    /** Java 侧监听接口，同时兼作 native 回调的落点。 */
    public interface eventLogListener {
        void onEventLogged(String text);
    }

    private static native void setLogListener(eventLogListener logListener);
}
