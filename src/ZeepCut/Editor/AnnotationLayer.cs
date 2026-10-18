using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

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
/// <para>
/// T17 起马赛克预览也是**真实像素化**：传入冻结图与选区在图像内的裁剪矩形后，
/// 这里按 <see cref="MosaicPixelator"/> 对该区域的原图像素做块平均（缓存结果，
/// 几何/参数变化才重算），再由 <see cref="AnnotationRenderer"/> 按 1:1 铺上去。
/// 导出走 <see cref="AnnotationExportRenderer"/> 的同一算法，两边像素一致。
/// </para>
/// </remarks>
public sealed class AnnotationLayer : FrameworkElement
{
    private readonly AnnotationDocument _document;
    private readonly Func<Annotation?> _previewProvider;
    private double _dipPerPixel;
    private readonly BitmapSource? _sourceImage;
    private readonly PixelRect? _cropRect;

    /// <summary>马赛克位图缓存：区域 + 块大小 + 强度 → 位图。</summary>
    private readonly Dictionary<(int Left, int Top, int Width, int Height, int Block, int Strength), BitmapSource?>
        _mosaicCache = new();

    /// <summary>冻结图尺寸（物理像素），用于夹取马赛克区域。</summary>
    private int _imageWidth;
    private int _imageHeight;

    /// <param name="document">已提交的标注文档。</param>
    /// <param name="dpi">目标屏幕 DPI（决定 DIP/像素缩放）。</param>
    /// <param name="previewProvider">返回正在拖拽的草稿标注（没有则为 null）。</param>
    /// <param name="sourceImage">冻结的整屏捕获图（用于马赛克取原图像素）；为 null 时马赛克退回块状示意。</param>
    /// <param name="cropRect">选区在冻结图内的裁剪矩形（标注坐标原点）；与 <paramref name="sourceImage"/> 一起使用。</param>
    public AnnotationLayer(
        AnnotationDocument document,
        int dpi,
        Func<Annotation?> previewProvider,
        BitmapSource? sourceImage = null,
        PixelRect? cropRect = null)
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
        _sourceImage = sourceImage;
        _cropRect = cropRect;
        _imageWidth = sourceImage?.PixelWidth ?? 0;
        _imageHeight = sourceImage?.PixelHeight ?? 0;

        IsHitTestVisible = false;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    private readonly int _dpi;
    private int? _dipPerPixelOverride;

    /// <summary>目标屏幕 DPI。</summary>
    public int Dpi => _dipPerPixelOverride.HasValue ? _dipPerPixelOverride.Value : _dpi;

    /// <summary>
    /// 覆写 DPI（T28 历史编辑窗口用）：窗口在 <c>Loaded</c> 之后才知道自己落在哪块屏上，
    /// 此时把真实 DPI 交给标注层，`OnRender` 里的 <c>pixelsPerDip</c> 才与 WPF 实际渲染一致
    /// （否则 125% 屏上文字/线宽会偏差 25%）。设置后需要 <see cref="InvalidateVisual"/>。
    /// </summary>
    public int? DpiOverride
    {
        get => _dipPerPixelOverride;
        set
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "DPI 必须为正数。");
            }

            _dipPerPixelOverride = value;
            if (value.HasValue)
            {
                _dipPerPixel = 96.0 / value.Value;
                InvalidateMosaicCache();
            }

            InvalidateVisual();
        }
    }

    /// <summary>清除马赛克缓存（选区变化或源图变化后调用）。</summary>
    public void InvalidateMosaicCache() => _mosaicCache.Clear();

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
                AnnotationRenderer.Draw(drawingContext, annotation, pixelsPerDip, GetMosaicPixels);
            }

            var preview = _previewProvider();
            if (preview is not null)
            {
                AnnotationRenderer.Draw(drawingContext, preview, pixelsPerDip, GetMosaicPixels);
            }
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    /// <summary>
    /// 取某个马赛克标注的像素化位图：按标注坐标（相对选区）从冻结图取原图像素，
    /// 再用 <see cref="MosaicPixelator"/> 处理。没有源图或区域在图像外时返回 null。
    /// </summary>
    private BitmapSource? GetMosaicPixels(MosaicAnnotation mosaic)
    {
        if (_sourceImage is null || !_cropRect.HasValue)
        {
            return null;
        }

        var imageRect = _cropRect.Value;
        var region = mosaic.Region;

        // 标注坐标 → 冻结图坐标；夹取到图像内（与导出时的越界裁剪一致）。
        var visible = region.Intersect(new PixelRect(0, 0, imageRect.Width, imageRect.Height));
        if (visible.IsEmpty)
        {
            return null;
        }

        var key = (visible.Left, visible.Top, visible.Width, visible.Height,
            mosaic.BlockSize, (int)Math.Round(mosaic.Strength * 1000));
        if (_mosaicCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? result;
        try
        {
            var pixels = new byte[imageRect.Width * imageRect.Height * MosaicPixelator.BytesPerPixel];
            var stride = imageRect.Width * MosaicPixelator.BytesPerPixel;
            _sourceImage.CopyPixels(
                new Int32Rect(imageRect.Left, imageRect.Top, imageRect.Width, imageRect.Height),
                pixels,
                stride,
                0);

            MosaicPixelator.Apply(pixels.AsSpan(), imageRect.Width, imageRect.Height, visible,
                mosaic.BlockSize, mosaic.Strength);

            // 只取区域那一小块，避免把整张选区都传进 GPU 纹理。
            var regionStride = visible.Width * MosaicPixelator.BytesPerPixel;
            var regionPixels = new byte[regionStride * visible.Height];
            for (var row = 0; row < visible.Height; row++)
            {
                Buffer.BlockCopy(
                    pixels,
                    ((visible.Top * imageRect.Width) + visible.Left) * MosaicPixelator.BytesPerPixel,
                    regionPixels,
                    row * regionStride,
                    regionStride);
            }

            result = BitmapSource.Create(
                visible.Width,
                visible.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                regionPixels,
                regionStride);
            result.Freeze();
        }
        catch (Exception)
        {
            result = null;
        }

        _mosaicCache[key] = result;
        return result;
    }
}
