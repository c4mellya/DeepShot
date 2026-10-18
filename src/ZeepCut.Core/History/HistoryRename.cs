namespace ZeepCut.Core.History;

/// <summary>
/// 图库重命名的校验与规范化（T26，纯逻辑、可单测）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么空名称要拒绝而不是静默回退</b>：图库里清空名称后如果悄悄变回
/// <c>截图</c>，用户会以为"改名生效了"，其实只是换了个默认名。T26 的选择是
/// **在输入框这一层就给出明确原因并拒绝提交**；只有调用方明确要求
/// <see cref="NormalizeOrFallback"/> 时（例如批量导入/程序化生成）才回退默认名。
/// </para>
/// <para>
/// <b>重名允许</b>：PLAN 只要求按名称搜索，不要求名称唯一（Id 才是主键），
/// 所以这里**不做**"同名自动追加 (2)"——那是新建记录（<see cref="HistoryName.CreateUniqueName"/>）的规则。
/// </para>
/// </remarks>
public static class HistoryRename
{
    /// <summary>名称不能为空时的原因文案。</summary>
    public const string EmptyNameMessage = "名称不能为空。";

    /// <summary>名称清洗后与原名相同时返回 true（UI 可跳过无谓的数据库写入）。</summary>
    public static bool IsUnchanged(string? currentName, string? input)
    {
        if (!TryNormalize(input, out var normalized, out _))
        {
            return false;
        }

        return string.Equals(
            HistoryName.Sanitize(currentName),
            normalized,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 校验并清洗用户输入的名称。
    /// </summary>
    /// <param name="input">用户输入（可能带首尾空白、非法字符、<c>.png</c>）。</param>
    /// <param name="name">规范化后的名称（成功时有效）。</param>
    /// <param name="error">失败原因（面向用户的中文文案）；成功时为 null。</param>
    /// <returns>输入可用返回 true；空白输入返回 false。</returns>
    public static bool TryNormalize(string? input, out string name, out string? error)
    {
        name = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = EmptyNameMessage;
            return false;
        }

        // 只由非法字符/点/空格组成的输入，清洗后也会变成空串：同样按"空名称"处理，
        // 否则用户输入 "???" 会得到 "__" 这种看起来像改成功的结果。
        var sanitized = SanitizeForRename(input);
        if (sanitized.Length == 0)
        {
            error = EmptyNameMessage;
            return false;
        }

        name = sanitized;
        return true;
    }

    /// <summary>
    /// 校验并清洗；输入不可用时回退到当前名称（再不行就默认前缀）。
    /// 用于"程序化改名"场景，保证总能得到一个可用名称。
    /// </summary>
    public static string NormalizeOrFallback(string? input, string? fallbackName = null)
    {
        if (TryNormalize(input, out var name, out _))
        {
            return name;
        }

        return HistoryName.Sanitize(fallbackName);
    }

    /// <summary>
    /// 清洗名称，但**不对"清洗后为空"的输入做默认名回退**（回退由调用方按语义决定）：
    /// 全是非法字符/点/空格的输入在这里返回空串。
    /// </summary>
    private static string SanitizeForRename(string input) =>
        HasUsableCharacter(input) ? HistoryName.Sanitize(input) : string.Empty;

    /// <summary>输入里是否至少有一个非控制、非 Windows 非法字符的可见字符。</summary>
    private static bool HasUsableCharacter(string input)
    {
        foreach (var ch in input)
        {
            if (char.IsControl(ch))
            {
                continue;
            }

            if (ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                continue;
            }

            if (ch is ' ' or '.')
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
