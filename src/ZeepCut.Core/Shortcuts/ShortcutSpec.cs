using System.Diagnostics.CodeAnalysis;

namespace ZeepCut.Core.Shortcuts;

/// <summary>
/// 全局快捷键的修饰键。数值刻意与 Win32 <c>MOD_ALT</c>/<c>MOD_CONTROL</c>/<c>MOD_SHIFT</c>/<c>MOD_WIN</c>
/// 保持一致，WPF 层注册时可直接转换为 <c>RegisterHotKey</c> 的 <c>fsModifiers</c>。
/// </summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// 全局快捷键描述：修饰键组合 + 主键虚拟键码。纯逻辑，不依赖 Win32/WPF。
/// </summary>
/// <remarks>
/// <para>支持的文本格式为 <c>修饰键+主键</c>，例如 <c>Alt+A</c>、<c>Ctrl+Alt+Shift+Win+F12</c>。</para>
/// <list type="bullet">
/// <item>修饰键：<c>Ctrl</c>（等价 <c>Control</c>）、<c>Alt</c>、<c>Shift</c>、<c>Win</c>（等价 <c>Windows</c>），不区分大小写，顺序任意，不可重复；</item>
/// <item>主键：<c>A</c>–<c>Z</c>、<c>0</c>–<c>9</c>、<c>F1</c>–<c>F12</c>；</item>
/// <item>至少需要一个修饰键（无修饰键的全局热键会吞掉普通打字，视为非法）；</item>
/// <item>规范化格式固定按 <c>Ctrl+Alt+Shift+Win+主键</c> 的顺序输出。</item>
/// </list>
/// <para>
/// 本类型是 <c>record</c>，值相等只看 <see cref="Modifiers"/> 与 <see cref="VirtualKey"/>，
/// 因此 <see cref="Equals(ShortcutSpec?)"/> 可直接用于冲突检测（<c>alt+a</c> 与 <c>ALT + A</c> 相等）。
/// </para>
/// </remarks>
public sealed record ShortcutSpec
{
    /// <summary>支持的修饰键全集。</summary>
    public const ShortcutModifiers AllModifiers =
        ShortcutModifiers.Alt | ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Win;

    private const int VirtualKey0 = 0x30;
    private const int VirtualKey9 = 0x39;
    private const int VirtualKeyA = 0x41;
    private const int VirtualKeyZ = 0x5A;
    private const int VirtualKeyF1 = 0x70;
    private const int VirtualKeyF12 = 0x7B;

