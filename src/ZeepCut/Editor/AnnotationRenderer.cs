using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Editor;

/// <summary>
/// 把 Core 的标注模型画到 WPF <see cref="DrawingContext"/>。绘制坐标是**图像内物理像素**，
/// 由 <see cref="AnnotationLayer"/> 统一套一个 96/dpi 的缩放变换，因此这里不需要知道 DPI。
/// </summary>
/// <remarks>
/// <para>
/// 箭头头部、画笔平滑、文字排版都是渲染层职责（模型只存两端点/点串/文本），
/// 这样 T17 导出可以复用同一个渲染器（把绘制上下文换成物理像素 1:1 的目标）。
/// </para>
/// <para>
/// 马赛克预览目前画的是半透明块状示意（按 <see cref="MosaicAnnotation.BlockSize"/> 铺格子），
/// 真实像素化/模糊属于 T17 导出渲染。
/// </para>
/// </remarks>
internal static class AnnotationRenderer
{
    /// <summary>画一个标注；<paramref name="pixelsPerDip"/> 用于文字排版。</summary>
    public static void Draw(DrawingContext context, Annotation annotation, double pixelsPerDip) =>
        Draw(context, annotation, pixelsPerDip, mosaicPixels: null);

    /// <summary>
    /// 画一个标注。<paramref name="mosaicPixels"/> 提供马赛克区域的**真实像素化**位图
    /// （预览与导出共用同一个像素化结果）；为 <c>null</c> 时退回块状示意（仅兜底，正常路径不用）。
    /// </summary>
    public static void Draw(
        DrawingContext context,
        Annotation annotation,
        double pixelsPerDip,
        Func<MosaicAnnotation, BitmapSource?>? mosaicPixels)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(annotation);

