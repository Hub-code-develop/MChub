/*
 * 移植自 AngelAuraMC/Amethyst-Android @ v3_openjdk（GPL-3.0-or-later）
 * 源文件：app_pojavlauncher/src/main/java/net/kdt/pojavlaunch/GrabListener.java
 *
 * 由 MChub 保留原样搬运（仅去掉 androidx/@Keep 等与 dex 编译无关的注解），
 * 因为 JNI 按「全限定类名 + 方法名」解析符号，包名与类名必须与上游完全一致。
 * 详见 MChub.Android/Java 下的说明。
 */
package net.kdt.pojavlaunch;

public interface GrabListener {
    void onGrabState(boolean isGrabbing);
}
