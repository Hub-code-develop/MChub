// mchubjvm —— MChub 移动端的 native 启动层。
//
// 职责：在 Android 进程（已运行在 ART 之上）里再创建一个真正的 OpenJDK 运行时，
// 并驱动 Minecraft 的主类跑起来。这是 PojavLauncher / Amethyst 走的同一条路：
// 同进程内的第二个 JVM 只能由 native 代码通过 JNI_CreateJavaVM 创建。
//
// 为什么这些逻辑必须在 C++：
//   1. JNI_CreateJavaVM 是 libjvm.so 导出的 C 函数，Java 层无 API 可调；
//   2. 主类 main() 会阻塞整个调用线程直到游戏退出，只有 native 能这样"顶着"；
//   3. 图形翻译层（GL4ES / Zink / ANGLE）与 LWJGL 的原生库加载也发生在这一层。
//
// 与 Java 侧的契约（见 hub/code/mchub/runtime/LauncherBridge.java）：
//   nativeLaunch(runtimeDir, mainClass, jvmArgs[], gameArgs[], gameDir) -> int
//   nativeAbort()
// 回调（见 NativeJvmLoader.java）：notifyGameStarted / notifyGameStopped / setNativeError
//
// 说明：本层只负责"把 JVM 起起来并调用主类"。图形翻译层与定制 LWJGL 原生库属于外部资产
// （Amethyst/Pojav 系发行包内），缺失时游戏会在 LWJGL 初始化阶段失败 —— 此时异常会被
// 捕获并原样回传给界面，绝不假装启动成功。

#include <jni.h>

#include <android/log.h>
#include <dlfcn.h>
#include <pthread.h>
#include <unistd.h>

#include <string>
#include <vector>

#define LOG_TAG "mchubjvm"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

