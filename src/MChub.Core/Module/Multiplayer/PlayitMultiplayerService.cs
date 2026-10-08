using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MChub.Core.Const;
using MChub.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace MChub.Core.Module.Multiplayer;

public enum PlayitStatus
{
    MissingComponent,
    Stopped,
    Downloading,
    Starting,
    Running,
    Error
}

/// <summary>Play It 公网联机隧道信息。</summary>
public sealed record PlayitTunnel(string Id, string Kind, int LocalPort, string? PublicAddress, bool Enabled,
    string? OfflineReason);

public sealed class PlayitState
{
    public PlayitStatus Status { get; set; } = PlayitStatus.MissingComponent;
    public bool BinaryInstalled { get; set; }
    public int? DownloadProgress { get; set; }
    public List<PlayitTunnel> Tunnels { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public bool IsAgentConnected { get; set; }
    public bool IsRefreshingTunnels { get; set; }
    public DateTimeOffset? LastTunnelRefresh { get; set; }
}

/// <summary>
/// Play It 内嵌多人联机服务。
/// 负责下载 playitd 内核、启停代理进程，并通过 playit.gg 的 /v1/tunnels/list 接口读取隧道公网地址。
/// </summary>
public sealed class PlayitMultiplayerService
{
    public const string ApiBase = "https://api.playit.gg";
    public const string ReleaseBase = "https://github.com/hub-code-develop/playit-connect-for-sl/releases/latest/download";
    public const string DashboardUrl = "https://playit.gg/account/agents";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    private Process? _daemon;
    private bool _disposed;

    private static string Root => Path.Combine(ConfigPath.UserDataRootPath, "Multiplayer", "PlayIt");
    private static string BinaryPath => Path.Combine(Root, BinaryName);
    private static string BinaryName => OperatingSystem.IsWindows() ? "playitd.exe" : "playitd";

    private PlayitMultiplayerService()
    {
    }

    public static PlayitMultiplayerService Instance { get; } = new();

    public event EventHandler? StateChanged;

    public PlayitState State { get; } = new();

    public static bool IsCurrentPlatformSupported => GetRidPlatform() is not null;

    /// <summary>当前本地是否已存在可用的 playitd 内核。</summary>
    public bool IsInstalled()
    {
        try
        {
            return File.Exists(BinaryPath) && new FileInfo(BinaryPath).Length > 1024 * 1024;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureInstalledAsync(CancellationToken cancellationToken, Action<float>? onProgress = null)
    {
        if (IsInstalled()) return;
        Directory.CreateDirectory(Root);
        State.Status = PlayitStatus.Downloading;
        State.BinaryInstalled = false;
        State.DownloadProgress = 0;
        Publish();
        var url = ReleaseUrl();
        Logger.Info($"[PlayIt] Downloading playitd from {url}");
        var temp = Root + ".tmp";
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, true);
            var buffer = new byte[128 * 1024];
            long read = 0;
            while (true)
            {
                var n = await source.ReadAsync(buffer, cancellationToken);
                if (n == 0) break;
                await target.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                read += n;
                if (total > 0)
                {
                    State.DownloadProgress = (int)(read * 100 / total);
                    onProgress?.Invoke((float)read / total);
                }
                if (n % (64 * 1024) == 0) Publish();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error($"[PlayIt] Download failed: {ex}");
            State.Status = PlayitStatus.Error;
            State.ErrorMessage =
                string.Format(CommonLanguageManager.Instance.multiplayer_playitDownloadFailed.CurrentValue(), ex.Message);
            TryDelete(temp);
            Publish();
            throw new IOException(State.ErrorMessage, ex);
        }
        TryDelete(temp);
        File.Copy(temp, BinaryPath, true);
        if (!OperatingSystem.IsWindows()) SetExecutable(BinaryPath);

        State.BinaryInstalled = true;
        State.Status = PlayitStatus.Stopped;
        State.DownloadProgress = 100;
        Publish();
    }

    public async Task StartAsync(string secret, CancellationToken cancellationToken)
    {
        await EnsureInstalledAsync(cancellationToken);
        if (State.Status == PlayitStatus.Error) return;
        StopDaemonProcess();
        State.Status = PlayitStatus.Starting;
        State.ErrorMessage = null;
        State.IsAgentConnected = false;
        Publish();
        await Task.Run(() =>
        {
            var socket = Path.Combine(Root, "playit.sock").Replace("\\", "/");
            var log = Path.Combine(Root, "playit.log");
            var psi = new ProcessStartInfo
            {
                FileName = BinaryPath,
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("--secret");
            psi.ArgumentList.Add(secret.Trim());
            psi.ArgumentList.Add("--socket-path");
            psi.ArgumentList.Add(socket);
            psi.ArgumentList.Add("--log-path");
            psi.ArgumentList.Add(log);
            _daemon = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _daemon.OutputDataReceived += OnDriverOutput;
            _daemon.ErrorDataReceived += OnDriverError;
            _daemon.Exited += OnDaemonExited;
            _daemon.Start();
            _daemon.BeginOutputReadLine();
            _daemon.BeginErrorReadLine();
            Logger.Info($"[PlayIt] Daemon started, socket={socket}, log={log}");
        }, cancellationToken);
        State.Status = PlayitStatus.Running;
        State.IsAgentConnected = true;
        Publish();
    }

    public async Task<List<PlayitTunnel>> ListTunnelsAsync(string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/tunnels/list");
        request.Headers.Authorization = new AuthenticationHeaderValue("Agent-Key", secret.Trim());
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseTunnels(json);
    }

    public void Stop()
    {
        StopDaemonProcess();
        State.Status = IsInstalled() ? PlayitStatus.Stopped : PlayitStatus.MissingComponent;
        State.IsAgentConnected = false;
        Publish();
    }

    private void StopDaemonProcess()
    {
        if (_daemon is null) return;
        try
        {
            if (!_daemon.HasExited) _daemon.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[PlayIt] Failed to stop daemon: {ex}");
        }
        _daemon.OutputDataReceived -= OnDriverOutput;
        _daemon.ErrorDataReceived -= OnDriverError;
        _daemon.Exited -= OnDaemonExited;
        _daemon.Dispose();
        _daemon = null;
    }

    private void OnDaemonExited(object? sender, EventArgs e)
    {
        _daemon = null;
        State.IsAgentConnected = false;
    }

    private void OnDriverOutput(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data)) Logger.Info($"[PlayIt] {e.Data}");
    }

