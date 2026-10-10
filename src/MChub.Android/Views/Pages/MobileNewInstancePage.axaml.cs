using Avalonia.Controls;
using Avalonia.Interactivity;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MChub.Core.Minecraft.Models;
using MChub.Localization;
using MChub.Mobile.Services;
using MChub.Mobile.ViewModels;
// Android 隐式全局 using 引入了 Android.Widget，Button/ProgressBar 会与 Avalonia.Controls 冲突。
using Button = Avalonia.Controls.Button;

namespace MChub.Mobile.Views.Pages;

/// <summary>
/// 「新建实例」页：在本机把游戏版本 + 加载器下载并安装成实例。
///
/// <p><b>不依赖桌面端</b>：版本清单、库、资源、加载器全部由设备直连官方源下载，
/// 安装完成后直接回主页就能启动。</p>
/// </summary>
public partial class MobileNewInstancePage : UserControl
{
    /// <summary>安装成功（参数为实例名）。外壳据此切回主页并刷新实例列表。</summary>
    public event EventHandler<string>? Installed;

    private readonly List<MobileVersionOption> _allVersions = [];

    /// <summary>与 <see cref="LoaderCombo"/> 各项一一对应；null 表示「原版」。</summary>
    private readonly List<LoaderKind?> _loaderKinds = [];

    private IReadOnlyList<IInstallEntry> _loaderVersions = [];

    /// <summary>已拉取过加载器版本的组合，避免来回切选择时重复请求网络。</summary>
    private (string MinecraftVersion, LoaderKind Kind)? _loaderVersionsLoadedFor;

    private VersionManifestEntry? _selectedVersion;
    private LoaderKind? _selectedLoader;
    private IInstallEntry? _selectedLoaderEntry;

    private bool _versionsRequested;
    private bool _instanceIdTouched;
    private bool _busy;

    public MobileNewInstancePage()
    {
        InitializeComponent();
        ApplyStaticText();
        BuildLoaderOptions();

        AttachedToVisualTree += (_, _) => RequestVersionsIfNeeded();
    }

    /// <summary>非模板化的静态文案（占位符之类没法用 Translate 标记扩展的属性）。</summary>
    private void ApplyStaticText()
    {
        var language = MobileLanguageManager.Instance;
        IntroText.Text = language.mobile_newIntro.CurrentValue();
        VersionSearch.PlaceholderText = language.mobile_newVersionPlaceholder.CurrentValue();
        ShowAllVersions.Content = language.mobile_newShowAllVersions.CurrentValue();
        VersionEmptyHint.Text = string.Empty;
        // 必须走 ApplyInstanceId：直接赋值会触发 TextChanged，把「用户改过实例名」的标记置上，
        // 之后自动推荐的名字就再也不更新了（实测踩过）。
        ApplyInstanceId(string.Empty);
        UpdateLoaderHint();
    }

    private void BuildLoaderOptions()
    {
        var language = MobileLanguageManager.Instance;
        var labels = new List<string> { language.mobile_newLoaderNone.CurrentValue() };
        _loaderKinds.Clear();
        _loaderKinds.Add(null);

        foreach (var kind in new[] { LoaderKind.Fabric, LoaderKind.Forge, LoaderKind.NeoForge, LoaderKind.Quilt, LoaderKind.OptiFine })
        {
            if (!MobileInstanceInstaller.CanInstallOnDevice(kind))
                continue;

            labels.Add(kind.ToString());
            _loaderKinds.Add(kind);
        }

        LoaderCombo.ItemsSource = labels;
        LoaderCombo.SelectedIndex = 0;
        LoaderVersionCard.IsVisible = false;
    }

    /// <summary>能不能装 Forge 系取决于包里有没有内置的 java 命令（见 MobileInstallerJava）。</summary>
    private void UpdateLoaderHint()
    {
        var language = MobileLanguageManager.Instance;
        var missing = new[] { LoaderKind.Forge, LoaderKind.NeoForge, LoaderKind.OptiFine }
            .Any(kind => !MobileInstanceInstaller.CanInstallOnDevice(kind));
        LoaderHintText.Text = missing ? language.mobile_newLoaderUnsupported.CurrentValue() : string.Empty;
    }

    private void RequestVersionsIfNeeded()
    {
        if (_versionsRequested)
            return;

        _versionsRequested = true;
        _ = LoadVersionsAsync();
    }

    private void ReloadVersions_OnClick(object? sender, RoutedEventArgs e) => _ = LoadVersionsAsync();

