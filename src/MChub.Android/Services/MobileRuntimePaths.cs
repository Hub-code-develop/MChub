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

    /// <summary>
    /// Java 运行时根目录。
    ///
    /// <p>移动端需要的不是桌面 JDK，而是 Amethyst(AngelAuraMC) / PojavLauncher 系
    /// 的 android-openjdk-build-multiarch 产物（携带 arm64 的 libjvm.so）。
    /// 这类运行时只随其 APK 分发或在 GitHub Actions 产物里（需登录），**没有稳定的公开下载地址**，
    /// 因此由用户在设置页导入归档（.tar.xz / .zip），解压到本目录。</p>
    /// </summary>
    public static string JavaRuntimeDirectory =>
        Path.Combine(AppDataDirectory, "Runtimes", "Java");

    /// <summary>实例根目录（与桌面端保持同样的 instances 布局约定）。</summary>
    public static string InstancesDirectory =>
        Path.Combine(AppDataDirectory, "instances");
}
