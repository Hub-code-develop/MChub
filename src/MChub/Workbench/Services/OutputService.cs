using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Threading;

namespace MChub.Workbench.Services;

/// <summary>
/// 一条输出通道（相当于 VS Code 的 Output Channel）。
/// 只保留最近的若干行，避免长时间下载/游戏日志把内存吃满。
/// </summary>
public sealed class OutputChannel
{
    private const int MaxLines = 5000;
    private readonly Queue<string> _lines = new();
    private readonly object _sync = new();

    public OutputChannel(string name) => Name = name;

    public string Name { get; }

    /// <summary>通道内容变化（已切到 UI 线程之前也不是问题，订阅方自行判断）。</summary>
    public event EventHandler? Changed;

    public string Text
    {
        get
        {
            lock (_sync)
                return string.Join(Environment.NewLine, _lines);
        }
    }

    public void Append(string line)
    {
        lock (_sync)
        {
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines)
                _lines.Dequeue();
        }

        RaiseChanged();
    }

    public void Append(IEnumerable<string> lines)
    {
        lock (_sync)
        {
            foreach (var line in lines)
            {
                _lines.Enqueue(line);
                while (_lines.Count > MaxLines)
                    _lines.Dequeue();
            }
        }

        RaiseChanged();
    }

    public void Clear()
    {
        lock (_sync)
            _lines.Clear();

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null) return;

        if (Dispatcher.UIThread.CheckAccess())
            handler(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => handler(this, EventArgs.Empty), DispatcherPriority.Background);
    }
}

/// <summary>
/// 工作台的输出面板后端。所有"看不见但需要留痕"的动作都往这里写：
/// 启动步骤、下载进度、整合包解压、原生库解压失败等等。
/// </summary>
public sealed class OutputService
{
    /// <summary>界面语言里的通道名，用于默认显示。</summary>
    public const string ChannelWorkbench = "MChub";
    public const string ChannelLaunch = "启动";
    public const string ChannelGame = "游戏日志";
    public const string ChannelDownload = "下载";

    private readonly Dictionary<string, OutputChannel> _channels = new(StringComparer.Ordinal);

    /// <summary>任意通道有新内容时触发，参数是通道本身。</summary>
    public event EventHandler<OutputChannel>? ChannelChanged;

    public OutputChannel Get(string name)
    {
        if (_channels.TryGetValue(name, out var existing))
            return existing;

        var channel = new OutputChannel(name);
        channel.Changed += (_, _) => ChannelChanged?.Invoke(this, channel);
        _channels[name] = channel;
        return channel;
    }

    public IReadOnlyCollection<OutputChannel> Channels => _channels.Values;

    /// <summary>写入一行，自动加时间戳。线程安全。</summary>
    public void Write(string channelName, string message)
    {
        var channel = Get(channelName);
        foreach (var line in SplitLines(message))
            channel.Append($"[{DateTime.Now:HH:mm:ss}] {line}");
    }

    public void WriteLine(string channelName, string message) => Write(channelName, message);

    /// <summary>写入不带时间戳的原始内容（例如游戏原始日志）。</summary>
    public void WriteRaw(string channelName, string message)
    {
        var channel = Get(channelName);
        foreach (var line in SplitLines(message))
            channel.Append(line);
    }

    private static IEnumerable<string> SplitLines(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            yield return string.Empty;
            yield break;
        }

        var builder = new StringBuilder(message.Length);
        foreach (var ch in message)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                yield return builder.ToString();
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }
}
