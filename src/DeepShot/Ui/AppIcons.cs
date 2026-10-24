using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeepShot.Ui;

/// <summary>
/// 全应用共用的矢量图标（24×24 描边图标，线条风格）。颜色全部走 <c>Theme.*</c> 资源引用，换主题即时生效。
/// </summary>
internal static class AppIcons
{
    internal const string Search = "M19,11 A8,8 0 1 1 3,11 A8,8 0 1 1 19,11 Z M21,21 L16.65,16.65";

    internal const string Close = "M18,6 L6,18 M6,6 L18,18";

    internal const string Refresh = "M23,4 V10 H17 M1,20 V14 H7 M3.51,9 A9,9 0 0 1 18.36,5.64 L23,10 M1,14 L5.64,18.36 A9,9 0 0 0 20.49,15";

    internal const string Star = "M12,2 L15.09,8.26 L22,9.27 L17,14.14 L18.18,21.02 L12,17.77 L5.82,21.02 L7,14.14 L2,9.27 L8.91,8.26 Z";

    /// <summary>复制：前后两张圆角卡片（截图浮层的复制按钮也用它）。</summary>
    internal const string Copy = "M11,9 H19 A2,2 0 0 1 21,11 V19 A2,2 0 0 1 19,21 H11 A2,2 0 0 1 9,19 V11 A2,2 0 0 1 11,9 Z M15,9 V5 A2,2 0 0 0 13,3 H5 A2,2 0 0 0 3,5 V13 A2,2 0 0 0 5,15 H9";

    /// <summary>贴图：图钉（与功能名"贴图/钉在桌面"一致；截图浮层、贴图工具条同用）。</summary>
    internal const string Pin = "M12,17 V22 M9,10.76 A2,2 0 0 1 7.89,12.55 L6.11,13.45 A2,2 0 0 0 5,15.24 V16 A1,1 0 0 0 6,17 H18 A1,1 0 0 0 19,16 V15.24 A2,2 0 0 0 17.89,13.45 L16.11,12.55 A2,2 0 0 1 15,10.76 V7 A1,1 0 0 1 16,6 A2,2 0 0 0 16,2 H8 A2,2 0 0 0 8,6 A1,1 0 0 1 9,7 Z";

    internal const string Rename = "M9,4 H15 M12,4 V20 M9,20 H15";

    internal const string Delete = "M3,6 H21 M19,6 V20 A2,2 0 0 1 17,22 H7 A2,2 0 0 1 5,20 V6 M8,6 V4 A2,2 0 0 1 10,2 H14 A2,2 0 0 1 16,4 V6 M10,11 V17 M14,11 V17";

    internal const string Annotate = "M17,3 A2.83,2.83 0 0 1 21,7 L7.5,20.5 L2,22 L3.5,16.5 Z";

    internal const string SaveAs = "M21,15 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V15 M7,10 L12,15 L17,10 M12,15 V3";

    internal const string Preview = "M1,12 C1,12 5,4 12,4 C19,4 23,12 23,12 C23,12 19,20 12,20 C5,20 1,12 1,12 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z";

    internal const string ZoomIn = "M12,5 V19 M5,12 H19";

    internal const string ZoomOut = "M5,12 H19";

    internal const string Fit = "M8,3 H5 A2,2 0 0 0 3,5 V8 M21,8 V5 A2,2 0 0 0 19,3 H16 M16,21 H19 A2,2 0 0 0 21,19 V16 M3,16 V19 A2,2 0 0 0 5,21 H8";

    internal const string Settings = "M4,21 V14 M4,10 V3 M12,21 V12 M12,8 V3 M20,21 V16 M20,12 V3 M1,14 H7 M9,8 H15 M17,16 H23";

