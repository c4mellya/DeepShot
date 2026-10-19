using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ZeepCut.Platform;

/// <summary>
/// 开机启动（T29）：当前用户方式，写 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么用 HKCU 而不用任务计划/启动文件夹</b>：PLAN 要求"开机启动采用当前用户机制"，
/// 写 HKCU 的 Run 项不需要管理员权限、不触发 UAC、卸载时容易清理，是这类托盘工具的通行做法。
/// </para>
/// <para>
/// <b>值内容</b>：完整可执行文件路径加引号（路径可能含空格，不加引号不会启动）。
/// 有安装包后这里应改成安装目录里的 exe（T34 再确认）；当前是构建产物路径。
/// </para>
/// <para>
/// <b>以设置为准</b>：<see cref="Sync"/> 无论用户勾选还是取消，都让注册表与设置一致；
/// 用户手工删掉启动项时，下次启动且设置为"开"时会重新写回（这是"设置里有开启"的正常表现）。
/// </para>
/// </remarks>
public static class StartupRegistration
{
    /// <summary>Run 键路径（当前用户）。</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>deep-cut 的启动项名称。</summary>
    public const string ValueName = "ZeepCut";

    /// <summary>
    /// 需要开机启动时要写入的命令行（带引号的 exe 路径）。
    /// </summary>
    /// <remarks>
    /// <b>调用方传裸路径</b>：这里统一加引号。桌面实测撞过一次双引号
    /// （<c>""C:\...\ZeepCut.exe""</c>，命令行解析会失败），所以对已经带引号的输入先剥掉再包。
    /// </remarks>
    public static string BuildCommand(string? executablePath = null)
    {
        var exe = executablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(exe))
        {
            throw new InvalidOperationException("无法确定当前可执行文件路径，无法设置开机启动。");
        }

        return "\"" + exe.Trim().Trim('"') + "\"";
    }

    /// <summary>读取注册表里当前的启动项命令（没有时为 null）。</summary>
    public static string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Startup", $"读取开机启动注册表项失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>是否已经存在 deep-cut 的启动项。</summary>
    public static bool IsEnabled() => !string.IsNullOrWhiteSpace(Read());

    /// <summary>写入启动项。失败返回 false（并写日志），不抛异常。</summary>
    public static bool Enable(string? executablePath = null)
    {
        try
        {
            var command = BuildCommand(executablePath);
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                AppLog.Warn("Startup", $"无法打开注册表键 HKCU\\{RunKeyPath}（写入开机启动失败）");
                return false;
            }

            key.SetValue(ValueName, command, RegistryValueKind.String);
            AppLog.Info("Startup", $"已写入开机启动项 {ValueName}={command}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Startup", $"写入开机启动项失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>删除启动项（本来就不存在也算成功）。失败返回 false，不抛异常。</summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return true;
            }

            if (key.GetValue(ValueName) is null)
            {
                return true;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            AppLog.Info("Startup", $"已删除开机启动项 {ValueName}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Startup", $"删除开机启动项失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 让注册表与用户意图一致（<paramref name="shouldEnable"/>）。返回是否成功同步。
    /// </summary>
    /// <remarks>
    /// 每次启动与每次在设置里切换都调它，因此"设置里是关的但注册表还留着"这种半成品状态会被纠正。
    /// </remarks>
    public static bool Sync(bool shouldEnable, string? executablePath = null)
    {
        var result = shouldEnable ? Enable(executablePath) : Disable();
        AppLog.Info(
            "Startup",
            $"开机启动同步：期望={(shouldEnable ? "开启" : "关闭")} 结果={(result ? "成功" : "失败")} " +
            $"注册表当前={(Read() ?? "(无)")}");
        return result;
    }
}

/// <summary>
/// 用资源管理器 / 默认浏览器打开外部位置（T29 数据目录、T32 下载页）。
/// </summary>
/// <remarks>
/// 不用 <c>Process.Start(url)</c>：它在 .NET Core+ 上要靠 <c>UseShellExecute</c> 且对 URL 的限制更多；
/// 这里统一走 <c>ShellExecuteW</c>，与用户在资源管理器里双击的行为一致，失败会返回 Win32 错误码。
/// </remarks>
public static class ExternalLinks
{
    private const int SwShownormal = 1;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ShellExecuteW(
        IntPtr hwnd,
        string? lpOperation,
        string lpFile,
        string? lpParameters,
        string? lpDirectory,
        int nShowCmd);

    /// <summary>打开一个文件夹（必须已存在；不存在时返回 false）。</summary>
    public static bool TryOpenFolder(string path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            error = $"目录不存在：{path}";
            return false;
        }

        return TryShellExecute(path, out error);
    }

    /// <summary>用默认浏览器打开一个 http/https 地址（拒绝其它协议，避免被清单里塞进来的东西执行）。</summary>
    public static bool TryOpenUrl(string url, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = $"不是有效的 http/https 地址：{url}";
            return false;
        }

        return TryShellExecute(uri.ToString(), out error);
    }

    private static bool TryShellExecute(string target, out string? error)
    {
        error = null;
        try
        {
            var result = ShellExecuteW(IntPtr.Zero, "open", target, null, null, SwShownormal);
            // ShellExecuteW 返回值 <= 32 表示失败（这是老 API 的约定）。
            var code = result.ToInt64();
            if (code > 32)
            {
                AppLog.Info("Platform", $"已请求外部程序打开：{target}");
                return true;
            }

            error = $"ShellExecute 失败（代码 {code}，Win32Error={Marshal.GetLastWin32Error()}）";
            AppLog.Warn("Platform", $"打开 {target} 失败：{error}");
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("Platform", $"打开 {target} 时出错", ex);
            return false;
        }
    }
}

/// <summary>数据目录的统计（设置窗口显示"共 N 条 · 图片 X 个文件"）。</summary>
public static class DataFolderSummary
{
    /// <summary>数一个目录里的文件（目录不存在或读不了时返回 0）。</summary>
    public static int CountFiles(string directory, string pattern = "*")
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Count()
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>数据目录里各子目录的文件数（images / thumbs / logs）。</summary>
    public static string Describe()
    {
        var builder = new StringBuilder();
        builder.Append("images=").Append(CountFiles(AppPaths.ImagesDirectory, "*.png"));
        builder.Append(" thumbs=").Append(CountFiles(AppPaths.ThumbsDirectory, "*.png"));
        builder.Append(" logs=").Append(CountFiles(AppPaths.LogsDirectory, "*.log"));
        return builder.ToString();
    }
}
