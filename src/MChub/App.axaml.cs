using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Module.Initialize;
using MChub.Core.Module.Ipc;
using MChub.Core.Services;
using MChub.Localization;
using MChub.Module;
using MChub.Module.Initialize;
using MChub.Views;
using MChub.Views.Pages;
using MChub.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Shared;

namespace MChub;

public partial class App : Application
{
    public delegate void UiLoadedEventHandler(TabWindow ui);

    private TabWindow _win;
    public static long StartupTimestamp;
    public static string? BedrockPackagePath { get; set; }
    public static string? JavaPackagePath { get; set; }

    public static TabWindow? MainWindow => (Current!.ApplicationLifetime
        as IClassicDesktopStyleApplicationLifetime).MainWindow as TabWindow;

    public static TopLevel TopLevel => TopLevel.GetTopLevel(MainWindow);
    public static event UiLoadedEventHandler? UiLoaded;

    private static void OpenJavaDownloadPage()
    {
        if (MainWindow is not { } window)
            return;

        var downloadPage = new DownloadPage();
        downloadPage.DownloadPageViewModel.NavigateType(typeof(JavaDownloadPage));
        window.OpenPage(downloadPage);
    }

    public override void Initialize()
    {
        Logger.Info(LogLanguageManager.Instance.app_initStart.CurrentValue());
        if (BedrockPackagePath == null)
            Initializer.App();
        else
            Initializer.BedrockPackageImport();
        _ = SendStartupTelemetryAsync();
        AvaloniaXamlLoader.Load(this);
        ApplyThemeVariant(Data.ConfigEntry.Theme);
        Data.ConfigEntry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Data.ConfigEntry.Theme))
                ApplyThemeVariant(Data.ConfigEntry.Theme);
        };
        Logger.Info(LogLanguageManager.Instance.app_initComplete.CurrentValue());
    }

    private static void ApplyThemeVariant(Theme theme)
    {
        if (Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            Theme.Light => ThemeVariant.Light,
            Theme.Dark => ThemeVariant.Dark,
            Theme.Mirage => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private static async Task SendStartupTelemetryAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await TelemetryService.SendAppOpenAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Logger.Warning(LogLanguageManager.Instance.telemetry_timedOut.CurrentValue());
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.telemetry_failed.CurrentValue(), exception.Message));
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Logger.Info("OnFrameworkInitializationCompleted");
        MinecraftLaunchService.OpenJavaDownloadPage = OpenJavaDownloadPage;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            Logger.Debug(LogLanguageManager.Instance.app_attachDevtools.CurrentValue());
#else
            Logger.Info(LogLanguageManager.Instance.app_registerGlobalExceptionHandling.CurrentValue());
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
#endif
            if (BedrockPackagePath is { } packagePath)
            {
                Initializer.Oobe();
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

                // 合规闸门对「双击包文件启动」同样生效：没登录正版就先登录，
                // 登录完成后再弹导入窗口。
                if (!AccountGate.IsSatisfied)
                {
                    ShowLoginWindow(desktop, afterLogin: () =>
                    {
                        var importWindow = new BedrockPackageImportWindow(packagePath);
                        LogWindowShowElapsed(importWindow);
                        desktop.MainWindow = importWindow;
                        importWindow.Show();
                    });
                }
                else
                {
                    var importWindow = new BedrockPackageImportWindow(packagePath);
                    LogWindowShowElapsed(importWindow);
                    desktop.MainWindow = importWindow;
                }
            }
            else if (Data.ConfigEntry.IsInitialized)
            {
                Initializer.Oobe();
                // 合规闸门：必须先登录微软正版账号，否则只给登录窗口。
                EnterLauncher(desktop);
            }
            else
            {
                Logger.Info(LogLanguageManager.Instance.app_notInitializedShowOobe.CurrentValue());
                Initializer.Oobe();
                var oobe = new OobeWindow();
                LogWindowShowElapsed(oobe);
                desktop.MainWindow = oobe;
                oobe.Completed += () =>
                {
                    Logger.Info(LogLanguageManager.Instance.app_initCompleteShowMainWindow.CurrentValue());
                    Data.ConfigEntry.IsInitialized = true;
                    ConfigSaver.FlushConfig();

                    // 首次启动多一步：先给迁移页。迁移里可能就带进来一个正版账号，
                    // 那样就不用再登录（闸门在迁移结束后统一判定）。
                    // 注意顺序：先让新窗口成为 MainWindow，再关 OOBE，
                    // 否则 ShutdownMode=OnMainWindowClose 时关掉主窗会直接退出。
                    ShowMigrationWindow(desktop);
                    oobe.Close();
                };
            }

            Logger.Info(LogLanguageManager.Instance.app_uiConfigComplete.CurrentValue());
#if DEBUG
            if (desktop.MainWindow is { } debugWindow)
                debugWindow.Opened += AttachDeveloperToolsAfterWindowOpened;
#endif
        }

        if (BedrockPackagePath == null)
        {
            MChubCommandQueue.ExecutionHandler = MChubCommandExecutor.ExecuteAsync;
            MChubCommandQueue.Initialize();
        }

        if (BedrockPackagePath == null && this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
            activatableLifetime.Activated += OnActivated;

        base.OnFrameworkInitializationCompleted();
    }

