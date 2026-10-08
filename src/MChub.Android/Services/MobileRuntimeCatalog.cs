namespace MChub.Mobile.Services;

/// <summary>一个可下载的原生库归档（AAR：内部按 <c>&lt;abi&gt;/*.so</c> 组织）。</summary>
internal sealed record MobileNativeArchive(string Name, IReadOnlyList<string> Urls);

/// <summary>一个可下载的散装文件（仓库里的单个文件）。</summary>
internal sealed record MobileNativeFile(string Name, IReadOnlyList<string> Urls);

/// <summary>
/// 移动端运行组件的下载源目录。
///
/// <p><b>为什么这些地址是"公开可取"的</b>：Android 上跑 MC Java 版需要的运行时与原生库，
/// 上游（Amethyst / Zalith Launcher 2）**都是打包进自己 APK 的**，所以它们没有"下载按钮"；
/// 但它们的仓库文件本身就是公开可取的（raw 直链 + release 资产）。本项目不依赖登录态的
/// CI 产物，也不用用户手动找包。</p>
///
/// <table>
/// <tr><th>组件</th><th>来源</th><th>许可</th></tr>
/// <tr><td>JRE 8 / 17 / 21 / 25（arm64）</td><td>AngelAuraMC/angelauramc-openjdk-build 公开 release</td><td>GPL-2.0-with-classpath</td></tr>
/// <tr><td>LWJGL 原生库 + OpenAL + ANGLE + SDL</td><td>Amethyst 仓库内的 prebuilt AAR</td><td>LGPL-3.0 / BSD</td></tr>
/// <tr><td>LWJGL Java 侧 jar（移动端补丁版）</td><td>Zalith Launcher 2 仓库 assets/app_runtime/lwjgl</td><td>GPL-3.0</td></tr>
/// <tr><td>GL4ES / OSMesa / shaderconv 等翻译层</td><td>Zalith Launcher 2 仓库 jniLibs</td><td>GPL-3.0</td></tr>
/// </table>
///
/// <p><b>已知缺口（如实说明）</b>：GLFW stub（<c>libglfw.so</c>）上游没有公开的预编译产物，
/// 只有源码（Amethyst 的 <c>jre_lwjgl3glfw/</c>，需 NDK 编译）。若某个版本的 LWJGL 走 GLFW 而非 SDL，
/// 缺它会在窗口初始化阶段失败 —— 届时的报错会原样呈现，不会假装启动成功。
/// 需要用 Actions 自行编译后发布到本仓库 release，再把地址填进
/// <see cref="GetSupplementaryLibraries"/>。</p>
/// </summary>
internal static class MobileRuntimeCatalog
{
    /// <summary>本项目只发 arm64（csproj 的 RuntimeIdentifiers=android-arm64）。</summary>
    public const string Abi = "arm64-v8a";

    /// <summary>LWJGL 版本：与移动端补丁 jar 的版本号一一对应。</summary>
    public const string LwjglVersion = "3.3.3";

    private const string AmethystRawBase =
        "https://raw.githubusercontent.com/AngelAuraMC/Amethyst-Android/v3_openjdk";

    private const string ZalithRawBase =
        "https://raw.githubusercontent.com/ZalithLauncher/ZalithLauncher2/main";

    private const string JreReleaseBase =
        "https://github.com/AngelAuraMC/angelauramc-openjdk-build/releases/download";

    /// <summary>本项目支持的 JRE 主版本（AngelAuraMC 的公开资产正好覆盖这四个）。</summary>
    private static readonly int[] SupportedRuntimeMajors = [8, 17, 21, 25];

    /// <summary>
    /// 把"实例要求的 Java 版本"归一到可下载的版本。
    ///
    /// <p>MC 的 Java 需求是"最低版本"语义：取"不小于要求的最小可下载版本"，
    /// 免得把要求 17 的实例喂给 JRE 8。</p>
    /// </summary>
    public static int NormalizeRuntimeMajor(int requiredMajorVersion)
    {
        foreach (var candidate in SupportedRuntimeMajors)
        {
            if (candidate >= requiredMajorVersion)
                return candidate;
        }

        return SupportedRuntimeMajors[^1];
    }

    /// <summary>JRE 归档的候选直链（arm64）。</summary>
    public static IReadOnlyList<string> GetRuntimeUrls(int majorVersion)
        => [$"{JreReleaseBase}/download_jre{majorVersion}/jre{majorVersion}-android-{Abi}.tar.xz"];

