/*
 * 与 AngelAuraMC/Amethyst-Android @ v3_openjdk（GPL-3.0-or-later）的同名类对应：
 * app_pojavlauncher/src/main/java/com/oracle/dalvik/VMLauncher.java
 *
 * 它只声明 native 入口，实现在 libpojavexec.so（源文件 jni/jre_launcher.c）：
 * 该实现 dlopen("libjli.so") 后调用 JLI_Launch()，也就是真正「用 java 命令启动」的等价物。
 *
 * 为什么 MChub 改用它、而不再自己写 JNI_CreateJavaVM：
 *   JLI_Launch 会一并把 -Djava.home、classpath 通配符、AWT 原生库、信号处理、
 *   ergonomics 这些琐事按 OpenJDK 自己的规则做掉；手搓一遍等于重造并漏掉细节。
 */
package com.oracle.dalvik;

public final class VMLauncher {
    private VMLauncher() {
    }

    /**
     * 以给定参数创建并运行 JVM（等价于 {@code java <args...>}）。
     *
     * <p>本方法会**阻塞到 JVM 结束**（游戏退出）。必须在非 UI 线程调用。</p>
     *
     * @param args 完整命令行：先是 JVM 选项（{@code -Xmx…}、{@code -Djava.home=…}、{@code -cp …}），
     *             最后是主类与游戏参数
     * @return JVM 退出码；参数为空或启动失败返回非 0
     */
    public static native int launchJVM(String[] args);
}
