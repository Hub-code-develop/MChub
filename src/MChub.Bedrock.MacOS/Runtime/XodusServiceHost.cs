using System.Diagnostics;
using System.Net.Sockets;

namespace MChub.Bedrock.MacOS;

/// <summary>
/// 托管 <c>xodus-service</c> 进程。它与 Wine 内的 <c>xgameruntime.dll</c> 通过 Xodus IPC 协议通信:
/// 监听 <c>/tmp/xodus.sock</c>,帧格式为 <c>magic(u32 LE) + msg_type(u16) + len(u16) + body</c>。
/// </summary>
public sealed class XodusServiceHost : IAsyncDisposable
{
    /// <summary>macOS 上固定的 socket 路径,与 xodus-service 的 <c>get_runtime_dir()</c> 一致。</summary>
    public const string SocketPath = "/tmp/xodus.sock";

    private readonly string _binaryPath;
    private Process? _process;

    public XodusServiceHost(string binaryPath) =>
        _binaryPath = Path.GetFullPath(binaryPath);

    /// <summary>本类启动的服务是否仍在运行。</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>socket 文件是否存在(不代表背后还有服务活着)。</summary>
    public static bool SocketExists() => File.Exists(SocketPath);

    /// <summary>
    /// socket 背后是否真有服务在监听。服务被强杀时 socket 文件会残留下来,
    /// 只判断文件存在会把这种残留误认成"服务已在运行",于是永远不再拉起服务,
    /// 游戏侧便一直报 <c>Could not load Xodus's service socket.</c>。
    /// </summary>
    public static bool IsSocketAlive()
    {
        if (!SocketExists())
            return false;

        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            // 无监听者时立刻返回 ECONNREFUSED / ENOENT,不需要额外超时。
            probe.Connect(new UnixDomainSocketEndPoint(SocketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// 启动服务并等待 socket 就绪。若已有服务在监听(例如另一个实例已拉起)则直接返回;
    /// 若只剩残留 socket 文件,则先清理再拉起服务。
    /// </summary>
    public async Task StartAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (IsSocketAlive())
            return;

        TryDeleteSocket();

        if (!File.Exists(_binaryPath))
            throw new FileNotFoundException($"未找到 xodus-service:{_binaryPath}", _binaryPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _binaryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // xodus-service 在 macOS 上把 socket 固定放在 /tmp,不读取 XDG_RUNTIME_DIR。
        startInfo.Environment.Remove("XDG_RUNTIME_DIR");
        if (!startInfo.Environment.ContainsKey("XODUS_LOG"))
            startInfo.Environment["XODUS_LOG"] = "info";

        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException($"无法启动 xodus-service:{_binaryPath}");
        _process.OutputDataReceived += (_, e) => Forward("out", e.Data);
        _process.ErrorDataReceived += (_, e) => Forward("err", e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"xodus-service 提前退出,退出码 {_process.ExitCode}。");
            if (SocketExists())
                return;
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待 {SocketPath} 超时。");
    }

    /// <summary>终止服务并清理 socket。</summary>
    public async ValueTask DisposeAsync()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 退出竞态或权限问题不应阻断调用方清理流程。
        }
        finally
        {
            process.Dispose();
            TryDeleteSocket();
        }
    }

    private static void TryDeleteSocket()
    {
        try
        {
            if (SocketExists())
                File.Delete(SocketPath);
        }
        catch (Exception)
        {
            // /tmp 下的 socket 由服务自身在退出时删除,这里失败可忽略。
        }
    }

    private static void Forward(string channel, string? line)
    {
        if (!string.IsNullOrEmpty(line))
            Console.WriteLine($"[xodus-service:{channel}] {line}");
    }
}