    /// <summary>
    /// 原生库归档（AAR）。Amethyst 的 AAR 内部布局是
    /// <c>assets/components/&lt;name&gt;/&lt;abi&gt;/*.so</c>，安装时按 <see cref="Abi"/> 过滤。
    /// </summary>
    public static IReadOnlyList<MobileNativeArchive> GetNativeArchives()
        =>
        [
            new("LWJGL 原生库",
            [
                $"{AmethystRawBase}/app_pojavlauncher/libs/lwjgl-{LwjglVersion}-natives-release.aar"
            ]),
            new("OpenAL",
            [
                $"{AmethystRawBase}/app_pojavlauncher/libs/openal-soft-release.aar"
            ]),
            new("ANGLE",
            [
                $"{AmethystRawBase}/app_pojavlauncher/libs/angle-release.aar"
            ]),
            new("SDL",
            [
                $"{AmethystRawBase}/app_pojavlauncher/libs/SDL-release.aar"
            ])
        ];

    /// <summary>
    /// LWJGL Java 侧 jar 的清单（移动端补丁版）。
    ///
    /// <p><b>清单与上游实际文件一一对应</b>（按 Zalith Launcher 2 仓库
    /// <c>assets/app_runtime/lwjgl/3.3.3/</c> 的真实目录校正）：移动端**没有**
    /// <c>lwjgl-glfw.jar</c> / <c>lwjgl-opengl.jar</c> —— opengl 等模块被合进了
    /// <c>lwjgl-&lt;ver&gt;-merged-modules.jar</c>，窗口层由启动器 native 侧顶替。
    /// 凭记忆写清单会 404。</p>
    /// </summary>
    public static IReadOnlyList<string> GetLwjglJarNames()
        =>
        [
            "lwjgl-3.3.3-merged-modules.jar",
            "lwjgl.jar",
            "lwjgl-lwjglx.jar",
            "lwjgl-openal.jar",
            "lwjgl-stb.jar",
            "lwjgl-tinyfd.jar",
            "lwjgl-freetype.jar",
            "lwjgl-nanovg.jar",
            "lwjgl-shaderc.jar",
            "lwjgl-spvc.jar",
            "lwjgl-vma.jar",
            "lwjgl-vulkan.jar"
        ];

    public static IReadOnlyList<string> GetLwjglJarUrls(string jarName)
        =>
        [
            $"{ZalithRawBase}/ZalithLauncher/src/main/assets/app_runtime/lwjgl/{LwjglVersion}/{jarName}",
            $"{AmethystRawBase}/app_pojavlauncher/src/main/assets/components/lwjgl3/{LwjglVersion}/{jarName}"
        ];

    /// <summary>
    /// GL 翻译层与辅助原生库（arm64）。GL4ES / OSMesa / shaderconv 是把桌面 OpenGL 调用
    /// 翻译到 Android GLES 的关键；jnidispatch 是 JNA；unpack200 供老版本 jar 解包。
    /// </summary>
    public static IReadOnlyList<string> GetGraphicsLibraryNames()
        =>
        [
            "libgl4es_114.so",
            "libshaderconv.so",
            "libOSMesa_8.so",
            "libOSMesa_2121.so",
            "libOSMesa_2300d.so",
            "libjnidispatch.so",
            "libunpack200.so",
            "libvirgl_test_server.so",
            "libvulkan_freedreno.so",
            "libVkLayer_khronos_timeline_semaphore.so"
        ];

    public static IReadOnlyList<string> GetGraphicsLibraryUrls(string libraryName)
        => [$"{ZalithRawBase}/ZalithLauncher/src/main/jniLibs/{Abi}/{libraryName}"];

    /// <summary>
    /// 上游没有公开产物的补充库（当前为空）。
    ///
    /// <p>GLFW stub 需要自行编译：用 Actions 跑 Amethyst 的 <c>jre_lwjgl3glfw/</c> 源码产出
    /// <c>libglfw.so</c>，打包成 <c>mchub-mobile-glfw-{abi}.tar.xz</c> 发到本仓库 release，
    /// 再把地址填到这里即可被安装器自动取用。</p>
    /// </summary>
    public static IReadOnlyList<MobileNativeFile> GetSupplementaryLibraries() => [];
}
