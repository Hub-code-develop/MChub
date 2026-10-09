using Hub.Code.Mchub.Runtime;

namespace MChub.Mobile.Services;

/// <summary>
/// Avalonia(C#) 侧访问 Java 后端的唯一入口。
///
/// <p>分工：下载 / 解压 JRE / 实例与账户管理在 <c>MChub.Core</c>（C#，移动端直接复用）；
/// 运行时校验与启动编排在 Java 侧 <c>hub.code.mchub.runtime.LauncherBridge</c>。
/// 本类只负责把调用翻译成托管绑定调用，不承载业务逻辑。</p>
///
/// <p><b>为什么用构建期生成的绑定，而不是裸 <c>Android.Runtime.JNIEnv</c></b>：
/// <c>JNIEnv.FindClass(string)</c> 返回的是 <b>全局引用</b>（.NET Android 实现为
/// <c>NewGlobalRef(JniEnvironment.Types.FindClass(...))</c>），而 <c>JNIEnv.DeleteLocalRef</c>
/// 会把这个句柄<b>无条件当成局部引用</b>去删。两者一配对，CheckJNI（Debug 包默认开启）就会
/// 直接 SIGABRT：<c>JNI DETECTED ERROR: expected reference of kind Local but found Global</c>，
/// 而且即使侥幸不崩，每次 FindClass 都会漏一个全局引用。</p>
///
/// <p>本项目的 Java 源文件（<c>Java/**/*.java</c>）在构建时已经生成托管绑定
/// （命名空间 <c>Hub.Code.Mchub.Runtime</c>），句柄类型由 Java.Interop 自己维护，
/// 既不会错删也不会泄漏，因此这里一律走绑定。</p>
///
/// <p>Java 侧方法约定为「不抛异常，错误经 lastError 回报」（见 LauncherBridge.java），
/// 所以这里的 try/catch 只兜底真正的 JNI/类加载失败。</p>
/// </summary>
internal static class JavaRuntimeBridge
{
    /// <summary>Java 侧 LauncherBridge 是否可用（类存在且可解析）。</summary>
    public static bool IsAvailable { get; private set; } = true;

    /// <summary>不可用的原因（仅在 <see cref="IsAvailable"/> 为 false 时有值）。</summary>
    public static string? UnavailableReason { get; private set; }

    /// <summary>检查运行时目录是否具备启动条件（存在 release 与 libjvm.so）。</summary>
    public static bool TryIsRuntimeReady(string runtimeDir, out bool ready, out string? failure)
    {
        ready = false;

        if (!TryEnsureClass(out failure))
            return false;

        try
        {
            ready = LauncherBridge.IsRuntimeReady(runtimeDir);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = MarkUnavailable("isRuntimeReady", exception);
            return false;
        }
    }

    /// <summary>读取运行时版本（release 中的 JAVA_VERSION）；读不到返回 null。</summary>
    public static string? ReadRuntimeVersion(string runtimeDir)
    {
        if (!TryEnsureClass(out _))
            return null;

        try
        {
            return LauncherBridge.ReadRuntimeVersion(runtimeDir);
        }
        catch (Exception exception)
        {
            MarkUnavailable("readRuntimeVersion", exception);
            return null;
        }
    }

    /// <summary>是否有游戏进程在运行。</summary>
    public static bool IsGameRunning()
    {
        if (!TryEnsureClass(out _))
            return false;

        try
        {
            return LauncherBridge.IsRunning;
        }
        catch (Exception exception)
        {
            MarkUnavailable("isRunning", exception);
            return false;
        }
    }

    /// <summary>native 启动层（libmchubjvm.so）是否已随 APK 打包并可加载。</summary>
    public static bool IsNativeLayerLoaded()
    {
        if (!TryEnsureClass(out _))
            return false;

        try
        {
            return LauncherBridge.IsNativeLayerLoaded;
        }
        catch (Exception exception)
        {
            MarkUnavailable("isNativeLayerLoaded", exception);
            return false;
        }
    }

    /// <summary>请求停止当前游戏进程。</summary>
    public static void Abort()
    {
        if (!TryEnsureClass(out _))
            return;

        try
        {
            LauncherBridge.Abort();
        }
        catch (Exception exception)
        {
            MarkUnavailable("abort", exception);
        }
    }

    /// <summary>取走 Java 侧最近一次错误（取后清空）。</summary>
    public static string? TakeLastError()
    {
        if (!TryEnsureClass(out _))
            return null;

        try
        {
            return LauncherBridge.TakeLastError();
        }
        catch (Exception exception)
        {
            MarkUnavailable("takeLastError", exception);
            return null;
        }
    }

    /// <summary>
    /// 启动游戏。参数由 C# 侧依据版本 JSON 组装（与桌面端同一套逻辑）。
    /// </summary>
    /// <param name="nativeLibDir">应用原生库目录（ApplicationInfo.nativeLibraryDir）：
    /// Java/native 侧靠它找 libpojavexec、GL 翻译层与 libopenal。</param>
    /// <param name="exitCode">成功调用时 Java 侧返回的退出码；调用失败时为 -1。</param>
    public static bool TryLaunch(string runtimeDir, string mainClass, string[] jvmArgs, string[] gameArgs,
        string gameDir, string nativeLibDir, out int exitCode, out string? failure)
    {
        exitCode = -1;

        if (!TryEnsureClass(out failure))
            return false;

        try
        {
            exitCode = LauncherBridge.Launch(runtimeDir, mainClass, jvmArgs, gameArgs, gameDir, nativeLibDir);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = MarkUnavailable("launch", exception);
            return false;
        }
    }

    private static bool TryEnsureClass(out string? failure)
    {
        if (IsAvailable)
        {
            failure = null;
            return true;
        }

        failure = UnavailableReason ?? "Java 后端不可用";
        return false;
    }

    private static string MarkUnavailable(string method, Exception exception)
    {
        UnavailableReason = $"调用 Java 后端 {method} 失败：{exception.Message}";
        IsAvailable = false;
        return UnavailableReason;
    }
}
