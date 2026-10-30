using Avalonia;
using Avalonia.Media;

namespace MChub.Workbench;

/// <summary>
/// 工作台配色与字体的统一取用点。
/// 侧边栏、编辑器内容、输出面板这些视图都是 C# 构建的控件，
/// 没法直接写 <c>{DynamicResource}</c>，统一走这里按当前主题变体解析。
/// </summary>
public static class WorkbenchTheme
{
    public static IBrush Brush(string key, string fallback)
    {
        if (Application.Current is { } app &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) &&
            value is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallback));
    }

    public static IBrush EditorBg => Brush("WbEditorBg", "#1E1E1E");

    public static IBrush SidebarBg => Brush("WbSidebarBg", "#252526");

    public static IBrush Border => Brush("WbBorder", "#2B2B2B");

    public static IBrush BorderStrong => Brush("WbBorderStrong", "#454545");

    public static IBrush ToolbarBg => Brush("WbToolbarBg", "#2D2D30");

    public static IBrush StripeBg => Brush("WbStripeBg", "#2D2D30");

    public static IBrush ToolHeaderBg => Brush("WbToolHeaderBg", "#2D2D30");

    public static IBrush TabStripBg => Brush("WbTabStripBg", "#252526");

    public static IBrush WidgetHover => Brush("WbWidgetHover", "#3E3E42");

    public static IBrush StatusBg => Brush("WbStatusBg", "#252526");

    public static IBrush StatusFg => Brush("WbStatusFg", "#CCCCCC");

    public static IBrush Foreground => Brush("WbFg", "#CCCCCC");

    public static IBrush ForegroundMuted => Brush("WbFgMuted", "#8C8C8C");

    public static IBrush ForegroundFaint => Brush("WbFgFaint", "#6E7681");

    public static IBrush Accent => Brush("WbAccent", "#007ACC");

    public static IBrush SelectionBg => Brush("WbSelectionBg", "#094771");

    public static IBrush HoverBg => Brush("WbHoverBg", "#2A2D2E");

    public static IBrush Danger => Brush("WbDanger", "#F14C4C");

    public static IBrush Success => Brush("WbSuccess", "#3C8527");

    public static FontFamily IconFont
    {
        get
        {
            if (Application.Current is { } app &&
                app.Resources.TryGetResource("WbIconFont", app.ActualThemeVariant, out var value) &&
                value is FontFamily font)
                return font;

            return FontFamily.Default;
        }
    }

    public static FontFamily CodeFont
    {
        get
        {
            if (Application.Current is { } app &&
                app.Resources.TryGetResource("WbCodeFont", app.ActualThemeVariant, out var value) &&
                value is FontFamily font)
                return font;

            return FontFamily.Default;
        }
    }
}
