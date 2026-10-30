using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using MChub.Workbench.Commands;

namespace MChub.Workbench.Services;

/// <summary>
/// 快捷键绑定。手势文本（<c>Ctrl+Shift+P</c>、<c>Ctrl+K W</c>）集中解析一次，
/// 菜单、命令面板、窗口按键处理共用同一份表，避免三处各写一遍判断。
/// </summary>
public sealed class KeybindingService
{
    /// <summary>macOS 上 "Ctrl" 一律按 Cmd 处理，和系统习惯一致。</summary>
    private static readonly KeyModifiers CtrlAlias =
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private static readonly TimeSpan ChordTimeout = TimeSpan.FromSeconds(2.5);

    private const KeyModifiers TrackedModifiers =
        KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    private readonly List<Binding> _bindings = new();

    private Binding? _pending;
    private DateTime _pendingAt;

    /// <summary>一段按键（例如 Ctrl+K，或单独的 W）。</summary>
    private readonly record struct KeyCombo(Key Key, KeyModifiers Modifiers)
    {
        public bool Matches(KeyCombo other) => Key == other.Key && Modifiers == other.Modifiers;
    }

    private sealed record Binding(string CommandId, KeyCombo[] Chords);

    public IReadOnlyList<string> CommandIds => _bindings.Select(b => b.CommandId).ToList();

    /// <summary>解析失败（例如手势写错）时返回 false，不抛异常——界面上少一个快捷键总比启动崩溃好。</summary>
    public bool Bind(string commandId, string? gesture)
    {
        if (string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(gesture))
            return false;

        if (!TryParseGesture(gesture, out var chords))
            return false;

        _bindings.RemoveAll(b => string.Equals(b.CommandId, commandId, StringComparison.Ordinal));
        _bindings.Add(new Binding(commandId, chords));
        return true;
    }

    /// <summary>把命令表里所有带 <see cref="WorkbenchCommand.Gesture"/> 的命令一次性绑定。</summary>
    public void BindFrom(CommandService commands)
    {
        foreach (var command in commands.All)
            Bind(command.Id, command.Gesture);
    }

    /// <summary>返回 true 表示这次按键被某个命令吃掉了，调用方不要再处理。</summary>
    public bool TryHandle(KeyEventArgs e)
    {
        if (_bindings.Count == 0)
            return false;

        var chord = new KeyCombo(e.Key, Normalize(e.KeyModifiers));

        if (_pending is not null)
        {
            var pending = _pending;
            _pending = null;

            if (DateTime.UtcNow - _pendingAt <= ChordTimeout &&
                pending.Chords.Length > 1 &&
                pending.Chords[1].Matches(chord))
            {
                WorkbenchServices.Commands.Execute(pending.CommandId);
                return true;
            }
            // 和弦的第二段不匹配就当没按过，继续走普通匹配。
        }

        foreach (var binding in _bindings)
        {
            if (binding.Chords.Length == 1 && binding.Chords[0].Matches(chord))
            {
                WorkbenchServices.Commands.Execute(binding.CommandId);
                return true;
            }
        }

        foreach (var binding in _bindings)
        {
            if (binding.Chords.Length > 1 && binding.Chords[0].Matches(chord))
            {
                _pending = binding;
                _pendingAt = DateTime.UtcNow;
                return true;
            }
        }

        return false;
    }

    /// <summary>打开会吃掉输入的地方（命令面板、对话框）时清掉挂起的和弦。</summary>
    public void ResetChord() => _pending = null;

    private static KeyModifiers Normalize(KeyModifiers modifiers) => modifiers & TrackedModifiers;

    private static bool TryParseGesture(string gesture, out KeyCombo[] chords)
    {
        chords = Array.Empty<KeyCombo>();

        var segments = gesture.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return false;

        var parsed = new List<KeyCombo>(segments.Length);
        foreach (var segment in segments)
        {
            if (!TryParseCombo(segment, out var combo))
                return false;
            parsed.Add(combo);
        }

        chords = parsed.ToArray();
        return true;
    }

    private static bool TryParseCombo(string text, out KeyCombo combo)
    {
        combo = default;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var modifiers = (KeyModifiers)0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= CtrlAlias;
                    break;
                case "cmd" or "command" or "meta" or "win" or "super":
                    modifiers |= KeyModifiers.Meta;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "alt" or "option" or "opt":
                    modifiers |= KeyModifiers.Alt;
                    break;
                default:
                    return false;
            }
        }

        var keyText = parts[^1];
        if (!Enum.TryParse<Key>(keyText, true, out var key))
        {
            // Enum.TryParse 不吃 "," / "." 这类符号键，单独兜一下。
            key = keyText switch
            {
                "," => Key.OemComma,
                "." => Key.OemPeriod,
                "/" => Key.OemQuestion,
                ";" => Key.OemSemicolon,
                "=" => Key.OemPlus,
                "-" => Key.OemMinus,
                _ => Key.None
            };
        }

        if (key == Key.None)
            return false;

        combo = new KeyCombo(key, Normalize(modifiers));
        return true;
    }
}
