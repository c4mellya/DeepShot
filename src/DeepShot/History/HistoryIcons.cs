using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// 历史界面用的矢量图标（24×24 描边图标，线条风格）。颜色全部走 <c>Theme.*</c> 资源引用，换主题即时生效。
/// </summary>
internal static class HistoryIcons
{
    internal const string Search = "M19,11 A8,8 0 1 1 3,11 A8,8 0 1 1 19,11 Z M21,21 L16.65,16.65";

    internal const string Close = "M18,6 L6,18 M6,6 L18,18";

    internal const string Refresh = "M23,4 V10 H17 M1,20 V14 H7 M3.51,9 A9,9 0 0 1 18.36,5.64 L23,10 M1,14 L5.64,18.36 A9,9 0 0 0 20.49,15";

    internal const string Star = "M12,2 L15.09,8.26 L22,9.27 L17,14.14 L18.18,21.02 L12,17.77 L5.82,21.02 L7,14.14 L2,9.27 L8.91,8.26 Z";

    internal const string Copy = "M11,9 H20 A2,2 0 0 1 22,11 V20 A2,2 0 0 1 20,22 H11 A2,2 0 0 1 9,20 V11 A2,2 0 0 1 11,9 Z M5,15 H4 A2,2 0 0 1 2,13 V4 A2,2 0 0 1 4,2 H13 A2,2 0 0 1 15,4 V5";

    internal const string Pin = "M21,10 C21,17 12,23 12,23 C12,23 3,17 3,10 A9,9 0 0 1 21,10 Z M15,10 A3,3 0 1 1 9,10 A3,3 0 1 1 15,10 Z";

    internal const string Rename = "M9,4 H15 M12,4 V20 M9,20 H15";

    internal const string Delete = "M3,6 H21 M19,6 V20 A2,2 0 0 1 17,22 H7 A2,2 0 0 1 5,20 V6 M8,6 V4 A2,2 0 0 1 10,2 H14 A2,2 0 0 1 16,4 V6 M10,11 V17 M14,11 V17";

    internal const string Annotate = "M17,3 A2.83,2.83 0 0 1 21,7 L7.5,20.5 L2,22 L3.5,16.5 Z";

    internal const string SaveAs = "M21,15 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V15 M7,10 L12,15 L17,10 M12,15 V3";

    internal const string Preview = "M1,12 C1,12 5,4 12,4 C19,4 23,12 23,12 C23,12 19,20 12,20 C5,20 1,12 1,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z";

    internal const string ZoomIn = "M12,5 V19 M5,12 H19";

    internal const string ZoomOut = "M5,12 H19";

    internal const string Fit = "M8,3 H5 A2,2 0 0 0 3,5 V8 M21,8 V5 A2,2 0 0 0 19,3 H16 M16,21 H19 A2,2 0 0 0 21,19 V16 M3,16 V19 A2,2 0 0 0 5,21 H8";

    internal const string ActualSize = "M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M10,8 L12,7 V17";

    internal const string Settings = "M4,21 V14 M4,10 V3 M12,21 V12 M12,8 V3 M20,21 V16 M20,12 V3 M1,14 H7 M9,8 H15 M17,16 H23";

    internal const string Gallery = "M4,3 H9 A1,1 0 0 1 10,4 V9 A1,1 0 0 1 9,10 H4 A1,1 0 0 1 3,9 V4 A1,1 0 0 1 4,3 Z M15,3 H20 A1,1 0 0 1 21,4 V9 A1,1 0 0 1 20,10 H15 A1,1 0 0 1 14,9 V4 A1,1 0 0 1 15,3 Z M15,14 H20 A1,1 0 0 1 21,15 V20 A1,1 0 0 1 20,21 H15 A1,1 0 0 1 14,20 V15 A1,1 0 0 1 15,14 Z M4,14 H9 A1,1 0 0 1 10,15 V20 A1,1 0 0 1 9,21 H4 A1,1 0 0 1 3,20 V15 A1,1 0 0 1 4,14 Z";

    internal const string ChevronLeft = "M15,18 L9,12 L15,6";

    internal const string ChevronRight = "M9,18 L15,12 L9,6";

    internal const string Image = "M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M10,8.5 A1.5,1.5 0 1 1 7,8.5 A1.5,1.5 0 1 1 10,8.5 Z M21,15 L16,10 L5,21";

    /// <summary>
    /// 创建一个图标元素（Viewbox 缩放 24×24 画布，线宽随之缩放，所以任何尺寸下线条粗细比例一致）。
    /// </summary>
    internal static Viewbox Create(
        string data,
        double size = 16,
        string strokeKey = ThemeManager.BrushKeys.Foreground,
        double strokeThickness = 1.9,
        bool filled = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
        };
        path.SetResourceReference(Shape.StrokeProperty, strokeKey);
        if (filled)
        {
            path.SetResourceReference(Shape.FillProperty, strokeKey);
        }

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = canvas,
            IsHitTestVisible = false,
            SnapsToDevicePixels = false,
        };
    }

    /// <summary>取出 <see cref="Create"/> 内部的 Path（改填充/描边色用）。</summary>
    internal static Path PathOf(Viewbox icon) => (Path)((Canvas)icon.Child).Children[0];
}
