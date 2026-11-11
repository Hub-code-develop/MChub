using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MinecraftLaunch.Base.Enums;
using MChub.Core.Classes;
using MChub.Core.Classes.Entries;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance.Java;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Shared;

namespace MChub.Core.Classes.Config;

public partial class ConfigEntry : ObservableObject
{
    [ObservableProperty] public partial bool IsInitialized { get; set; } = true;
    [ObservableProperty] public partial string? TelemetryUserId { get; set; }
    [ObservableProperty] public partial bool EnableCustomForegroundColor { get; set; } = false;
    [ObservableProperty] public partial bool EnableCheckAutoUpdate { get; set; } = true;
    [ObservableProperty] public partial UpdateSource UpdateSource { get; set; } = UpdateSource.Github;
    [ObservableProperty] public partial string? UpdateChannel { get; set; }
    [ObservableProperty] public partial DownloadSourceMode MinecraftMetadataSource { get; set; } = DownloadSourceMode.Auto;
    [ObservableProperty] public partial DownloadSourceMode MinecraftFileSource { get; set; } = DownloadSourceMode.Auto;
    [ObservableProperty] public partial ResourceDownloadSourceMode ResourceDownloadSource { get; set; } = ResourceDownloadSourceMode.Auto;
    [ObservableProperty] public partial ResourceDownloadSourceMode ModrinthResourceDownloadSource { get; set; } = ResourceDownloadSourceMode.Auto;
    [ObservableProperty] public partial ResourceDownloadSourceMode CurseForgeResourceDownloadSource { get; set; } = ResourceDownloadSourceMode.Auto;
    [ObservableProperty] public partial bool EnableFragmentDownload { get; set; }
    [ObservableProperty] public partial bool EnableGameOverlay { get; set; } = true;
    [ObservableProperty] public partial bool EnableFullscreen { get; set; }
    [ObservableProperty] public partial bool AutoSetChineseLanguage { get; set; } = true;
    [ObservableProperty] public partial bool EnableManagedWindowDecorationsOnWindows { get; set; }
    [ObservableProperty] public partial bool EnableManagedWindowBorderOnWindows { get; set; } = true;
    [ObservableProperty] public partial string Language { get; set; } = "zh-CN";
    [ObservableProperty] public partial bool EnableCustomUserAgent { get; set; }
    [ObservableProperty] public partial bool EnableProxyServer { get; set; }
    [ObservableProperty] public partial bool DisableSystemProxy { get; set; }
    [ObservableProperty] public partial bool EnableGithubMirror { get; set; }
    [ObservableProperty] public partial bool EnableDebugConsole { get; set; }
    [ObservableProperty] public partial Logger.LogLevel MinimumLogLevel { get; set; } = Logger.LogLevel.Info;
    [ObservableProperty] public partial bool AutoSetJavaHighPerformanceGpu { get; set; } = true;
    [ObservableProperty] public partial bool AutoOptimizeMemoryBeforeGameLaunch { get; set; }
    [ObservableProperty] public partial bool EnableBedrockAccountInjection { get; set; }
    [ObservableProperty] public partial bool ShowDragDropTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowUpdateTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowUsingAccountTip { get; set; } = true;
    [ObservableProperty] public partial bool ShowMinecraftNews { get; set; } = false;
    [ObservableProperty] public partial bool ShowRecentPlays { get; set; } = true;
    [ObservableProperty] public partial bool NewTabRecentPlaysExpanded { get; set; } = true;
    [ObservableProperty] public partial bool StartPageRecentPlaysExpanded { get; set; }
    [ObservableProperty] public partial int CustomHomepageType { get; set; }
    [ObservableProperty] public partial int CustomHomepagePreset { get; set; }
    [ObservableProperty] public partial string CustomHomepageUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial bool DownloadJavaEditionExpanded { get; set; } = true;
    [ObservableProperty] public partial bool DownloadBedrockEditionExpanded { get; set; }
    [ObservableProperty] public partial bool DownloadOthersExpanded { get; set; }
    [ObservableProperty] public partial bool SettingsNavGeneralExpanded { get; set; } = true;
    [ObservableProperty] public partial bool SettingsNavGameExpanded { get; set; } = true;
    [ObservableProperty] public partial bool SettingsNavNetworkExpanded { get; set; } = true;
    [ObservableProperty] public partial DownloadSearchSource DefaultDownloadSearchSource { get; set; } = DownloadSearchSource.All;
    [ObservableProperty] public partial ModLoaderType DownloadSearchLoader { get; set; } = ModLoaderType.Any;
    [ObservableProperty] public partial DownloadSearchSort DefaultDownloadSearchSort { get; set; } = DownloadSearchSort.Relevance;
    [ObservableProperty] public partial int ResourceListSortIndex { get; set; }
    [ObservableProperty] public partial string DownloadLastSelectedPage { get; set; } = string.Empty;
    [ObservableProperty] public partial string? AiServerBaseUrl { get; set; }
    [ObservableProperty] public partial string? AiApiKey { get; set; }
    [ObservableProperty] public partial string AiModel { get; set; } = "gpt-4o";
    [ObservableProperty] public partial string MultiplayerLastSelectedPage { get; set; } = "Java";
    [ObservableProperty] public partial string? PlayitSecretKey { get; set; }
    [ObservableProperty] public partial string GravityConeRelaySources { get; set; } =
        Module.Multiplayer.GravityConeRelayClient.DefaultRelaySourceUrl;
    [ObservableProperty] public partial bool GravityConeRelayAutoUpdate { get; set; } = true;
    [ObservableProperty] public partial string? BackgroundImagePath { get; set; }
    [ObservableProperty] public partial string? CustomUserAgent { get; set; }
    [ObservableProperty] public partial string? ProxyServer { get; set; }
    [ObservableProperty] public partial string? GithubMirrorUrl { get; set; }
    [ObservableProperty] public partial string? CustomLauncherInfo { get; set; }
    [ObservableProperty] public partial string? OnlinePlayerName { get; set; }
    [ObservableProperty] public partial string DefaultPage { get; set; } = string.Empty;
    [ObservableProperty] public partial string? OverrideMinecraftWindowTitle { get; set; }
    [ObservableProperty] public partial string? BeforeLaunchCommand { get; set; }
    [ObservableProperty] public partial string? AfterLaunchCommand { get; set; }
    [ObservableProperty] public partial string? JvmArgs { get; set; }
    [ObservableProperty] public partial string? PackagedCommand { get; set; }
    [ObservableProperty] public partial Color BackgroundSolidColor { get; set; } = Color.Parse("#2d2d2d");
    [ObservableProperty] public partial Color ForegroundColor { get; set; } = Color.Parse("#494c4f");
    [ObservableProperty] public partial Color ThemeColor { get; set; } = Color.Parse("#1890ff");
    [ObservableProperty] public partial Color CustomWindowBorderColor { get; set; } = Color.Parse("#6a6c70");
    [ObservableProperty] public partial double CustomWindowBorderThickness { get; set; } = 1;
    [ObservableProperty] public partial double CustomWindowBorderCornerRadius { get; set; } = 10;
    [ObservableProperty] public partial NewTabContent NewTabContent { get; set; } = NewTabContent.NewTabPage;
    [ObservableProperty] public partial NewTabLayout NewTabLayout { get; set; } = NewTabLayout.SideBySide;
    [ObservableProperty] public partial int NewTabVerticalInstanceRows { get; set; } = 2;
    [ObservableProperty] public partial GithubMirrorMode GithubMirrorMode { get; set; } = GithubMirrorMode.Prefix;
    [ObservableProperty] public partial NoticeWay NoticeWay { get; set; } = NoticeWay.Toast;
    [ObservableProperty] public partial Theme Theme { get; set; } = Theme.Light;
    [ObservableProperty] public partial FilePicker FilePicker { get; set; } = FilePicker.System;
    [ObservableProperty] public partial BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Default;
    [ObservableProperty] public partial MChubVisibleMode MChubVisibleMode { get; set; } = MChubVisibleMode.NoOperation;
    [ObservableProperty] public partial InstanceSortType DefaultInstanceSortType { get; set; } = InstanceSortType.PlayTime;
    [ObservableProperty] public partial int DownloadMaxThreadCount { get; set; } = 64;
    [ObservableProperty] public partial int DownloadMaxRetryCount { get; set; } = 4;
    [ObservableProperty] public partial int DownloadMaxFragmentCount { get; set; } = 32;
    [ObservableProperty] public partial int CustomDownloadMaxFragmentCount { get; set; } = 8;
    [ObservableProperty] public partial int MinecraftWindowWidth { get; set; } = 854;
    [ObservableProperty] public partial int MinecraftWindowHeight { get; set; } = 480;
    [ObservableProperty] public partial int MinecraftMaxMemory { get; set; } = 4096;
    [ObservableProperty] public partial double ControlOpacity { get; set; } = 1;
    [ObservableProperty] public partial double TranslucentControlOpacity { get; set; } = 1;
    [ObservableProperty] public partial double AcrylicOpacity { get; set; } = 0.2;
    [ObservableProperty] public partial double ImageBlurRadius { get; set; } = 0.0;
    [ObservableProperty] public partial double MicaOpacity { get; set; } = 0.8;
    [ObservableProperty] public partial double BlurOpacity { get; set; } = 0.5;
    [ObservableProperty] public partial bool EnableImageMask { get; set; }
    [ObservableProperty] public partial Color ImageMaskColor { get; set; } = Color.Parse("#000000");
    [ObservableProperty] public partial double ImageMaskOpacity { get; set; } = 0.3;
    [ObservableProperty] public partial bool ShowWidgetBackground { get; set; } = true;
    /// <summary>
    /// 是否让 UI 元件（标题栏/标签栏、侧边导航、弹出菜单、卡片等）使用磨砂玻璃材质。
    /// 关闭后退化为纯半透明底色（低性能设备或用户不想看模糊时使用）。
    /// </summary>
    [ObservableProperty] public partial bool EnableSurfaceAcrylic { get; set; } = true;
    /// <summary>
    /// UI 磨砂玻璃的着色不透明度：越小越透（磨砂感越强）。
    /// </summary>
    [ObservableProperty] public partial double SurfaceAcrylicOpacity { get; set; } = 0.5;
    /// <summary>
    /// UI 磨砂玻璃的着色。跟随主题时可留空（空 = 由控件按主题自动取色）。
    /// </summary>
    [ObservableProperty] public partial Color SurfaceAcrylicTintColor { get; set; } = Colors.Transparent;
    [ObservableProperty] public partial double AppScale { get; set; } = 1.0;
    [ObservableProperty] public partial double TabWindowWidth { get; set; }
    [ObservableProperty] public partial double TabWindowHeight { get; set; }
    [ObservableProperty] public partial bool HasTabWindowSize { get; set; }
    [ObservableProperty] public partial List<WidgetLayoutData> WidgetLayout { get; set; } = [];
    [ObservableProperty] public partial List<GameListWidgetEntry> HomePageItems { get; set; } = [];

