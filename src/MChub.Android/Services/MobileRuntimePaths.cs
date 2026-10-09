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

    /// <summary>
    /// 预置 LWJGL jar 归档（zip）在 assets 里的路径。
    ///
    /// <p>不能直接把 <c>.jar</c> 放进 assets：.NET Android 会把它们当成
    /// <c>AndroidJavaLibrary</c> 参与 dex，而两套 LWJGL 版本有同名 jar、内容不同 → 报 XA1014。
    /// 这些 jar 只是 JVM classpath 上的条目，不需要进 dex，所以打 zip 由运行时解压。</p>
    /// </summary>
    public static string BundledJwjglArchivePath(string lwjglVersion)
        => $"{BundledAssetRoot}/lwjgl/{lwjglVersion}.zip";

    /// <summary>
    /// 预置 LWJGL 原生库的归档（zip）在 assets 里的路径（按版本分放）。
    ///
    /// <p>必须打 zip，两个原因：① 3.3.3 与 3.4.1 有同名 .so（<c>liblwjgl.so</c> 等），
    /// 放同一目录会互相覆盖；② assets 里直接放 .so 会被 .NET Android 检查 ABI，
    /// 路径里没有 ABI 名就报 XA4301。运行时按实例需要的版本解压到私有目录。</p>
    /// </summary>
    public static string BundledLwjglNativesArchivePath(string lwjglVersion)
        => $"{BundledAssetRoot}/lwjgl-natives/{lwjglVersion}.zip";

    /// <summary>预置 LWJGL 原生库归档所在的 assets 目录。</summary>
    public const string BundledLwjglNativesAssetRoot = "runtime/lwjgl-natives";

    /// <summary>
    /// 预置原生库归档（zip）在 assets 里的路径。
    ///
    /// <p><b>为什么原生库不放进 APK 的 <c>lib/&lt;abi&gt;/</c></b>：平台会对那里的每个 <c>.so</c> 做
    /// 16 KB 页对齐检查，不达标就让 App 强制跑在「页面大小兼容模式」并在启动时弹警告
    /// （Android 15+ 的要求）。而这批库多数来自上游**预编译**产物（AngelAuraMC / Zalith 的 AAR、
    /// JRE 自带的 libawt*），ELF 段对齐是 4 KB，我们无法重链接。实测：放进 <c>lib/</c> 就弹窗，
    /// 改走「assets + 私有目录 + dlopen」就不弹。</p>
    ///
    /// <p>所以运行时解压到 <see cref="NativesLibraryDirectory"/>，再由
    /// <c>JREUtils.loadNativeLayer</c>（需要 JNI 注册的那几个）与 <c>PojavRuntimeSupport</c>
    /// （GL 翻译层等，按绝对路径 dlopen）装载。</p>
    /// </summary>
    public static string BundledNativesArchivePath
        => $"{BundledAssetRoot}/natives.zip";

    /// <summary>某个 LWJGL 版本的原生库解压目标目录（私有目录下按版本分开）。</summary>
    public static string LwjglNativesDirectoryFor(string lwjglVersion)
        => Path.Combine(NativesDirectory, "lwjgl-natives", lwjglVersion);

    /// <summary>
    /// 系统为本应用解压原生库的目录（=<c>ApplicationInfo.nativeLibraryDir</c>）。
    ///
    /// <p><b>现在这里只有我们自己编的 <c>libmchubjvm.so</c></b> —— 移动端运行时那批
    /// 上游原生库已经改走 assets（见 <see cref="BundledNativesArchivePath"/>），
    /// 它们解在 <see cref="NativesLibraryDirectory"/>。</p>
    /// </summary>
    public static string NativeLibraryDirectory =>
        Android.App.Application.Context.ApplicationInfo!.NativeLibraryDir!;
}
