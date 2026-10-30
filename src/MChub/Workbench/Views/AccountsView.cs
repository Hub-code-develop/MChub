using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Module.Initialize;

namespace MChub.Workbench.Views;

/// <summary>
/// 账户侧边栏：列出所有账户，点一下就切成当前账户。
/// 登录走原来的 <c>LoginWindow</c>（设备码流程），不重写认证。
/// </summary>
public sealed class AccountsView : UserControl
{
    private readonly StackPanel _list = new() { Spacing = 1 };

    /// <summary>账户集合或当前账户变了。</summary>
    public event Action? AccountsChanged;

    public AccountsView()
    {
        var addButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\ue645",
                        FontFamily = WorkbenchTheme.IconFont,
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = WorkbenchTheme.ForegroundMuted
                    },
                    new TextBlock
                    {
                        Text = "添加账户",
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            Height = 26,
            Margin = new Thickness(8, 6, 8, 0),
            Padding = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = WorkbenchTheme.ForegroundMuted,
            CornerRadius = new CornerRadius(0)
        };
        addButton.Click += (_, _) => WorkbenchServices.Commands.Execute("mchub.accounts.add");

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        var grid = (Grid)Content;
        grid.Children.Add(addButton);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _list,
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        Data.ConfigEntry.MinecraftAccounts.CollectionChanged += (_, _) => Rebuild();
        AttachedToVisualTree += (_, _) => Rebuild();
    }

    public void Rebuild()
    {
        _list.Children.Clear();

        if (Data.ConfigEntry.MinecraftAccounts.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "还没有账户。点上面的「添加账户」登录微软账号，或添加离线账户。",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(14, 8),
                Foreground = WorkbenchTheme.ForegroundFaint
            });
            return;
        }

        foreach (var account in Data.ConfigEntry.MinecraftAccounts)
            _list.Children.Add(BuildRow(account));
    }

    private Control BuildRow(MinecraftAccount account)
    {
        var isCurrent = ReferenceEquals(account, Data.ConfigEntry.UsingMinecraftMinecraftAccount);

        var head = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(2),
            Background = WorkbenchTheme.Brush("WbPanelBg", "#1E1E1E"),
            ClipToBounds = true,
            Child = BuildHead(account)
        };

        var name = new TextBlock
        {
            Text = account.Name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = WorkbenchTheme.Foreground
        };

        var type = new TextBlock
        {
            Text = account.ShortType,
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkbenchTheme.ForegroundFaint
        };

        var text = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { name, type }
        };

        var check = new TextBlock
        {
            Text = "\ue62d",
            FontFamily = WorkbenchTheme.IconFont,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = isCurrent,
            Foreground = WorkbenchTheme.Accent
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        head.Margin = new Thickness(0, 0, 9, 0);
        Grid.SetColumn(head, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(check, 2);
        row.Children.Add(head);
        row.Children.Add(text);
        row.Children.Add(check);

        var button = new Button
        {
            Content = row,
            Classes = { "wb-flat" },
            Height = 30,
            Padding = new Thickness(14, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = isCurrent ? WorkbenchTheme.SelectionBg : Brushes.Transparent,
            Margin = new Thickness(0)
        };

        button.Click += (_, _) => SwitchTo(account);

        var flyout = new MenuFlyout();
        var useItem = new MenuItem { Header = "设为当前账户" };
        useItem.Click += (_, _) => SwitchTo(account);
        flyout.Items.Add(useItem);

        var removeItem = new MenuItem { Header = "移除账户" };
        removeItem.Click += (_, _) => Remove(account);
        flyout.Items.Add(removeItem);
        button.ContextFlyout = flyout;

        return button;
    }

    private static Control BuildHead(MinecraftAccount account)
    {
        try
        {
            Bitmap bitmap = account.Head;
            return new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch (Exception)
        {
            return new TextBlock
            {
                Text = "\ue627",
                FontFamily = WorkbenchTheme.IconFont,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WorkbenchTheme.ForegroundFaint
            };
        }
    }

    private void SwitchTo(MinecraftAccount account)
    {
        if (ReferenceEquals(account, Data.ConfigEntry.UsingMinecraftMinecraftAccount))
            return;

        Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
        ConfigSaver.FlushConfig();

        WorkbenchServices.Output.Write(Services.OutputService.ChannelWorkbench,
            $"当前账户切换为「{account.Name}」（{account.ShortType}）");

        Rebuild();
        AccountsChanged?.Invoke();
    }

    private void Remove(MinecraftAccount account)
    {
        Data.ConfigEntry.MinecraftAccounts.Remove(account);

        if (ReferenceEquals(account, Data.ConfigEntry.UsingMinecraftMinecraftAccount))
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();

        ConfigSaver.FlushConfig();
        Rebuild();
        AccountsChanged?.Invoke();
    }
}
