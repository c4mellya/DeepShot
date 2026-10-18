using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ZeepCut.Platform;

/// <summary>
/// 极简文件日志：<c>%LOCALAPPDATA%\ZeepCut\logs\app-YYYYMMDD.log</c>，同一天追加。
/// 每条记录带本地时间戳、级别、来源、线程 ID；异常附带完整堆栈。
/// 日志写入失败绝不能影响应用运行，因此所有 IO 异常都被吞掉（仅 <see cref="Debug"/> 输出）。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static string? _currentLogPath;

    /// <summary>创建数据目录，并写入启动记录。应在应用启动时调用一次。</summary>
    public static void Initialize()
    {
        AppPaths.EnsureCreated();
        Write("INFO", "App", $"===== Zeep Cut 启动 (pid={Environment.ProcessId}, version={GetVersion()}) =====", null);
        Write("INFO", "App", $"数据目录: {AppPaths.DataRoot}", null);
    }

    /// <summary>最近一次成功写入的日志文件完整路径。</summary>
    public static string? CurrentLogPath
    {
        get
        {
            lock (Gate)
            {
                return _currentLogPath;
            }
        }
    }

    public static void Info(string source, string message) => Write("INFO", source, message, null);

    public static void Warn(string source, string message) => Write("WARN", source, message, null);

    public static void Error(string source, string message, Exception? exception = null) =>
        Write("ERROR", source, message, exception);

    private static void Write(string level, string source, string message, Exception? exception)
    {
        try
        {
            var now = DateTime.Now;
            var path = AppPaths.GetLogFilePath(now);
            var builder = new StringBuilder(256);
            builder.Append('[')
                   .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                   .Append("] [").Append(level)
                   .Append("] [").Append(source)
                   .Append("] [T").Append(Environment.CurrentManagedThreadId)
                   .Append("] ").Append(message);
            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogsDirectory);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Utf8NoBom);
                writer.WriteLine(builder.ToString());
                _currentLogPath = path;
            }
        }
        catch (Exception logFailure)
        {
            Debug.WriteLine($"[AppLog] 写日志失败: {logFailure}");
        }
    }

    private static string GetVersion() =>
        typeof(AppLog).Assembly.GetName().Version?.ToString() ?? "unknown";
}