        switch (annotation)
        {
            case RectangleAnnotation rectangle:
                context.DrawRectangle(null, CreatePen(annotation), ToRect(rectangle.Region));
                break;

            case EllipseAnnotation ellipse:
                DrawEllipse(context, annotation, ellipse.Region);
                break;

            case ArrowAnnotation arrow:
                DrawArrow(context, arrow, pixelsPerDip);
                break;

            case LineAnnotation line:
                context.DrawLine(CreatePen(line), ToPoint(line.Start), ToPoint(line.End));
                break;

            case FreehandAnnotation freehand:
                DrawFreehand(context, freehand);
                break;

            case TextAnnotation text:
                DrawText(context, text, pixelsPerDip);
                break;

            case MosaicAnnotation mosaic:
                DrawMosaic(context, mosaic, mosaicPixels?.Invoke(mosaic));
                break;
        }
    }

    private static void DrawEllipse(DrawingContext context, Annotation annotation, PixelRect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return;
        }

        var center = new Point(region.Left + (region.Width / 2d), region.Top + (region.Height / 2d));
        context.DrawEllipse(null, CreatePen(annotation), center, region.Width / 2d, region.Height / 2d);
    }

    private static void DrawArrow(DrawingContext context, ArrowAnnotation arrow, double pixelsPerDip)
    {
        var start = ToPoint(arrow.Start);
        var end = ToPoint(arrow.End);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length <= 0.01)
        {
            return;
        }

        var pen = CreatePen(arrow);
        var brush = CreateBrush(arrow.Color);

        // 箭头头部长度随粗细增长，但不短于 10px，也不超过线段本身。
        var headLength = Math.Min(length, Math.Max(10d, arrow.Thickness * 4d));
        var halfWidth = headLength * 0.35;

        var ux = dx / length;
        var uy = dy / length;
        var baseX = end.X - (ux * headLength);
        var baseY = end.Y - (uy * headLength);

        // 杆画到头部底边稍前的位置，避免粗线穿透三角形尖端。
        var shaftEnd = new Point(end.X - (ux * headLength * 0.35), end.Y - (uy * headLength * 0.35));
        context.DrawLine(pen, start, shaftEnd);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(end, isFilled: true, isClosed: true);
            ctx.LineTo(new Point(baseX - (uy * halfWidth), baseY + (ux * halfWidth)), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(baseX + (uy * halfWidth), baseY - (ux * halfWidth)), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawFreehand(DrawingContext context, FreehandAnnotation freehand)
    {
        var points = freehand.Points;
        if (points.Count == 0)
        {
            return;
        }

        if (points.Count == 1)
        {
            // 单击画笔：画一个圆点，半径取描边半宽（至少 1px）。
            var radius = Math.Max(1d, freehand.Thickness / 2d);
            context.DrawEllipse(CreateBrush(freehand.Color), null, ToPoint(points[0]), radius, radius);
            return;
        }

        var geometry = BuildFreehandGeometry(points, freehand.Smooth);
        context.DrawGeometry(null, CreatePen(freehand), geometry);
    }

    /// <summary>
    /// 画笔几何：不平滑时直接连线；平滑时用相邻点中点为端、原点为控制点做二次贝塞尔，
    /// 这是轻量且不会过冲的平滑方式。
    /// </summary>
    private static Geometry BuildFreehandGeometry(IReadOnlyList<PixelPoint> points, bool smooth)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(points[0]), isFilled: false, isClosed: false);

            if (!smooth || points.Count < 3)
            {
                for (var i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(ToPoint(points[i]), isStroked: true, isSmoothJoin: true);
                }
            }
            else
            {
                for (var i = 1; i < points.Count - 1; i++)
                {
                    var control = ToPoint(points[i]);
                    var next = ToPoint(points[i + 1]);
                    var mid = new Point((control.X + next.X) / 2d, (control.Y + next.Y) / 2d);
                    ctx.QuadraticBezierTo(control, mid, isStroked: true, isSmoothJoin: true);
                }

                ctx.LineTo(ToPoint(points[^1]), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawText(DrawingContext context, TextAnnotation annotation, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(annotation.Text))
        {
            return;
        }

        var formatted = new FormattedText(
            annotation.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            annotation.FontSize,
            CreateBrush(annotation.Color),
            Math.Max(0.1, pixelsPerDip));

        var origin = ToPoint(annotation.Position);

        if (annotation.BackgroundColor is { } background)
        {
            context.DrawRectangle(CreateBrush(background), null, new Rect(origin.X, origin.Y, formatted.Width, formatted.Height));
        }

        context.DrawText(formatted, origin);
    }

    /// <summary>
    /// 马赛克：优先把**真实像素化**的位图铺到区域上（与导出完全一致）；
    /// 拿不到位图时退回块状示意（半透明压暗 + 交替块），只作为兜底。
    /// </summary>
    private static void DrawMosaic(
        DrawingContext context,
        MosaicAnnotation mosaic,
        BitmapSource? pixelated)
    {
        if (pixelated is not null && !mosaic.Region.IsEmpty)
        {
            // 位图尺寸就是区域物理像素，按 1:1 画到区域上（绘制单位 = 物理像素）。
            // 最近邻由宿主（AnnotationLayer / 导出 DrawingVisual）的 RenderOptions 指定。
            context.DrawImage(pixelated, ToRect(mosaic.Region));
            return;
        }

        DrawMosaicCheckerboard(context, mosaic);
    }

    private static void DrawMosaicCheckerboard(DrawingContext context, MosaicAnnotation mosaic)
    {
        var region = ToRect(mosaic.Region);
        if (region.IsEmpty)
        {
            return;
        }

        var block = Math.Max(AnnotationDefaults.MinMosaicBlockSize, mosaic.BlockSize);
        var dark = CreateBrush(AnnotationColor.FromArgb(0x99, 0x00, 0x00, 0x00));
        var light = CreateBrush(AnnotationColor.FromArgb(0x44, 0xFF, 0xFF, 0xFF));

        context.DrawRectangle(dark, null, region);

        for (var y = region.Top; y < region.Bottom; y += block)
        {
            for (var x = region.Left; x < region.Right; x += block)
            {
                var column = (int)((x - region.Left) / block);
                var row = (int)((y - region.Top) / block);
                if ((column + row) % 2 != 0)
                {
                    continue;
                }

                var width = Math.Min(block, region.Right - x);
                var height = Math.Min(block, region.Bottom - y);
                context.DrawRectangle(light, null, new Rect(x, y, width, height));
            }
        }
    }

    private static Pen CreatePen(Annotation annotation) => CreatePen(annotation.Color, annotation.Thickness);

    private static Pen CreatePen(AnnotationColor color, int thickness)
    {
        var pen = new Pen(CreateBrush(color), Math.Max(0.5, thickness))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush CreateBrush(AnnotationColor color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }

    private static Rect ToRect(PixelRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Point ToPoint(PixelPoint point) => new(point.X, point.Y);
}
