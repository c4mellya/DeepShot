using System.Windows;
using System.Windows.Markup;

namespace DeepShot.Ui;

/// <summary>
/// 全应用统一的控件外观（WinUI 3 / Windows 11 Fluent 规格）：按钮、下拉框、开关、输入框、滚动条、提示、右键菜单。
/// </summary>
/// <remarks>
/// <para>
/// 以<b>隐式样式</b>写进应用资源字典，所有窗口自动换上新外观。颜色全部是 <c>DynamicResource Theme.*</c>，
/// 切换主题时即时生效。
/// </para>
/// <para>
/// 悬停/按下用一层"前景色"薄膜实现（而不是改 Background），所以给按钮显式设置了底色（强调色等）时
/// 反馈依然成立：强调按钮上是一层浅膜，中性按钮上是一层深膜，与 WinUI 的状态色方向一致。
/// 没有缩放、回弹与柔光——原生控件只用颜色表达状态。
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

  <!-- ===== 键盘焦点环（Fluent：2px 高对比外环，只在键盘导航时出现）===== -->
  <Style x:Key="DeepShot.FocusVisual">
    <Setter Property="Control.Template">
      <Setter.Value>
        <ControlTemplate>
          <Border Margin="-3" CornerRadius="7" BorderThickness="2" SnapsToDevicePixels="True"
                  BorderBrush="{DynamicResource Theme.FocusRing}" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== Button（标准按钮，也是所有按钮层级的基础）===== -->
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="11,5" />
    <Setter Property="MinHeight" Value="32" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="UseLayoutRounding" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{StaticResource DeepShot.FocusVisual}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid>
            <Border x:Name="Bd"
                    CornerRadius="{Binding Path=(ui:UiAssist.CornerRadius), RelativeSource={RelativeSource TemplatedParent}}"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" />
            <Border x:Name="Ov" IsHitTestVisible="False" Opacity="0"
                    CornerRadius="{Binding Path=(ui:UiAssist.CornerRadius), RelativeSource={RelativeSource TemplatedParent}}"
                    Background="{TemplateBinding Foreground}" />
            <ContentPresenter x:Name="Content" Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Ov" Storyboard.TargetProperty="Opacity" To="0.06" Duration="0:0:0.083" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Ov" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.083" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Content" Property="Opacity" Value="0.78" />
              <Setter TargetName="Bd" Property="Opacity" Value="0.86" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== 按钮层级（UiKit.StyleButton 选用）===== -->
  <Style x:Key="DeepShot.Button.Secondary" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}" />

  <Style x:Key="DeepShot.Button.Primary" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="{DynamicResource Theme.Accent}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
    <Setter Property="Foreground" Value="{DynamicResource Theme.OnAccent}" />
  </Style>

  <Style x:Key="DeepShot.Button.Danger" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="{DynamicResource Theme.Error}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Error}" />
    <Setter Property="Foreground" Value="{DynamicResource Theme.OnAccent}" />
  </Style>

  <Style x:Key="DeepShot.Button.Subtle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Background" Value="Transparent" />
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
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
  </Style>

  <!-- ===== CheckBox（Fluent ToggleSwitch：40×20，悬停时滑块放大）===== -->
  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Grid Width="40" Height="20" VerticalAlignment="Center">
              <Border x:Name="Track" CornerRadius="10" BorderThickness="1" Background="Transparent"
                      BorderBrush="{DynamicResource Theme.SecondaryForeground}" />
              <Border x:Name="TrackOn" CornerRadius="10" Opacity="0" Background="{DynamicResource Theme.Accent}" />
              <Border x:Name="FocusRing" CornerRadius="13" Margin="-3" BorderThickness="2" Visibility="Collapsed"
                      IsHitTestVisible="False" BorderBrush="{DynamicResource Theme.FocusRing}" />
              <Ellipse x:Name="Thumb" Width="12" Height="12" HorizontalAlignment="Left" Margin="4,0,0,0"
                       Fill="{DynamicResource Theme.SecondaryForeground}" RenderTransformOrigin="0.5,0.5">
                <Ellipse.RenderTransform>
                  <TransformGroup>
                    <ScaleTransform x:Name="ThumbScale" ScaleX="1" ScaleY="1" />
                    <TranslateTransform x:Name="ThumbShift" X="0" />
                  </TransformGroup>
                </Ellipse.RenderTransform>
              </Ellipse>
            </Grid>
            <ContentPresenter x:Name="Label" Margin="12,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True" />
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Track" Property="BorderBrush" Value="Transparent" />
              <Setter TargetName="Thumb" Property="Fill" Value="{DynamicResource Theme.OnAccent}" />
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X" To="20" Duration="0:0:0.167">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="TrackOn" Storyboard.TargetProperty="Opacity" To="1" Duration="0:0:0.083" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X" To="0" Duration="0:0:0.167">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut" /></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                    <DoubleAnimation Storyboard.TargetName="TrackOn" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.083" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbScale" Storyboard.TargetProperty="ScaleX" To="1.167" Duration="0:0:0.083" />
                    <DoubleAnimation Storyboard.TargetName="ThumbScale" Storyboard.TargetProperty="ScaleY" To="1.167" Duration="0:0:0.083" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbScale" Storyboard.TargetProperty="ScaleX" To="1" Duration="0:0:0.083" />
                    <DoubleAnimation Storyboard.TargetName="ThumbScale" Storyboard.TargetProperty="ScaleY" To="1" Duration="0:0:0.083" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="FocusRing" Property="Visibility" Value="Visible" />
            </Trigger>
            <!-- 无文字的独立开关：不留标签间距，轨道右缘与同列其他控件对齐。 -->
            <Trigger Property="HasContent" Value="False">
              <Setter TargetName="Label" Property="Visibility" Value="Collapsed" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== TextBox（Fluent：底部描边，聚焦时变为 2px 强调色）===== -->
  <Style TargetType="TextBox">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="CaretBrush" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="SelectionBrush" Value="{DynamicResource Theme.Accent}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="10,5,6,6" />
    <Setter Property="MinHeight" Value="32" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Grid>
            <Border x:Name="Bd" CornerRadius="4"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}">
              <!-- Padding 由 TextBox 自己作用在内容宿主上；外框再套一次会让文字内缩翻倍。 -->
              <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                            VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                            HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
            </Border>
            <Border x:Name="Ov" CornerRadius="4" Opacity="0" IsHitTestVisible="False"
                    Background="{DynamicResource Theme.Foreground}" />
            <Border x:Name="Underline" CornerRadius="4" BorderThickness="0,0,0,1" IsHitTestVisible="False"
                    BorderBrush="{DynamicResource Theme.TertiaryForeground}" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.025" />
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0" />
              <Setter TargetName="Underline" Property="BorderThickness" Value="0,0,0,2" />
              <Setter TargetName="Underline" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ComboBoxItem（选中项：左侧强调色短竖条）===== -->
  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Padding" Value="12,7,12,7" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Grid Margin="0,1" Background="Transparent">
            <Border CornerRadius="4" x:Name="Ov" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <Border x:Name="Pill" Width="3" Height="16" CornerRadius="1.5" HorizontalAlignment="Left" Visibility="Collapsed"
                    Background="{DynamicResource Theme.Accent}" />
            <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.06" />
              <Setter TargetName="Pill" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.06" />
            </Trigger>
            <MultiTrigger>
              <MultiTrigger.Conditions>
                <Condition Property="IsSelected" Value="True" />
                <Condition Property="IsHighlighted" Value="True" />
              </MultiTrigger.Conditions>
              <Setter TargetName="Ov" Property="Opacity" Value="0.09" />
            </MultiTrigger>
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
    <Setter Property="Padding" Value="11,5,34,6" />
    <Setter Property="MinHeight" Value="32" />
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
                    <Border x:Name="Bd" CornerRadius="4" BorderThickness="1"
                            Background="{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                            BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource AncestorType=ComboBox}}" />
                    <Border x:Name="Ov" CornerRadius="4" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Ov" Property="Opacity" Value="0.04" />
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="Ov" Property="Opacity" Value="0.02" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False"
                              HorizontalAlignment="Left" VerticalAlignment="Center"
                              Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" />
            <Path x:Name="Arrow" Data="M0,0 L4,4 L8,0" Stroke="{DynamicResource Theme.SecondaryForeground}"
                  StrokeThickness="1" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                  HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,1,14,0" IsHitTestVisible="False" />
            <!-- 外层留白给投影；HorizontalOffset 抵消留白，让列表左缘与下拉框对齐。 -->
            <Popup x:Name="PART_Popup" Placement="Bottom" VerticalOffset="2" HorizontalOffset="-12"
                   AllowsTransparency="True" Focusable="False"
                   IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="Fade">
              <Grid Margin="12,2,12,18">
                <Border CornerRadius="8" Background="{DynamicResource Theme.Surface}">
                  <Border.Effect>
                    <DropShadowEffect BlurRadius="18" ShadowDepth="5" Direction="270" Opacity="0.18"
                                      Color="#000000" RenderingBias="Performance" />
                  </Border.Effect>
                </Border>
                <Border CornerRadius="8" BorderThickness="1" Padding="4"
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
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ScrollBar（Fluent：平时是细条，指针移入滚动条区域后展开）===== -->
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
                        <Border x:Name="T" CornerRadius="1" Margin="5,2" Opacity="0.45"
                                Background="{DynamicResource Theme.SecondaryForeground}" />
                      </Grid>
                      <ControlTemplate.Triggers>
                        <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ScrollBar}}" Value="True">
                          <Setter TargetName="T" Property="Margin" Value="3,2" />
                          <Setter TargetName="T" Property="CornerRadius" Value="3" />
                          <Setter TargetName="T" Property="Opacity" Value="0.6" />
                        </DataTrigger>
                        <DataTrigger Binding="{Binding Orientation, RelativeSource={RelativeSource AncestorType=ScrollBar}}" Value="Horizontal">
                          <Setter TargetName="T" Property="Margin" Value="2,5" />
                        </DataTrigger>
                        <MultiDataTrigger>
                          <MultiDataTrigger.Conditions>
                            <Condition Binding="{Binding Orientation, RelativeSource={RelativeSource AncestorType=ScrollBar}}" Value="Horizontal" />
                            <Condition Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ScrollBar}}" Value="True" />
                          </MultiDataTrigger.Conditions>
                          <Setter TargetName="T" Property="Margin" Value="2,3" />
                        </MultiDataTrigger>
                        <Trigger Property="IsDragging" Value="True">
                          <Setter TargetName="T" Property="Opacity" Value="0.8" />
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

  <!-- ===== ToolTip ===== -->
  <Style TargetType="ToolTip">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Padding" Value="8,5,8,6" />
    <Setter Property="HasDropShadow" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Grid Margin="8,2,8,10">
            <Border CornerRadius="4" Background="{TemplateBinding Background}">
              <Border.Effect>
                <DropShadowEffect BlurRadius="10" ShadowDepth="3" Direction="270" Opacity="0.16"
                                  Color="#000000" RenderingBias="Performance" />
              </Border.Effect>
            </Border>
            <Border CornerRadius="4" BorderThickness="1" Padding="{TemplateBinding Padding}" MaxWidth="320"
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

  <!-- ===== ContextMenu / MenuItem / Separator（Fluent MenuFlyout）===== -->
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
            <Border CornerRadius="8" Background="{DynamicResource Theme.Surface}">
              <Border.Effect>
                <DropShadowEffect BlurRadius="20" ShadowDepth="6" Direction="270" Opacity="0.2"
                                  Color="#000000" RenderingBias="Performance" />
              </Border.Effect>
            </Border>
            <Border CornerRadius="8" BorderThickness="1" Padding="4" MinWidth="180"
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
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Grid SnapsToDevicePixels="True" Background="Transparent" Margin="0,1">
            <Border x:Name="Ov" CornerRadius="4" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <Grid Margin="{TemplateBinding Padding}">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="22" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
              </Grid.ColumnDefinitions>
              <Path x:Name="Check" Visibility="Collapsed" Data="M0,4 L3.5,7.5 L10,0" Stroke="{DynamicResource Theme.Foreground}"
                    StrokeThickness="1.3" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
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
              <Setter TargetName="Ov" Property="Opacity" Value="0.06" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.4" />
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
          <Border Height="1" Margin="-4,4" Background="{DynamicResource Theme.Divider}" />
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== 自绘标题栏的窗口按钮（与系统标题栏同尺寸 46×32）===== -->
  <Style x:Key="DeepShot.CaptionButton" TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Width" Value="46" />
    <Setter Property="Height" Value="32" />
    <Setter Property="Focusable" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid Background="{TemplateBinding Background}">
            <Border x:Name="Bd" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="0.06" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="0.04" />
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
            <!-- Windows 11 标题栏关闭按钮的系统红，不跟随主题。 -->
            <Border x:Name="Bd" Background="#C42B1C" Opacity="0" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="1" />
              <Setter Property="Foreground" Value="White" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Opacity" Value="0.9" />
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
