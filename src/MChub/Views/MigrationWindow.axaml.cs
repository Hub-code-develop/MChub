using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;
using MChub.Core.Migration;
using MChub.Core.Module.Initialize;

namespace MChub.Views;

/// <summary>
/// 首次启动多出来的一步：从 PCL / SJMCL / 官方启动器迁移。
/// 迁移完（或跳过）后由 App 判断闸门：已经导入了正版账号就直接进主界面，不再要求登录。
/// </summary>
public partial class MigrationWindow : FAAppWindow
{
    private readonly List<(MigrationSourceInfo Info, CheckBox Box)> _rows = [];
    private bool _loaded;
    private bool _finished;

    public MigrationWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            Populate();
        };
    }

    /// <summary>迁移结束（导入完成或跳过）。</summary>
    public event Action? Completed;

    private void Populate()
    {
        var sources = MigrationService.Scan();
        if (sources.Count == 0)
        {
            StatusText.Text = "没有检测到其它启动器的游戏目录，可以直接继续。";
            ImportButton.IsVisible = false;
            SkipButton.Content = "继续";
            return;
        }

        foreach (var source in sources)
        {
            var summary = new List<string>();
            if (source.VersionCount > 0) summary.Add($"{source.VersionCount} 个版本");
            if (source.SaveCount > 0) summary.Add($"{source.SaveCount} 个存档");
            if (source.ResourcePackCount > 0) summary.Add($"{source.ResourcePackCount} 个资源包");
            if (source.HasOptions) summary.Add("含设置");

            var box = new CheckBox
            {
                IsChecked = true,
                Content = $"{source.DisplayName}" +
                          (summary.Count > 0 ? $"（{string.Join("，", summary)}）" : string.Empty) +
                          $"\n{source.RootPath}"
            };

            _rows.Add((source, box));
            SourcesPanel.Children.Add(box);
        }

        StatusText.Text = $"检测到 {sources.Count} 个来源。";
    }

    private async void Import(object? sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Info).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "没有勾选任何来源。";
            return;
        }

        ImportButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        StatusText.Text = "正在导入…";

        var lines = new List<string>();
        var accounts = 0;
        var folders = 0;

        foreach (var source in selected)
        {
            try
            {
                var result = await MigrationService.ImportAsync(source);
                if (result.FolderRegistered) folders++;
                accounts += result.AccountsImported;
                lines.Add($"{result.Source.DisplayName}：{(result.FolderRegistered ? "已注册目录" : "目录已存在")}" +
                          (result.AccountsImported > 0 ? $"，导入 {result.AccountsImported} 个账号" : string.Empty));
                lines.AddRange(result.Warnings.Select(warning => $"  · {warning}"));
            }
            catch (Exception ex)
            {
                lines.Add($"{source.DisplayName}：导入失败 - {ex.Message}");
            }
        }

        ConfigSaver.FlushConfig();

        StatusText.Text = string.Join(Environment.NewLine, lines);
        ImportButton.IsVisible = false;
        SkipButton.Content = "继续";
        SkipButton.IsEnabled = true;

        if (accounts > 0)
            StatusText.Text += Environment.NewLine +
                               "已导入可用的微软账号，接下来不需要再登录。";
    }

    private void Skip(object? sender, RoutedEventArgs e)
    {
        if (_finished) return;
        _finished = true;
        Completed?.Invoke();
    }
}