    /// <summary>
    /// LauncherX 外壳已是唯一形态（沉浸背景 + 底部悬浮胶囊导航），不再提供开关。
    /// 保留为只读恒真属性，仅为兼容既有引用；旧配置里的同名键会被忽略。
    /// </summary>
    public bool EnableLauncherXShell => true;

    /// <summary>LauncherX 外壳下，底部导航条是否显示"开始游戏"按钮。</summary>
    [ObservableProperty] public partial bool LauncherXDockShowLaunchButton { get; set; } = true;

    /// <summary>没有背景图时，是否用渐变作为沉浸底（关闭则用单色）。</summary>
    [ObservableProperty] public partial bool LauncherXBackdropUseGradient { get; set; } = true;

    /// <summary>渐变起点色（左上）。</summary>
    [ObservableProperty] public partial Color LauncherXBackdropStartColor { get; set; } = Color.Parse("#161616");

    /// <summary>渐变终点色（右下）。</summary>
    [ObservableProperty] public partial Color LauncherXBackdropEndColor { get; set; } = Color.Parse("#050505");

    /// <summary>单色模式下的底色。</summary>
    [ObservableProperty] public partial Color LauncherXBackdropSolidColor { get; set; } = Color.Parse("#0A0A0A");

    [ObservableProperty] public partial string? LaunchPageInstanceFolderPath { get; set; }
    [ObservableProperty] public partial MinecraftAccount? UsingMinecraftMinecraftAccount { get; set; }
    [ObservableProperty] public partial BedrockAccount? UsingBedrockAccount { get; set; }
    [ObservableProperty] public partial MinecraftFolderEntry? DefaultMinecraftFolder { get; set; }
    [ObservableProperty] public partial Dictionary<int, string> JavaVersionDefaultPaths { get; set; } = new();
    [ObservableProperty] public partial ShortcutConfig Shortcuts { get; set; } = new();
}
