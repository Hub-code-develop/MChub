// mchubjava —— MChub 移动端自带的「java 命令」。
//
// 为什么需要它：
//   Forge / NeoForge / OptiFine 的安装依赖一串安装处理器（installertools、FART、SpecialSource…），
//   MinecraftLaunch 是以 `Process.Start(javaPath, ["-cp", cp, mainClass, ...])` 的方式
//   去调用一个**外部 java 可执行文件**的。Android 上没有现成的 java 可执行文件，而应用私有目录
//   里的文件自 targetSdk 29 起不允许 execve（W^X 策略）——唯一稳妥的“自带可执行文件”位置是
//   APK 的 lib/<abi>/：安装时由系统解到 nativeLibraryDir，那里既可执行、又受系统保护。
//   所以本程序编成 PIE 可执行文件，并且**命名为 libmchubjava.so** 随 APK 打进 lib/。
//
//   注意：它不参与 .NET Android 的启动期 JNI 预加载（csproj 里用 AndroidNativeLibraryNoJniPreload），
//   否则 linker 会把它当动态库 dlopen，直接报「不是共享库」。
//
// 用法（java 命令行里我们用到的那部分）：
//   libmchubjava.so [-Dk=v | -Xxx]... -cp <classpath> <mainClass> [程序参数...]
//
// 环境变量：
//   MCHUB_JAVA_HOME   JRE 根目录（等价于 java.home）。缺省时按包名退化为扫描
//                     /data/data/<pkg>/files/Runtimes/Java/<n>，取最高的可用版本。
//
// 退出码：0 = 主类正常返回；1 = 启动失败或主类抛异常（原因写 stderr，父进程会收进安装日志）。

#include <jni.h>

#include <android/log.h>
#include <dlfcn.h>
#include <dirent.h>
#include <sys/stat.h>

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#define LOG_TAG "mchubjava"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

namespace {

using CreateJavaVmFn = jint (*)(JavaVM**, void**, void*);

/// 运行时里 libjvm.so 的候选相对路径（顺序与 LauncherBridge / mchubjvm 保持一致）。
const char* const kJvmLibraryCandidates[] = {
        "lib/server/libjvm.so",
        "lib/aarch64/server/libjvm.so",
        "lib/arm64/server/libjvm.so",
};

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

bool IsJvmHome(const std::string& home) {
    for (const char* candidate : kJvmLibraryCandidates) {
        struct stat info {};
        if (stat((home + "/" + candidate).c_str(), &info) == 0) {
            return true;
        }
    }
    return false;
}

std::string FindJvmLibrary(const std::string& home) {
    for (const char* candidate : kJvmLibraryCandidates) {
        const std::string path = home + "/" + candidate;
        struct stat info {};
        if (stat(path.c_str(), &info) == 0) {
            return path;
        }
    }
    return {};
}

/// 从 /proc/self/cmdline 取包名（Android 上应用进程的 cmdline 就是包名）。
std::string ReadPackageName() {
    FILE* file = fopen("/proc/self/cmdline", "rb");
    if (file == nullptr) {
        return {};
    }
    char buffer[256] = {0};
    const size_t count = fread(buffer, 1, sizeof(buffer) - 1, file);
    fclose(file);
    if (count == 0) {
        return {};
    }
    return std::string(buffer);
}

/// 没给 MCHUB_JAVA_HOME 时的兜底：按包名找应用私有目录下版本号最大的可用运行时。
std::string GuessJavaHome() {
    const std::string package = ReadPackageName();
    if (package.empty()) {
        return {};
    }

    const char* const roots[] = {"/data/data/", "/data/user/0/"};
    std::vector<std::string> candidates;
    for (const char* root : roots) {
        const std::string directory = std::string(root) + package + "/files/Runtimes/Java";
        DIR* handle = opendir(directory.c_str());
        if (handle == nullptr) {
            continue;
        }
        while (struct dirent* entry = readdir(handle)) {
            const std::string name = entry->d_name;
            if (name == "." || name == ".." || name.empty() || name[0] < '0' || name[0] > '9') {
                continue;
            }
            candidates.push_back(directory + "/" + name);
        }
        closedir(handle);
    }

    // 版本号最大的优先（字符串比较对 "8"/"17"/"21"/"25" 也成立：位数多的更大）。
    std::sort(candidates.begin(), candidates.end(), std::greater<std::string>());
    for (const std::string& candidate : candidates) {
        if (IsJvmHome(candidate)) {
            LOGI("按包名推断出 java.home：%s", candidate.c_str());
            return candidate;
        }
    }
    return {};
}

/// 把当前挂起的 Java 异常转成一行可读文本（取后清除，避免污染后续 JNI 调用）。
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
        env->DeleteLocalRef(throwableClass);
    }
    env->DeleteLocalRef(throwable);
    return description;
}

void PrintUsage() {
    fprintf(stderr,
            "用法: libmchubjava.so [-Dk=v|-Xxx]... -cp <classpath> <mainClass> [args...]\n"
            "环境变量: MCHUB_JAVA_HOME=<JRE 根目录>\n");
}

}  // namespace

