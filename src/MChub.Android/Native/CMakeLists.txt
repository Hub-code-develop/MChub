cmake_minimum_required(VERSION 3.22)
project(mchubnative LANGUAGES CXX)

# 这里只构建 mchubjava：移动端自带的「java 命令」（Forge / NeoForge / OptiFine 的安装处理器要用）。
#
# 创建游戏 JVM 的活不再由本目录承担 —— 那件事交给 libpojavexec.so 里的 JLI_Launch
# （经 com.oracle.dalvik.VMLauncher.launchJVM），也就是 Amethyst/Pojav 走的那条路。
# 早先自研的 mchub_jvm.cpp / libmchubjvm.so 已删除，理由见 Java/hub/code/mchub/runtime/GameRuntimeState.java。

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

# 显式 16 KB 段对齐（Android 15+ 对 lib/<abi>/ 的硬要求；不跟随 NDK 默认值）。
target_link_options(mchubjava PRIVATE "-Wl,-z,max-page-size=16384")

set_target_properties(mchubjava PROPERTIES OUTPUT_NAME "libmchubjava.so")

find_library(android_log_library_java log)
target_link_libraries(mchubjava PRIVATE ${android_log_library_java})
