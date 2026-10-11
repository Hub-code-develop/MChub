using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Module.Initialize;
using MChub.Localization;
using MChub.Mobile.ViewModels;
// Android 隐式全局 using 引入了 Android.Widget，Button 会与 Avalonia.Controls.Button 冲突。
using Button = Avalonia.Controls.Button;

namespace MChub.Mobile.Views.Pages;

/// <summary>
/// 移动端账户页。原来账户是塞在主页里的一个浮层（还是个手搓的按钮列表），
/// 现在提成独立页面：左侧账户卡片（头像 / 名字 / 类型 / 当前标记 / 卡片内操作），
/// 右侧添加-重命名面板与微软账户说明，删除走模态确认（手机上没有右键菜单可退）。
///
/// <para>移动端目前只做离线账户：微软账户要走浏览器 OAuth 回调，属于另一条链路；
/// 这里如实说明「暂未接入」，不给一个点了没反应的按钮。</para>
/// </summary>
public partial class MobileAccountPage : UserControl
{
    private readonly ObservableCollection<MobileAccountEntry> _entries = [];

    /// <summary>正在重命名的账户；为 null 表示编辑器处于「添加」模式。</summary>
    private MinecraftAccount? _renaming;

    /// <summary>等待确认删除的账户。</summary>
    private MinecraftAccount? _pendingDelete;

    /// <summary>账户集合有变化（增 / 删 / 切换）——外壳拿它刷新主页的账户卡。</summary>
    public event EventHandler? AccountsChanged;

    public MobileAccountPage()
    {
        InitializeComponent();
        AccountGrid.ItemsSource = _entries;
        Rebuild();
        EnterAddMode();
    }

    /// <summary>重新读配置并重建卡片（外壳切回本页时调用）。</summary>
    public void Reload() => Rebuild();

    // ==================== 列表 ====================

    private void Rebuild()
    {
        _entries.Clear();
        _renaming = null;
        HideConfirm();

        foreach (var account in Data.ConfigEntry.MinecraftAccounts)
        {
            // 用 Equals 而不是 ReferenceEquals：配置反序列化出来的「当前账户」可能是另一个同值实例
            // （见 Config.Initialize 的归一化），按值比较才不会把「当前」标记弄丢。
            _entries.Add(new MobileAccountEntry(account)
            {
                IsCurrent = account.Equals(Data.ConfigEntry.UsingMinecraftMinecraftAccount)
            });
        }

        EmptyState.IsVisible = _entries.Count == 0;
    }