int main(int argc, char** argv) {
    std::vector<std::string> jvmOptions;
    std::string classPath;
    std::string mainClass;
    std::vector<std::string> programArguments;

    for (int index = 1; index < argc; ++index) {
        const std::string argument = argv[index];

        if (!mainClass.empty()) {
            programArguments.push_back(argument);
            continue;
        }

        if (argument == "-cp" || argument == "-classpath" || argument == "--class-path") {
            if (index + 1 < argc) {
                classPath = argv[++index];
            }
            continue;
        }

        // 主类之前的 -D / -X / -XX 都是 JVM 选项，原样透传。
        if (argument.rfind("-D", 0) == 0 || argument.rfind("-X", 0) == 0) {
            jvmOptions.push_back(argument);
            continue;
        }

        // 只在 java 命令行里有意义、对 JNI 创建 JVM 无意义的开关，忽略掉。
        if (argument == "-client" || argument == "-server" || argument == "-version" ||
            argument == "--version" || argument == "-ea" || argument == "-enableassertions") {
            continue;
        }

        mainClass = argument;
    }

    if (mainClass.empty()) {
        PrintUsage();
        return 1;
    }

    std::string javaHome = getenv("MCHUB_JAVA_HOME") != nullptr ? getenv("MCHUB_JAVA_HOME") : "";
    if (javaHome.empty() || !IsJvmHome(javaHome)) {
        const std::string guessed = GuessJavaHome();
        if (guessed.empty()) {
            fprintf(stderr, "找不到可用的 Java 运行时（MCHUB_JAVA_HOME=%s）\n", javaHome.c_str());
            LOGE("找不到可用的 Java 运行时（MCHUB_JAVA_HOME=%s）", javaHome.c_str());
            return 1;
        }
        javaHome = guessed;
    }

    const std::string libraryPath = FindJvmLibrary(javaHome);
    if (libraryPath.empty()) {
        fprintf(stderr, "运行时里找不到 libjvm.so：%s\n", javaHome.c_str());
        return 1;
    }

    // 递归加载 libjvm.so 及其依赖：运行时自带 RPATH($ORIGIN)，正常无需额外搜索路径。
    void* jvmLibrary = dlopen(libraryPath.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (jvmLibrary == nullptr) {
        const std::string reason = dlerror() != nullptr ? dlerror() : "dlopen 失败";
        fprintf(stderr, "加载 libjvm.so 失败（%s）：%s\n", libraryPath.c_str(), reason.c_str());
        return 1;
    }

    auto createJavaVm = reinterpret_cast<CreateJavaVmFn>(dlsym(jvmLibrary, "JNI_CreateJavaVM"));
    if (createJavaVm == nullptr) {
        fprintf(stderr, "libjvm.so 未导出 JNI_CreateJavaVM\n");
        return 1;
    }

    // java.home 放最前：JVM 依赖它定位 lib/java、lib/nio 等自带库。
    std::vector<std::string> options;
    options.push_back("-Djava.home=" + javaHome);
    if (!classPath.empty()) {
        options.push_back("-Djava.class.path=" + classPath);
    }
    options.insert(options.end(), jvmOptions.begin(), jvmOptions.end());

    std::vector<JavaVMOption> vmOptions(options.size());
    for (size_t index = 0; index < options.size(); ++index) {
        vmOptions[index].optionString = const_cast<char*>(options[index].c_str());
        vmOptions[index].extraInfo = nullptr;
    }

    JavaVMInitArgs initArgs{};
    initArgs.version = JNI_VERSION_1_6;
    initArgs.nOptions = static_cast<jint>(vmOptions.size());
    initArgs.options = vmOptions.empty() ? nullptr : vmOptions.data();
    initArgs.ignoreUnrecognized = JNI_TRUE;

    JavaVM* vm = nullptr;
    JNIEnv* env = nullptr;
    LOGI("创建 JVM：java.home=%s，main=%s，%zu 个程序参数", javaHome.c_str(), mainClass.c_str(),
         programArguments.size());

    const jint createResult = createJavaVm(&vm, reinterpret_cast<void**>(&env), &initArgs);
    if (createResult != JNI_OK || vm == nullptr || env == nullptr) {
        fprintf(stderr, "JNI_CreateJavaVM 失败，错误码 %d\n", createResult);
        return 1;
    }

    int exitCode = 0;
    const std::string descriptor = ToClassDescriptor(mainClass);
    jclass mainClazz = env->FindClass(descriptor.c_str());
    if (mainClazz == nullptr) {
        const std::string reason = DescribePendingException(env);
        fprintf(stderr, "找不到主类 %s：%s\n", mainClass.c_str(), reason.c_str());
        exitCode = 1;
    } else {
        jmethodID mainMethod = env->GetStaticMethodID(mainClazz, "main", "([Ljava/lang/String;)V");
        if (mainMethod == nullptr) {
            const std::string reason = DescribePendingException(env);
            fprintf(stderr, "主类缺少 main(String[]) 方法：%s\n", reason.c_str());
            exitCode = 1;
        } else {
            jclass stringClass = env->FindClass("java/lang/String");
            jobjectArray arguments = env->NewObjectArray(
                    static_cast<jsize>(programArguments.size()), stringClass, nullptr);
            for (size_t index = 0; index < programArguments.size(); ++index) {
                jstring element = env->NewStringUTF(programArguments[index].c_str());
                env->SetObjectArrayElement(arguments, static_cast<jsize>(index), element);
                env->DeleteLocalRef(element);
            }
            env->DeleteLocalRef(stringClass);

            env->CallStaticVoidMethod(mainClazz, mainMethod, arguments);

            const std::string pending = DescribePendingException(env);
            if (!pending.empty()) {
                fprintf(stderr, "主类抛出异常：%s\n", pending.c_str());
                exitCode = 1;
            }
        }
    }

    // 与 java 启动器一致：等非守护线程跑完再退出（安装处理器会起线程写输出）。
    if (vm->DestroyJavaVM() != JNI_OK) {
        LOGI("DestroyJavaVM 未正常返回，直接退出");
    }

    fflush(nullptr);
    return exitCode;
}
