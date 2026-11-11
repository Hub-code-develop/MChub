using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MChub.Views.Components;

/// <summary>
/// 底部悬浮胶囊导航条（LauncherX 风格）。
/// 由外层窗口填充 <see cref="Items" />，并在 <see cref="ItemInvoked" /> 里切换页面。
/// </summary>
public partial class LauncherXDock : UserControl, INotifyPropertyChanged
{
    private bool _canGoBack;
    private string? _currentInstanceName;
    private bool _hasCurrentInstance;
    private string _launchText = "开始游戏";
    private bool _showLaunchButton = true;

    public LauncherXDock()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<LauncherXDockItem> Items { get; } = [];

    public bool CanGoBack
    {
        get => _canGoBack;
        set => SetField(ref _canGoBack, value);
    }

    public bool HasCurrentInstance
    {
        get => _hasCurrentInstance;
        set => SetField(ref _hasCurrentInstance, value);
    }

    public string? CurrentInstanceName
    {
        get => _currentInstanceName;
        set => SetField(ref _currentInstanceName, value);
    }

    public string LaunchText
    {
        get => _launchText;
        set => SetField(ref _launchText, value);
    }

    public bool ShowLaunchButton
    {
        get => _showLaunchButton;
        set => SetField(ref _showLaunchButton, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>用户点了某个导航项。</summary>
    public event EventHandler<LauncherXDockItem>? ItemInvoked;

    /// <summary>用户点了返回。</summary>
    public event EventHandler? BackRequested;

    /// <summary>用户点了开始游戏。</summary>
    public event EventHandler? LaunchRequested;

    public void SetSelected(string? key)
    {
        foreach (var item in Items)
            item.IsSelected = string.Equals(item.Key, key, StringComparison.Ordinal);
    }

    public LauncherXDockItem? Find(string key)
    {
        return Items.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
    }

    private void Item_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LauncherXDockItem item) return;

        SetSelected(item.Key);
        ItemInvoked?.Invoke(this, item);
    }

    private void Back_OnClick(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Launch_OnClick(object? sender, RoutedEventArgs e)
    {
        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>底部导航项。</summary>
public partial class LauncherXDockItem : ObservableObject
{
    public LauncherXDockItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
