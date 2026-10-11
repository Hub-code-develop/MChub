/*
 * 本文件是 MChub 自己写的「壳」，**不是**上游 Tools 的移植。
 *
 * 上游 net.kdt.pojavlaunch.Tools 有近 2000 行，绝大多数是界面/对话框/资源管理，
 * 与「把游戏跑起来」无关；而本轮只需要满足 libpojavexec.so 导出的三个符号：
 *
 *   Java_net_kdt_pojavlaunch_Tools_getJavaVMPointer
 *   Java_net_kdt_pojavlaunch_Tools_jObjectToString
 *   Java_net_kdt_pojavlaunch_Tools_00024SDL_initializeControllerSubsystems   （内部类 Tools$SDL）
 *
 * 所以这里只保留这三个入口，并如实注明它们是占位能力：
 * 手柄（SDL）子系统初始化在 MChub 的移动端尚未接入，调用它只会走 native 里的
 * SDL_Init，不保证生效；等接入 SDL 时再补。
 *
 * 包名/类名不能改（JNI 按全限定名解析符号）。
 */
package net.kdt.pojavlaunch;

public final class Tools {
    private Tools() {
    }

    /** 第二个 JVM 的 JavaVM* 指针（native 侧保存的）。未创建 JVM 时返回 0。 */
    public static native long getJavaVMPointer();

    /** 把任意 Java 对象转成字符串（native 侧用 jobject 反射调用 toString）。 */
    public static native String jObjectToString(Object object);

    /** 对应上游 Tools$SDL —— 内部类名必须是 SDL，native 符号是 Tools_00024SDL。 */
    public static final class SDL {
        private SDL() {
        }

        /**
         * 初始化 SDL 的手柄/摇杆/事件子系统。
         *
         * <p>调用前必须已经 dlopen 过 libSDL3.so（native 侧直接 dlopen("libSDL3.so")）。</p>
         */
        public static native void initializeControllerSubsystems();
    }
}
