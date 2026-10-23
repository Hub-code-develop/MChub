using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using Iridium.Authentication;
using MChub.Core.Const;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Module.Initialize;
using MChub.Core.Services;
using MChub.Module;

namespace MChub.Views;

/// <summary>
/// 强制登录窗口：没有微软正版账号时顶替主窗口显示。
/// 登录成功后把账号写入配置并触发 <see cref="Completed"/>，由 App 打开主窗口。
/// </summary>
public partial class LoginWindow : FAAppWindow
{
    private bool _started;
    private bool _verifyRequested;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_started) return;
            _started = true;
            if (_verifyRequested)
                _ = VerifyThenAuthenticateAsync();
            else
                _ = StartAuthAsync();
        };
    }

    /// <summary>登录成功（已写入配置）。</summary>
    public event Action? Completed;

    /// <summary>
    /// 以「复核已有凭证」模式启动：先联网验证本地账号是否还是正版，
    /// 验证不过就自动落到重新登录的设备码流程。必须在 Show 之前调用。
    /// </summary>
    public void BeginVerify()
    {
        _verifyRequested = true;
    }

    private async Task VerifyThenAuthenticateAsync()
    {
        SetBusy("正在验证正版账号…");

        if (await AccountGate.ValidateAsync())
        {
            await Dispatcher.UIThread.InvokeAsync(() => Completed?.Invoke());
            return;
        }

        ErrorText.Text = "本地登录凭证已失效，需要重新登录一次才能继续使用。";
        ErrorText.IsVisible = true;
        await StartAuthAsync();
    }

    private async Task StartAuthAsync()
    {
        SetBusy("正在获取登录验证码…");

        try
        {
            var authenticator = new MicrosoftAuthenticator(CredentialsService.MicrosoftClientId);

            var token = await authenticator.DeviceFlowAuthAsync(deviceCode =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CodeText.Text = deviceCode.UserCode;
                    UrlText.Text = deviceCode.VerificationUrl;
                    CodePanel.IsVisible = true;
                    StatusText.Text = "请打开下面的网址，输入验证码完成登录。";
                });
            });

            SetBusy("已授权，正在获取 Minecraft 档案…");
            var account = await authenticator.AuthenticateAsync(token);

            var minecraftAccount = new MinecraftAccount(AccountType.Microsoft)
            {
                CreateAt = DateTime.Now,
                LastLoginTime = DateTime.Now,
                LastRefreshTime = DateTime.Now,
                RefreshToken = account.RefreshToken,
                AccessToken = account.AccessToken,
                Uuid = account.Uuid,
                Name = account.Name
            };

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Data.ConfigEntry.MinecraftAccounts.Add(minecraftAccount);
                ConfigSaver.FlushConfig();
                StatusText.Text = $"登录成功：{account.Name}";
                Completed?.Invoke();
            });
        }
        catch (Exception e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = "登录失败。";
                ErrorText.Text = e.Message;
                ErrorText.IsVisible = true;
                RetryButton.IsVisible = true;
            });
        }
    }

    private void SetBusy(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = message;
            ErrorText.IsVisible = false;
            RetryButton.IsVisible = false;
        });
    }

    private void CopyCode(object? sender, RoutedEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(CodeText.Text ?? string.Empty);
    }

    private void OpenUrl(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(UrlText.Text) &&
            Uri.TryCreate(UrlText.Text, UriKind.Absolute, out var uri))
        {
            TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
        }
    }

    private void Retry(object? sender, RoutedEventArgs e)
    {
        CodePanel.IsVisible = false;
        _ = StartAuthAsync();
    }

    private void Quit(object? sender, RoutedEventArgs e) => Close();
}
