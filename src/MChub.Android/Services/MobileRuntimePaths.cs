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
}