    private void OnDriverError(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        Logger.Error($"[PlayIt] {e.Data}");
        State.ErrorMessage = e.Data;
    }

    private static List<PlayitTunnel> ParseTunnels(string json)
    {
        var tunnels = new List<PlayitTunnel>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("success", out var success)) return tunnels;
        if (!success.TryGetProperty("tunnels", out var list)) return tunnels;
        foreach (var item in list.EnumerateArray())
        {
            var id = GetString(item, "id") ?? string.Empty;
            var type = GetString(item, "tunnel_type") ?? string.Empty;
            string kind;
            switch (type)
            {
                case "minecraft-java": kind = "java"; break;
                case "minecraft-bedrock": kind = "bedrock"; break;
                default: continue;
            }
            var enabled = !item.TryGetProperty("user_enabled", out var ue) || ue.GetBoolean();
            var localPort = ReadLocalPort(item, kind);
            var publicAddress = ReadPublicAddress(item);
            var offline = ReadOfflineReason(item);
            tunnels.Add(new PlayitTunnel(id, kind, localPort, publicAddress, enabled, offline));
        }
        return tunnels;
    }

    private static int ReadLocalPort(JsonElement tunnel, string kind)
    {
        if (tunnel.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.Object &&
            origin.TryGetProperty("type", out var t) && t.GetString() == "agent" &&
            origin.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("config_data", out var cfg) && cfg.ValueKind == JsonValueKind.Object &&
            cfg.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                var name = GetString(field, "name") ?? string.Empty;
                var value = GetString(field, "value");
                if (value is null) continue;
                var nameLower = name.ToLowerInvariant();
                if (!nameLower.Contains("port") && !nameLower.Contains("local")) continue;
                if (int.TryParse(value, out var port)) return port;
            }
        }
        return kind == "bedrock" ? 19132 : 25565;
    }

    private static string? ReadPublicAddress(JsonElement tunnel)
    {
        if (tunnel.TryGetProperty("public_allocations", out var allocs) && allocs.ValueKind == JsonValueKind.Array)
        {
            foreach (var alloc in allocs.EnumerateArray())
            {
                var aType = GetString(alloc, "type");
                if (aType is null || !alloc.TryGetProperty("details", out var det) || det.ValueKind != JsonValueKind.Object)
                    continue;
                if (aType == "HostnameRouting")
                {
                    var host = GetString(det, "hostname");
                    if (!string.IsNullOrWhiteSpace(host)) return host;
                }
                if (aType == "PortAllocation")
                {
                    var host = GetString(det, "ip_hostname") ?? GetString(det, "auto_domain");
                    if (host is "0.0.0.0" or "127.0.0.1") host = GetString(det, "auto_domain");
                    var port = det.TryGetProperty("port", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
                    if (!string.IsNullOrWhiteSpace(host)) return port > 0 ? $"{host}:{port}" : host;
                }
            }
        }
        if (tunnel.TryGetProperty("connect_addresses", out var connects) && connects.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in connects.EnumerateArray())
            {
                if (GetString(c, "type") is not ({} cType and ("addr4" or "addr6" or "domain" or "ip4" or "ip6"))) continue;
                if (!c.TryGetProperty("value", out var val) || val.ValueKind != JsonValueKind.Object) continue;
                var address = GetString(val, "address") ?? GetString(val, "hostname") ?? GetString(val, "ip");
                if (!string.IsNullOrWhiteSpace(address)) return address;
            }
        }
        return null;
    }

    private static string? ReadOfflineReason(JsonElement tunnel)
    {
        if (tunnel.TryGetProperty("offline_reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var r in reasons.EnumerateArray())
            {
                var s = r.GetString();
                if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
        return null;
    }

    private static string ReleaseUrl()
    {
        var file = GetRidPlatform() switch
        {
            "macos-arm64" => "playitd-macos-arm64",
            "macos-x64" => "playitd-macos-x86_64",
            "linux-arm64" => "playitd-linux-arm64",
            "linux-x64" => "playitd-linux-amd64",
            "win-arm64" => "playitd-windows-arm64.exe",
            "win-x64" => "playitd-windows-x86_64.exe",
            _ => throw new PlatformNotSupportedException()
        };
        return $"{ReleaseBase}/{file}";
    }

    private static string? GetRidPlatform()
    {
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "macos-arm64" : "macos-x64";
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        if (OperatingSystem.IsWindows())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        return null;
    }

    private static void SetExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var info = new FileInfo(path);
            info.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private void Publish()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_daemon is { HasExited: false }) _daemon.Kill();
        }
        catch
        {
        }
        _daemon?.Dispose();
        _daemon = null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}