    internal const string Gallery = "M4,3 H9 A1,1 0 0 1 10,4 V9 A1,1 0 0 1 9,10 H4 A1,1 0 0 1 3,9 V4 A1,1 0 0 1 4,3 Z M15,3 H20 A1,1 0 0 1 21,4 V9 A1,1 0 0 1 20,10 H15 A1,1 0 0 1 14,9 V4 A1,1 0 0 1 15,3 Z M15,14 H20 A1,1 0 0 1 21,15 V20 A1,1 0 0 1 20,21 H15 A1,1 0 0 1 14,20 V15 A1,1 0 0 1 15,14 Z M4,14 H9 A1,1 0 0 1 10,15 V20 A1,1 0 0 1 9,21 H4 A1,1 0 0 1 3,20 V15 A1,1 0 0 1 4,14 Z";

    internal const string ChevronLeft = "M15,18 L9,12 L15,6";

    internal const string ChevronRight = "M9,18 L15,12 L9,6";

    internal const string Image = "M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M10,8.5 A1.5,1.5 0 1 1 7,8.5 A1.5,1.5 0 1 1 10,8.5 Z M21,15 L16,10 L5,21";

    /// <summary>圆圈感叹号（输入校验错误、警示提示）。</summary>
    internal const string Alert = "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 Z M12,8 V12.5 M12,16 L12.01,16";

    // --- 设置页行图标 ---

    /// <summary>键盘（截图快捷键）。</summary>
    internal const string Keyboard = "M4,6 H20 A2,2 0 0 1 22,8 V16 A2,2 0 0 1 20,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M6.5,10 H6.51 M10.5,10 H10.51 M14.5,10 H14.51 M18,10 H18.01 M8,14 H16";

    /// <summary>闪电（快速截图）。</summary>
    internal const string Bolt = "M13,2 L4,14 H12 L11,22 L20,10 H12 Z";

    /// <summary>裁切框（截图方式）。</summary>
    internal const string Crop = "M6,2 V16 A2,2 0 0 0 8,18 H22 M2,6 H16 A2,2 0 0 1 18,8 V22";

    /// <summary>地球（语言）。</summary>
    internal const string Globe = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 Z M3,12 H21 M12,3 C14.3,5.5 15.5,8.6 15.5,12 C15.5,15.4 14.3,18.5 12,21 C9.7,18.5 8.5,15.4 8.5,12 C8.5,8.6 9.7,5.5 12,3 Z";

    /// <summary>月亮（主题）。</summary>
    internal const string Moon = "M20.5,13.5 A8.5,8.5 0 1 1 10.5,3.5 A6.6,6.6 0 0 0 20.5,13.5 Z";

    /// <summary>电源（开机启动）。</summary>
    internal const string Power = "M12,3 V12 M17.7,6.8 A8,8 0 1 1 6.3,6.8";

    /// <summary>文件夹（数据目录）。</summary>
    internal const string Folder = "M3,7 A2,2 0 0 1 5,5 H9 L11,7.5 H19 A2,2 0 0 1 21,9.5 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z";

    /// <summary>时钟（历史保留）。</summary>
    internal const string Clock = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 Z M12,7 V12 L15.5,14";

    /// <summary>字体（关于 · 字体致谢）。</summary>
    internal const string Type = "M5,7 V5 H19 V7 M9,19 H15 M12,5 V19";

    // --- 状态徽标（提示框、检查更新窗口共用） ---

    /// <summary>圆角三角感叹号（警告 / 破坏性确认 / 检查失败）。</summary>
    internal const string Warning = "M10.29,3.86 L1.82,18 A2,2 0 0 0 3.53,21 H20.47 A2,2 0 0 0 22.18,18 L13.71,3.86 A2,2 0 0 0 10.29,3.86 Z M12,9 V13 M12,17 L12.01,17";

    /// <summary>字母 i（信息；放在圆形徽标里，本身不再画圆圈）。</summary>
    internal const string Info = "M12,10.5 V18 M12,6 L12.01,6";

    /// <summary>问号（询问；放在圆形徽标里）。</summary>
    internal const string Help = "M8.5,8.5 A3.5,3.5 0 0 1 15.5,8.5 C15.5,11 12,11.5 12,14 M12,18.5 L12.01,18.5";

    /// <summary>对勾（成功 / 已是最新）。</summary>
    internal const string Check = "M20,6 L9,17 L4,12";

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
