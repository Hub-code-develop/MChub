namespace MChub.Mobile.Services;

/// <summary>
/// 移动端的路径约定。
///
/// <p>Android 的应用私有目录只能经 Context 获取，不能硬编码；集中放在这里，
/// 避免各页面散落字符串。</p>
/// </summary>
internal static class MobileRuntimePaths
{
    /// <summary>应用私有数据根目录（Android 上即 context.FilesDir）。</summary>
    public static string AppDataDirectory =>
        Android.App.Application.Context.FilesDir!.AbsolutePath;

    /// <summary>Java 运行时根目录（各主版本按子目录分开放，可并存）。</summary>
    public static string JavaRuntimesRootDirectory =>
        Path.Combine(AppDataDirectory, "Runtimes", "Java");

    /// <summary>
    /// 某个主版本的运行时目录。
    ///
    /// <p>按版本分目录是必须的：MC 1.16 要 Java 8、1.21 要 21、26.x 要 25，
    /// 同一台设备上经常要并存多个运行时；共用一个目录会导致"装了 21 就再也装不上 25"。</p>
    /// </summary>
    public static string JavaRuntimeDirectoryFor(int majorVersion)
        => Path.Combine(JavaRuntimesRootDirectory, majorVersion.ToString());

    /// <summary>实例根目录（与桌面端保持同样的 instances 布局约定）。</summary>
    public static string InstancesDirectory =>
        Path.Combine(AppDataDirectory, "instances");

    /// <summary>运行组件根目录（LWJGL + GL 翻译层）。</summary>
    public static string NativesDirectory =>
        Path.Combine(AppDataDirectory, "Runtimes", "Natives");

    /// <summary>原生库目录（.so，作为 java.library.path / LWJGL librarypath）。</summary>
    public static string NativesLibraryDirectory => Path.Combine(NativesDirectory, "lib");

    /// <summary>移动端 LWJGL jar 目录（启动时前置到 classpath）。</summary>
    public static string NativesJarDirectory => Path.Combine(NativesDirectory, "jars");

    /// <summary>
    /// APK 内预置资产的根目录。
    ///
    /// <p>运行时组件**随 APK 一起分发**（由 .github/workflows/build-android.yml 在打包前
    /// 从本仓库 release 装配），用户装机即用，不需要再联网下载。与之对应：
    /// native 库以 <c>AndroidNativeLibrary</c> 打进 <c>lib/arm64-v8a/</c>，
    /// JRE 与 LWJGL jar 以 <c>AndroidAsset</c> 打进 <c>assets/runtime/</c>。</p>
    /// </summary>
    public const string BundledAssetRoot = "runtime";

    /// <summary>预置 JRE 归档在 assets 里的相对路径。</summary>
    public static string BundledJreAssetPath(int majorVersion)
        => $"{BundledAssetRoot}/jre/{majorVersion}/jre{majorVersion}-android-{MobileRuntimeCatalog.Abi}.tar.xz";

    /// <summary>预置 LWJGL jar 在 assets 里的目录（相对 assets 根）。</summary>
    public static string BundledJwjglAssetDirectory(string lwjglVersion)
        => $"{BundledAssetRoot}/lwjgl/{lwjglVersion}";

    /// <summary>
    /// 系统为本应用解压原生库的目录。
    ///
    /// <p>APK 里 <c>lib/arm64-v8a/*.so</c> 在安装时由系统解压到这里，可直接 dlopen，
    /// 也可以拼进 <c>java.library.path</c> —— 预置方案下无需再往私有目录复制一份。</p>
    /// </summary>
    public static string NativeLibraryDirectory =>
        Android.App.Application.Context.ApplicationInfo!.NativeLibraryDir!;
}
