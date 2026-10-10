using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MinecraftLaunch.Base.Utilities;

public static class EnvironmentUtil {
    private const ushort PE_SIGNATURE = 23117;
    private const ushort IMAGE_FILE_MACHINE_IA64 = 267;
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 523;
    private const uint PE_OPTIONAL_HEADER_SIGNATURE = 17744;

    public static string Arch
        => Environment.Is64BitOperatingSystem ? "64" : "32";

    public static bool IsMac
        => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsLinux
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsWindow
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// 是否运行在 Android 上。
    ///
    /// <p><b>必须单独判断</b>：Android 的 RID 是 <c>android-arm64</c>，OS 报告为 android，
    /// 因此 <c>RuntimeInformation.IsOSPlatform(OSPlatform.Linux)</c> 返回 <b>false</b>。
    /// 不判它就落到 <see cref="GetPlatformName"/> 末尾的 throw —— 在设备上表现为安装到
    /// 「下载库」这一步直接失败：<c>NotSupportedException: Specified method is not supported.</c>
    /// （MinecraftEntry.IsLibraryEnabled → GetPlatformName，实测踩过）。</p>
    /// </summary>
    public static bool IsAndroid
        => OperatingSystem.IsAndroid();

    public static string GetPlatformName() {
        if (IsAndroid) {
            // 按 linux 归类：MC 的库规则（rules.os.name）与原生分类器只有 windows/linux/osx 三档，
            // 没有 android。取 linux 变体最接近 —— 而且运行时真正用的是移动端补丁版 LWJGL
            // （见 MobileLaunchPlanBuilder），这里只影响「下载哪些依赖 / 按 .so 解压」。
            return "linux";
        }

        if (IsMac) {
            return "osx";
        } else if (IsLinux) {
            return "linux";
        } else if (IsWindow) {
            return "windows";
        }

        throw new NotSupportedException();
    }

    [SupportedOSPlatform("Windows")]
    public static bool Is64BitJavaForWindow(string path) {
        ushort architecture = 0;

        try {
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var binaryReader = new BinaryReader(fileStream);

            if (binaryReader.ReadUInt16() == PE_SIGNATURE) {
                fileStream.Seek(0x3A, SeekOrigin.Current);
                fileStream.Seek(binaryReader.ReadUInt32(), SeekOrigin.Begin);

                if (binaryReader.ReadUInt32() == PE_OPTIONAL_HEADER_SIGNATURE) {
                    fileStream.Seek(20, SeekOrigin.Current);
                    architecture = binaryReader.ReadUInt16();
                }
            }
        } catch (Exception) { }

        return architecture is IMAGE_FILE_MACHINE_AMD64 or IMAGE_FILE_MACHINE_IA64;
    }
}