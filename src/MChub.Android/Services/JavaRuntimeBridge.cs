using Android.Runtime;

namespace MChub.Mobile.Services;

/// <summary>
/// Avalonia(C#) 侧访问 Java 后端的唯一入口。
///
/// <p>分工：下载 / 解压 JRE / 实例与账户管理在 <c>MChub.Core</c>（C#，移动端直接复用）；
/// 运行时校验与启动编排在 Java 侧 <c>hub.code.mchub.runtime.LauncherBridge</c>。
/// 本类只负责把调用翻译成 JNI 调用，不承载业务逻辑。</p>
///
/// <p>所有方法都返回"成功与否 + 原因"，调用方据此在界面上如实反馈，不做静默降级。</p>
/// </summary>
internal static class JavaRuntimeBridge
{
    private const string BridgeClassName = "hub/code/mchub/runtime/LauncherBridge";

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

        IntPtr classRef = IntPtr.Zero;
        IntPtr argument = IntPtr.Zero;
        try
        {
            classRef = JNIEnv.FindClass(BridgeClassName);
            IntPtr methodId = JNIEnv.GetStaticMethodID(classRef, "isRuntimeReady", "(Ljava/lang/String;)Z");
            // JValue 没有 string 重载：字符串必须先经 JNIEnv.NewString 变成 Java 字符串引用。
            argument = JNIEnv.NewString(runtimeDir);
            ready = JNIEnv.CallStaticBooleanMethod(classRef, methodId, [new JValue(argument)]);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            UnavailableReason = $"调用 Java 后端 isRuntimeReady 失败：{exception.Message}";
            IsAvailable = false;
            failure = UnavailableReason;
            return false;
        }
        finally
        {
            DeleteLocalRef(argument);
            DeleteLocalRef(classRef);
        }
    }

    /// <summary>读取运行时版本（release 中的 JAVA_VERSION）；读不到返回 null。</summary>
    public static string? ReadRuntimeVersion(string runtimeDir)
    {
        if (!TryEnsureClass(out _))
            return null;

        IntPtr classRef = IntPtr.Zero;
        IntPtr argument = IntPtr.Zero;
        IntPtr resultRef = IntPtr.Zero;
        try
        {
            classRef = JNIEnv.FindClass(BridgeClassName);
            IntPtr methodId = JNIEnv.GetStaticMethodID(classRef, "readRuntimeVersion",
                "(Ljava/lang/String;)Ljava/lang/String;");
            argument = JNIEnv.NewString(runtimeDir);
            resultRef = JNIEnv.CallStaticObjectMethod(classRef, methodId, [new JValue(argument)]);
            return resultRef == IntPtr.Zero ? null : JNIEnv.GetString(resultRef, JniHandleOwnership.DoNotTransfer);
        }
        catch (Exception exception)
        {
            UnavailableReason = $"调用 Java 后端 readRuntimeVersion 失败：{exception.Message}";
            IsAvailable = false;
            return null;
        }
        finally
        {
            DeleteLocalRef(resultRef);
            DeleteLocalRef(argument);
            DeleteLocalRef(classRef);
        }
    }

    /// <summary>是否有游戏进程在运行。</summary>
    public static bool IsGameRunning()
        => TryCallBoolean("isRunning", "()Z", [], out var running, out _) && running;

    /// <summary>请求停止当前游戏进程。</summary>
    public static void Abort()
        => TryCallVoid("abort", "()V", []);

    /// <summary>取走 Java 侧最近一次错误（取后清空）。</summary>
    public static string? TakeLastError()
        => TryCallString("takeLastError", "()Ljava/lang/String;", []);

    /// <summary>
    /// 启动游戏。参数由 C# 侧依据版本 JSON 组装（与桌面端同一套逻辑）。
    /// </summary>
    /// <param name="exitCode">成功调用时 Java 侧返回的退出码；调用失败时为 -1。</param>
    public static bool TryLaunch(string runtimeDir, string mainClass, string[] jvmArgs, string[] gameArgs,
        string gameDir, out int exitCode, out string? failure)
    {
        exitCode = -1;

        if (!TryEnsureClass(out failure))
            return false;

        IntPtr classRef = IntPtr.Zero;
        IntPtr runtimeDirRef = IntPtr.Zero;
        IntPtr mainClassRef = IntPtr.Zero;
        IntPtr jvmArgsRef = IntPtr.Zero;
        IntPtr gameArgsRef = IntPtr.Zero;
        IntPtr gameDirRef = IntPtr.Zero;

        try
        {
            classRef = JNIEnv.FindClass(BridgeClassName);
            IntPtr methodId = JNIEnv.GetStaticMethodID(classRef, "launch",
                "(Ljava/lang/String;Ljava/lang/String;[Ljava/lang/String;[Ljava/lang/String;Ljava/lang/String;)I");

            runtimeDirRef = JNIEnv.NewString(runtimeDir);
            mainClassRef = JNIEnv.NewString(mainClass);
            jvmArgsRef = JNIEnv.NewArray(jvmArgs);
            gameArgsRef = JNIEnv.NewArray(gameArgs);
            gameDirRef = JNIEnv.NewString(gameDir);

            exitCode = JNIEnv.CallStaticIntMethod(classRef, methodId,
            [
                new JValue(runtimeDirRef),
                new JValue(mainClassRef),
                new JValue(jvmArgsRef),
                new JValue(gameArgsRef),
                new JValue(gameDirRef)
            ]);

            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = $"调用 Java 启动接口失败：{exception.Message}";
            return false;
        }
        finally
        {
            DeleteLocalRef(gameDirRef);
            DeleteLocalRef(gameArgsRef);
            DeleteLocalRef(jvmArgsRef);
            DeleteLocalRef(mainClassRef);
            DeleteLocalRef(runtimeDirRef);
            DeleteLocalRef(classRef);
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

    private static bool TryCallBoolean(string method, string signature, JValue[] arguments, out bool value,
        out string? failure)
    {
        value = false;

        if (!TryEnsureClass(out failure))
            return false;

        IntPtr classRef = IntPtr.Zero;
        try
        {
            classRef = JNIEnv.FindClass(BridgeClassName);
            IntPtr methodId = JNIEnv.GetStaticMethodID(classRef, method, signature);
            value = JNIEnv.CallStaticBooleanMethod(classRef, methodId, arguments);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            UnavailableReason = $"调用 Java 后端 {method} 失败：{exception.Message}";
            IsAvailable = false;
            failure = UnavailableReason;
            return false;
        }
        finally
        {
            DeleteLocalRef(classRef);
        }
    }

    private static string? TryCallString(string method, string signature, JValue[] arguments)
    {
        if (!TryEnsureClass(out _))
            return null;

        IntPtr classRef = IntPtr.Zero;
        IntPtr resultRef = IntPtr.Zero;
        try
        {
            classRef = JNIEnv.FindClass(BridgeClassName);
            IntPtr methodId = JNIEnv.GetStaticMethodID(classRef, method, signature);
            resultRef = JNIEnv.CallStaticObjectMethod(classRef, methodId, arguments);
            return resultRef == IntPtr.Zero ? null : JNIEnv.GetString(resultRef, JniHandleOwnership.DoNotTransfer);
        }
        catch (Exception exception)
        {
            UnavailableReason = $"调用 Java 后端 {method} 失败：{exception.Message}";
            IsAvailable = false;
            return null;
        }
        finally
        {
            DeleteLocalRef(resultRef);
            DeleteLocalRef(classRef);
        }
    }

    private static void TryCallVoid(string method, string signature, JValue[] arguments)
    {
        if (!TryEnsureClass(out _))
            return;

        IntPtr classRef = IntPtr.Zero;
        try
        {
            classRef = JNIEnv.FindClass(BridgeClassName);
            IntPtr methodId = JNIEnv.GetStaticMethodID(classRef, method, signature);
            JNIEnv.CallStaticVoidMethod(classRef, methodId, arguments);
        }
        catch (Exception exception)
        {
            UnavailableReason = $"调用 Java 后端 {method} 失败：{exception.Message}";
            IsAvailable = false;
        }
        finally
        {
            DeleteLocalRef(classRef);
        }
    }

    private static void DeleteLocalRef(IntPtr reference)
    {
        if (reference != IntPtr.Zero)
            JNIEnv.DeleteLocalRef(reference);
    }
}
