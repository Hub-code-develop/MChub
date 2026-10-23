using Avalonia.Threading;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Services;
using MChub.Core.Module.Initialize;

namespace MChub.Module;

/// <summary>
/// 启动闸门：按合规要求，必须先登录一个微软正版账号才能进入启动器功能。
///
/// 判定分两层：
/// 1. <see cref="IsSatisfied"/>：本地存在带 refresh token 的 Microsoft 账号
///    （离线账号、第三方验证服务器账号都不算，防绕过）。
/// 2. <see cref="IsFresh"/> / <see cref="ValidateAsync"/>：这个凭证是不是真的还有效。
///    超过 <see cref="FreshWindow"/> 就联网刷新一次，刷新失败就等于是「不是正版」，
///    必须重新登录才能进界面 —— 只靠本地一个字段是骗不过去的。
/// </summary>
public static class AccountGate
{
    /// <summary>凭证多久之内算「刚验证过」，避免每次启动都打一次网络请求。</summary>
    private static readonly TimeSpan FreshWindow = TimeSpan.FromHours(24);

    /// <summary>当前可用的微软正版账号（没有则为 null）。</summary>
    public static MinecraftAccount? OfficialAccount
        => Data.ConfigEntry.MinecraftAccounts.FirstOrDefault(IsUsableMicrosoftAccount);

    /// <summary>本地是否存在可刷新的正版账号。</summary>
    public static bool IsSatisfied => OfficialAccount != null;

    /// <summary>是否在新鲜期内（不需要联网复核）。</summary>
    public static bool IsFresh =>
        OfficialAccount?.LastRefreshTime is { } time && DateTime.Now - time < FreshWindow;

    /// <summary>
    /// 联网复核凭证：新鲜期内直接通过，否则调用微软刷新；
    /// 刷新成功会把新凭证写回账号列表，失败返回 false。
    /// </summary>
    public static async Task<bool> ValidateAsync()
    {
        var account = OfficialAccount;
        if (account is null)
            return false;

        if (IsFresh)
            return true;

        var refreshed = await AccountRefresher.RefreshMicrosoft(account);
        if (refreshed is null)
            return false;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 刷新只是换一份新凭证；如果这个账号正好是「当前游玩用的账号」，
            // 选中项也要跟着换，否则启动时拿到的还是旧对象（token 已失效）。
            var wasSelected = ReferenceEquals(Data.ConfigEntry.UsingMinecraftMinecraftAccount, account);

            var index = Data.ConfigEntry.MinecraftAccounts.IndexOf(account);
            if (index >= 0)
                Data.ConfigEntry.MinecraftAccounts[index] = refreshed;
            else
                Data.ConfigEntry.MinecraftAccounts.Add(refreshed);

            if (wasSelected)
                Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
        });

        ConfigSaver.FlushConfig();
        return true;
    }

    private static bool IsUsableMicrosoftAccount(MinecraftAccount account)
        => account.AccountType == AccountType.Microsoft
           && !string.IsNullOrWhiteSpace(account.RefreshToken);
}
