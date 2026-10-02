using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using MChub.Bedrock.Standard.Interface;
using MChub.Bedrock.Standard.Manifest;
using MChub.Localization;

namespace MChub.Bedrock.MacOS;

/// <summary>
/// macOS 上的基岩版启动实现。基岩版没有 macOS 原生客户端，因此这里通过 Wine 系运行时
/// （Apple Game Porting Toolkit / CrossOver / Whisky / Homebrew wine）运行 Windows GDK 版。
/// 与 Linux 不同：macOS 侧没有 WineGDK/Proton 那一整套联网登录补丁，因此 v1 只负责
/// 刷新令牌写入、GameInput、GamePatch 与数据隔离，在线登录能否成功取决于所用运行时的兼容性。
/// </summary>
public sealed class BedrockLaunch : IBedrockLaunch
{
    private readonly BedrockInstanceConfig _instanceConfig;
    private readonly MacBedrockRuntimeResolver _runtimeResolver;

    public BedrockLaunch(BedrockInstanceConfig instanceConfig,
        MacBedrockRuntimeResolver? runtimeResolver = null)
    {
        _instanceConfig = instanceConfig ?? throw new ArgumentNullException(nameof(instanceConfig));
        _runtimeResolver = runtimeResolver ?? new MacBedrockRuntimeResolver();
    }

