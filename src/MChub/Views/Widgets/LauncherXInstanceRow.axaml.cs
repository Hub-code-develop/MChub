using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;
using MChub.Core.Module;
using MChub.Views.Pages;
using TioUi.Common.Extensions;

namespace MChub.Views.Widgets;

/// <summary>LauncherX 风格的实例行：图标 / 名称 / 版本徽章 / 时长 / 齿轮 + 启动。</summary>
public partial class LauncherXInstanceRow : UserControl
{
    public LauncherXInstanceRow()
    {
        InitializeComponent();
    }

    private void Row_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null))
            return;

        if (DataContext is MinecraftInstance instance &&
            TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(instance, topLevel);
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MinecraftInstance instance &&
            TopLevel.GetTopLevel(this) is { } topLevel)
            InstanceDetailPage.Open(instance, topLevel);
    }

    private void Launch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance instance)
            return;

        _ = MinecraftLaunchService.LaunchAsync(instance, TopLevel.GetTopLevel(this),
            MinecraftLaunchOptionsFactory.Create(instance,
                logSession => MinecraftLogPage.Open(logSession, this.GetTopLevel())));
    }

    private async void CreateShortcut_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance instance)
            return;

        await DesktopShortcutUi.CreateAsync(TopLevel.GetTopLevel(this),
            () => DesktopShortcutService.CreateAsync(instance));
    }

    private void Favorite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MinecraftInstance { Config: not null } instance)
            return;

        instance.Config.IsFavorite = !instance.Config.IsFavorite;
        instance.SaveConfig();
    }

    private void Block_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MinecraftInstance instance)
            BlockListService.Instance.ToggleInstanceBlock(instance);
    }
}
