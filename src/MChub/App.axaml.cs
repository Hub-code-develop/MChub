using System.Diagnostics;
using System.Linq;
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
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Core.Module.Initialize;
using MChub.Core.Module.Ipc;
using MChub.Core.Services;
using MChub.Launcher;
using MChub.Localization;
using MChub.Module;
using MChub.Module.Initialize;
using MChub.Workbench.Shell;
using MChub.Views;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Shared;

namespace MChub;

public partial class App : Application
{
    public delegate void UiLoadedEventHandler(WorkbenchWindow ui);

    private WorkbenchWindow? _win;
    private HomeWindow? _home;
    private MinecraftInstance? _pendingInstance;
    public static long StartupTimestamp;
    public static string? BedrockPackagePath { get; set; }
    public static string? JavaPackagePath { get; set; }

    public static WorkbenchWindow? MainWindow => (Current!.ApplicationLifetime
        as IClassicDesktopStyleApplicationLifetime).MainWindow as WorkbenchWindow;

    public static TopLevel TopLevel => TopLevel.GetTopLevel(MainWindow);
    public static event UiLoadedEventHandler? UiLoaded;

    private static void OpenJavaDownloadPage()
    {
        // 旧实现是往 TabWindow 里塞 DownloadPage。新工作台里下载会成为侧栏视图，
        // 这里先留空，P3 迁回来时改为执行对应命令。
        Logger.Info("请求打开 Java 下载页（新工作台尚未接入）");
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
            // 窗口创建推迟一帧：同步初始化会把 UI 线程占满，
            // 首页要等初始化结束才画得出来，扫描进度也就没法显示了。
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            Dispatcher.UIThread.Post(() => BeginStartup(desktop), DispatcherPriority.Background);

            Logger.Info(LogLanguageManager.Instance.app_uiConfigComplete.CurrentValue());
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

    /// <summary>
    /// 首页的第一帧画完之后才跑这里。同步做的分支判断整体搬进这个方法，
    /// 逻辑不变，只是晚了一帧。
    /// </summary>
    private void BeginStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
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
                        CompleteAccountGate();
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
                    importWindow.Show();
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
                oobe.Show();
            }

#if DEBUG
            if (desktop.MainWindow is { } debugWindow)
                debugWindow.Opened += AttachDeveloperToolsAfterWindowOpened;
#endif
        }
        catch (Exception exception)
        {
            Logger.Error("启动流程异常", exception);
        }
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

    /// <summary>
    /// 首页：启动器打开后的第一屏（版本列表）。选完版本由
    /// <see cref="OpenWorkbench"/> 打开工作台，工作台关掉再回到这里。
    /// </summary>
    private void ShowHomeWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _home = new HomeWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        LogWindowShowElapsed(_home);
        desktop.MainWindow = _home;
        _home.InstanceActivated += OpenWorkbench;
        _home.Show();
    }

    /// <summary>从首页点开某个版本：开工作台，首页先藏起来。</summary>
    private void OpenWorkbench(MinecraftInstance instance)
    {
        var workbench = new WorkbenchWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        TextOptions.SetTextRenderingMode(workbench, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(workbench, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(workbench, BaselinePixelAlignment.Aligned);

        LogWindowShowElapsed(workbench);
        _pendingInstance = instance;
        workbench.Loaded += Function;
        workbench.Closed += (_, _) =>
        {
            // 工作台关掉不等于退出启动器，回到版本列表
            _win = null;
            if (_home is not null)
            {
                _home.Show();
                _home.Activate();
            }
        };

        _win = workbench;
        workbench.Show();
        _home?.Hide();
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
            // 迁移里可能带进来一个正版账号；交给统一入口判断是否还需要登录
            EnterLauncher(desktop);
            migration.Close();
        };
        migration.Show();
    }

    /// <summary>
    /// 强制登录：没有微软正版账号时用它顶替主窗口。登录成功后由回调打开主窗口。
    /// 关掉登录窗口等于退出启动器（此时主窗口还没创建）。
    /// </summary>
    /// <summary>
    /// 统一入口。闸门**只在软件首次启动时检查一次**：
    /// - 已经通过过（AccountGateCompleted）→ 以后每次启动都直接进主界面
    /// - 首次启动且已经存在正版账号（比如迁移进来的）→ 直接放行，不要求重新登录
    /// - 首次启动没有正版账号 → 强制登录，成功后记下「已通过」
    /// </summary>
    private void EnterLauncher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (Data.ConfigEntry.AccountGateCompleted)
        {
            ShowHomeWindow(desktop);
            return;
        }

        if (AccountGate.IsSatisfied)
        {
            CompleteAccountGate();
            ShowHomeWindow(desktop);
            return;
        }

        ShowLoginWindow(desktop, afterLogin: () =>
        {
            CompleteAccountGate();
            ShowHomeWindow(desktop);
        });
    }

    /// <summary>标记「首次启动的账号闸门已通过」，之后不再检查。</summary>
    private static void CompleteAccountGate()
    {
        if (Data.ConfigEntry.AccountGateCompleted)
            return;

        Data.ConfigEntry.AccountGateCompleted = true;
        ConfigSaver.FlushConfig();
        Logger.Info("首次启动账号闸门已通过，后续启动不再检查");
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

            ShowHomeWindow(desktop);
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
        if (_win is null)
            return;
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
            if (_win is { } window)
            {
                window.IsUiLoading = false;
                MChubCommandQueue.MarkUiLoaded();
                UiLoaded?.Invoke(window);

                // 首页点进来的实例，等初始化把实例集合刷完再打开，
                // 否则文档会挂在重扫前的旧对象上。
                if (_pendingInstance is { } pending)
                {
                    _pendingInstance = null;
                    var resolved = InstanceManager.Instance.Instances
                                       .FirstOrDefault(item => item.InstanceFolderPath == pending.InstanceFolderPath)
                                   ?? pending;
                    window.OpenInstance(resolved);
                }
            }
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
