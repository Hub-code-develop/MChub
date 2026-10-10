using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using MChub.Localization;
using MChub.Mobile.Views.Pages;
// Android 隐式全局 using 引入了 Android.Widget，Button 会与 Avalonia.Controls.Button 冲突。
using Button = Avalonia.Controls.Button;

namespace MChub.Mobile.Views;

/// <summary>
/// 移动端外壳。布局参照 Zalith Launcher 2（ZL2）：顶部工具栏（左标题 + 右侧图标功能项）+ 内容区。
/// </summary>
public partial class MobileShellView : UserControl
{
    private const string AccentBrushKey = "AccentFillColorDefaultBrush";
    private const string SecondaryTextBrushKey = "TextFillColorSecondaryBrush";

    private NavItem _current = NavItem.Home;
    private MobileLauncherPage? _launcherPage;
    private MobileNewInstancePage? _newInstancePage;
    private MobileSettingsPage? _settingsPage;

    public MobileShellView()
    {
        InitializeComponent();
        ShowPage(NavItem.Home);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 构造期控件还没接进视觉树，主题资源取不到；挂载后再刷一次 rail 的选中态配色。
        ApplyRailState();
    }

    private void HomeNav_OnClick(object? sender, RoutedEventArgs e) => ShowPage(NavItem.Home);

    private void NewInstanceNav_OnClick(object? sender, RoutedEventArgs e) => ShowPage(NavItem.NewInstance);

    private void SettingsNav_OnClick(object? sender, RoutedEventArgs e) => ShowPage(NavItem.Settings);

    private void ShowPage(NavItem item)
    {
        _current = item;

        switch (item)
        {
            case NavItem.Home:
                _launcherPage ??= CreateLauncherPage();
                // 每次回到主页都重扫一遍：刚装完的实例要立刻出现。
                _launcherPage.Reload();
                PageHost.Content = _launcherPage;
                // ZL2 在启动器主屏幕上显示的是启动器标识（而不是「主页」这种页面名）。
                PageTitle.Text = "MChub";
                break;
            case NavItem.NewInstance:
                _newInstancePage ??= CreateNewInstancePage();
                PageHost.Content = _newInstancePage;
                PageTitle.Text = MobileLanguageManager.Instance.mobile_navNewInstance.CurrentValue();
                break;
            default:
                _settingsPage ??= new MobileSettingsPage();
                PageHost.Content = _settingsPage;
                PageTitle.Text = MobileLanguageManager.Instance.mobile_navSettings.CurrentValue();
                break;
        }

        ApplyRailState();
    }

    private MobileLauncherPage CreateLauncherPage()
    {
        var page = new MobileLauncherPage();
        page.NewInstanceRequested += (_, _) => ShowPage(NavItem.NewInstance);
        return page;
    }

    private MobileNewInstancePage CreateNewInstancePage()
    {
        var page = new MobileNewInstancePage();
        // 装完就回主页，实例列表此时已经是新的了。
        page.Installed += (_, _) => ShowPage(NavItem.Home);
        return page;
    }

    /// <summary>
    /// ZL2 的功能项：选中时展开文字并染上强调色，未选中只留图标。
    /// </summary>
    private void ApplyRailState()
    {
        ApplyRailState(HomeNavButton, HomeNavIcon, HomeNavLabel, _current == NavItem.Home);
        ApplyRailState(NewInstanceNavButton, NewInstanceNavIcon, NewInstanceNavLabel,
            _current == NavItem.NewInstance);
        ApplyRailState(SettingsNavButton, SettingsNavIcon, SettingsNavLabel, _current == NavItem.Settings);
    }

    private void ApplyRailState(Button button, FASymbolIcon icon, TextBlock label, bool selected)
    {
        if (selected)
        {
            if (!button.Classes.Contains("selected"))
                button.Classes.Add("selected");
        }
        else
        {
            button.Classes.Remove("selected");
        }

        label.IsVisible = selected;

        if (!this.TryFindResource(selected ? AccentBrushKey : SecondaryTextBrushKey, out var value) ||
            value is not IBrush brush)
            return;

        icon.Foreground = brush;
        label.Foreground = brush;
    }

    private enum NavItem
    {
        Home,
        NewInstance,
        Settings
    }
}
