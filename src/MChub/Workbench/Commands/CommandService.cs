using System;
using System.Collections.Generic;
using System.Linq;

namespace MChub.Workbench.Commands;

/// <summary>
/// 工作台命令。菜单、快捷键、命令面板、右键菜单都只引用 <see cref="Id"/>，
/// 具体行为集中在这里注册一次，避免同一动作在多个界面各写一份。
/// </summary>
public sealed class WorkbenchCommand
{
    public required string Id { get; init; }

    /// <summary>已本地化的显示名。</summary>
    public required string Title { get; init; }

    /// <summary>命令面板里的分组名，可为空。</summary>
    public string? Category { get; init; }

    /// <summary>iconfont 字形（直接是字体里的字符，不做映射表）。</summary>
    public string? Glyph { get; init; }

    /// <summary>快捷键文本，仅用于展示（实际绑定在 <see cref="CommandService"/>）。</summary>
    public string? Gesture { get; init; }

    /// <summary>为 null 表示始终可执行。</summary>
    public Func<bool>? CanExecute { get; init; }

    public required Action Execute { get; init; }

    public bool IsEnabled => CanExecute?.Invoke() ?? true;
}

/// <summary>
/// 命令注册表。唯一职责：按 id 存命令、判断可执行性、执行。
/// 不关心谁调用的（菜单 / 面板 / 快捷键）。
/// </summary>
public sealed class CommandService
{
    private readonly Dictionary<string, WorkbenchCommand> _commands = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    public IReadOnlyList<WorkbenchCommand> All =>
        _commands.Values.OrderBy(c => c.Category, StringComparer.Ordinal)
                        .ThenBy(c => c.Title, StringComparer.Ordinal)
                        .ToList();

    public void Register(WorkbenchCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Id))
            throw new ArgumentException("命令必须有 id", nameof(command));

        _commands[command.Id] = command;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RegisterRange(params WorkbenchCommand[] commands)
    {
        foreach (var command in commands)
            _commands[command.Id] = command;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public WorkbenchCommand? Find(string id) => _commands.GetValueOrDefault(id);

    public bool CanExecute(string id) => _commands.TryGetValue(id, out var c) && c.IsEnabled;

    /// <summary>执行命令；id 不存在或当前不可执行时返回 false，不抛异常。</summary>
    public bool Execute(string id)
    {
        if (!_commands.TryGetValue(id, out var command))
            return false;

        if (!command.IsEnabled)
            return false;

        command.Execute();
        return true;
    }
}
