using System.Globalization;
using System.IO;
using ZeepCut.Core;

namespace ZeepCut.Platform;

/// <summary>
/// 应用数据目录布局：<c>%LOCALAPPDATA%\ZeepCut\{logs,images,thumbs}</c>。
/// 所有数据都放在当前用户的 LocalAppData，不写入安装目录。
/// </summary>
public static class AppPaths
{
    /// <summary>数据根目录：<c>%LOCALAPPDATA%\ZeepCut</c>。</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppInfo.DataFolderName);

    /// <summary>日志目录：<c>%LOCALAPPDATA%\ZeepCut\logs</c>。</summary>
    public static string LogsDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>截图像目录：<c>%LOCALAPPDATA%\ZeepCut\images</c>（T23 历史启用）。</summary>
    public static string ImagesDirectory => Path.Combine(DataRoot, "images");

    /// <summary>缩略图目录：<c>%LOCALAPPDATA%\ZeepCut\thumbs</c>（T23 历史启用）。</summary>
    public static string ThumbsDirectory => Path.Combine(DataRoot, "thumbs");

    /// <summary>启动时创建全部数据目录；目录已存在时不做任何事。</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(ThumbsDirectory);
    }

    /// <summary>
    /// 按本地日期得到当天的日志文件：<c>logs\app-YYYYMMDD.log</c>。
    /// 同一天的所有记录追加到同一个文件。
    /// </summary>
    public static string GetLogFilePath(DateTime localTime) =>
        Path.Combine(LogsDirectory, $"app-{localTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
}