#if DEBUG
    private async void AttachDeveloperToolsAfterWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
            window.Opened -= AttachDeveloperToolsAfterWindowOpened;
        await Task.Delay(1000);
        this.AttachDeveloperTools();
    }
#endif

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is not ProtocolActivatedEventArgs { Uri: { } uri }) return;
        Logger.Info(string.Format(LogLanguageManager.Instance.app_protocolReceived.CurrentValue(), uri));
        if (MChubCommandParser.Parse([uri.ToString()], out var command, out var error) ==
            MChubCliParseStatus.Command && command is not null)
            MChubCommandQueue.Enqueue(command);
        else if (error is not null)
            Logger.Error(string.Format(LogLanguageManager.Instance.app_protocolLinkInvalid.CurrentValue(), error));
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _win = new TabWindow(true)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        TextOptions.SetTextRenderingMode(_win, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(_win, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(_win, BaselinePixelAlignment.Aligned);

        LogWindowShowElapsed(_win);
        desktop.MainWindow = _win;
        _win.Loaded += Function;
    }

    /// <summary>
    /// 首次启动的迁移页：迁移完（或跳过）再判定闸门 ——
    /// 如果迁移进来一个可用的微软账号，直接进主界面，不再要求登录。
    /// </summary>
    private void ShowMigrationWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var migration = new MigrationWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        LogWindowShowElapsed(migration);
        desktop.MainWindow = migration;
        migration.Completed += () =>
        {
            var accountReady = AccountGate.IsSatisfied;
            if (accountReady)
            {
                ShowMainWindow(desktop);
                _win!.Show();
            }
            else
            {
                ShowLoginWindow(desktop);
            }

            migration.Close();
        };
        migration.Show();
    }

    /// <summary>
    /// 强制登录：没有微软正版账号时用它顶替主窗口。登录成功后由回调打开主窗口。
    /// 关掉登录窗口等于退出启动器（此时主窗口还没创建）。
    /// </summary>
    /// <summary>
    /// 统一入口：所有「能不能进界面」的判断都走这里。
    /// - 没有可刷新的微软账号 → 登录
    /// - 有账号但凭证过期（超过 24h） → 先联网复核，复核不过就要求重新登录
    /// - 刚验证过 → 直接进主界面
    /// </summary>
    private void EnterLauncher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!AccountGate.IsSatisfied)
        {
            ShowLoginWindow(desktop);
            return;
        }

        if (AccountGate.IsFresh)
        {
            ShowMainWindow(desktop);
            _win?.Show();
            return;
        }

        ShowLoginWindow(desktop, verify: true);
    }

    /// <param name="afterLogin">登录成功后要做什么；为空表示进主界面。</param>
    /// <param name="verify">true = 先复核本地凭证，失败再走设备码登录。</param>
    private void ShowLoginWindow(IClassicDesktopStyleApplicationLifetime desktop,
        Action? afterLogin = null, bool verify = false)
    {
        Logger.Info("显示登录窗口（" + (verify ? "复核凭证" : "首次登录") + "）");

        var login = new LoginWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        if (verify)
            login.BeginVerify();

        LogWindowShowElapsed(login);
        desktop.MainWindow = login;
        login.Completed += () =>
        {
            if (afterLogin is not null)
            {
                afterLogin();
                login.Close();
                return;
            }

            ShowMainWindow(desktop);
            _win!.Show();
            login.Close();
        };
        login.Show();
    }

    private static void LogWindowShowElapsed(Window window)
    {
        window.Opened += (_, _) =>
        {
            var elapsed = Stopwatch.GetElapsedTime(StartupTimestamp).TotalMilliseconds.ToString("F0");
            Logger.Info(string.Format(LogLanguageManager.Instance.app_windowShowElapsed.CurrentValue(), elapsed));
        };
    }

    private async void Function(object? sender, RoutedEventArgs e)
    {
        Logger.Info(LogLanguageManager.Instance.app_uiLoadComplete.CurrentValue());
        _win.Loaded -= Function;
        await Task.Yield();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await Initializer.UiAsync();
            Logger.Info(string.Format(LogLanguageManager.Instance.app_uiDataLoadComplete.CurrentValue(), stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception)
        {
            Logger.Error(LogLanguageManager.Instance.app_uiDataLoadFailed.CurrentValue(), exception);
        }
        finally
        {
            _win.IsUiLoading = false;
            MChubCommandQueue.MarkUiLoaded();
            UiLoaded?.Invoke(_win);
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Logger.Fatal(LogLanguageManager.Instance.app_crashDomainException.CurrentValue(), exception);
        else
            Logger.Fatal(string.Format(LogLanguageManager.Instance.app_crashDomainExceptionWithObject.CurrentValue(), e.ExceptionObject));
        try
        {
            var win = new CrashWindow(e.ToString() ?? "Unhandled Exception");
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Fatal(LogLanguageManager.Instance.app_crashWindowShowFailed.CurrentValue(), ex);
        }
    }

    private void UIThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Fatal(LogLanguageManager.Instance.app_crashUiThreadException.CurrentValue(), e.Exception);
        try
        {
            var win = new CrashWindow(e.Exception.ToString());
            win.Show();
        }
        catch (Exception ex)
        {
            Logger.Fatal(LogLanguageManager.Instance.app_crashWindowShowFailed.CurrentValue(), ex);
        }
        finally
        {
            e.Handled = true;
        }
    }
}
