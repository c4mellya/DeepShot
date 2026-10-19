using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ZeepCut.Capture;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.History;
using ZeepCut.Core.Output;
using ZeepCut.Core.Pins;
using ZeepCut.Output;
using ZeepCut.Pins;
using ZeepCut.Platform;
using ZeepCut.Ui;

namespace ZeepCut.History;

/// <summary>
/// 历史图库里针对单条记录的操作（T25/T26）：复制、另存为、贴图、删除、收藏、重命名。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么独立出来</b>：图库窗口与预览窗口都要能"复制 / 贴图 / 删除"，把这些动作写在
/// 窗口事件里会重复两份（并且两份对"失败要不要提示"的处理迟早分叉）。这里每个方法返回
/// 一句面向用户的中文结果文案，由调用方显示在状态栏，**绝不抛异常**（图库不该因为
/// 一次 GIF 解码失败整窗崩掉）。
/// </para>
/// <para>
/// <b>贴图不新增历史</b>（PLAN / T22）：一律走 <see cref="PinWindows.CreateFromHistory"/>，
/// 它固定用 <see cref="PinSource.History"/>，因此 <c>ShouldRecordHistory</c> 为 false。
/// 方法里还会把贴图前后的记录数写进日志，供真实桌面验证核对。
/// </para>
/// <para>
/// <b>句柄</b>：读图一律走 <see cref="HistoryStore.TryLoad"/>（<c>BitmapCacheOption.OnLoad</c>），
/// 读完立刻释放文件，因此删除/覆盖不会被自己锁住（T23 已实测）。
/// </para>
/// </remarks>
internal static class HistoryActions
{
    /// <summary>读取记录的原图（失败返回 null，调用方提示"图片文件已丢失"）。</summary>
    internal static BitmapSource? LoadImage(HistoryStore store, HistoryRecord record)
    {
        var image = store.TryLoad(record.RelativeImagePath);
        if (image is null)
        {
            AppLog.Warn(
                "History",
                $"读取历史图片失败（文件可能已被删除）：Id={record.Id} 名称={record.Name} " +
                $"相对路径={record.RelativeImagePath}");
        }

        return image;
    }

