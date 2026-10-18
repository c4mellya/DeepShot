namespace ZeepCut.Core.History;

/// <summary>
/// 历史记录的来源（T23 存储层的 <c>Source</c> 列）。
/// </summary>
/// <remarks>
/// <para>
/// 与贴图的 <c>PinSource</c> 是**两套枚举**：贴图来源决定"这次操作要不要记一条历史"，
/// 历史来源记录"这条历史是怎么产生并保存下来的"。不要把它们合并 —— 贴图的
/// <c>PinSource.History</c> 表示"从历史图库再贴图，不新增记录"，而
/// <c>HistorySource.History</c> 表示"这条记录本身就是从历史里来的"（T28 覆盖编辑用）。
/// </para>
/// <para>
/// 写进 SQLite 时使用枚举名（<see cref="HistorySourceText.ToStorageValue"/>），
/// 稳定、可读、不随界面语言变化。
/// </para>
/// </remarks>
public enum HistorySource
{
    /// <summary>内部截图动作（复制 / 另存为 / 贴图）。</summary>
    Screenshot = 0,

    /// <summary>外部剪贴板贴图（Alt+S）。</summary>
    Clipboard = 1,

    /// <summary>历史记录本身（T28 覆盖编辑、从历史再贴图）。</summary>
    History = 2,
}

/// <summary><see cref="HistorySource"/> 的描述、存储值与解析（纯逻辑，可单测）。</summary>
public static class HistorySourceText
{
    /// <summary>界面/日志用的中文描述。</summary>
    public static string Describe(HistorySource source) => source switch
    {
        HistorySource.Screenshot => "截图",
        HistorySource.Clipboard => "剪贴板贴图",
        HistorySource.History => "历史记录",
        _ => source.ToString(),
    };

    /// <summary>写入 SQLite 的字符串值（枚举名）。</summary>
    public static string ToStorageValue(HistorySource source) => source.ToString();

    /// <summary>
    /// 从数据库读回来源。无法识别时返回 false 并由调用方回退（不要抛异常，
    /// 一条脏数据不应该让整个图库读不出来）。
    /// </summary>
    public static bool TryParse(string? value, out HistorySource source)
    {
        source = HistorySource.Screenshot;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out source))
        {
            return false;
        }

        // Enum.TryParse 对 "7" 这类越界数字也返回 true，必须再查一次定义。
        if (!Enum.IsDefined(source))
        {
            source = HistorySource.Screenshot;
            return false;
        }

        return true;
    }
}