namespace {

/// 运行时里 libjvm.so 的候选相对路径（与 LauncherBridge 的探测顺序保持一致）。
const char* const kJvmLibraryCandidates[] = {
        "lib/server/libjvm.so",
        "lib/aarch64/server/libjvm.so",
        "lib/arm64/server/libjvm.so",
};

/// JNI_CreateJavaVM 的函数指针类型（从 libjvm.so 动态解析）。
using CreateJavaVmFn = jint (*)(JavaVM**, void**, void*);

JavaVM* g_vm = nullptr;
JNIEnv* g_env = nullptr;

jclass g_loaderClass = nullptr;         // NativeJvmLoader 的全局引用
jmethodID g_notifyStarted = nullptr;
jmethodID g_notifyStopped = nullptr;
jmethodID g_setNativeError = nullptr;

pthread_mutex_t g_launchLock = PTHREAD_MUTEX_INITIALIZER;

std::string ToStdString(JNIEnv* env, jstring value) {
    if (value == nullptr) {
        return {};
    }
    const char* chars = env->GetStringUTFChars(value, nullptr);
    std::string result = chars != nullptr ? chars : "";
    if (chars != nullptr) {
        env->ReleaseStringUTFChars(value, chars);
    }
    return result;
}

std::vector<std::string> ToStdVector(JNIEnv* env, jobjectArray array) {
    std::vector<std::string> result;
    if (array == nullptr) {
        return result;
    }
    const jsize count = env->GetArrayLength(array);
    result.reserve(static_cast<size_t>(count));
    for (jsize index = 0; index < count; ++index) {
        auto element = reinterpret_cast<jstring>(env->GetObjectArrayElement(array, index));
        result.push_back(ToStdString(env, element));
        if (element != nullptr) {
            env->DeleteLocalRef(element);
        }
    }
    return result;
}

jobjectArray ToJavaStringArray(JNIEnv* env, const std::vector<std::string>& items) {
    jclass stringClass = env->FindClass("java/lang/String");
    jobjectArray array = env->NewObjectArray(static_cast<jsize>(items.size()), stringClass, nullptr);
    for (size_t index = 0; index < items.size(); ++index) {
        jstring element = env->NewStringUTF(items[index].c_str());
        env->SetObjectArrayElement(array, static_cast<jsize>(index), element);
        env->DeleteLocalRef(element);
    }
    env->DeleteLocalRef(stringClass);
    return array;
}

/// 把当前挂起的 Java 异常转成一行可读文本（取后清除异常，避免污染后续 JNI 调用）。
std::string DescribePendingException(JNIEnv* env) {
    jthrowable throwable = env->ExceptionOccurred();
    if (throwable == nullptr) {
        return {};
    }
    env->ExceptionClear();

    std::string description = "未知 Java 异常";
    jclass throwableClass = env->GetObjectClass(throwable);
    if (throwableClass != nullptr) {
        jmethodID toString = env->GetMethodID(throwableClass, "toString", "()Ljava/lang/String;");
        if (toString != nullptr) {
            auto text = reinterpret_cast<jstring>(env->CallObjectMethod(throwable, toString));
            if (text != nullptr) {
                description = ToStdString(env, text);
                env->DeleteLocalRef(text);
            }
            env->ExceptionClear();
        }
        jmethodID getMessage = env->GetMethodID(throwableClass, "getMessage", "()Ljava/lang/String;");
        if (getMessage != nullptr) {
            auto message = reinterpret_cast<jstring>(env->CallObjectMethod(throwable, getMessage));
            if (message != nullptr) {
                description += ": " + ToStdString(env, message);
                env->DeleteLocalRef(message);
            }
            env->ExceptionClear();
        }
        env->DeleteLocalRef(throwableClass);
    }
    env->DeleteLocalRef(throwable);
    return description;
}

void SetNativeError(JNIEnv* env, const std::string& message) {
    LOGE("%s", message.c_str());
    if (env == nullptr || g_loaderClass == nullptr || g_setNativeError == nullptr) {
        return;
    }
    jstring text = env->NewStringUTF(message.c_str());
    env->CallStaticVoidMethod(g_loaderClass, g_setNativeError, text);
    env->DeleteLocalRef(text);
    env->ExceptionClear();
}

void NotifyLoader(JNIEnv* env, jmethodID method) {
    if (env == nullptr || g_loaderClass == nullptr || method == nullptr) {
        return;
    }
    env->CallStaticVoidMethod(g_loaderClass, method);
    env->ExceptionClear();
}

/// 在运行时目录里找 libjvm.so；找不到返回空串。
std::string FindJvmLibrary(const std::string& runtimeDir) {
    for (const char* candidate : kJvmLibraryCandidates) {
        std::string path = runtimeDir + "/" + candidate;
        if (access(path.c_str(), R_OK) == 0) {
            return path;
        }
    }
    return {};
}

/// 主类名（点分）转成 FindClass 需要的斜杠形式。
std::string ToClassDescriptor(const std::string& mainClass) {
    std::string descriptor = mainClass;
    for (char& character : descriptor) {
        if (character == '.') {
            character = '/';
        }
    }
    return descriptor;
}

/// 执行 nativeAbort 的内部实现（需要已附加到 JVM 的线程）。
void RequestSystemExit(JNIEnv* env) {
    if (env == nullptr) {
        return;
    }
    jclass systemClass = env->FindClass("java/lang/System");
    if (systemClass == nullptr) {
        env->ExceptionClear();
        return;
    }
    jmethodID exitMethod = env->GetStaticMethodID(systemClass, "exit", "(I)V");
    if (exitMethod != nullptr) {
        env->CallStaticVoidMethod(systemClass, exitMethod, 0);
    }
    env->ExceptionClear();
    env->DeleteLocalRef(systemClass);
}

}  // namespace