    /// <summary>复制到剪贴板（PNG 图像格式）。</summary>
    internal static string Copy(HistoryStore store, HistoryRecord record)
    {
        var image = LoadImage(store, record);
        if (image is null)
        {
            return "图片文件已丢失，无法复制。";
        }

        try
        {
            ClipboardWriter.Write(image);
            AppLog.Info(
                "History",
                $"历史复制成功：名称={record.Name} 尺寸={image.PixelWidth}x{image.PixelHeight} " +
                $"Id={record.Id}");
            return StringsUi.Format("S.History.Copied", record.Name, image.PixelWidth, image.PixelHeight);
        }
        catch (COMException ex)
        {
            AppLog.Warn("History", $"历史复制失败（剪贴板被占用）：{ex.Message}");
            return StringsUi.Format("S.History.CopyFailed", StringsUi.Get("S.Message.ClipboardBusy"));
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"历史复制失败：{record.Name}", ex);
            return StringsUi.Format("S.History.CopyFailed", ex.Message);
        }
    }

    /// <summary>
    /// 另存为 PNG/JPEG。对话框期间临时取消 Owner 的 Topmost（T11 实测：Topmost 会让
    /// Win32 文件对话框自行返回）。
    /// </summary>
    internal static string SaveAs(Window? owner, HistoryStore store, HistoryRecord record)
    {
        var image = LoadImage(store, record);
        if (image is null)
        {
            return "图片文件已丢失，无法另存为。";
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = StringsUi.Get("S.Dialog.SaveTitle"),
            FileName = record.Name + CaptureFileName.GetExtension(CaptureImageFormat.Png),
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            Filter = StringsUi.Get("S.Dialog.SaveFilter"),
            FilterIndex = 1,
            RestoreDirectory = true,
        };

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrWhiteSpace(pictures) && Directory.Exists(pictures))
        {
            dialog.InitialDirectory = pictures;
        }

        var wasTopmost = owner?.Topmost ?? false;
        bool? result;
        try
        {
            if (owner is not null)
            {
                owner.Topmost = false;
            }

            AppLog.Info("History", $"历史另存为对话框已打开：默认名称={dialog.FileName}");
            result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            AppLog.Info("History", $"历史另存为对话框已关闭：DialogResult={result} 选择文件={dialog.FileName}");
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"历史另存为失败（对话框）：{record.Name}", ex);
            return StringsUi.Format("S.History.SaveFailed", ex.Message);
        }
        finally
        {
            if (owner is not null)
            {
                owner.Topmost = wasTopmost;
                owner.Activate();
            }
        }

        if (result != true)
        {
            AppLog.Info("History", "历史另存为已取消，不写入文件");
            return "已取消另存为。";
        }

        var format = CaptureFileName.TryGetFormatFromExtension(dialog.FileName, out var parsed)
            ? parsed
            : dialog.FilterIndex == 2
                ? CaptureImageFormat.Jpeg
                : CaptureImageFormat.Png;
        var path = Path.HasExtension(dialog.FileName)
            ? dialog.FileName
            : dialog.FileName + CaptureFileName.GetExtension(format);

        try
        {
            CaptureImageEncoder.Save(image, path, format);
            AppLog.Info(
                "History",
                $"历史图片已保存：{path} 格式={format} 尺寸={image.PixelWidth}x{image.PixelHeight} " +
                $"名称={record.Name}");
            return $"已保存到 {path}。";
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"历史图片保存失败：{path}", ex);
            return StringsUi.Format("S.History.SaveFailed", ex.Message);
        }
    }

    /// <summary>
    /// 从历史记录贴图（PLAN：历史贴图**不新增历史**）。贴图摆在光标附近，夹进光标所在屏幕。
    /// </summary>
    internal static string Pin(HistoryStore store, HistoryRecord record)
    {
        var image = LoadImage(store, record);
        if (image is null)
        {
            return StringsUi.Get("S.History.ImageMissingPreview");
        }

        var before = store.Count();
        try
        {
            var cursor = new PixelPoint(0, 0);
            if (NativeMethods.GetCursorPos(out var cursorPoint))
            {
                cursor = new PixelPoint(cursorPoint.X, cursorPoint.Y);
            }

            var display = MonitorLayout.ReadDisplaySpace();
            var monitor = display.FindByPoint(cursor.X, cursor.Y);
            var placement = PinPlacement.NearCursor(
                cursor,
                image.PixelWidth,
                image.PixelHeight,
                monitor.Bounds);
            var bounds = new PixelRect(
                placement.Left,
                placement.Top,
                image.PixelWidth,
                image.PixelHeight);

            var pin = PinWindows.CreateFromHistory(image, bounds, monitor.Bounds, monitor.Dpi);
            var after = store.Count();

            AppLog.Info(
                "History",
                $"从历史贴图完成：名称={record.Name} Id={record.Id} 图像={image.PixelWidth}x{image.PixelHeight} " +
                $"摆放={bounds} 屏幕={monitor.DeviceName} DPI={monitor.Dpi} " +
                $"贴图总数={PinWindows.Count} 历史记录数 {before} → {after}（不新增历史）");

            return StringsUi.Format("S.History.Pinned", record.Name, pin.Bounds.Width, pin.Bounds.Height, after);
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"从历史贴图失败：{record.Name}", ex);
            return StringsUi.Format("S.History.PinFailed", ex.Message);
        }
    }

    /// <summary>删除一条记录（行 + 图片 + 缩略图文件）。调用方负责先确认。</summary>
    internal static string Delete(HistoryStore store, HistoryRecord record)
    {
        var imagePath = record.RelativeImagePath;
        var thumbPath = record.RelativeThumbnailPath;
        try
        {
            var removed = store.Repository.Delete(record.Id);
            var imageExists = File.Exists(PathsFor(store, imagePath));
            var thumbExists = thumbPath is not null && File.Exists(PathsFor(store, thumbPath));

            AppLog.Info(
                "History",
                $"历史删除：名称={record.Name} Id={record.Id} 行已删除={removed} " +
                $"图片={imagePath} 图片仍在={imageExists} " +
                $"缩略图={thumbPath ?? "-"} 缩略图仍在={thumbExists} " +
                $"剩余记录数={store.Count()}");

            return removed
                ? StringsUi.Format("S.History.Deleted", record.Name)
                : StringsUi.Format("S.History.DeleteFailed", "?");
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"历史删除失败：{record.Name}", ex);
            return StringsUi.Format("S.History.DeleteFailed", ex.Message);
        }
    }

    /// <summary>切换收藏（T26）：写入数据库并返回更新后的记录。</summary>
    internal static HistoryRecord? ToggleFavorite(HistoryStore store, HistoryRecord record)
    {
        try
        {
            var updated = store.Repository.SetFavorite(record.Id, !record.IsFavorite, DateTime.UtcNow);
            if (updated is null)
            {
                AppLog.Warn("History", $"切换收藏失败（记录已不存在）：Id={record.Id}");
                return null;
            }

            AppLog.Info(
                "History",
                $"历史收藏切换：名称={updated.Name} Id={updated.Id} 收藏={record.IsFavorite} → {updated.IsFavorite}");
            return updated;
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"切换收藏失败：{record.Name}", ex);
            return null;
        }
    }

    /// <summary>重命名（T26）：空名称/全非法字符由 Core 的 <see cref="HistoryRename"/> 拒绝。</summary>
    internal static HistoryRecord? Rename(HistoryStore store, HistoryRecord record, string? input, out string? error)
    {
        error = null;
        if (!HistoryRename.TryNormalize(input, out var name, out var reason))
        {
            error = reason;
            AppLog.Info(
                "History",
                $"历史重命名被拒绝：Id={record.Id} 输入=\"{input}\" 原因={reason}");
            return null;
        }

        if (string.Equals(name, record.Name, StringComparison.Ordinal))
        {
            AppLog.Info("History", $"历史重命名：名称未变化（{name}），跳过写入");
            return record;
        }

        try
        {
            var updated = record.WithName(name, DateTime.UtcNow);
            if (!store.Repository.Update(updated))
            {
                error = "记录已不存在。";
                return null;
            }

            AppLog.Info(
                "History",
                $"历史重命名成功：Id={record.Id} {record.Name} → {updated.Name} " +
                $"（创建时间不变={HistoryRecord.FormatUtc(updated.CreatedAtUtc)}）");
            return updated;
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"历史重命名失败：{record.Name}", ex);
            error = ex.Message;
            return null;
        }
    }

    /// <summary>把相对路径换算成绝对路径（删除后核对文件是否真的没了）。</summary>
    private static string PathsFor(HistoryStore store, string relativePath) =>
        HistoryPaths.ToAbsolute(store.Repository.DataRoot, relativePath);
}
