using System.Globalization;

namespace ZeepCut.Core.Updates;

/// <summary>
/// 应用版本号的解析与比较（T32，纯逻辑、可单测）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要单独一个类型</b>：更新清单是"外部数据"（网络上的 JSON，可能被配错、被截断、
/// 甚至被换成完全不同的东西）。用 <see cref="System.Version.Parse"/> 直接解析会在 <c>"0.2"</c>、
/// <c>"v0.2.0"</c>、<c>"0.2.0-beta"</c> 这些常见写法上抛异常，而"解析失败"绝不能变成
/// "已是最新版"（PLAN 明确禁止虚报）。所以这里自己解析，失败一律返回明确原因。
/// </para>
/// <para>
/// <b>接受的写法</b>：可选前缀 <c>v</c>/<c>V</c>；1–4 段数字（<c>0.2</c> = <c>0.2.0.0</c>）；
/// 可选的预发布后缀 <c>-beta</c>、<c>-beta.2</c>（点/连字符分隔的字母数字段）。空段、非数字、
/// 负数、超过 4 段都视为非法。
/// </para>
/// <para>
/// <b>比较规则</b>：先逐段比较数字（缺的段按 0，所以 <c>0.2</c> == <c>0.2.0.0</c>）；
/// 数字相同时，**正式版高于预发布版**（<c>1.0 &gt; 1.0-beta</c>），两个预发布版按段逐位比较
/// （数字段按数值、字母段按序数比较，<c>beta</c> &lt; <c>rc</c>）。这与语义化版本（SemVer）的常识一致，
/// 也避免了"发布了 1.0-beta 却提示比 1.0 新"这种会让用户困惑的结论。
/// </para>
/// </remarks>
public readonly record struct AppVersion : IComparable<AppVersion>, IComparable
{
    /// <summary>段数固定为 4（Major.Minor.Build.Revision），不足补 0。</summary>
    public const int SegmentCount = 4;

    private readonly int[]? _segments;

    private AppVersion(int[] segments, string? preRelease)
    {
        _segments = segments;
        PreRelease = preRelease;
    }

    /// <summary>主版本号。</summary>
    public int Major => Segment(0);

    /// <summary>次版本号。</summary>
    public int Minor => Segment(1);

    /// <summary>修订号（第三段）。</summary>
    public int Build => Segment(2);

    /// <summary>第四段。</summary>
    public int Revision => Segment(3);

    /// <summary>预发布标识（<c>beta</c>、<c>rc.1</c>；正式版为 null）。</summary>
    public string? PreRelease { get; }

    /// <summary>是否预发布版。</summary>
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>四段数字。</summary>
    public IReadOnlyList<int> Segments => _segments ?? new[] { 0, 0, 0, 0 };

    /// <summary>
    /// 解析版本号文本。成功返回实例，失败返回 null 并给出面向用户的原因。
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version, out string? error)
    {
        version = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "版本号为空。";
            return false;
        }

        var raw = text.Trim();
        if (raw.Length > 1 && (raw[0] == 'v' || raw[0] == 'V'))
        {
            raw = raw[1..].TrimStart();
        }

        // 预发布后缀：第一个 '-' 之后的部分（'0.2.0-beta.2' → core='0.2.0', pre='beta.2'）。
        string? preRelease = null;
        var dash = raw.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            preRelease = raw[(dash + 1)..].Trim();
            raw = raw[..dash];
            if (preRelease.Length == 0)
            {
                error = $"版本号“{text}”的预发布标识为空。";
                return false;
            }

            foreach (var part in preRelease.Split('.', StringSplitOptions.None))
            {
                if (part.Length == 0 || !part.All(char.IsAsciiLetterOrDigit))
                {
                    error = $"版本号“{text}”的预发布标识“{preRelease}”无效（只允许字母、数字与点）。";
                    return false;
                }
            }
        }

        var pieces = raw.Split('.', StringSplitOptions.None);
        if (pieces.Length is < 1 or > SegmentCount)
        {
            error = $"版本号“{text}”无效：应为 1–{SegmentCount} 段数字（例如 0.2.0）。";
            return false;
        }

        var segments = new int[SegmentCount];
        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i].Trim();
            if (piece.Length == 0)
            {
                error = $"版本号“{text}”无效：存在空的段。";
                return false;
            }

            if (!int.TryParse(
                    piece,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var number) ||
                number < 0)
            {
                error = $"版本号“{text}”无效：第 {i + 1} 段“{piece}”不是非负整数。";
                return false;
            }

            segments[i] = number;
        }

        version = new AppVersion(segments, preRelease);
        return true;
    }

    /// <summary>解析版本号文本；失败抛 <see cref="FormatException"/>（消息可直接展示给用户）。</summary>
    public static AppVersion Parse(string text) =>
        TryParse(text, out var version, out var error) ? version : throw new FormatException(error);

    /// <summary>按比较规则排序。<paramref name="other"/> 更大时返回负数。</summary>
    public int CompareTo(AppVersion other)
    {
        for (var i = 0; i < SegmentCount; i++)
        {
            var diff = Segment(i).CompareTo(other.Segment(i));
            if (diff != 0)
            {
                return diff;
            }
        }

        // 数字相同：正式版 > 预发布版。
        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        AppVersion other => CompareTo(other),
        _ => throw new ArgumentException("只能与 AppVersion 比较。", nameof(obj)),
    };

    /// <summary>规范文本：<c>0.2.0</c>（第四段为 0 时省略；预发布标识跟 <c>-</c>）。</summary>
    public string ToNormalizedString()
    {
        var parts = 3;
        if (Segment(3) != 0)
        {
            parts = 4;
        }

        var numbers = new string[parts];
        for (var i = 0; i < parts; i++)
        {
            numbers[i] = Segment(i).ToString(CultureInfo.InvariantCulture);
        }

        var text = string.Join('.', numbers);
        return PreRelease is null ? text : text + "-" + PreRelease;
    }

    public override string ToString() => ToNormalizedString();

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// 值相等 = 比较结果为 0（<c>0.2</c> 与 <c>0.2.0</c> 相等）。record struct 的默认相等性会比较
    /// 数组字段的**引用**，那会让两个相同版本号不相等，所以必须带上 <see cref="CompareTo(AppVersion)"/>。
    /// </summary>
    public bool Equals(AppVersion other) => CompareTo(other) == 0;

    /// <summary>与 <see cref="Equals(AppVersion)"/> 一致的哈希（只由版本数值决定）。</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < SegmentCount; i++)
        {
            hash.Add(Segment(i));
        }

        hash.Add(PreRelease?.ToLowerInvariant(), StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private int Segment(int index) => _segments is { Length: SegmentCount } segments ? segments[index] : 0;

    /// <summary>比较两个预发布标识（都非空）：按分隔段逐位比，数字段按数值、字母段按序数。</summary>
    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.', '-', '_');
        var rightParts = right.Split('.', '-', '_');
        var count = Math.Min(leftParts.Length, rightParts.Length);

        for (var i = 0; i < count; i++)
        {
            var a = leftParts[i];
            var b = rightParts[i];
            var aIsNumber = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bIsNumber = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);

            if (aIsNumber && bIsNumber)
            {
                var diff = aNumber.CompareTo(bNumber);
                if (diff != 0)
                {
                    return diff;
                }

                continue;
            }

            // 数字段低于字母段（SemVer 惯例：1.0-alpha < 1.0-alpha.1，数字标识优先级最低）。
            if (aIsNumber)
            {
                return -1;
            }

            if (bIsNumber)
            {
                return 1;
            }

            var textDiff = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (textDiff != 0)
            {
                return textDiff;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
