using MChub.Core.Const;
using MChub.Workbench;
using TioUi.Shared;

namespace MChub;

public partial class App
{
    private void ThemeMirage_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.Mirage;
    }

    private void ThemeDark_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.Dark;
    }

    private void ThemeLight_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.Light;
    }

    private void ThemeDefault_OnClick(object? sender, EventArgs e)
    {
        Data.ConfigEntry.Theme = Theme.System;
    }

    /// <summary>原生菜单里的「设置」：走命令系统，不再直接 new 页面。</summary>
    private void OpenSetting_OnClick(object? sender, EventArgs e)
    {
        WorkbenchServices.Commands.Execute("workbench.openSettings");
        MainWindow?.Activate();
    }
}
