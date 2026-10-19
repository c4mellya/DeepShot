using System.IO;
using ZeepCut.Core.Settings;

namespace ZeepCut.Platform;

/// <summary>
/// 应用层设置存储器（T29）：内存里的一份 <see cref="AppSettings"/> + 磁盘上的 settings.json。
/// </summary>
/// <remarks>
/// <para>
/// <b>当前值与磁盘值分开</b>：设置窗口的"保存失败"必须能回滚到磁盘上那份（<see cref="Reload"/>），
/// 所以这里同时保留 <see cref="Current"/>（正在生效）与最近的保存结果。
/// </para>
/// <para>
/// <b>保存失败不破坏旧文件</b>：写盘走 <see cref="AppSettingsFile.Save"/> → <see cref="Core.AtomicFile"/>，
/// 失败时抛异常且旧文件原样；调用方（设置窗口）提示用户并把界面回滚。
/// 内存里的 <see cref="Current"/> 只在写盘成功后才更新。
/// </para>
/// <para>
/// <b>路径</b>：固定 <c>%LOCALAPPDATA%\ZeepCut\settings.json</c>（AGENTS：数据都在 LocalAppData）。
/// </para>
/// </remarks>
public sealed class SettingsStore
{
    private readonly string _path;

    /// <param name="dataRoot">数据根目录（默认 <see cref="AppPaths.DataRoot"/>）。</param>
    /// <param name="fileName">设置文件名（测试可换成别的）。</param>
    public SettingsStore(string? dataRoot = null, string fileName = AppSettings.FileName)
    {
        _path = Path.Combine(dataRoot ?? AppPaths.DataRoot, fileName);
    }

    /// <summary>设置文件的完整路径。</summary>
    public string FilePath => _path;

    /// <summary>启动时读取到的分类（用于日志）。</summary>
    public LoadStatus LoadStatus { get; private set; } = LoadStatus.Missing;

    /// <summary>启动时读取的警告（非法值回退说明）。</summary>
    public IReadOnlyList<string> LoadWarnings { get; private set; } = Array.Empty<string>();

    /// <summary>当前生效的设置。</summary>
    public AppSettings Current { get; private set; } = AppSettings.Default;

    /// <summary>
    /// 从磁盘加载（幂等：可重复调用，用磁盘内容覆盖内存）。
    /// </summary>
    /// <returns>加载结果（<see cref="Current"/> 已更新）。</returns>
    public AppSettingsLoadResult Load()
    {
        var result = AppSettingsFile.Load(_path, out var exists);
        Current = result.Settings;
        LoadStatus = result.Status;
        LoadWarnings = result.Warnings;

        AppLog.Info(
            "Settings",
            $"设置已加载：{result} 文件={_path} 存在={exists}");
        foreach (var warning in result.Warnings)
        {
            AppLog.Warn("Settings", $"设置回退：{warning}");
        }

        return result;
    }

    /// <summary>
    /// 保存一份新设置（原子写）。**失败时抛异常，且 <see cref="Current"/> 不变、磁盘旧文件不变**。
    /// </summary>
    /// <param name="settings">新的设置（会先校验）。</param>
    /// <returns>实际写入的（校验后的）设置。</returns>
    public AppSettings Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var sanitized = AppSettings.Sanitize(settings);
        foreach (var warning in sanitized.Warnings)
        {
            AppLog.Warn("Settings", $"保存前回退非法值：{warning}");
        }

        AppSettingsFile.Save(_path, sanitized.Settings);
        Current = sanitized.Settings;
        LoadStatus = LoadStatus.Ok;
        AppLog.Info("Settings", $"设置已保存：{sanitized.Settings} 文件={_path}");
        return sanitized.Settings;
    }

    /// <summary>保存失败后的回滚：重新从磁盘读一份（磁盘也读不了时退回默认值）。</summary>
    public AppSettings Reload() => Load().Settings;

    /// <summary>把内存里的设置改成一个新值（不写盘；只用于"想先看效果"的场景，如主题预览）。</summary>
    public void SetVolatile(AppSettings settings) => Current = AppSettings.Sanitize(settings).Settings;
}
