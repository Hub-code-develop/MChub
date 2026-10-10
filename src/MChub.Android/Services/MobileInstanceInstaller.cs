using System.ComponentModel;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using MChub.Core.Const;
using MChub.Core.Minecraft;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Core.Minecraft.Models;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace MChub.Mobile.Services;

/// <summary>一次设备内安装的结果。</summary>
/// <param name="Success">是否装完（不含"装完就能玩"的保证）。</param>
/// <param name="Cancelled">是否被用户取消。</param>
/// <param name="InstanceId">实例名（= 实例目录名）。</param>
/// <param name="Message">失败原因，成功时为 null。</param>
internal sealed record MobileInstallResult(bool Success, bool Cancelled, string InstanceId, string? Message);

/// <summary>
/// 设备内安装：把「版本清单 → 加载器版本 → 下载安装 → 生成实例」整条链路放在手机本机完成，
/// **不依赖桌面端**。业务逻辑全部复用 <c>MChub.Core</c> 里桌面端同一套
/// （<see cref="MinecraftInstallationTasks"/>），移动端只负责：
///   ① 提供本机游戏目录；② 提供设备内的「java 命令」（Forge 系安装处理器要用，见 MobileInstallerJava）；
///   ③ 把任务进度翻译成界面可展示的文本。
///
/// <p>下载器（版本 JSON / 库 / 资源文件）来自 MinecraftLaunch，其并发、下载源与 UA
/// 在 <see cref="MobileBootstrap"/> 里已按配置引导过。</p>
/// </summary>
internal static class MobileInstanceInstaller
{
    /// <summary>该加载器能否在设备上安装（Forge 系要跑安装处理器，需要自带的 java 命令）。</summary>
    public static bool CanInstallOnDevice(LoaderKind kind)
        => !MinecraftInstallationTasks.RequiresJavaRuntime([kind]) || MobileInstallerJava.IsAvailable;