    public override async Task Launch(CancellationToken cancellationToken)
    {
        MacBedrockRuntimeResolver.EnsureSupportedPlatform();
        if (_instanceConfig.BuildType != BedrockBuildType.GDK)
            throw new PlatformNotSupportedException(CommonLanguageManager.Instance.bedrockLaunch_macGdkOnly.CurrentValue());

        var executablePath = Path.GetFullPath(Path.Combine(_instanceConfig.InstancePath, "Minecraft.Windows.exe"));
        if (!File.Exists(executablePath))
            throw new FileNotFoundException(CommonLanguageManager.Instance.bedrockLaunch_missingMinecraftExe.CurrentValue(), executablePath);

        var runtime = _runtimeResolver.Resolve(message =>
        {
            Log(BedrockLogLevel.Information, message);
            UpdateProgress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_statusFormat.CurrentValue(), message), null);
        });
        Log(BedrockLogLevel.Information,
            string.Format(CommonLanguageManager.Instance.bedrockLaunch_macWineRuntimeReady.CurrentValue(), runtime.WineBinary));

        if (runtime.Bundled is { } bundled)
        {
            Log(BedrockLogLevel.Information,
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_macBundledRuntimeInUse.CurrentValue(), bundled.Root));
            await EnsureXodusServiceAsync(bundled, cancellationToken).ConfigureAwait(false);
        }

        await EnsurePrefixAsync(runtime, cancellationToken).ConfigureAwait(false);
        if (Authentication != null)
            await SetRefreshTokenAsync(runtime, Authentication.RefreshToken, cancellationToken).ConfigureAwait(false);
        else
            Log(BedrockLogLevel.Warning, CommonLanguageManager.Instance.bedrockLaunch_macNoXboxAccountSkipped.CurrentValue());

        await EnsureGameInputAsync(runtime, cancellationToken).ConfigureAwait(false);
        await EnsureGamePatchAsync(executablePath, cancellationToken).ConfigureAwait(false);
        MacBedrockDataIsolation.Prepare(_instanceConfig, (message, level) => Log(level, message));
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = CreateStartInfo(runtime, MacBedrockRuntime.ToWinePath(executablePath),
            _instanceConfig.InstancePath, ParseArguments(_instanceConfig.LaunchArguments).ToArray());
        if (_instanceConfig.EnableCreatorEditor)
            startInfo.ArgumentList.Add("minecraft://creator/?Editor=true");
        ApplyRuntimeEnvironment(startInfo, runtime);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Information);
        process.ErrorDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Error);

        Log(BedrockLogLevel.Information,
            string.Format(CommonLanguageManager.Instance.bedrockLaunch_macLaunchingWithWine.CurrentValue(), runtime.WineBinary, runtime.PrefixPath));
        if (!process.Start()) throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_macWineStartFailed.CurrentValue());

        MinecraftProcess = process;
        ProcessStarted?.Invoke(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Log(BedrockLogLevel.Information, string.Format(CommonLanguageManager.Instance.bedrockLaunch_macWineStarted.CurrentValue(), process.Id));
        UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_gameLaunchCommandSubmitted.CurrentValue(), 100);
        LaunchFinish?.Invoke();
    }

    public override Process GetProcess() => MinecraftProcess ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_gameNotStarted.CurrentValue());

    /// <summary>
    /// 组装 Wine 调用参数。GPTK 的包装脚本要求把前缀作为第一个参数传入，
    /// 裸 wine64 / CrossOver / Whisky 则依赖 <c>WINEPREFIX</c> 环境变量。
    /// </summary>
    private static ProcessStartInfo CreateStartInfo(MacBedrockRuntime runtime, string executable,
        string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.WineBinary,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (runtime.PrefixAsArgument) startInfo.ArgumentList.Add(runtime.PrefixPath);
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static void ApplyRuntimeEnvironment(ProcessStartInfo startInfo, MacBedrockRuntime runtime)
    {
        startInfo.Environment["WINEPREFIX"] = runtime.PrefixPath;
        if (runtime.Bundled is { } bundled)
        {
            // 随应用分发的 WineGDK：覆盖 d3d11/dxgi/d3d12 与 xgameruntime，并让 winevulkan 找到 MoltenVK。
            foreach (var (key, value) in WineEnvironment.Create(bundled, runtime.PrefixPath))
                startInfo.Environment[key] = value;
        }

        // 第三方运行时（GPTK/CrossOver/Whisky）下不设置 WINEDLLOVERRIDES，避免禁用其 D3DMetal 后端。
        startInfo.Environment["MICROSOFT_WINDOWSAPPRUNTIME_BOOTSTRAP_INITIALIZE_SHOWUI"] = "0";
        startInfo.Environment["MICROSOFT_WINDOWSAPPRUNTIME_BOOTSTRAP_INITIALIZE_FAILFAST"] = "0";
        startInfo.Environment["MICROSOFT_WINDOWSAPPRUNTIME_DEPLOYMENT_INITIALIZE_ONERRORSHOWUI"] = "0";
    }

    private async Task EnsurePrefixAsync(MacBedrockRuntime runtime, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(runtime.System32, "kernel32.dll"))) return;

        cancellationToken.ThrowIfCancellationRequested();
        Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macPreparingWinePrefix.CurrentValue());
        UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_macPreparingWinePrefix.CurrentValue(), null);
        Directory.CreateDirectory(runtime.PrefixPath);

        var startInfo = CreateStartInfo(runtime, "wineboot", runtime.PrefixPath, "-u");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        ApplyRuntimeEnvironment(startInfo, runtime);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Information);
        process.ErrorDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Warning);
        if (!process.Start())
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_macWineStartFailed.CurrentValue());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macWinePrefixReady.CurrentValue());
    }

    private void ForwardLog(string? message, BedrockLogLevel level)
    {
        if (!string.IsNullOrEmpty(message)) Log(level, message);
    }

    private void Log(BedrockLogLevel level, string message) => LogReceived?.Invoke(message, level);

    private static string FormatBytes(double bytes)
    {
        var units = new[] { "B", "KiB", "MiB", "GiB" };
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:F0} {units[unit]}" : $"{bytes:F1} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Format(CommonLanguageManager.Instance.bedrockLaunch_durationHoursFormat.CurrentValue(), (int)duration.TotalHours, duration.Minutes)
        : duration.TotalMinutes >= 1
            ? string.Format(CommonLanguageManager.Instance.bedrockLaunch_durationMinutesFormat.CurrentValue(), (int)duration.TotalMinutes, duration.Seconds)
            : string.Format(CommonLanguageManager.Instance.bedrockLaunch_durationSecondsFormat.CurrentValue(), Math.Max(1, (int)duration.TotalSeconds));

    private async Task EnsureGamePatchAsync(string executablePath, CancellationToken cancellationToken)
    {
        var instancePath = Path.GetDirectoryName(executablePath)!;
        var preload = Path.Combine(instancePath, "preload");
        var patch = Path.Combine(preload, "mcpatcher_core.dll");
        if (File.Exists(patch)) return;

        const string url = "https://github.com/RoundMCDev/ProtonGDK-Release/releases/download/Release10-32/GamePatch.zip";
        var archivePath = Path.Combine(MacBedrockPaths.CacheRoot, "GamePatch.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        try
        {
            if (File.Exists(archivePath) && !IsValidZipArchive(archivePath))
            {
                Log(BedrockLogLevel.Warning, CommonLanguageManager.Instance.bedrockLaunch_macGamePatchCacheCorrupted.CurrentValue());
                File.Delete(archivePath);
            }

            if (!File.Exists(archivePath))
            {
                Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macDownloadingGamePatch.CurrentValue());
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MChub-Bedrock-MacOS/1.0");
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var temporaryArchivePath = archivePath + ".download";
                {
                    await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await using var output = new FileStream(temporaryArchivePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var buffer = new byte[1024 * 64];
                    long downloadedBytes = 0;
                    var stopwatch = Stopwatch.StartNew();
                    var lastReport = TimeSpan.Zero;
                    var lastLoggedPercentage = -1;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        downloadedBytes += read;
                        if (stopwatch.Elapsed - lastReport < TimeSpan.FromMilliseconds(250) &&
                            !(totalBytes > 0 && downloadedBytes == totalBytes)) continue;

                        var speed = stopwatch.Elapsed.TotalSeconds > 0
                            ? downloadedBytes / stopwatch.Elapsed.TotalSeconds
                            : 0;
                        var percentage = totalBytes > 0
                            ? downloadedBytes * 100d / totalBytes
                            : (double?)null;
                        TimeSpan? remaining = speed > 0 && totalBytes > 0
                            ? TimeSpan.FromSeconds(Math.Max(0, totalBytes - downloadedBytes) / speed)
                            : null;
                        var text = totalBytes > 0
                            ? string.Format(CommonLanguageManager.Instance.bedrockLaunch_downloadingPatchWithProgress.CurrentValue(),
                                  percentage, FormatBytes(downloadedBytes), FormatBytes(totalBytes), FormatBytes(speed)) +
                              (remaining is { } time ? string.Format(CommonLanguageManager.Instance.bedrockLaunch_remainingSuffix.CurrentValue(), FormatDuration(time)) : string.Empty)
                            : string.Format(CommonLanguageManager.Instance.bedrockLaunch_downloadingPatchNoProgress.CurrentValue(), FormatBytes(downloadedBytes), FormatBytes(speed));
                        UpdateProgress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_statusFormat.CurrentValue(), text), percentage);
                        var integerPercentage = totalBytes > 0 ? (int)percentage!.Value : -1;
                        if (integerPercentage != lastLoggedPercentage)
                        {
                            Log(BedrockLogLevel.Information, text);
                            lastLoggedPercentage = integerPercentage;
                        }
                        lastReport = stopwatch.Elapsed;
                    }
                }
                UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_gamePatchDownloadComplete.CurrentValue(), 100);
                if (!IsValidZipArchive(temporaryArchivePath))
                    throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_macGamePatchCacheCorrupted.CurrentValue());
                File.Move(temporaryArchivePath, archivePath, true);
            }

            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(item =>
                item.FullName.Replace('\\', '/').Equals("gdk/mcpatcher_core.dll",
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null) throw new InvalidDataException(CommonLanguageManager.Instance.bedrockLaunch_gamePatchMissingMcpatcher.CurrentValue());

            Directory.CreateDirectory(preload);
            await using var source = entry.Open();
            await using var destination = new FileStream(patch, FileMode.Create, FileAccess.Write,
                FileShare.Read, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macGamePatchDeployed.CurrentValue());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log(BedrockLogLevel.Warning, string.Format(CommonLanguageManager.Instance.bedrockLaunch_macGamePatchUnavailable.CurrentValue(), exception.Message));
        }
    }

    private static bool IsValidZipArchive(string path)
    {
        try
        {
            using var _ = ZipFile.OpenRead(path);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task EnsureGameInputAsync(MacBedrockRuntime runtime, CancellationToken cancellationToken)
    {
        var installer = Path.Combine(_instanceConfig.InstancePath, "Installers", "GameInputRedist.msi");
        if (!File.Exists(installer)) return;

        var marker = Path.Combine(runtime.PrefixPath, ".portal-gameinput-installed");
        if (File.Exists(marker) || HasGameInput(runtime)) return;

        cancellationToken.ThrowIfCancellationRequested();
        Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macInstallingGameInputViaWine.CurrentValue());
        UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_statusInstallingGameInput.CurrentValue(), null);
        var startInfo = CreateStartInfo(runtime, "msiexec", runtime.WineRoot,
            "/i", MacBedrockRuntime.ToWinePath(installer), "/qn");
        ApplyRuntimeEnvironment(startInfo, runtime);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var errorBuffer = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            Log(BedrockLogLevel.Information, args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            errorBuffer.AppendLine(args.Data);
            Log(BedrockLogLevel.Warning, args.Data);
        };
        if (!process.Start()) throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_cannotStartGameInputInstaller.CurrentValue());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var cancellation = timeout.Token.Register(() => KillProcess(process));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            if (cancellationToken.IsCancellationRequested)
            {
                Log(BedrockLogLevel.Warning, CommonLanguageManager.Instance.bedrockLaunch_macGameInputInstallCancelled.CurrentValue());
                UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_statusGameInputCancelled.CurrentValue(), null);
                throw;
            }

            Log(BedrockLogLevel.Warning, CommonLanguageManager.Instance.bedrockLaunch_macGameInputInstallTimeout.CurrentValue());
            UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_statusGameInputTimeout.CurrentValue(), null);
            return;
        }
        var errorText = errorBuffer.ToString().Trim();
        if (process.ExitCode != 0)
        {
            Log(BedrockLogLevel.Warning,
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_macGameInputInstallFailedContinue.CurrentValue(), process.ExitCode, errorText));
            UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_statusGameInputFailed.CurrentValue(), null);
            return;
        }

        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);
        UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_statusGameInputInstalled.CurrentValue(), 100);
        Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macGameInputInstalled.CurrentValue());
    }

    private static bool HasGameInput(MacBedrockRuntime runtime)
    {
        var x64 = Path.Combine(runtime.DriveC, "Program Files", "Microsoft GameInput", "x64");
        return File.Exists(Path.Combine(x64, "GameInputRedist.dll")) &&
               File.Exists(Path.Combine(x64, "GameInputRedistService.exe")) &&
               File.Exists(Path.Combine(runtime.System32, "GameInputRedist.dll"));
    }

    private async Task SetRefreshTokenAsync(MacBedrockRuntime runtime, string refreshToken,
        CancellationToken cancellationToken)
    {
        var registryFile = Path.Combine(runtime.PrefixPath, $"portal-xbox-{Guid.NewGuid():N}.reg");
        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(registryFile,
            "Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\Software\\Wine\\WineGDK]\n" +
            $"\"RefreshToken\"=\"{EscapeRegistryValue(refreshToken)}\"\n", cancellationToken).ConfigureAwait(false);
        try { File.SetUnixFileMode(registryFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch (PlatformNotSupportedException) { }

        var startInfo = CreateStartInfo(runtime, "reg", runtime.WineRoot,
            "import", MacBedrockRuntime.ToWinePath(registryFile));
        ApplyRuntimeEnvironment(startInfo, runtime);
        try
        {
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_cannotWriteWineGdkConfig.CurrentValue());
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_writeWineGdkConfigFailed.CurrentValue());
            Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macRefreshTokenWritten.CurrentValue());
        }
        finally
        {
            File.Delete(registryFile);
        }
    }

    private static string EscapeRegistryValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static readonly object XodusServiceLock = new();
    private static XodusServiceHost? XodusService;

    /// <summary>
    /// 拉起随应用分发的 xodus-service。它提供 MSA/XSTS/许可，WineGDK 的 xgameruntime 通过
    /// <c>/tmp/xodus.sock</c> 访问，因此必须在游戏进程之前就绪；同一进程内只启动一次。
    /// </summary>
    private async Task EnsureXodusServiceAsync(McbeMacOSRuntime runtime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        XodusServiceHost host;
        lock (XodusServiceLock)
        {
            if (XodusService is { IsRunning: true }) return;
            XodusService = host = new XodusServiceHost(runtime.XodusServiceBinary);
        }

        Log(BedrockLogLevel.Information, CommonLanguageManager.Instance.bedrockLaunch_macXodusServiceStarting.CurrentValue());
        try
        {
            await host.StartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            // 登录后端不可用不阻断启动：游戏仍可离线运行，只是在线登录可能失败。
            Log(BedrockLogLevel.Warning,
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_macXodusServiceFailed.CurrentValue(), exception.Message));
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IEnumerable<string> ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) yield break;
        var current = new StringBuilder();
        var quoted = false;
        foreach (var character in arguments)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length == 0) continue;
                yield return current.ToString();
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        if (quoted) throw new FormatException(CommonLanguageManager.Instance.bedrockLaunch_unclosedQuote.CurrentValue());
        if (current.Length > 0) yield return current.ToString();
    }
}