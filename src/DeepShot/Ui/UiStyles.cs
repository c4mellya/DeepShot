using System.Windows;
using System.Windows.Markup;

namespace DeepShot.Ui;

/// <summary>
/// 全应用统一的控件外观：按钮、下拉框、开关、输入框、滚动条、提示、右键菜单。
/// </summary>
/// <remarks>
/// <para>
/// 以<b>隐式样式</b>写进应用资源字典，所有窗口（设置/历史/贴图菜单/更新对话框…）自动换上新外观，
/// 不需要逐个控件改。颜色全部是 <c>DynamicResource Theme.*</c>，切换主题时即时生效，不必重建控件。
/// </para>
/// <para>
/// 悬停/按下用一层半透明"前景色"覆盖实现（而不是改 Background），所以即使代码里给按钮显式设置了
/// 底色（主按钮的强调色等），悬停反馈依然有效。悬停淡入、按下轻微缩放（0.97）都是动画，不是硬切。
/// </para>
/// <para>
/// <b>按钮层级</b>：<c>DeepShot.Button.Primary/Secondary/Subtle/Ghost/Danger</c> 五个键控样式，
/// 代码里通过 <c>UiKit.StyleButton</c> 选用；主按钮下方有一层同色模糊柔光（<see cref="UiAssist.GlowProperty"/>）。
/// </para>
/// <para>
/// <b>投影只加在兄弟底板上</b>：<c>DropShadowEffect</c> 会把元素连同子元素一起位图化、文字发虚，
/// 所以下拉列表/右键菜单/提示的投影都画在单独的底板上，内容层叠在它上面。
/// </para>
/// </remarks>
internal static class UiStyles
{
    private static bool _installed;

    /// <summary>把样式装进应用资源（只装一次）。</summary>
    internal static void Install(Application? application)
    {
        if (application is null || _installed)
        {
            return;
        }

        _installed = true;
        var dictionary = (ResourceDictionary)XamlReader.Parse(Xaml);
        application.Resources.MergedDictionaries.Add(dictionary);
    }

    private const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:ui="clr-namespace:DeepShot.Ui;assembly=DeepShot">

