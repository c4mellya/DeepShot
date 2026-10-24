using System.Windows;

namespace DeepShot.Ui;

/// <summary>
/// 全局控件模板读取的附加属性（<see cref="UiStyles"/> 的 XAML 通过 <c>ui:UiAssist.*</c> 引用）。
/// </summary>
/// <remarks>
/// 必须是 <c>public</c>：样式用 <see cref="System.Windows.Markup.XamlReader"/> 解析，松散 XAML 只能访问公开类型。
/// </remarks>
public static class UiAssist
{
    /// <summary>按钮/输入框圆角（默认 8；图标按钮、胶囊标签可改）。</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius",
        typeof(CornerRadius),
        typeof(UiAssist),
        new FrameworkPropertyMetadata(new CornerRadius(Tokens.Radius.Control)));

    /// <summary>主按钮下方的强调色柔光（只给"主操作"用，一个界面最多一个）。</summary>
    public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
        "Glow",
        typeof(bool),
        typeof(UiAssist),
        new FrameworkPropertyMetadata(false));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static bool GetGlow(DependencyObject element) => (bool)element.GetValue(GlowProperty);

    public static void SetGlow(DependencyObject element, bool value) => element.SetValue(GlowProperty, value);
}
