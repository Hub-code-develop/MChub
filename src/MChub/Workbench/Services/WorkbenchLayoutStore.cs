using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using MChub.Core.Const;

namespace MChub.Workbench.Services;

/// <summary>窗口布局的持久化状态。字段保持简单，旧版本读不到新字段时用默认值兜住。</summary>
public sealed class WorkbenchLayoutState
{
    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }

    public double SideBarWidth { get; set; } = 260;

    public double PanelHeight { get; set; } = 190;

    public bool SideBarVisible { get; set; } = true;

    public bool PanelVisible { get; set; } = true;

    public bool PanelCollapsed { get; set; }

    public string? ActiveActivity { get; set; } = "explorer";

    public string? ActivePanel { get; set; } = "output";

    /// <summary>右侧工具窗口宽度（IDEA 式布局新增）。</summary>
    public double RightBarWidth { get; set; } = 300;

    /// <summary>右侧工具窗口是否展开。默认收起——右侧放的是新闻/皮肤这类辅助信息。</summary>
    public bool RightBarVisible { get; set; }

    public string? ActiveRightTool { get; set; } = "news";

    /// <summary>最近打开过的实例（实例文件夹绝对路径），用于欢迎页。</summary>
    public List<string> RecentInstances { get; set; } = new();

    public void RememberInstance(string folderPath, int keep = 8)
    {
        RecentInstances.RemoveAll(p => string.Equals(p, folderPath, StringComparison.Ordinal));
        RecentInstances.Insert(0, folderPath);
        if (RecentInstances.Count > keep)
            RecentInstances.RemoveRange(keep, RecentInstances.Count - keep);
    }
}

/// <summary>
/// 布局读写。单独存一个 Workbench.json，不塞进 Setting.mchub ——
/// 布局属于"界面状态"，丢了顶多窗口位置不对，不该影响主配置的读写与迁移。
/// </summary>
public static class WorkbenchLayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static DispatcherTimer? _saveTimer;
    private static WorkbenchLayoutState? _pending;

    private static string LayoutFilePath => Path.Combine(ConfigPath.UserDataRootPath, "Workbench.json");

    public static WorkbenchLayoutState Load()
    {
        try
        {
            var path = LayoutFilePath;
            if (!File.Exists(path))
                return new WorkbenchLayoutState();

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return new WorkbenchLayoutState();

            return JsonSerializer.Deserialize<WorkbenchLayoutState>(text, Options) ?? new WorkbenchLayoutState();
        }
        catch (Exception exception)
        {
            Tio.Avalonia.Standard.Modules.DiskIO.Logger.Warning($"工作台布局读取失败，使用默认布局：{exception.Message}");
            return new WorkbenchLayoutState();
        }
    }

    /// <summary>合并多次变更后写一次盘（拖动分隔条会连续触发）。</summary>
    public static void ScheduleSave(WorkbenchLayoutState state)
    {
        _pending = state;
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                if (_pending is null) return;
                Save(_pending);
                _pending = null;
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public static void Save(WorkbenchLayoutState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(LayoutFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(LayoutFilePath, JsonSerializer.Serialize(state, Options));
        }
        catch (Exception exception)
        {
            Tio.Avalonia.Standard.Modules.DiskIO.Logger.Warning($"工作台布局保存失败：{exception.Message}");
        }
    }

    /// <summary>窗口关闭时立刻落盘，不等防抖计时器。</summary>
    public static void Flush(WorkbenchLayoutState state)
    {
        _saveTimer?.Stop();
        _pending = null;
        Save(state);
    }
}