extern "C" JNIEXPORT jint JNICALL
Java_hub_code_mchub_runtime_LauncherBridge_nativeLaunch(
        JNIEnv* env, jclass, jstring jRuntimeDir, jstring jMainClass,
        jobjectArray jJvmArgs, jobjectArray jGameArgs, jstring jGameDir) {
    const std::string runtimeDir = ToStdString(env, jRuntimeDir);
    const std::string mainClass = ToStdString(env, jMainClass);
    const std::string gameDir = ToStdString(env, jGameDir);

    if (runtimeDir.empty() || mainClass.empty() || gameDir.empty()) {
        SetNativeError(env, "nativeLaunch 参数不完整（runtimeDir / mainClass / gameDir）");
        return -1;
    }

    pthread_mutex_lock(&g_launchLock);
    if (g_vm != nullptr) {
        pthread_mutex_unlock(&g_launchLock);
        SetNativeError(env, "本进程内已存在一个 JVM，无法重复启动");
        return -1;
    }

    const std::string libraryPath = FindJvmLibrary(runtimeDir);
    if (libraryPath.empty()) {
        pthread_mutex_unlock(&g_launchLock);
        SetNativeError(env, "运行时里找不到 libjvm.so：" + runtimeDir);
        return -1;
    }

    // 递归加载 libjvm.so 及其依赖：运行时自带 RPATH($ORIGIN)，正常无需额外搜索路径。
    void* jvmLibrary = dlopen(libraryPath.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (jvmLibrary == nullptr) {
        const std::string reason = dlerror() != nullptr ? dlerror() : "dlopen 失败";
        pthread_mutex_unlock(&g_launchLock);
        SetNativeError(env, "加载 libjvm.so 失败（" + libraryPath + "）：" + reason);
        return -1;
    }

    auto createJavaVm = reinterpret_cast<CreateJavaVmFn>(dlsym(jvmLibrary, "JNI_CreateJavaVM"));
    if (createJavaVm == nullptr) {
        const std::string reason = dlerror() != nullptr ? dlerror() : "dlsym 失败";
        pthread_mutex_unlock(&g_launchLock);
        SetNativeError(env, "libjvm.so 未导出 JNI_CreateJavaVM：" + reason);
        return -1;
    }

    std::vector<std::string> jvmArguments = ToStdVector(env, jJvmArgs);
    const std::vector<std::string> gameArguments = ToStdVector(env, jGameArgs);

    // 运行时目录只有 native 侧确切知道，这里兜底补上 -Djava.home（C# 侧没给才补）。
    bool hasJavaHome = false;
    for (const std::string& argument : jvmArguments) {
        if (argument.rfind("-Djava.home=", 0) == 0) {
            hasJavaHome = true;
            break;
        }
    }
    if (!hasJavaHome) {
        jvmArguments.insert(jvmArguments.begin(), "-Djava.home=" + runtimeDir);
    }

    std::vector<JavaVMOption> options(jvmArguments.size());
    for (size_t index = 0; index < jvmArguments.size(); ++index) {
        options[index].optionString = const_cast<char*>(jvmArguments[index].c_str());
        options[index].extraInfo = nullptr;
    }

    JavaVMInitArgs initArgs{};
    initArgs.version = JNI_VERSION_1_6;
    initArgs.nOptions = static_cast<jint>(options.size());
    initArgs.options = options.empty() ? nullptr : options.data();
    // 宽容处理无法识别的选项：版本 JSON 里的 JVM 参数是按桌面平台写的，
    // 拿来在 Android 上跑时不该因为一个未知 -XX 就直接拒绝创建 JVM。
    initArgs.ignoreUnrecognized = JNI_TRUE;

    LOGI("创建 JVM：%s（%zu 个 JVM 参数，%zu 个游戏参数）", libraryPath.c_str(),
         jvmArguments.size(), gameArguments.size());

    jint createResult = createJavaVm(&g_vm, reinterpret_cast<void**>(&g_env), &initArgs);
    if (createResult != JNI_OK || g_vm == nullptr || g_env == nullptr) {
        g_vm = nullptr;
        g_env = nullptr;
        pthread_mutex_unlock(&g_launchLock);
        SetNativeError(env, "JNI_CreateJavaVM 失败，错误码 " + std::to_string(createResult));
        return -1;
    }

    // 缓存回调（加载器状态回传）。
    jclass loaderClass = env->FindClass("hub/code/mchub/runtime/NativeJvmLoader");
    if (loaderClass != nullptr) {
        g_loaderClass = static_cast<jclass>(env->NewGlobalRef(loaderClass));
        g_notifyStarted = env->GetStaticMethodID(g_loaderClass, "notifyGameStarted", "()V");
        g_notifyStopped = env->GetStaticMethodID(g_loaderClass, "notifyGameStopped", "()V");
        g_setNativeError = env->GetStaticMethodID(g_loaderClass, "setNativeError",
                                                 "(Ljava/lang/String;)V");
        env->DeleteLocalRef(loaderClass);
    }
    env->ExceptionClear();

    pthread_mutex_unlock(&g_launchLock);

    NotifyLoader(env, g_notifyStarted);

    // 游戏按当前工作目录解析相对路径（存档、日志等），切换过去。
    if (chdir(gameDir.c_str()) != 0) {
        LOGI("chdir(%s) 失败，继续使用进程原工作目录", gameDir.c_str());
    }

    const std::string descriptor = ToClassDescriptor(mainClass);
    jclass mainClazz = g_env->FindClass(descriptor.c_str());
    if (mainClazz == nullptr) {
        const std::string reason = DescribePendingException(g_env);
        SetNativeError(env, "找不到启动主类 " + mainClass + "：" + reason);
        NotifyLoader(env, g_notifyStopped);
        return 1;
    }

    jmethodID mainMethod = g_env->GetStaticMethodID(mainClazz, "main", "([Ljava/lang/String;)V");
    if (mainMethod == nullptr) {
        const std::string reason = DescribePendingException(g_env);
        SetNativeError(env, "主类缺少 main(String[]) 方法：" + reason);
        NotifyLoader(env, g_notifyStopped);
        return 1;
    }

    jobjectArray arguments = ToJavaStringArray(g_env, gameArguments);
    // 注意：这里会一直阻塞到游戏退出（Minecraft 的 main 里跑的就是整个游戏循环）。
    g_env->CallStaticVoidMethod(mainClazz, mainMethod, arguments);

    const std::string pending = DescribePendingException(g_env);
    if (!pending.empty()) {
        SetNativeError(g_env, "游戏主类抛出异常：" + pending);
        NotifyLoader(g_env, g_notifyStopped);
        return 1;
    }

    LOGI("游戏主类已返回，视为正常退出");
    NotifyLoader(g_env, g_notifyStopped);
    return 0;
}

extern "C" JNIEXPORT void JNICALL
Java_hub_code_mchub_runtime_LauncherBridge_nativeAbort(JNIEnv* env, jclass) {
    if (g_vm == nullptr) {
        return;
    }

    // 调用线程不一定是当初创建 JVM 的那条（UI 线程来点"停止"），
    // 需要先附加到 JVM 才能发 JNI 调用。
    JNIEnv* targetEnv = nullptr;
    bool attached = false;
    const jint status = g_vm->GetEnv(reinterpret_cast<void**>(&targetEnv), JNI_VERSION_1_6);
    if (status == JNI_EDETACHED) {
        // 注意：Android 的 JNI 头里 AttachCurrentThread 取 JNIEnv**（GetEnv 取 void**），
        // 两者签名不同，这里不能共用同一个强转。
        if (g_vm->AttachCurrentThread(&targetEnv, nullptr) != JNI_OK) {
            LOGE("nativeAbort：附加到 JVM 失败");
            return;
        }
        attached = true;
    } else if (status != JNI_OK) {
        LOGE("nativeAbort：GetEnv 失败，错误码 %d", status);
        return;
    }

    LOGI("nativeAbort：请求 System.exit(0)");
    RequestSystemExit(targetEnv);

    if (attached) {
        g_vm->DetachCurrentThread();
    }
    (void) env;
}