    private async Task LoadVersionsAsync()
    {
        var language = MobileLanguageManager.Instance;

        if (!MobileBootstrap.InstallReady)
        {
            VersionEmptyHint.Text = string.Format(language.mobile_newVersionFailed.CurrentValue(),
                MobileBootstrap.FailureReason ?? language.mobile_settingsStorageUnknown.CurrentValue());
            VersionEmptyHint.IsVisible = true;
            return;
        }

        ReloadVersionsButton.IsEnabled = false;
        VersionEmptyHint.IsVisible = false;
        StatusText.Text = language.mobile_newVersionLoading.CurrentValue();

        try
        {
            var entries = await MobileInstanceInstaller.LoadVersionsAsync();

            _allVersions.Clear();
            foreach (var entry in entries)
                _allVersions.Add(new MobileVersionOption { Entry = entry, TypeLabel = DescribeVersionType(entry.Type) });

            StatusText.Text = string.Empty;
            ApplyVersionFilter();
        }
        catch (Exception exception)
        {
            _allVersions.Clear();
            VersionList.ItemsSource = null;
            VersionEmptyHint.Text = string.Format(language.mobile_newVersionFailed.CurrentValue(),
                exception.Message);
            VersionEmptyHint.IsVisible = true;
            StatusText.Text = string.Empty;
        }
        finally
        {
            ReloadVersionsButton.IsEnabled = !_busy;
        }
    }

    private string DescribeVersionType(string? type)
    {
        var language = MobileLanguageManager.Instance;
        return type switch
        {
            "release" => language.mobile_newTypeRelease.CurrentValue(),
            "snapshot" => language.mobile_newTypeSnapshot.CurrentValue(),
            _ => language.mobile_newTypeOld.CurrentValue()
        };
    }

    private void VersionSearch_OnTextChanged(object? sender, TextChangedEventArgs e) => ApplyVersionFilter();

    private void ShowAllVersions_OnChanged(object? sender, RoutedEventArgs e) => ApplyVersionFilter();

    /// <summary>
    /// 关键词非空时忽略「只看正式版」开关 —— 用户明确搜了某个版本号就该搜得到，
    /// 否则搜快照会得到"没有匹配的版本"这种莫名其妙的空列表。
    /// </summary>
    private void ApplyVersionFilter()
    {
        var language = MobileLanguageManager.Instance;
        var keyword = VersionSearch.Text?.Trim() ?? string.Empty;
        var includeAll = ShowAllVersions.IsChecked == true;

        var filtered = _allVersions.Where(option =>
                (keyword.Length > 0 || includeAll || option.Entry.Type == "release") &&
                (keyword.Length == 0 || option.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var previous = VersionList.SelectedItem as MobileVersionOption;
        VersionList.ItemsSource = filtered;

        if (previous is not null && filtered.Contains(previous))
            VersionList.SelectedItem = previous;

        // 清单还没拿到（拉取失败/正在拉取）时不要用「没有匹配的版本」盖掉真正的原因 ——
        // 用户一敲字就会触发过滤，之前那条失败提示会被冲掉，看起来像"搜索没结果"（实测踩过）。
        if (_allVersions.Count == 0)
            return;

        VersionEmptyHint.Text = language.mobile_newVersionEmpty.CurrentValue();
        VersionEmptyHint.IsVisible = filtered.Count == 0;
    }

    private void VersionList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VersionList.SelectedItem is not MobileVersionOption option)
            return;

        _selectedVersion = option.Entry;
        UpdateRecommendedInstanceId();
        _ = EnsureLoaderVersionsAsync();
    }

    private void LoaderCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = LoaderCombo.SelectedIndex;
        _selectedLoader = index >= 0 && index < _loaderKinds.Count ? _loaderKinds[index] : null;

        // 换加载器就要重新取版本列表（同一个游戏版本下不同加载器的版本号完全不同）。
        _loaderVersionsLoadedFor = null;
        _loaderVersions = [];
        LoaderVersionCombo.ItemsSource = null;
        _selectedLoaderEntry = null;

        LoaderVersionCard.IsVisible = _selectedLoader is not null;
        UpdateRecommendedInstanceId();

