using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

/// <summary>
/// BTA（Better Than Adventure!）安装条目。
///
/// <para>BTA 不是 Fabric 系装载器：它是基于 Minecraft <c>b1.7.3</c> 的独立客户端，
/// 直接整包替换原版客户端 jar（MultiMC 包里的 <c>org.multimc.jarmod.bta</c> 就是这个意思）。
/// 因此这里记录的是 GitHub release 里客户端 jar 的地址，安装时叠加在 b1.7.3 之上。</para>
/// </summary>
public record BtaInstallEntry : IInstallEntry {
    /// <summary>GitHub release tag，形如 <c>v7.3</c>。</summary>
    public string TagName { get; init; }

    /// <summary>BTA 版本号，形如 <c>7.3</c>。</summary>
    public string LoaderVersion { get; init; }

    /// <summary>客户端 jar 的下载地址（直接指向 release 资产）。</summary>
    public string ClientJarUrl { get; init; }

    /// <summary>BTA 所基于的原版版本，固定 <c>b1.7.3</c>。</summary>
    [JsonIgnore] public string McVersion => BtaInstallerDefaults.BaseMcVersion;

    [JsonIgnore] public string BuildVersion => LoaderVersion;

    [JsonIgnore] public string DisplayVersion => LoaderVersion;

    [JsonIgnore] public ModLoaderType ModLoaderType => ModLoaderType.Bta;

    [JsonIgnore] public string Description => TagName;
}

/// <summary>
/// Rift 安装条目。
///
/// <para>Rift 是 1.13 / 1.13.2 时代的轻量装载器，官方 GitHub 已归档、服务器下线，
/// 现存的可靠分发只剩 CurseForge 上的 MultiMC 实例包（<c>rift-1.0.4-66.zip</c>）。
/// 安装时从这个包里提取内嵌的版本 JSON 与库，属于「尽力而为」。</para>
/// </summary>
public record RiftInstallEntry : IInstallEntry {
    /// <summary>Rift 版本号，形如 <c>1.0.4-66</c>。</summary>
    public string LoaderVersion { get; init; }

    /// <summary>CurseForge 上的 MultiMC 实例包直链。</summary>
    public string PackageUrl { get; init; }

    /// <summary>目标原版版本（1.13 / 1.13.2）。</summary>
    public string TargetMcVersion { get; init; }

    [JsonIgnore] public string McVersion => TargetMcVersion;

    [JsonIgnore] public string BuildVersion => LoaderVersion;

    [JsonIgnore] public string DisplayVersion => LoaderVersion;

    [JsonIgnore] public ModLoaderType ModLoaderType => ModLoaderType.Rift;

    [JsonIgnore] public string Description => LoaderVersion;
}

/// <summary>
/// LiteLoader 安装条目（1.5.2 ~ 1.12.2）。
///
/// <para>LiteLoader 不是 Fabric 系也不替换整包：它是在原版之上叠加
/// <c>net.minecraft.launchwrapper.Launch</c> + 一个 <c>--tweakClass</c>，
/// 并把 <c>com.mumfrey:liteloader</c> 与若干库挂到 classpath。
/// 版本数据来自官方 <c>versions.json</c>，构建项里自带 tweakClass / libraries / 仓库地址。</para>
/// </summary>
public record LiteLoaderInstallEntry : IInstallEntry {
    /// <summary>目标原版版本，如 <c>1.12.2</c>。</summary>
    public string TargetMcVersion { get; init; }

    /// <summary>LiteLoader 版本号，如 <c>1.12.2-SNAPSHOT</c>。</summary>
    public string LoaderVersion { get; init; }

    /// <summary>构建标识，如 <c>1.12.2-SNAPSHOT-r4CC2BB0-b4-4</c>；旧版本可能为空。</summary>
    public string Build { get; init; }

    /// <summary>启动 tweak 类，如 <c>com.mumfrey.liteloader.launch.LiteLoaderTweaker</c>。</summary>
    public string TweakClass { get; init; }

    /// <summary>该构建声明的依赖库（原始 JSON 数组文本，逐项直接并进版本 JSON）。</summary>
    public string LibrariesJson { get; init; }

    /// <summary>LiteLoader 自身的 maven 仓库基址（jar 从这里取）。</summary>
    public string MavenBaseUrl { get; init; }

    [JsonIgnore] public string McVersion => TargetMcVersion;

    [JsonIgnore] public string BuildVersion => string.IsNullOrEmpty(Build) ? LoaderVersion : Build;

    [JsonIgnore] public string DisplayVersion => LoaderVersion;

    [JsonIgnore] public ModLoaderType ModLoaderType => ModLoaderType.LiteLoader;

    [JsonIgnore] public string Description => string.IsNullOrEmpty(Build) ? "Release" : "Snapshot";
}

/// <summary>BTA 安装常量（供 Core/UI 复用，避免各处散落字面量）。</summary>
public static class BtaInstallerDefaults {
    /// <summary>BTA 始终基于该原版版本。</summary>
    public const string BaseMcVersion = "b1.7.3";

    /// <summary>BTA release 仓库。</summary>
    public const string ReleasesRepository = "Better-than-Adventure/bta-download-repo";
}
