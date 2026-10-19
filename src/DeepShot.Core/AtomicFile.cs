using System.Text;

namespace DeepShot.Core;

/// <summary>
/// 原子写入文本文件（T29）：先写同目录下的临时文件，再整体替换目标文件。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：设置文件写一半就断电/崩溃，下次启动读到半个 JSON 时整份设置都会丢；
/// 用户最直观的感受是"刚改的快捷键、主题、开机启动全没了"。所以这里保证：
/// **要么是旧内容，要么是新内容，不会是半份内容**。
/// </para>
/// <para>
/// <b>做法</b>：临时文件放在目标文件**同一个目录**（跨卷 <c>File.Replace/Move</c> 会失败），
/// 写完 <c>Flush(true)</c> 落盘，再替换目标文件：
/// <list type="number">
/// <item>目标文件存在 → <see cref="File.Replace(string, string, string?)"/>（保留原索引/ACL，原子替换）；</item>
/// <item>目标文件不存在 → <see cref="File.Move(string, string)"/>（不同卷/无原文件时的回退路径）。</item>
/// </list>
/// </para>
/// <para>
/// <b>失败语义</b>：任何一步失败都会清理临时文件，**并保证旧文件原封不动**
/// （<c>AtomicWrite_RenameFails_OldFileIntact</c> 用"目标是个目录"制造替换失败来锁住这条）。
/// 失败以 <see cref="IOException"/> / 原始异常类型向上抛，由调用方决定是否提示用户。
/// </para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>临时文件后缀（同一目录，写完即删/即替换）。</summary>
    public const string TemporarySuffix = ".tmp";

    /// <summary>UTF-8 无 BOM（与日志、history-ui.json 一致；Notepad/编辑器都能认）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 以原子方式写入文本。目标目录必须已存在（调用方负责创建，避免"顺手建目录"掩盖权限问题）。
    /// </summary>
    /// <param name="path">目标文件完整路径。</param>
    /// <param name="content">完整文本内容（会被整体替换，不是追加）。</param>
    public static void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException($"路径缺少目录部分：{path}", nameof(path));
        }

        var temporaryPath = fullPath + TemporarySuffix;

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.None))
            {
                var bytes = Utf8NoBom.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
                // 落盘：不刷盘的话"文件已替换但内容还在页缓存里"，掉电后就是空文件。
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>删除一个临时文件（尽力而为，绝不再抛；用于失败收尾）。</summary>
    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 文件被占用等：留给下次清理，不影响本次失败结果。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