  <!-- ===== 键盘焦点环（只在键盘导航时出现，鼠标点击不出现）===== -->
  <Style x:Key="DeepShot.FocusVisual">
    <Setter Property="Control.Template">
      <Setter.Value>
        <ControlTemplate>
          <Border Margin="-3" CornerRadius="11" BorderThickness="2" Opacity="0.65" SnapsToDevicePixels="True"
                  BorderBrush="{DynamicResource Theme.Accent}" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== Button（次按钮外观，也是所有按钮层级的基础）===== -->
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="14,6" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="UseLayoutRounding" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{StaticResource DeepShot.FocusVisual}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid x:Name="Root" RenderTransformOrigin="0.5,0.5">
            <Grid.RenderTransform>
              <ScaleTransform x:Name="Scale" ScaleX="1" ScaleY="1" />
            </Grid.RenderTransform>
            <!-- 主按钮柔光：同色底板模糊后下移，像按钮把颜色"投"在了下面。 -->
            <Border x:Name="Glow" Margin="6,8,6,-5" Opacity="0.42" Visibility="Collapsed" IsHitTestVisible="False"
                    CornerRadius="{Binding Path=(ui:UiAssist.CornerRadius), RelativeSource={RelativeSource TemplatedParent}}"
                    Background="{TemplateBinding Background}">
              <Border.Effect>
                <BlurEffect Radius="14" RenderingBias="Performance" />
              </Border.Effect>
            </Border>
            <Border x:Name="Bd"
                    CornerRadius="{Binding Path=(ui:UiAssist.CornerRadius), RelativeSource={RelativeSource TemplatedParent}}"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" />
            <Border x:Name="Ov" IsHitTestVisible="False" Opacity="0"
                    CornerRadius="{Binding Path=(ui:UiAssist.CornerRadius), RelativeSource={RelativeSource TemplatedParent}}"
                    Background="{DynamicResource Theme.Foreground}" />
            <Border x:Name="Pr" IsHitTestVisible="False" Opacity="0"
                    CornerRadius="{Binding Path=(ui:UiAssist.CornerRadius), RelativeSource={RelativeSource TemplatedParent}}"
                    Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="ui:UiAssist.Glow" Value="True">
              <Setter TargetName="Glow" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Ov" Storyboard.TargetProperty="Opacity" To="0.07" Duration="0:0:0.12">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="Glow" Storyboard.TargetProperty="Opacity" To="0.7" Duration="0:0:0.18">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Ov" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.18">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="Glow" Storyboard.TargetProperty="Opacity" To="0.42" Duration="0:0:0.24">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Pr" Property="Opacity" Value="0.07" />
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Scale" Storyboard.TargetProperty="ScaleX" To="0.97" Duration="0:0:0.08" />
                    <DoubleAnimation Storyboard.TargetName="Scale" Storyboard.TargetProperty="ScaleY" To="0.97" Duration="0:0:0.08" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Scale" Storyboard.TargetProperty="ScaleX" To="1" Duration="0:0:0.2">
                      <DoubleAnimation.EasingFunction><BackEase EasingMode="EaseOut" Amplitude="0.6" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="Scale" Storyboard.TargetProperty="ScaleY" To="1" Duration="0:0:0.2">
                      <DoubleAnimation.EasingFunction><BackEase EasingMode="EaseOut" Amplitude="0.6" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45" />
              <Setter TargetName="Glow" Property="Visibility" Value="Collapsed" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== 按钮层级（UiKit.StyleButton 选用）===== -->
  <Style x:Key="DeepShot.Button.Secondary" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Padding" Value="16,7" />
  </Style>

  <Style x:Key="DeepShot.Button.Primary" TargetType="Button" BasedOn="{StaticResource DeepShot.Button.Secondary}">
    <Setter Property="Background" Value="{DynamicResource Theme.Accent}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
    <Setter Property="Foreground" Value="{DynamicResource Theme.OnAccent}" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="ui:UiAssist.Glow" Value="True" />
  </Style>

  <Style x:Key="DeepShot.Button.Danger" TargetType="Button" BasedOn="{StaticResource DeepShot.Button.Secondary}">
    <Setter Property="Background" Value="{DynamicResource Theme.Error}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Error}" />
    <!-- 与主按钮同一前景键：浅色主题白字压深红，深色主题深字压浅珊瑚红（白字在浅珊瑚红上对比度不够）。 -->
    <Setter Property="Foreground" Value="{DynamicResource Theme.OnAccent}" />
    <Setter Property="FontWeight" Value="SemiBold" />
  </Style>

  <Style x:Key="DeepShot.Button.Subtle" TargetType="Button" BasedOn="{StaticResource DeepShot.Button.Secondary}">
    <Setter Property="Background" Value="{DynamicResource Theme.SurfaceAlt}" />
    <Setter Property="BorderBrush" Value="Transparent" />
  </Style>

  <Style x:Key="DeepShot.Button.Ghost" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="Padding" Value="8,5" />
  </Style>