        if (_selectedLoader is not null)
            _ = EnsureLoaderVersionsAsync();
    }

    private void LoaderVersionCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = LoaderVersionCombo.SelectedIndex;
        // 第 0 项是「最新（推荐）」，直接取列表首个（加载器接口本身按新→旧返回）。
        _selectedLoaderEntry = index <= 0
            ? _loaderVersions.FirstOrDefault()
            : index - 1 < _loaderVersions.Count
                ? _loaderVersions[index - 1]
                : null;

        UpdateRecommendedInstanceId();
    }

    private async Task EnsureLoaderVersionsAsync()
    {
        if (_selectedVersion is not { } version || _selectedLoader is not { } kind)
            return;

        if (_loaderVersionsLoadedFor == (version.Id, kind))
            return;

        var language = MobileLanguageManager.Instance;
        _loaderVersionsLoadedFor = (version.Id, kind);
        StatusText.Text = language.mobile_newLoaderVersionLoading.CurrentValue();

        try
        {
            var entries = await MobileInstanceInstaller.LoadLoaderVersionsAsync(kind, version.Id);

            // 请求期间用户可能又换了版本/加载器，结果过期就丢掉。
            if (_loaderVersionsLoadedFor != (version.Id, kind))
                return;

            _loaderVersions = entries;

            var labels = new List<string> { language.mobile_newLoaderVersionLatest.CurrentValue() };
            labels.AddRange(entries.Select(entry => MobileInstanceInstaller.GetLoaderVersion(kind, entry)));

            LoaderVersionCombo.ItemsSource = labels;
            LoaderVersionCombo.SelectedIndex = 0;
            StatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            if (_loaderVersionsLoadedFor != (version.Id, kind))
                return;

            _loaderVersions = [];
            LoaderVersionCombo.ItemsSource = null;
            StatusText.Text = string.Format(language.mobile_newLoaderVersionFailed.CurrentValue(),
                exception.Message);
        }
    }

    /// <summary>实例名没被用户手动改过时，跟着「版本 + 加载器」自动更新。</summary>
    private void UpdateRecommendedInstanceId()
    {
        if (_instanceIdTouched || _selectedVersion is null)
            return;

        var loaders = new Dictionary<LoaderKind, IInstallEntry>();
        if (_selectedLoader is { } kind && _selectedLoaderEntry is { } entry)
            loaders[kind] = entry;

        ApplyInstanceId(MobileInstanceInstaller.RecommendedInstanceId(_selectedVersion, loaders));
    }

    private void InstanceIdBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        // 只有用户自己敲字才算"改过"；自动填充触发的变更不回写标记。
        if (!_settingInstanceId)
            _instanceIdTouched = true;
    }

    private bool _settingInstanceId;

    private async void Install_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        var language = MobileLanguageManager.Instance;

        if (_selectedVersion is not { } version)
        {
            StatusText.Text = language.mobile_newSelectVersion.CurrentValue();
            return;
        }

        var instanceId = InstanceIdBox.Text?.Trim() ?? string.Empty;
        if (instanceId.Length == 0)
        {
            StatusText.Text = language.mobile_newInstanceNameEmpty.CurrentValue();
            return;
        }

        if (instanceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusText.Text = language.mobile_newInstanceNameInvalid.CurrentValue();
            return;
        }

        var loaders = new Dictionary<LoaderKind, IInstallEntry>();
        if (_selectedLoader is { } kind)
        {
            var entry = _selectedLoaderEntry ?? _loaderVersions.FirstOrDefault();
            if (entry is null)
            {
                StatusText.Text = language.mobile_newLoaderVersionLoading.CurrentValue();
                return;
            }

            loaders[kind] = entry;
        }

        try
        {
            if (Directory.Exists(Path.Combine(MobileGameStorage.InstancesDirectory, instanceId)))
            {
                StatusText.Text = language.mobile_newInstanceNameExists.CurrentValue();
                return;
            }
        }
        catch (Exception)
        {
            // 目录探测失败不阻断：真正的冲突由安装任务自己报错。
        }

        SetBusy(true);
        StatusText.Text = language.mobile_newInstalling.CurrentValue();

        try
        {
            var progress = new Progress<MobileInstallProgress>(report =>
            {
                InstallProgress.Value = Math.Clamp(report.Fraction, 0, 1);
                StatusText.Text = report.Fraction > 0
                    ? $"{report.Stage} {report.Fraction:P0}"
                    : report.Stage;
            });

            var result = await MobileInstanceInstaller.InstallAsync(version, instanceId, loaders, progress);
            MobileInstanceInstaller.RefreshInstances();

            if (result.Success)
            {
                StatusText.Text = string.Format(language.mobile_newInstallDone.CurrentValue(), instanceId);
                Installed?.Invoke(this, instanceId);
            }
            else if (result.Cancelled)
            {
                StatusText.Text = language.mobile_newInstallCancelled.CurrentValue();
            }
            else
            {
                StatusText.Text = string.Format(language.mobile_newInstallFailed.CurrentValue(),
                    result.Message ?? language.mobile_newInstallFailed.CurrentValue());
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = string.Format(language.mobile_newInstallFailed.CurrentValue(), exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = !busy;
        ReloadVersionsButton.IsEnabled = !busy;
        InstallProgress.IsVisible = busy;
        if (!busy)
            InstallProgress.Value = 0;
    }

    /// <summary>自动填充实例名时不该被当成"用户改过"。</summary>
    private void ApplyInstanceId(string value)
    {
        _settingInstanceId = true;
        try
        {
            InstanceIdBox.Text = value;
        }
        finally
        {
            _settingInstanceId = false;
        }
    }
}
