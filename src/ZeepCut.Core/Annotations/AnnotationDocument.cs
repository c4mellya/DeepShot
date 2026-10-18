using System.Collections;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 有序标注集合：列表顺序就是渲染顺序（越靠后越在上层），最后加入的标注在最上层。
/// </summary>
/// <remarks>
/// <para>
/// 文档不做序列化，但为序列化做好了准备：每个标注有稳定的 <see cref="Annotation.Id"/>，
/// 可以按列表顺序遍历（实现了 <see cref="IEnumerable{T}"/> 并暴露 <see cref="Items"/>），
/// 也可以通过 <see cref="Insert"/> 还原到指定层级。
/// </para>
/// <para>
/// 删除后再 <see cref="Insert"/> 同一对象时保留其 <see cref="Annotation.CreationOrder"/>，
/// 这样撤销删除能还原"创建顺序"与层级。文档本身不负责撤销，撤销由
/// <c>ZeepCut.Core.Editing.EditHistory</c> 的命令完成。
/// </para>
/// </remarks>
public sealed class AnnotationDocument : IEnumerable<Annotation>
{
    private readonly List<Annotation> _items = new();
    private long _nextCreationOrder = 1;

    /// <summary>按 Z 序（底 → 顶）排列的标注列表。</summary>
    public IReadOnlyList<Annotation> Items => _items;

    /// <summary>标注数量。</summary>
    public int Count => _items.Count;

    /// <summary>是否为空。</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>
    /// 所有标注包围盒的并集（物理像素）。忽略零尺寸包围盒（单点画笔、零长度箭头等），
    /// 没有可用包围盒时返回 <see cref="PixelRect.Empty"/>。
    /// </summary>
    public PixelRect Bounds
    {
        get
        {
            var hasBounds = false;
            var left = 0;
            var top = 0;
            var right = 0;
            var bottom = 0;

            foreach (var annotation in _items)
            {
                var bounds = annotation.Bounds;
                if (bounds.IsEmpty)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    left = bounds.Left;
                    top = bounds.Top;
                    right = bounds.Right;
                    bottom = bounds.Bottom;
                    hasBounds = true;
                    continue;
                }

                left = Math.Min(left, bounds.Left);
                top = Math.Min(top, bounds.Top);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
            }

            return hasBounds ? new PixelRect(left, top, right - left, bottom - top) : PixelRect.Empty;
        }
    }

    /// <summary>把标注加到最上层（列表末尾）。</summary>
    /// <exception cref="InvalidOperationException">同一对象已在文档中。</exception>
    public Annotation Add(Annotation annotation) => Insert(annotation, _items.Count);

    /// <summary>
    /// 把标注插入到指定层级（0 = 最底层）。同一对象不能重复加入；
    /// 对象已有 <see cref="Annotation.CreationOrder"/> 时保留（撤销删除的场景）。
    /// </summary>
    /// <exception cref="InvalidOperationException">同一对象已在文档中。</exception>
    public Annotation Insert(Annotation annotation, int index)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        if (index < 0 || index > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "插入位置必须在 0 到 Count 之间。");
        }

        if (_items.Contains(annotation))
        {
            throw new InvalidOperationException("同一标注对象不能重复加入文档。");
        }

        if (annotation.CreationOrder <= 0)
        {
            annotation.CreationOrder = _nextCreationOrder++;
        }
        else
        {
            _nextCreationOrder = Math.Max(_nextCreationOrder, annotation.CreationOrder + 1);
        }

        _items.Insert(index, annotation);
        return annotation;
    }

    /// <summary>移除标注；不存在时返回 false。</summary>
    public bool Remove(Annotation annotation) => _items.Remove(annotation);

    /// <summary>按索引移除并返回被移除的标注。</summary>
    public Annotation RemoveAt(int index)
    {
        var annotation = _items[index];
        _items.RemoveAt(index);
        return annotation;
    }

    /// <summary>清空文档，返回被移除的标注（按原顺序，底 → 顶）。</summary>
    public IReadOnlyList<Annotation> Clear()
    {
        var removed = _items.ToArray();
        _items.Clear();
        return removed;
    }

    /// <summary>标注在列表中的位置；不在文档中返回 -1。</summary>
    public int IndexOf(Annotation annotation) => _items.IndexOf(annotation);

    /// <summary>标注是否在文档中（按对象引用判断）。</summary>
    public bool Contains(Annotation annotation) => _items.Contains(annotation);

    /// <summary>
    /// 把标注移动到新的层级（0 = 最底层，Count-1 = 最上层）。
    /// </summary>
    public void MoveToIndex(Annotation annotation, int newIndex)
    {
        var oldIndex = _items.IndexOf(annotation);
        if (oldIndex < 0)
        {
            throw new ArgumentException("标注不在当前文档中。", nameof(annotation));
        }

        if (newIndex < 0 || newIndex >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(newIndex), newIndex, "目标层级必须在 0 到 Count-1 之间。");
        }

        if (oldIndex == newIndex)
        {
            return;
        }

        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, annotation);
    }

    /// <summary>把标注移到最上层。</summary>
    public void BringToFront(Annotation annotation) => MoveToIndex(annotation, _items.Count - 1);

    /// <summary>把标注移到最底层。</summary>
    public void SendToBack(Annotation annotation) => MoveToIndex(annotation, 0);

    /// <summary>
    /// 命中测试，返回最上层（列表末尾）命中的标注；没有命中返回 null。
    /// </summary>
    public Annotation? HitTest(PixelPoint point, int tolerance = 0)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].HitTest(point, tolerance))
            {
                return _items[i];
            }
        }

        return null;
    }

    /// <summary>命中测试（整数坐标重载），返回最上层命中的标注。</summary>
    public Annotation? HitTest(int x, int y, int tolerance = 0) =>
        HitTest(new PixelPoint(x, y), tolerance);

    public IEnumerator<Annotation> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
