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
/// 底色（主按钮的强调色等），悬停反馈依然有效。
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
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ===== Button ===== -->
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="Background" Value="{DynamicResource Theme.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Theme.Border}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="14,6" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Grid>
            <Border x:Name="Bd" CornerRadius="8"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" />
            <Border x:Name="Ov" CornerRadius="8" IsHitTestVisible="False" Opacity="0"
                    Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.07" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.14" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.45" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
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
              <Ellipse x:Name="Thumb" Width="16" Height="16" HorizontalAlignment="Left" Margin="4,0,0,0"
                       Fill="{DynamicResource Theme.SecondaryForeground}">
                <Ellipse.RenderTransform>
                  <TranslateTransform x:Name="ThumbShift" X="0" />
                </Ellipse.RenderTransform>
              </Ellipse>
            </Grid>
            <ContentPresenter Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True" />
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Track" Property="Background" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Thumb" Property="Fill" Value="{DynamicResource Theme.OnAccent}" />
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X"
                                     To="18" Duration="0:0:0.16">
                      <DoubleAnimation.EasingFunction>
                        <CubicEase EasingMode="EaseOut" />
                      </DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ThumbShift" Storyboard.TargetProperty="X"
                                     To="0" Duration="0:0:0.16">
                      <DoubleAnimation.EasingFunction>
                        <CubicEase EasingMode="EaseOut" />
                      </DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
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
    <Setter Property="Padding" Value="10,6" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="Bd" CornerRadius="8"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  Padding="{TemplateBinding Padding}">
            <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                          HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
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
    <Setter Property="Padding" Value="10,7" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="SnapsToDevicePixels" Value="True" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Grid Margin="0,1">
            <Border CornerRadius="6" x:Name="Sel" Opacity="0" Background="{DynamicResource Theme.Accent}" />
            <Border CornerRadius="6" x:Name="Ov" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Sel" Property="Opacity" Value="0.16" />
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.07" />
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
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
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
                    <Border x:Name="Ov" CornerRadius="8" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Ov" Property="Opacity" Value="0.05" />
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin="{TemplateBinding Padding}" IsHitTestVisible="False"
                              HorizontalAlignment="Left" VerticalAlignment="Center"
                              Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" />
            <Path Data="M0,0 L4.5,4.5 L9,0" Stroke="{DynamicResource Theme.SecondaryForeground}"
                  StrokeThickness="1.6" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                  HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,13,0" IsHitTestVisible="False" />
            <Popup x:Name="PART_Popup" Placement="Bottom" VerticalOffset="4" AllowsTransparency="True" Focusable="False"
                   IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="Fade">
              <Border CornerRadius="10" BorderThickness="1" Padding="4"
                      Background="{DynamicResource Theme.Surface}"
                      BorderBrush="{DynamicResource Theme.Border}"
                      MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                <ScrollViewer MaxHeight="280" CanContentScroll="True">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ===== ScrollBar ===== -->
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
                      <Border x:Name="T" CornerRadius="4" Margin="3" Opacity="0.35"
                              Background="{DynamicResource Theme.SecondaryForeground}" />
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="T" Property="Opacity" Value="0.65" />
                        </Trigger>
                        <Trigger Property="IsDragging" Value="True">
                          <Setter TargetName="T" Property="Opacity" Value="0.85" />
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
    <Setter Property="Padding" Value="10,6" />
    <Setter Property="HasDropShadow" Value="False" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToolTip">
          <Border CornerRadius="8" BorderThickness="1" Padding="{TemplateBinding Padding}"
                  Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}">
            <ContentPresenter />
          </Border>
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
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Border CornerRadius="10" BorderThickness="1" Padding="4" MinWidth="180"
                  Background="{DynamicResource Theme.Surface}" BorderBrush="{DynamicResource Theme.Border}">
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle" />
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="MenuItem">
    <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
    <Setter Property="FontFamily" Value="{DynamicResource Theme.FontFamily}" />
    <Setter Property="Padding" Value="8,7" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Grid SnapsToDevicePixels="True">
            <Border x:Name="Ov" CornerRadius="6" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <Grid Margin="{TemplateBinding Padding}">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="20" />
                <ColumnDefinition Width="*" />
              </Grid.ColumnDefinitions>
              <Path x:Name="Check" Visibility="Collapsed" Data="M0,4 L3.5,7.5 L10,0" Stroke="{DynamicResource Theme.Accent}"
                    StrokeThickness="1.8" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                    HorizontalAlignment="Left" VerticalAlignment="Center" Margin="2,0,0,0" />
              <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True" />
            </Grid>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Check" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.08" />
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
          <Border Height="1" Margin="8,4" Background="{DynamicResource Theme.Divider}" />
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