    private ShortcutSpec(ShortcutModifiers modifiers, int virtualKey)
    {
        if (modifiers == ShortcutModifiers.None || (modifiers & ~AllModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "修饰键组合非法。");
        }

        if (!IsSupportedVirtualKey(virtualKey))
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), virtualKey, "主键虚拟键码不受支持。");
        }

        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    /// <summary>修饰键组合，至少包含一个修饰键。</summary>
    public ShortcutModifiers Modifiers { get; }

    /// <summary>主键的 Win32 虚拟键码（例如 A=0x41、0=0x30、F1=0x70）。</summary>
    public int VirtualKey { get; }

    /// <summary>主键的规范名称，例如 <c>A</c>、<c>7</c>、<c>F12</c>。</summary>
    public string KeyName => GetKeyName(VirtualKey);

    /// <summary>是否为受支持的主键虚拟键码（A–Z、0–9、F1–F12）。</summary>
    public static bool IsSupportedVirtualKey(int virtualKey) =>
        (virtualKey >= VirtualKeyA && virtualKey <= VirtualKeyZ) ||
        (virtualKey >= VirtualKey0 && virtualKey <= VirtualKey9) ||
        (virtualKey >= VirtualKeyF1 && virtualKey <= VirtualKeyF12);

    /// <summary>虚拟键码 → 规范主键名；不受支持时抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    public static string GetKeyName(int virtualKey)
    {
        if (virtualKey >= VirtualKeyA && virtualKey <= VirtualKeyZ)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey >= VirtualKey0 && virtualKey <= VirtualKey9)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey >= VirtualKeyF1 && virtualKey <= VirtualKeyF12)
        {
            return $"F{virtualKey - VirtualKeyF1 + 1}";
        }

        throw new ArgumentOutOfRangeException(nameof(virtualKey), virtualKey, "主键虚拟键码不受支持。");
    }

    /// <summary>
    /// 解析快捷键文本。成功时 <paramref name="shortcut"/> 非空、<paramref name="error"/> 为 <c>null</c>；
    /// 失败时 <paramref name="shortcut"/> 为 <c>null</c>，<paramref name="error"/> 为面向用户的中文原因。
    /// </summary>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out ShortcutSpec? shortcut,
        [NotNullWhen(false)] out string? error)
    {
        shortcut = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var modifiers = ShortcutModifiers.None;
        int? virtualKey = null;

        foreach (var rawToken in text.Split('+'))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                error = "快捷键格式无效：存在空的按键部分（请检查多余的“+”）。";
                return false;
            }

            if (TryParseModifier(token, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"修饰键“{token}”重复。";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (TryGetKey(token, out var key))
            {
                if (virtualKey is not null)
                {
                    error = $"快捷键只能包含一个主键，但发现了“{GetKeyName(virtualKey.Value)}”和“{token}”。";
                    return false;
                }

                virtualKey = key;
                continue;
            }

            error = $"无法识别“{token}”。修饰键支持 Ctrl、Alt、Shift、Win；主键支持 A–Z、0–9、F1–F12。";
            return false;
        }

        if (modifiers == ShortcutModifiers.None)
        {
            error = "快捷键至少需要一个修饰键（Ctrl/Alt/Shift/Win）。";
            return false;
        }

        if (virtualKey is null)
        {
            error = "快捷键缺少主键（A–Z、0–9、F1–F12）。";
            return false;
        }

        shortcut = new ShortcutSpec(modifiers, virtualKey.Value);
        error = null;
        return true;
    }

    /// <summary>解析快捷键文本；非法输入抛 <see cref="FormatException"/>（消息可直接展示给用户）。</summary>
    public static ShortcutSpec Parse(string text)
    {
        if (!TryParse(text, out var shortcut, out var error))
        {
            throw new FormatException(error);
        }

        return shortcut;
    }

    /// <summary>规范文本格式，例如 <c>Ctrl+Alt+F1</c>。顺序固定为 Ctrl、Alt、Shift、Win、主键。</summary>
    public string Format()
    {
        var parts = new List<string>(5);

        if (Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    /// <summary>人类可读文本，等同 <see cref="Format"/>。</summary>
    public override string ToString() => Format();

    /// <summary>
    /// 冲突检测：两个快捷键的修饰键与主键都相同即冲突（与书写顺序、大小写无关）。
    /// </summary>
    public bool ConflictsWith(ShortcutSpec? other) => other is not null && Equals(other);

    private static bool TryParseModifier(string token, out ShortcutModifiers modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = ShortcutModifiers.Control;
                return true;
            case "alt":
                modifier = ShortcutModifiers.Alt;
                return true;
            case "shift":
                modifier = ShortcutModifiers.Shift;
                return true;
            case "win":
            case "windows":
                modifier = ShortcutModifiers.Win;
                return true;
            default:
                modifier = ShortcutModifiers.None;
                return false;
        }
    }

    private static bool TryGetKey(string token, out int virtualKey)
    {
        if (token.Length == 1)
        {
            var ch = token[0];

            if (ch is >= 'A' and <= 'Z')
            {
                virtualKey = ch;
                return true;
            }

            if (ch is >= 'a' and <= 'z')
            {
                virtualKey = char.ToUpperInvariant(ch);
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }
        }

        if (token.Length is 2 or 3 &&
            (token[0] == 'F' || token[0] == 'f') &&
            token[1] != '0' &&
            int.TryParse(token.AsSpan(1), out var number) &&
            number is >= 1 and <= 12)
        {
            virtualKey = VirtualKeyF1 + number - 1;
            return true;
        }

        virtualKey = 0;
        return false;
    }
}
