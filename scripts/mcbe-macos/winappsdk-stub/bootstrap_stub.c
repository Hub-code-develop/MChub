/*
 * Microsoft.WindowsAppRuntime.Bootstrap.dll 的最小替代实现。
 *
 * 背景:Minecraft.Windows.exe(Windows GDK 版)静态导入该 Bootstrapper 的
 * MddBootstrapInitialize2 / MddBootstrapShutdown。真实实现在初始化时会通过
 * PackageManager 查找 "Microsoft.WindowsAppRuntime.1.8" 这个 MSIX 框架包,
 * 而 Wine 的 appx/PackageManager 只是空壳,于是运行时报:
 *
 *   ERROR 0x80004001: Bootstrapper initialization failed
 *   while looking for version 1.8 (MSIX package version >= 8000.770.947.0)
 *
 * 随后主程序直接退出。随实例分发的 WinAppSDK 是"自包含"布局(所有
 * Microsoft.WindowsAppRuntime.dll / Microsoft.UI.* / MRM.dll 等都在 exe 同级
 * 目录),此时引导器本就不需要做任何包查找,因此这里让它直接返回 S_OK,
 * 由同目录的自包含 DLL 提供后续实现。
 *
 * 构建:scripts/mcbe-macos/build-winappsdk-stub.sh(mingw-w64 交叉编译)。
 */

#include <windows.h>

/* PACKAGE_VERSION 是一个 8 字节的联合体(ULONGLONG Version),按值传递。 */
typedef struct _PACKAGE_VERSION
{
    unsigned long long Version;
} PACKAGE_VERSION;

__declspec(dllexport) long MddBootstrapInitialize2(unsigned int majorMinorVersion,
                                                  const wchar_t* versionTag,
                                                  PACKAGE_VERSION minVersion,
                                                  unsigned int options)
{
    (void)majorMinorVersion;
    (void)versionTag;
    (void)minVersion;
    (void)options;
    return 0; /* S_OK */
}

__declspec(dllexport) void MddBootstrapShutdown(void)
{
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reason;
    (void)reserved;
    return TRUE;
}