using System.IO;
using System.Text.Json;
using DeepShot.Platform;

namespace DeepShot.History;

/// <summary>
/// 历史相关的一次性提示状态（T27/T28）：<c>%LOCALAPPDATA%\DeepShot\history-ui.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// 只存"提示是否已经给过"这类用户偏好，不存历史数据本身（历史在 SQLite 里）。
/// 需要独立文件的原因：PLAN 要求"首次使用时解释覆盖不保留旧版本"，首次应该是
/// **跨启动的第一 次**，而不只是每次运行的头一次。
/// </para>
/// <para>
/// <b>容错</b>：文件不存在、内容损坏、没有写权限 —— 一律当作"还没解释过"（首次提示会再出现一次，
/// 不会因为一个可达性问题把功能卡死），并且**绝不向上抛异常**（这个文件的失败不该影响保存）。
/// </para>
/// </remarks>
internal static class HistoryUiState
{
    /// <summary>状态文件名。</summary>
    internal const string FileName = "history-ui.json";

    private static bool _loaded;

    /// <summary>覆盖编辑的说明是否已经给过。</summary>
    internal static bool OverwriteExplained { get; private set; }

    private static string FilePath => Path.Combine(AppPaths.DataRoot, FileName);

    private sealed class Model
    {
        public bool OverwriteExplained { get; set; }
    }

    /// <summary>启动时读一次（幂等）。</summary>
    internal static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            if (!File.Exists(FilePath))
            {
                AppLog.Info("History", $"历史提示状态文件不存在（{FilePath}），覆盖说明将首次显示");
                return;
            }

            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath));
            OverwriteExplained = model?.OverwriteExplained ?? false;
            AppLog.Info("History", $"历史提示状态已读取：覆盖说明已解释={OverwriteExplained}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"读取历史提示状态失败（按未解释处理）：{ex.Message}");
        }
    }

    /// <summary>覆盖说明显示过之后调用：内存 + 文件都记下来（写文件失败只记日志）。</summary>
    internal static void MarkOverwriteExplained()
    {
        OverwriteExplained = true;

        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(new Model { OverwriteExplained = true }));
            AppLog.Info("History", $"已记录“覆盖说明已解释”到 {FilePath}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"写入历史提示状态失败（本次运行内仍然生效）：{ex.Message}");
        }
    }
}
