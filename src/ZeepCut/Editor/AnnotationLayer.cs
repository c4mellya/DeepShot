using System.Windows;
using System.Windows.Media;
using ZeepCut.Core.Annotations;

namespace ZeepCut.Editor;

/// <summary>
/// 标注绘制层：叠在冻结图/压暗层之上、恰好覆盖选区的一个 <see cref="FrameworkElement"/>。
/// </summary>
/// <remarks>
/// <para>
/// 尺寸与位置由遮罩窗口按选区 DIP 设置；内部始终按**图像物理像素**绘制：
/// <c>OnRender</c> 先压入 <c>ScaleTransform(96/dpi, 96/dpi)</c>，于是 100 物理像素在 125%
/// 屏幕上正好占 80 DIP（100 设备像素），标注不会整体放大/缩小。
/// </para>
/// <para>
/// 正在拖拽的草稿通过 <c>previewProvider</c> 取到并画在已提交标注之后（最上层），
/// 因此实时预览与最终结果使用同一套渲染代码。
/// </para>
/// </remarks>
public sealed class AnnotationLayer : FrameworkElement
{
    private readonly AnnotationDocument _document;
    private readonly Func<Annotation?> _previewProvider;
    private readonly double _dipPerPixel;

    /// <param name="document">已提交的标注文档。</param>
    /// <param name="dpi">目标屏幕 DPI（决定 DIP/像素缩放）。</param>
    /// <param name="previewProvider">返回正在拖拽的草稿标注（没有则为 null）。</param>
    public AnnotationLayer(
        AnnotationDocument document,
        int dpi,
        Func<Annotation?> previewProvider)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(previewProvider);

        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI 必须为正数。");
        }

        _document = document;
        _previewProvider = previewProvider;
        _dipPerPixel = 96.0 / dpi;
        _dpi = dpi;

        IsHitTestVisible = false;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    private readonly int _dpi;

    /// <summary>目标屏幕 DPI。</summary>
    public int Dpi => _dpi;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (_dipPerPixel <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var pixelsPerDip = 1d / _dipPerPixel;

        drawingContext.PushTransform(new ScaleTransform(_dipPerPixel, _dipPerPixel));
        try
        {
            foreach (var annotation in _document)
            {
                AnnotationRenderer.Draw(drawingContext, annotation, pixelsPerDip);
            }

            var preview = _previewProvider();
            if (preview is not null)
            {
                AnnotationRenderer.Draw(drawingContext, preview, pixelsPerDip);
            }
        }
        finally
        {
            drawingContext.Pop();
        }
    }
}
