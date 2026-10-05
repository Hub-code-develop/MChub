using System.Runtime.InteropServices;

namespace MChub.Platform.Windows;

/// <summary>
/// Windows 上窗口客户区延伸进窗口装饰（ExtendClientAreaToDecorationsHint）后，右上角依旧由
/// 系统绘制最小化 / 最大化 / 关闭按钮，且它们会盖在窗口内容之上。
/// Avalonia 的 WindowDecorationMargin 在 Win32 只给出标题栏高度（左右恒为 0，见
/// Avalonia.Win32 的 UpdateExtendMargins），拿不到这排按钮的宽度，所以这里直接向 DWM 要
/// 按钮区矩形；取不到时再按系统度量估算。
/// </summary>
internal static class WindowChromeMetrics
{
    /// <summary>DWMWA_CAPTION_BUTTON_BOUNDS：标题栏按钮区矩形。</summary>
    private const int DwmwaCaptionButtonBounds = 5;

    /// <summary>SM_CXSIZE：标题栏按钮宽度。</summary>
    private const int SmCxSize = 30;

    /// <summary>SM_CXPADDEDBORDER：标题栏按钮外侧的附加留白。</summary>
    private const int SmCxPaddedBorder = 92;

    /// <summary>系统按钮区与自绘标题栏内容之间保留的视觉间隙（逻辑像素）。</summary>
    public const double Gap = 2;

    /// <summary>
    /// 取三个系统按钮（最小化 / 最大化 / 关闭）占用的总宽度，单位为物理像素；取不到返回 0。
    /// </summary>
    public static int GetCaptionButtonsWidth(IntPtr windowHandle, uint dpi)
    {
        if (windowHandle == IntPtr.Zero) return 0;

        try
        {
            if (DwmGetWindowAttribute(windowHandle, DwmwaCaptionButtonBounds, out var bounds,
                    Marshal.SizeOf<NativeRect>()) == 0)
            {
                var width = bounds.Right - bounds.Left;
                if (width > 0) return width;
            }
        }
        catch (DllNotFoundException)
        {
            // 没有 dwmapi（理论上的非 Windows 宿主），退回系统度量
        }
        catch (EntryPointNotFoundException)
        {
        }

        var button = SystemMetric(SmCxSize, dpi);
        var padding = SystemMetric(SmCxPaddedBorder, dpi);
        var estimated = 3 * (button + padding);
        return estimated > 0 ? estimated : 3 * 46;
    }

    /// <summary>优先按传入 DPI 取值，旧系统上退回不区分 DPI 的 GetSystemMetrics。</summary>
    private static int SystemMetric(int index, uint dpi)
    {
        if (dpi != 0)
            try
            {
                return GetSystemMetricsForDpi(index, dpi);
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 10 1607 以下没有该 API
            }

        return GetSystemMetrics(index);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out NativeRect value, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
