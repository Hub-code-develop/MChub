using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;

namespace MChub.Module;

/// <summary>
/// 启动闸门：按合规要求，必须先登录一个微软正版账号才能进入启动器功能。
/// 判定口径是「存在一个带 refresh token 的 Microsoft 账号」——离线账号、第三方
/// 验证服务器账号都不算，避免用离线档案绕过。
/// </summary>
public static class AccountGate
{
    /// <summary>当前可用的微软正版账号（没有则为 null）。</summary>
    public static MinecraftAccount? OfficialAccount
        => Data.ConfigEntry.MinecraftAccounts.FirstOrDefault(IsUsableMicrosoftAccount);

    /// <summary>闸门是否已满足。</summary>
    public static bool IsSatisfied => OfficialAccount != null;

    private static bool IsUsableMicrosoftAccount(MinecraftAccount account)
        => account.AccountType == AccountType.Microsoft
           && !string.IsNullOrWhiteSpace(account.RefreshToken);
}
