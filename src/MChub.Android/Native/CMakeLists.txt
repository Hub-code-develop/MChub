cmake_minimum_required(VERSION 3.22)
project(mchubjvm LANGUAGES CXX)

# native 启动层：在 Android 进程里创建 JVM 并驱动 Minecraft 主类。
# 构建方式见同目录 build-android.sh（用 Android NDK 交叉编译）。
add_library(mchubjvm SHARED mchub_jvm.cpp)

target_compile_features(mchubjvm PRIVATE cxx_std_17)
target_compile_options(mchubjvm PRIVATE -Wall -Wextra)

# 强制 16 KB 段对齐（PT_LOAD 的 p_align = 0x4000）。
#
# Android 15+ 要求 APK 的 lib/<abi>/ 下 .so 满足 16 KB 页对齐，否则装机被系统判为
# 「页面大小兼容模式」并在启动时弹警告；16 KB 对齐在 4 KB 页设备上同样有效，所以无条件加上。
#
# 为什么必须**显式**写：NDK 的默认值随版本变（r26 及更早 4 KB，r27+ 16 KB），
# 本地与 CI 用不同 NDK 就会出现「本地没事、CI 打出来的包不过门槛」。
# 实测：同一份源码，本地 NDK 25.2 出 0x4000，CI 的 NDK 出 0x1000（被 CI 的 16 KB 门槛拦下）。
target_link_options(mchubjvm PRIVATE "-Wl,-z,max-page-size=16384")

# JNIEXPORT 自带 default 可见性，其余符号收起来，避免污染宿主进程的符号空间。
set_target_properties(mchubjvm PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN ON)

# Android 日志（logcat 里能看到 mchubjvm 标签）。
find_library(android_log_library log)
target_link_libraries(mchubjvm PRIVATE ${android_log_library})

# ---------------------------------------------------------------------------
# mchubjava：移动端自带的「java 命令」（供 Forge / NeoForge / OptiFine 的安装处理器使用）。
#
# 它必须是**可执行文件**而不是共享库 —— MinecraftLaunch 是以 Process.Start(javaPath, ...)
# 拉起它的。Android 上应用私有目录不允许 execve，而 lib/<abi>/ 解出来的文件既可执行又受系统保护，
# 所以输出名直接定为 libmchubjava.so，随 APK 进 lib/（csproj 里标记为不进启动期 JNI 预加载，
# 否则 linker 会拿它当动态库 dlopen 并报「不是共享库」）。
#
# 详细缘由见 mchubjava.cpp 头部注释。
# ---------------------------------------------------------------------------
add_executable(mchubjava mchubjava.cpp)

target_compile_features(mchubjava PRIVATE cxx_std_17)
target_compile_options(mchubjava PRIVATE -Wall -Wextra)

# 与 libmchubjvm.so 同理：显式 16 KB 段对齐（Android 15+ 对 lib/<abi>/ 的硬要求）。
target_link_options(mchubjava PRIVATE "-Wl,-z,max-page-size=16384")

set_target_properties(mchubjava PROPERTIES OUTPUT_NAME "libmchubjava.so")

find_library(android_log_library_java log)
target_link_libraries(mchubjava PRIVATE ${android_log_library_java})
