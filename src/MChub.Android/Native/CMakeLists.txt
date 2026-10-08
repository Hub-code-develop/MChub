cmake_minimum_required(VERSION 3.22)
project(mchubjvm LANGUAGES CXX)

# native 启动层：在 Android 进程里创建 JVM 并驱动 Minecraft 主类。
# 构建方式见同目录 build-android.sh（用 Android NDK 交叉编译）。
add_library(mchubjvm SHARED mchub_jvm.cpp)

target_compile_features(mchubjvm PRIVATE cxx_std_17)
target_compile_options(mchubjvm PRIVATE -Wall -Wextra)

# JNIEXPORT 自带 default 可见性，其余符号收起来，避免污染宿主进程的符号空间。
set_target_properties(mchubjvm PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN ON)

# Android 日志（logcat 里能看到 mchubjvm 标签）。
find_library(android_log_library log)
target_link_libraries(mchubjvm PRIVATE ${android_log_library})
