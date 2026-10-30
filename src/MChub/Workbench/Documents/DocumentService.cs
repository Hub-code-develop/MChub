using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;

namespace MChub.Workbench.Documents;

/// <summary>
/// 编辑器里打开的一个东西。文本、贴图、结构预览、构建日志都是文档，
/// 这样标签页、脏标记、关闭提示只需要实现一次。
/// </summary>
public interface IWorkbenchDocument
{
    /// <summary>唯一标识（通常是文件绝对路径，或 <c>mchub://xxx</c>）。</summary>
    string Id { get; }

    string Title { get; }

    /// <summary>标签页图标（iconfont 字符）。</summary>
    string? Glyph { get; }

    bool IsDirty { get; }

    /// <summary>工作台中间区域显示的内容。</summary>
    Control View { get; }

    /// <summary>返回 false 表示取消关闭（例如用户点了"取消"）。</summary>
    bool TryClose();
}

/// <summary>
/// 文档集合。只管"打开了哪些、当前是哪个、关得掉吗"，
/// 不关心文档内部是什么控件。
/// </summary>
public sealed class DocumentService
{
    private IWorkbenchDocument? _active;

    public ObservableCollection<IWorkbenchDocument> Documents { get; } = new();

    /// <summary>当前激活的文档变了，或者文档集合变了。</summary>
    public event EventHandler? StateChanged;

    public IWorkbenchDocument? Active
    {
        get => _active;
        private set
        {
            if (ReferenceEquals(_active, value)) return;
            _active = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>已经打开过就直接激活，不重复开标签。</summary>
    public void Open(IWorkbenchDocument document)
    {
        var existing = Documents.FirstOrDefault(d => d.Id == document.Id);
        if (existing is not null)
        {
            Active = existing;
            return;
        }

        Documents.Add(document);
        Active = document;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Activate(IWorkbenchDocument document)
    {
        if (Documents.Contains(document))
            Active = document;
    }

    /// <summary>关闭文档；文档拒绝关闭（<see cref="IWorkbenchDocument.TryClose"/> 返回 false）时保持原状。</summary>
    public bool Close(IWorkbenchDocument document)
    {
        if (!Documents.Contains(document))
            return true;

        if (!document.TryClose())
            return false;

        var index = Documents.IndexOf(document);
        Documents.Remove(document);

        if (ReferenceEquals(_active, document))
        {
            // 关掉当前标签后落到右边那个，没有就落到左边，和常用编辑器一致。
            _active = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void CloseAll()
    {
        foreach (var document in Documents.ToList())
            Close(document);
    }
}
