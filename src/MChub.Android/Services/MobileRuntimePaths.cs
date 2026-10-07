namespace MChub.Mobile.Services;

/// <summary>
/// 移动端的路径约定。
///
/// <p>Android 的应用私有目录只能经 Context 获取，不能硬编码；集中放在这里，
/// 避免各页面散落字符串。</p>
/// </summary>
internal static class MobileRuntimePaths
{
    /// <summary>
    /// Java 运行时根目录。
    ///
    /// <p>移动端需要的不是桌面 JDK，而是 Amethyst(AngelAuraMC) / PojavLauncher 系
    /// 的 android-openjdk-build-multiarch 产物（携带 arm64 的 libjvm.so）。
    /// 归档的下载与解压复用 C# 侧 MChub.Core（已支持 tar.xz），解压目标即本目录。</p>
    /// </summary>
    public static string JavaRuntimeDirectory =>
        Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "Runtimes", "Java");

    /// <summary>实例根目录（与桌面端保持同样的 instances 布局约定）。</summary>
    public static string InstancesDirectory =>
        Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "instances");
}