  <!-- ===== 卡片（设置分组、欢迎页、更新说明）===== -->
  <Style x:Key="DeepShot.Card" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Divider}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="12" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
  </Style>

  <!-- ===== CheckBox（开关）===== -->
  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Grid Width="42" Height="24" VerticalAlignment="Center">
              <Border x:Name="Track" CornerRadius="12" BorderThickness="1"
                      Background="{DynamicResource Theme.SurfaceAlt}"
                      BorderBrush="{DynamicResource Theme.Border}" />
              <Border x:Name="TrackOn" CornerRadius="12" Opacity="0"
                      Background="{DynamicResource Theme.Accent}" />
              <Border x:Name="FocusRing" CornerRadius="15" Margin="-3" BorderThickness="2" Opacity="0"
                      IsHitTestVisible="False" BorderBrush="{DynamicResource Theme.Accent}" />
              <Grid x:Name="ThumbHost" Width="16" Height="16" HorizontalAlignment="Left" Margin="4,0,0,0">
                <Grid.RenderTransform>
                  <TranslateTransform x:Name="ThumbShift" X="0" />
                </Grid.RenderTransform>
                <Ellipse Fill="#000000" Opacity="0.18" Margin="0,1.5,0,-1.5" IsHitTestVisible="False">
                  <Ellipse.Effect>
                    <BlurEffect Radius="3" RenderingBias="Performance" />
                  </Ellipse.Effect>
                </Ellipse>
                <Ellipse x:Name="Thumb" Fill="{DynamicResource Theme.SecondaryForeground}" />
              </Grid>
            </Grid>
            <ContentPresenter x:Name="Label" Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True" />
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Thumb" Property="Fill" Value="{DynamicResource Theme.OnAccent}" />
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X" To="18" Duration="0:0:0.22">
                      <DoubleAnimation.EasingFunction><BackEase EasingMode="EaseOut" Amplitude="0.35" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="TrackOn" Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.16" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X" To="0" Duration="0:0:0.22">
                      <DoubleAnimation.EasingFunction><BackEase EasingMode="EaseOut" Amplitude="0.35" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="TrackOn" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.16" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="FocusRing" Property="Opacity" Value="0.55" />
            </Trigger>
            <!-- 无文字的独立开关：不留标签间距，轨道右缘与同列其他控件对齐。 -->
            <Trigger Property="HasContent" Value="False">
              <Setter TargetName="Label" Property="Visibility" Value="Collapsed" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== TextBox ===== -->
  <Style TargetType="TextBox">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="CaretBrush" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="SelectionBrush" Value="{DynamicResource Theme.Accent}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="10,7" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Grid>
            <!-- 聚焦时外圈一层淡强调色"光晕"（3px），比单纯换边框色更醒目。 -->
            <Border x:Name="Halo" Margin="-3" CornerRadius="11" BorderThickness="3" Opacity="0" IsHitTestVisible="False"
                    BorderBrush="{DynamicResource Theme.Accent}" />
            <Border x:Name="Bd" CornerRadius="8"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}">
              <!-- Padding 由 TextBox 自己作用在内容宿主上；外框再套一次会让文字内缩翻倍（约 22px）。 -->
              <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                            VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                            HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
            </Border>
            <Border x:Name="Edge" CornerRadius="8" BorderThickness="1" Opacity="0" IsHitTestVisible="False"
                    BorderBrush="{DynamicResource Theme.Accent}" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Edge" Property="Opacity" Value="0.45" />
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Edge" Property="Opacity" Value="1" />
              <Setter TargetName="Halo" Property="Opacity" Value="0.18" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ComboBoxItem ===== -->
  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Padding" Value="10,7,30,7" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Grid Margin="0,1" Background="Transparent">
            <Border CornerRadius="6" x:Name="Sel" Opacity="0" Background="{DynamicResource Theme.AccentSoft}" />
            <Border CornerRadius="6" x:Name="Ov" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center" />
            <Path x:Name="Check" Data="M0,4 L3.5,7.5 L10,0" Visibility="Collapsed" IsHitTestVisible="False"
                  Stroke="{DynamicResource Theme.Accent}" StrokeThickness="1.8"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                  HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,11,0" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Sel" Property="Opacity" Value="1" />
              <Setter TargetName="Check" Property="Visibility" Value="Visible" />
              <Setter Property="Foreground" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.06" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ComboBox ===== -->
  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Padding" Value="12,7,34,7" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{StaticResource DeepShot.FocusVisual}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Grid>
                    <Border x:Name="Bd" CornerRadius="8" BorderThickness="1"
                            Background="{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                            BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource AncestorType=ComboBox}}" />
                    <Border x:Name="Edge" CornerRadius="8" BorderThickness="1" Opacity="0"
                            BorderBrush="{DynamicResource Theme.Accent}" />
                    <Border x:Name="Ov" CornerRadius="8" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Ov" Property="Opacity" Value="0.03" />
                      <Setter TargetName="Edge" Property="Opacity" Value="0.45" />
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="Edge" Property="Opacity" Value="1" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False"
                              HorizontalAlignment="Left" VerticalAlignment="Center"
                              Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" />
            <Path x:Name="Arrow" Data="M0,0 L4.5,4.5 L9,0" Stroke="{DynamicResource Theme.SecondaryForeground}"
                  StrokeThickness="1.6" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                  HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,13,0" IsHitTestVisible="False"
                  RenderTransformOrigin="0.5,0.5">
              <Path.RenderTransform>
                <RotateTransform x:Name="ArrowTurn" Angle="0" />
              </Path.RenderTransform>
            </Path>
            <!-- 外层留白给投影；HorizontalOffset 抵消留白，让列表左缘与下拉框对齐。 -->
            <Popup x:Name="PART_Popup" Placement="Bottom" VerticalOffset="2" HorizontalOffset="-12"
                   AllowsTransparency="True" Focusable="False"
                   IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="Fade">
              <Grid Margin="12,2,12,18">
                <Border CornerRadius="10" Background="{DynamicResource Theme.Surface}">
                  <Border.Effect>
                    <DropShadowEffect BlurRadius="22" ShadowDepth="6" Direction="270" Opacity="0.16"
                                      Color="#0A1022" RenderingBias="Performance" />
                  </Border.Effect>
                </Border>
                <Border CornerRadius="10" BorderThickness="1" Padding="4"
                        Background="{DynamicResource Theme.Surface}"
                        BorderBrush="{DynamicResource Theme.Border}"
                        MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                  <ScrollViewer MaxHeight="300" CanContentScroll="True">
                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                  </ScrollViewer>
                </Border>
              </Grid>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsDropDownOpen" Value="True">
              <Setter TargetName="Arrow" Property="Stroke" Value="{DynamicResource Theme.Accent}" />
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ArrowTurn" Storyboard.TargetProperty="Angle" To="180" Duration="0:0:0.18">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ArrowTurn" Storyboard.TargetProperty="Angle" To="0" Duration="0:0:0.18">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ScrollBar（细滑块，悬停变粗）===== -->
  <Style x:Key="DeepShotScrollPage" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RepeatButton">
          <Border Background="Transparent" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="12" />
    <Setter Property="MinWidth" Value="0" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Grid Background="Transparent" SnapsToDevicePixels="True">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton>
                <RepeatButton Style="{StaticResource DeepShotScrollPage}" Command="ScrollBar.PageUpCommand" />
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style="{StaticResource DeepShotScrollPage}" Command="ScrollBar.PageDownCommand" />
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Grid Background="Transparent">
                        <Border x:Name="T" CornerRadius="3" Margin="4" Opacity="0.32"
                                Background="{DynamicResource Theme.SecondaryForeground}" />
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="T" Property="Opacity" Value="0.6" />
                          <Setter TargetName="T" Property="Margin" Value="2.5" />
                          <Setter TargetName="T" Property="CornerRadius" Value="4" />
                        </Trigger>
                        <Trigger Property="IsDragging" Value="True">
                          <Setter TargetName="T" Property="Opacity" Value="0.8" />
                          <Setter TargetName="T" Property="Margin" Value="2.5" />
                          <Setter TargetName="T" Property="CornerRadius" Value="4" />
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
              <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False" />
              <Setter Property="Width" Value="Auto" />
              <Setter Property="Height" Value="12" />
              <Setter Property="MinHeight" Value="0" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ToolTip（带投影、长文自动换行）===== -->
  <Style TargetType="ToolTip">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Padding" Value="10,6" />
    <Setter Property="HasDropShadow" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Grid Margin="8,2,8,10">
            <Border CornerRadius="8" Background="{TemplateBinding Background}">
              <Border.Effect>
                <DropShadowEffect BlurRadius="12" ShadowDepth="3" Direction="270" Opacity="0.14"
                                  Color="#0A1022" RenderingBias="Performance" />
              </Border.Effect>
            </Border>
            <Border CornerRadius="8" BorderThickness="1" Padding="{TemplateBinding Padding}" MaxWidth="340"
                    Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}">
              <ContentPresenter>
                <ContentPresenter.Resources>
                  <Style TargetType="TextBlock">
                    <Setter Property="TextWrapping" Value="Wrap" />
                  </Style>
                </ContentPresenter.Resources>
              </ContentPresenter>
            </Border>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ContextMenu / MenuItem / Separator ===== -->
  <Style TargetType="ContextMenu">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="HasDropShadow" Value="False" />
    <!-- 抵消模板外圈给投影留的空白，让菜单左上角仍然对准鼠标/目标。 -->
    <Setter Property="HorizontalOffset" Value="-12" />
    <Setter Property="VerticalOffset" Value="-2" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Grid Margin="12,2,12,18">
            <Border CornerRadius="10" Background="{DynamicResource Theme.Surface}">
              <Border.Effect>
                <DropShadowEffect BlurRadius="24" ShadowDepth="6" Direction="270" Opacity="0.18"
                                  Color="#0A1022" RenderingBias="Performance" />
              </Border.Effect>
            </Border>
            <Border CornerRadius="10" BorderThickness="1" Padding="5" MinWidth="188"
                    Background="{DynamicResource Theme.Surface}" BorderBrush="{DynamicResource Theme.Border}">
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle" />
            </Border>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="MenuItem">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Padding" Value="8,7,12,7" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Grid SnapsToDevicePixels="True" Background="Transparent">
            <Border x:Name="Ov" CornerRadius="6" Opacity="0" Background="{DynamicResource Theme.AccentSoft}" />
            <Grid Margin="{TemplateBinding Padding}">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="22" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
              </Grid.ColumnDefinitions>
              <Path x:Name="Check" Visibility="Collapsed" Data="M0,4 L3.5,7.5 L10,0" Stroke="{DynamicResource Theme.Accent}"
                    StrokeThickness="1.8" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                    HorizontalAlignment="Left" VerticalAlignment="Center" Margin="2,0,0,0" />
              <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True" />
              <TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Margin="24,0,0,0" FontSize="12"
                         VerticalAlignment="Center" Foreground="{DynamicResource Theme.SecondaryForeground}" />
            </Grid>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Check" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="1" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Separator">
          <Border Height="1" Margin="10,5" Background="{DynamicResource Theme.Divider}" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== 自绘标题栏的窗口按钮 ===== -->
  <Style x:Key="DeepShot.CaptionButton" TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource Theme.SecondaryForeground}" />
    <Setter Property="Width" Value="46" />
    <Setter Property="Height" Value="40" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="Cursor" Value="Arrow" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid Background="Transparent">
            <Border x:Name="Bd" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="0.09" />
              <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="0.16" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="DeepShot.CaptionCloseButton" TargetType="Button" BasedOn="{StaticResource DeepShot.CaptionButton}">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid Background="Transparent">
            <!-- 刻意用 Windows 标题栏关闭按钮的系统红（与系统窗口一致，不跟随主题）；深色玻璃上的危险色见 Tokens.Danger。 -->
            <Border x:Name="Bd" Background="#E81123" Opacity="0" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="1" />
              <Setter Property="Foreground" Value="White" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="0.8" />
              <Setter Property="Foreground" Value="White" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>
""";
}
