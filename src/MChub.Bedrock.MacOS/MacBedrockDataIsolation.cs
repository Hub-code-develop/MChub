using System.Reflection;
using System.Text;
using System.Text.Json;
using PeNet;
using MChub.Bedrock.Standard.Interface;
using MChub.Bedrock.Standard.Manifest;

namespace MChub.Bedrock.MacOS;

/// <summary>
/// 与 Linux 侧同构的数据隔离准备：把基岩版主程序备份为 portal-original.exe，
/// 注入 Bootstrap 导入项并部署 Preload/Bootstrap 组件，使游戏在 Wine 下把存档
/// 写到实例隔离目录而不是共享的 AppData。
/// </summary>
internal static class MacBedrockDataIsolation
{
    private const string BootstrapName = "MChub.Bootstrap.dll";
    private const string PreloadName = "MChub.Preload.dll";
    private const string OriginalName = "Minecraft.Windows.portal-original.exe";

    public static void Prepare(BedrockInstanceConfig config, Action<string, BedrockLogLevel>? log = null)
    {
        var executable = Path.Combine(config.InstancePath, "Minecraft.Windows.exe");
        var portalFolder = Path.Combine(config.InstancePath, "config", "MChub");
        Directory.CreateDirectory(portalFolder);
        var original = Path.Combine(portalFolder, OriginalName);
        if (!File.Exists(original)) File.Copy(executable, original);
        else File.Copy(original, executable, true);

        PrepareLaunchInfo(config, log);
        DeployResource("MChub.Preload.dll", Path.Combine(config.InstancePath, PreloadName));
        DeployResource("MChub.Bootstrap.dll", Path.Combine(config.InstancePath, BootstrapName));
        WriteConfig(config);
        AddImport(executable, BootstrapName, "Load");
    }

    private static void PrepareLaunchInfo(BedrockInstanceConfig config, Action<string, BedrockLogLevel>? log)
    {
        var output = Path.Combine(config.InstancePath, "config", "MChub", "launch-info");
        Directory.CreateDirectory(output);
        foreach (var file in Directory.EnumerateFiles(output, "*.lang")) File.Delete(file);
        if (!config.EnableLaunchInfo) return;

        var texts = Path.Combine(config.InstancePath, "data", "resource_packs", "vanilla", "texts");
        if (!Directory.Exists(texts)) return;
        var version = string.IsNullOrWhiteSpace(config.LauncherVersion) ? "local-build" : config.LauncherVersion.Trim();
        var copyright = $"©Mojang AB· MChub {version.Replace('\r', ' ').Replace('\n', ' ')}";
        var count = 0;
        foreach (var source in Directory.EnumerateFiles(texts, "*.lang"))
        {
            var content = File.ReadAllText(source, Encoding.UTF8);
            var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
            var found = false;
            for (var i = 0; i < lines.Count; i++)
                if (lines[i].StartsWith("menu.copyright=", StringComparison.Ordinal))
                {
                    lines[i] = $"menu.copyright={copyright}";
                    found = true;
                }

            if (!found) lines.Add($"menu.copyright={copyright}");
            File.WriteAllText(Path.Combine(output, Path.GetFileName(source)), string.Join("\n", lines),
                new UTF8Encoding(false));
            count++;
        }

        log?.Invoke($"Prepared MChub launch information for {count} languages.", BedrockLogLevel.Information);
    }

    private static void WriteConfig(BedrockInstanceConfig config)
    {
        var folder = Path.Combine(config.InstancePath, "config", "MChub");
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        var data = new
        {
            config = new
            {
                isConsole = false,
                isVersionIsolated = !config.EnableLauncherSharedData,
                isDetailedLog = false,
                launchInfoEnabled = config.EnableLaunchInfo,
                folderPolicyString = "portal",
                nativeLogFile = $"native-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log"
            },
            info = new { versionType = config.Type == BedrockInstanceReleaseType.Release ? 1 : 0 }
        };
        File.WriteAllText(Path.Combine(folder, "config.json"), JsonSerializer.Serialize(data));
    }

    private static void DeployResource(string name, string destination)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(file);
    }

    private static void AddImport(string executable, string dll, string function)
    {
        using var stream = new FileStream(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var pe = new PeFile(stream);
        if (pe.ImportedFunctions?.Any(x => string.Equals(x.DLL, dll, StringComparison.OrdinalIgnoreCase)) == true) return;
        pe.AddImport(dll, function);
        pe.Flush();
    }
}