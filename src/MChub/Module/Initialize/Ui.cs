using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using MChub.Core.App.Events;
using MChub.Core.Classes;
using MChub.Core.Classes.Config;
using MChub.Core.Classes.Entries;
using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Instance;
using MChub.Core.Module.Initialize;
using MChub.Core.Module.Multiplayer;
using MChub.Core.Module.Update;
using MChub.Core.Minecraft.Services;
using MChub.Core.Services;
using MChub.Core.Services.SystemResources;
using MChub.Localization;
using MChub.Services;
using MChub.Views;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Platform;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Common;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Helpers;
using TioUi.Common.Language;

namespace MChub.Module.Initialize;

public static partial class Initializer
{
    public static void Oobe()
    {
        LocalizationService.Register(CommonLanguageManager.Instance);
        LocalizationService.Register(LogLanguageManager.Instance);
        LocalizationService.Register(AppLanguageManager.Instance);
        LocalizationService.Register(WidgetsLanguageManager.Instance);
        LocalizationService.Register(PagesLanguageManager.Instance);
        LocalizationService.Register(InstancesLanguageManager.Instance);
        LocalizationService.Register(DownloadsLanguageManager.Instance);
        LocalizationService.Register(SettingsLanguageManager.Instance);
        LocalizationService.Register(ToolsLanguageManager.Instance);
        LocalizationService.Register(StaticPagesLanguageManager.Instance);
        LocalizationService.Register(ComponentsLanguageManager.Instance);

        LocalizationService.CultureChanged += culture =>
        {
            var language = culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                ? Languages.en_us
                : Languages.zh_cn;
            LangManager.SetLanguage(language);
        };

        LocalizationService.SetCulture(CultureInfo.GetCultureInfo(Data.ConfigEntry.Language));

        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;
    }

    public static async Task UiAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(LogLanguageManager.Instance.ui_initStart.CurrentValue());
        Logger.Info(string.Format(LogLanguageManager.Instance.ui_writingAppPath.CurrentValue(),
            ConfigPath.AppPathDataPath));
        File.WriteAllText(ConfigPath.AppPathDataPath,
            Process.GetCurrentProcess().MainModule.FileName);

        ThemeHelper.SetThemeColor(Data.ConfigEntry.ThemeColor);
        ThemeHelper.ToggleTheme(Data.ConfigEntry.Theme);
        if (Data.ConfigEntry.EnableCustomForegroundColor)
            ConfigEntry.SetForegroundColor(Data.ConfigEntry.ForegroundColor);

        LoopGc.BeginLoop();
        MemoryOptimizationService.StartAutomaticWorkingSetTrim();

        NotificationGateway.IsToastFunc = () => Data.ConfigEntry.NoticeWay == NoticeWay.Toast;
        UiEvents.AppScaleChanged += AppScaling.ApplyScale;
        // 游戏内浮层（OverlayWindow）随旧 UI 一起移除；联机大厅/实例详情会在 P3 以新形态回来
        UiEvents.ShowGameOverlay = (_, _) => { };

        Events.CoreSaveSettings += ConfigSaver.SaveConfig;

        if (Data.ConfigEntry.BackgroundMode == BackgroundMode.Default)
        {
            Application.Current.Resources.Remove("BackGroundOpacity");
            Application.Current.Resources.Remove("TranslucentBackGroundOpacity");
        }
        else
        {
            Application.Current.Resources["BackGroundOpacity"] = Data.ConfigEntry.ControlOpacity;
            Application.Current.Resources["TranslucentBackGroundOpacity"] = Data.ConfigEntry.TranslucentControlOpacity;
        }

        if (Data.ConfigEntry.EnableCheckAutoUpdate &&
            (AppVersionService.Instance.Version.Type != "dev" || DebugSettings.EnableAutomaticUpdates))
        {
            Logger.Info(LogLanguageManager.Instance.update_autoUpdateCheckEnabled.CurrentValue());
            UpdateApp.Prepare(null, true)
                .Forget(CommonLanguageManager.Instance.update_checkForUpdatesForget.CurrentValue());
        }

        Logger.Info(LogLanguageManager.Instance.multiplayer_prefetchRelaysStart.CurrentValue());
        GravityConeRelayClient.Instance.PrefetchAsync().Forget(
            CommonLanguageManager.Instance.multiplayer_prefetchRelaysForget.CurrentValue());

        Logger.Info(LogLanguageManager.Instance.ui_initializingServices.CurrentValue());
        RecentPlayListService.Initialize();
        BlockListService.Initialize();
        Task.Run(SystemResourceService.Initialize).Forget(
            LogLanguageManager.Instance.systemResources_gpuWarmupFailed.CurrentValue());
        CleanupTempFolderAsync().Forget(LogLanguageManager.Instance.config_tempCleared.CurrentValue());
        await LoadUiDataAsync();

        InitializationEvents.RaiseAfterUiLoaded();
        Logger.Info(string.Format(LogLanguageManager.Instance.ui_initComplete.CurrentValue(), stopwatch.ElapsedMilliseconds));
    }

    public static async Task LoadBedrockPackageImportDataAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
        Logger.Info(string.Format(LogLanguageManager.Instance.ui_scanBedrockStart.CurrentValue(), folders.Length));
        var instances = await Task.Run(() => InstanceManager.Instance.ScanBedrock(folders));
        InstanceManager.Instance.ApplyInstances(instances);
        Logger.Info(string.Format(LogLanguageManager.Instance.ui_bedrockScanComplete.CurrentValue(),
            instances.Count, stopwatch.ElapsedMilliseconds));
    }

    private static async Task LoadUiDataAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var folders = Data.ConfigEntry.MinecraftFolders.ToArray();
        Logger.Info(string.Format(LogLanguageManager.Instance.ui_scanInstances.CurrentValue(), folders.Length));
        var instancesTask = InstanceManager.ScanAllAsync(folders);
        var newsTask = Task.Run(NewsService.InitializeFromCache);

        var instances = await instancesTask;
        InstanceManager.Instance.ApplyInstances(instances);
        Logger.Info(string.Format(LogLanguageManager.Instance.ui_instancesLoaded.CurrentValue(), instances.Count, stopwatch.ElapsedMilliseconds));

        await newsTask;
        Logger.Info(LogLanguageManager.Instance.ui_newsCacheLoadedNotify.CurrentValue());
        NewsService.RaiseNewsUpdated();
        Task.Run(NewsService.FetchAndRefreshAsync).Forget(CommonLanguageManager.Instance.news_refreshNewsForget.CurrentValue());
    }

    private static async Task CleanupTempFolderAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Task.Run(() => Helper.ClearFolder(ConfigPath.TempFolderPath)).ConfigureAwait(false);
        Logger.Debug(LogLanguageManager.Instance.config_tempCleared.CurrentValue());
    }

}