    /// <summary>拉取原版版本清单（正式版 + 快照 + 旧版），按发布时间倒序。</summary>
    public static async Task<IReadOnlyList<VersionManifestEntry>> LoadVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await VanillaInstaller.EnumerableMinecraftAsync(cancellationToken);
        return entries?
                   .Where(entry => entry is { Id.Length: > 0, Url.Length: > 0 })
                   .OrderByDescending(entry => entry.ReleaseTime)
                   .ToList()
               ?? [];
    }

    /// <summary>拉某个加载器在该游戏版本下的可用版本列表。</summary>
    public static async Task<IReadOnlyList<IInstallEntry>> LoadLoaderVersionsAsync(LoaderKind kind,
        string minecraftVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return kind switch
        {
            LoaderKind.Fabric => (await FabricInstaller.EnumerableFabricAsync(minecraftVersion))
                .Cast<IInstallEntry>().ToList(),
            LoaderKind.Forge => (await ForgeInstaller.EnumerableForgeAsync(minecraftVersion))
                .Cast<IInstallEntry>().ToList(),
            LoaderKind.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(minecraftVersion, true))
                .Cast<IInstallEntry>().ToList(),
            LoaderKind.Quilt => (await QuiltInstaller.EnumerableQuiltAsync(minecraftVersion))
                .Cast<IInstallEntry>().ToList(),
            LoaderKind.OptiFine => (await OptifineInstaller.EnumerableOptifineAsync(minecraftVersion))
                .Cast<IInstallEntry>().ToList(),
            _ => []
        };
    }

    /// <summary>加载器版本号（Quilt 的展示与其他加载器不同，复用桌面端同一实现）。</summary>
    public static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry)
        => MinecraftInstallationTasks.GetLoaderVersion(kind, entry);

    /// <summary>推荐实例名（与桌面端「新建实例」的命名一致：<c>1.20.1</c> / <c>1.20.1 Fabric-0.16.5</c>）。</summary>
    public static string RecommendedInstanceId(VersionManifestEntry vanilla,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> loaders)
    {
        if (loaders.Count == 0)
            return vanilla.Id;

        var names = loaders.Select(pair => $"{pair.Key}-{GetLoaderVersion(pair.Key, pair.Value)}");
        return $"{vanilla.Id} {string.Join(" + ", names)}";
    }

    /// <summary>该游戏版本要求的 Java 主版本。</summary>
    public static int GetRequiredJavaMajorVersion(string minecraftVersion)
        => MinecraftInstallationTasks.GetRecommendedJavaVersion(minecraftVersion);

    /// <summary>
    /// 在设备上装出这个实例。失败时返回原因而不是抛出，界面只负责展示。
    /// </summary>
    public static async Task<MobileInstallResult> InstallAsync(VersionManifestEntry vanilla, string instanceId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> loaders,
        IProgress<MobileInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        MinecraftFolderEntry folder;
        string? javaPath;

        try
        {
            folder = MobileGameStorage.EnsureRegistered();

            // Forge / NeoForge / Quilt / OptiFine 的安装要跑 Java 安装处理器：
            // 必须交给我们设备内的 java 命令，绝不能让上层去装桌面 JDK。
            if (MinecraftInstallationTasks.RequiresJavaRuntime(loaders.Keys))
            {
                javaPath = await MobileInstallerJava.EnsureAsync(GetRequiredJavaMajorVersion(vanilla.Id),
                    progress, cancellationToken);
            }
            else
            {
                javaPath = null;
            }
        }
        catch (OperationCanceledException)
        {
            return new MobileInstallResult(false, true, instanceId, null);
        }
        catch (Exception exception)
        {
            return new MobileInstallResult(false, false, instanceId, exception.Message);
        }

        var task = MinecraftInstallationTasks.CreateInstallationTask(vanilla, folder, instanceId, loaders, javaPath);
        var reporter = new TaskProgressReporter(task, progress);
        reporter.Attach();

        try
        {
            task.Start();
            await task.Completion;

            return task.Status switch
            {
                ManagedTaskStatus.Completed => new MobileInstallResult(true, false, instanceId, null),
                ManagedTaskStatus.Cancelled => new MobileInstallResult(false, true, instanceId, null),
                _ => new MobileInstallResult(false, false, instanceId,
                    DescribeFailure(task))
            };
        }
        catch (Exception exception)
        {
            return new MobileInstallResult(false, false, instanceId, exception.Message);
        }
        finally
        {
            reporter.Detach();
        }
    }

    /// <summary>安装完（或取消后）重扫实例，让界面立刻看到新实例。</summary>
    public static void RefreshInstances()
    {
        try
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
        }
        catch (Exception)
        {
            // 重扫失败不该盖掉安装结果本身。
        }
    }

    /// <summary>任务树里最深处的那条描述比根任务的"正在安装"信息量更大（会带下载速度/步骤名）。</summary>
    private static string DescribeFailure(ManagedTask task)
    {
        var exception = task.Exception;
        var message = exception?.Message;
        if (!string.IsNullOrWhiteSpace(message))
            return message;

        var description = task.DisplayDescription;
        return string.IsNullOrWhiteSpace(description) ? "安装失败（未提供原因）" : description;
    }

    /// <summary>把 <see cref="ManagedTask"/> 的进度翻译成界面的「阶段 + 百分比」。</summary>
    private sealed class TaskProgressReporter(ManagedTask task, IProgress<MobileInstallProgress>? progress)
    {
        public void Attach()
        {
            if (progress is not null)
                task.PropertyChanged += OnPropertyChanged;
        }

        public void Detach() => task.PropertyChanged -= OnPropertyChanged;

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(ManagedTask.DisplayDescription) or nameof(ManagedTask.Description)
                or nameof(ManagedTask.AggregateProgress) or nameof(ManagedTask.Progress)))
            {
                return;
            }

            var stage = task.DisplayDescription ?? task.Description ?? "正在安装…";
            progress!.Report(new MobileInstallProgress(stage, task.AggregateProgress ?? task.Progress ?? 0));
        }
    }
}