    private void AccountCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: MobileAccountEntry entry })
            SetCurrent(entry.Account);
    }

    private void SetCurrent_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MobileAccountEntry entry })
            SetCurrent(entry.Account);
    }

    private void SetCurrent(MinecraftAccount account)
    {
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = account;
        ConfigSaver.SaveConfig();

        foreach (var item in _entries)
            item.IsCurrent = item.Account.Equals(account);

        EmptyState.IsVisible = _entries.Count == 0;
        EditorStatus.Text = string.Format(
            MobileLanguageManager.Instance.mobile_accountOfflineSwitched.CurrentValue(), account.Name);

        AccountsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ==================== 添加 / 重命名 ====================

    /// <summary>编辑器回到「添加」模式（标题、按钮文案、输入框一次配齐）。</summary>
    private void EnterAddMode(string? presetName = null)
    {
        var language = MobileLanguageManager.Instance;

        _renaming = null;
        EditorTitle.Text = language.mobile_accountAddTitle.CurrentValue();
        EditorSubmitButton.Content = language.mobile_accountAddOffline.CurrentValue();
        EditorCancelButton.IsVisible = false;
        NameBox.PlaceholderText = language.mobile_accountOfflineNameHint.CurrentValue();
        NameBox.Text = presetName ?? string.Empty;
        EditorStatus.Text = string.Empty;
    }

    private void EnterRenameMode(MinecraftAccount account)
    {
        var language = MobileLanguageManager.Instance;

        _renaming = account;
        EditorTitle.Text = string.Format(language.mobile_accountRenameTitle.CurrentValue(), account.Name);
        EditorSubmitButton.Content = language.mobile_accountSave.CurrentValue();
        EditorCancelButton.Content = language.mobile_accountCancel.CurrentValue();
        EditorCancelButton.IsVisible = true;
        NameBox.PlaceholderText = language.mobile_accountOfflineNameHint.CurrentValue();
        NameBox.Text = account.Name;
        EditorStatus.Text = string.Empty;
        HideConfirm();
    }

    private void Rename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MobileAccountEntry entry })
            EnterRenameMode(entry.Account);
    }

    private void EditorCancel_OnClick(object? sender, RoutedEventArgs e) => EnterAddMode();

    private void EditorSubmit_OnClick(object? sender, RoutedEventArgs e)
    {
        var language = MobileLanguageManager.Instance;
        var name = NameBox.Text?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            EditorStatus.Text = language.mobile_accountOfflineNameEmpty.CurrentValue();
            return;
        }

        if (!OfflineNameRegex().IsMatch(name))
        {
            EditorStatus.Text = language.mobile_accountOfflineNameInvalid.CurrentValue();
            return;
        }

        var clash = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault(account =>
            account.IsOffline && string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));

        // ---- 重命名 ----
        if (_renaming is { } target)
        {
            if (string.Equals(target.Name, name, StringComparison.Ordinal))
            {
                EnterAddMode();
                return;
            }

            if (clash is not null && !ReferenceEquals(clash, target))
            {
                EditorStatus.Text = language.mobile_accountOfflineExists.CurrentValue();
                return;
            }

            target.Name = name;
            // 离线 UUID 是由角色名派生的，改名必须一起重算，否则进服务器会认成另一个人。
            target.Uuid = MinecraftAccount.GetMinecraftOfflineUuid(name);
            ConfigSaver.SaveConfig();

            Rebuild();
            EnterAddMode();
            EditorStatus.Text = string.Format(language.mobile_accountRenamed.CurrentValue(), name);
            AccountsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // ---- 添加 ----
        if (clash is not null)
        {
            // 同名离线角色直接切过去，别在列表里堆重复项。
            SetCurrent(clash);
            EnterAddMode();
            EditorStatus.Text = language.mobile_accountOfflineExists.CurrentValue();
            return;
        }

        var created = new MinecraftAccount(AccountType.Offline)
        {
            Name = name,
            Uuid = MinecraftAccount.GetMinecraftOfflineUuid(name)
        };
        Data.ConfigEntry.MinecraftAccounts.Add(created);

        Rebuild();
        SetCurrent(created);
        EnterAddMode();
    }

    /// <summary>离线角色名的合法字符（与原版服务端一致）。</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial System.Text.RegularExpressions.Regex OfflineNameRegex();

    // ==================== 删除（模态确认） ====================

    private void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MobileAccountEntry entry })
            return;

        var language = MobileLanguageManager.Instance;

        _pendingDelete = entry.Account;
        if (_renaming is not null)
            EnterAddMode();

        ConfirmText.Text = string.Format(
            language.mobile_accountConfirmDelete.CurrentValue(), entry.Account.Name);
        ConfirmCancelButton.Content = language.mobile_accountCancel.CurrentValue();
        ConfirmDeleteButton.Content = language.mobile_accountDelete.CurrentValue();
        ConfirmOverlay.IsVisible = true;
    }

    private void ConfirmCancel_OnClick(object? sender, RoutedEventArgs e) => HideConfirm();

    /// <summary>点暗底 = 取消。点在弹窗内容上也会冒泡到这里，所以要判来源，别把「确认删除」吞成取消。</summary>
    private void ConfirmOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
            HideConfirm();
    }

    private void ConfirmDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_pendingDelete is not { } account)
        {
            HideConfirm();
            return;
        }

        var language = MobileLanguageManager.Instance;
        var wasCurrent = ReferenceEquals(account, Data.ConfigEntry.UsingMinecraftMinecraftAccount);

        Data.ConfigEntry.MinecraftAccounts.Remove(account);

        // 删掉的正好是当前账户：顺位落到还剩下的第一个，配置里不留悬空引用。
        if (wasCurrent)
            Data.ConfigEntry.UsingMinecraftMinecraftAccount = Data.ConfigEntry.MinecraftAccounts.FirstOrDefault();

        ConfigSaver.SaveConfig();

        HideConfirm();
        Rebuild();
        EnterAddMode();
        EditorStatus.Text = string.Format(language.mobile_accountDeleted.CurrentValue(), account.Name);
        AccountsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HideConfirm()
    {
        _pendingDelete = null;
        ConfirmOverlay.IsVisible = false;
    }
}
