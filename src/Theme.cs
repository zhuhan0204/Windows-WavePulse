using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal static class Theme
    {
        public static readonly Color WindowColor = Color.FromRgb(244, 247, 250);
        public static readonly Color CardColor = Colors.White;
        public static readonly Color TextColor = Color.FromRgb(25, 35, 48);
        public static readonly Color MutedColor = Color.FromRgb(100, 116, 139);
        public static readonly Color AccentColor = Color.FromRgb(27, 118, 255);
        public static readonly Color AccentSoftColor = Color.FromRgb(230, 240, 255);
        public static readonly Color SuccessColor = Color.FromRgb(18, 166, 104);
        public static readonly Color DangerColor = Color.FromRgb(224, 67, 67);
        public static readonly Color DangerSoftColor = Color.FromRgb(255, 235, 235);
        public static readonly Color BorderColor = Color.FromRgb(228, 235, 243);
        private static Style _modernButtonStyle;
        private static Style _modernComboBoxStyle;
        private static Style _modernContextMenuStyle;
        private static Style _modernMenuItemStyle;
        private static Style _modernCheckBoxStyle;
        private static Style _modernSliderStyle;
        private static Style _colorSwatchButtonStyle;
        private static Style _modernTextBoxStyle;
        private static Style _thinScrollBarStyle;
        private static Style _iconButtonStyle;
        private static Style _floatingActionButtonStyle;
        private static Style _trayMenuButtonStyle;

        public static Style ModernButtonStyle
        {
            get
            {
                if (_modernButtonStyle == null)
                {
                    _modernButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='SnapsToDevicePixels' Value='True'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
    <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='11' Padding='{TemplateBinding Padding}'>
      <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.88'/><Setter TargetName='Surface' Property='BorderBrush' Value='#80AEF1'/></Trigger>
      <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.72'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#1B76FF'/><Setter TargetName='Surface' Property='BorderThickness' Value='2'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.46'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _modernButtonStyle;
            }
        }

        public static Style ModernTextBoxStyle
        {
            get
            {
                if (_modernTextBoxStyle == null)
                {
                    _modernTextBoxStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type TextBox}'>
  <Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#192330'/><Setter Property='BorderBrush' Value='#D7E1ED'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='11,5'/><Setter Property='CaretBrush' Value='#1B76FF'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TextBox}'>
    <Border x:Name='Outer' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
      <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Outer' Property='BorderBrush' Value='#8AB9FF'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Outer' Property='BorderBrush' Value='#1B76FF'/><Setter TargetName='Outer' Property='BorderThickness' Value='2'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.55'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _modernTextBoxStyle;
            }
        }

        public static Style ThinScrollBarStyle
        {
            get
            {
                if (_thinScrollBarStyle == null)
                {
                    _thinScrollBarStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='8'/><Setter Property='Margin' Value='2,4,3,4'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Background='Transparent'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb MinHeight='30' Width='6'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='Grip' Background='#B9C6D6' CornerRadius='3'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#7EA9E8'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#1B76FF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Background='Transparent'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
      </Track>
    </Grid>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _thinScrollBarStyle;
            }
        }

        public static Style IconButtonStyle
        {
            get
            {
                if (_iconButtonStyle == null)
                {
                    _iconButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#64748B'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'><Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='9'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E6F1FF'/><Setter Property='Foreground' Value='#1B76FF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#D8E9FF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.42'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _iconButtonStyle;
            }
        }

        public static Style FloatingActionButtonStyle
        {
            get
            {
                if (_floatingActionButtonStyle == null)
                {
                    _floatingActionButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Setter Property='Width' Value='48'/><Setter Property='Height' Value='48'/><Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#1B76FF'/><Setter Property='BorderBrush' Value='#D6E4F5'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'><Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='24'><Border.Effect><DropShadowEffect BlurRadius='18' ShadowDepth='4' Opacity='0.2' Color='#38577E'/></Border.Effect><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#1B76FF'/><Setter Property='Foreground' Value='White'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#0F5ED5'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.48'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _floatingActionButtonStyle;
            }
        }

        public static Style TrayMenuButtonStyle
        {
            get
            {
                if (_trayMenuButtonStyle == null)
                {
                    _trayMenuButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Setter Property='Height' Value='40'/><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#192330'/><Setter Property='BorderThickness' Value='0'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'><Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='8' Padding='10,0'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#EAF3FF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='#DCEBFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _trayMenuButtonStyle;
            }
        }

        public static Style ColorSwatchButtonStyle
        {
            get
            {
                if (_colorSwatchButtonStyle == null)
                {
                    _colorSwatchButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='BorderBrush' Value='#D3DFEC'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='5'/><Setter Property='Background' Value='White'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
    <Border x:Name='Outer' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}'>
      <ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Stretch'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Outer' Property='BorderBrush' Value='#1B76FF'/><Setter TargetName='Outer' Property='BorderThickness' Value='2'/></Trigger>
      <Trigger Property='IsPressed' Value='True'><Setter TargetName='Outer' Property='Opacity' Value='0.76'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _colorSwatchButtonStyle;
            }
        }

        public static Style ModernCheckBoxStyle
        {
            get
            {
                if (_modernCheckBoxStyle == null)
                {
                    _modernCheckBoxStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type CheckBox}'>
  <Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='11'/><Setter Property='Foreground' Value='#192330'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CheckBox}'>
    <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
      <Border x:Name='Box' Width='18' Height='18' CornerRadius='5' Background='White' BorderBrush='#C7D3E1' BorderThickness='1.2'>
        <Path x:Name='Check' Data='M 3 8 L 7 12 L 15 4' Stroke='White' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
      </Border>
      <ContentPresenter Margin='7,0,0,0' VerticalAlignment='Center'/>
    </StackPanel>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#1B76FF'/></Trigger>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#1B76FF'/><Setter TargetName='Box' Property='BorderBrush' Value='#1B76FF'/><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.48'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _modernCheckBoxStyle;
            }
        }

        public static Style ModernSliderStyle
        {
            get
            {
                if (_modernSliderStyle == null)
                {
                    _modernSliderStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
  <Setter Property='Height' Value='24'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
    <Grid Margin='8,0'>
      <Track x:Name='PART_Track' VerticalAlignment='Center'>
        <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' IsTabStop='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='5' CornerRadius='3' Background='#1B76FF'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' IsTabStop='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='5' CornerRadius='3' Background='#DDE6F0'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Width='17' Height='17'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='Knob' CornerRadius='9' Background='White' BorderBrush='#1B76FF' BorderThickness='2'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Knob' Property='Background' Value='#EAF3FF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _modernSliderStyle;
            }
        }

        public static Style ModernComboBoxStyle
        {
            get
            {
                if (_modernComboBoxStyle == null)
                {
                    _modernComboBoxStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ComboBox}'>
  <Setter Property='Background' Value='White'/>
  <Setter Property='Foreground' Value='#192330'/>
  <Setter Property='BorderBrush' Value='#D7E1ED'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='12,0,10,0'/>
  <Setter Property='SnapsToDevicePixels' Value='True'/>
  <Setter Property='ItemContainerStyle'>
    <Setter.Value>
      <Style TargetType='{x:Type ComboBoxItem}'>
        <Setter Property='Padding' Value='12,8'/>
        <Setter Property='Margin' Value='4,2'/>
        <Setter Property='Foreground' Value='#192330'/>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='{x:Type ComboBoxItem}'>
              <Border x:Name='ItemBorder' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
                <ContentPresenter/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='#E8F2FF'/></Trigger>
                <Trigger Property='IsSelected' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='#DCEBFF'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
                <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </Setter.Value>
  </Setter>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type ComboBox}'>
        <Grid>
          <Border x:Name='OuterBorder' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
            <Grid Margin='{TemplateBinding Padding}'>
              <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='18'/></Grid.ColumnDefinitions>
              <ContentPresenter VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
              <Path Grid.Column='1' Width='8' Height='5' Stretch='Fill' Stroke='#64748B' StrokeThickness='1.6' Data='M 0 0 L 4 4 L 8 0' HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Grid>
          </Border>
          <ToggleButton Focusable='False' Background='Transparent' BorderThickness='0' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
            <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'><Border Background='Transparent'/></ControlTemplate></ToggleButton.Template>
          </ToggleButton>
          <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' PopupAnimation='Fade' Focusable='False'>
            <Border Background='White' BorderBrush='#D7E1ED' BorderThickness='1' CornerRadius='9' Padding='4' Margin='0,5,0,0' MinWidth='180'>
              <ScrollViewer MaxHeight='320' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='OuterBorder' Property='BorderBrush' Value='#77AFFF'/></Trigger>
          <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='OuterBorder' Property='BorderBrush' Value='#1B76FF'/><Setter TargetName='OuterBorder' Property='BorderThickness' Value='2'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter TargetName='OuterBorder' Property='Background' Value='#F1F4F8'/><Setter Property='Opacity' Value='0.65'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
                }
                return _modernComboBoxStyle;
            }
        }

        public static Style ModernContextMenuStyle
        {
            get
            {
                if (_modernContextMenuStyle == null)
                {
                    _modernContextMenuStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ContextMenu}'>
  <Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#D7E1ED'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='5'/><Setter Property='HasDropShadow' Value='True'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ContextMenu}'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'><StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/></Border></ControlTemplate></Setter.Value></Setter>
</Style>");
                }
                return _modernContextMenuStyle;
            }
        }

        public static Style ModernMenuItemStyle
        {
            get
            {
                if (_modernMenuItemStyle == null)
                {
                    _modernMenuItemStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type MenuItem}'>
  <Setter Property='FontFamily' Value='Microsoft YaHei UI'/>
  <Setter Property='FontSize' Value='12'/>
  <Setter Property='Foreground' Value='#192330'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Margin' Value='2'/>
  <Setter Property='MinWidth' Value='154'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type MenuItem}'>
        <Grid>
          <Border x:Name='ItemBorder' Background='{TemplateBinding Background}' CornerRadius='7'>
            <Grid MinHeight='36'>
              <TextBlock x:Name='CheckMark' Text='✓' FontSize='13' FontWeight='SemiBold' Foreground='#1B76FF' Width='20' Margin='8,0,0,0' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/>
              <ContentPresenter ContentSource='Header' RecognizesAccessKey='True' Margin='32,7,32,7' VerticalAlignment='Center'/>
              <Path x:Name='Arrow' Data='M 0 0 L 4 4 L 0 8' Stroke='#64748B' StrokeThickness='1.5' Width='5' Height='9' Stretch='Fill' Margin='0,0,12,0' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>
            </Grid>
          </Border>
          <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsSubmenuOpen}' Placement='Right' PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
            <Border Background='White' BorderBrush='#D7E1ED' BorderThickness='1' CornerRadius='10' Padding='5' Margin='5,0,0,0' MinWidth='164'>
              <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='ItemBorder' Property='Background' Value='#E6F1FF'/></Trigger>
          <Trigger Property='IsChecked' Value='True'><Setter TargetName='CheckMark' Property='Visibility' Value='Visible'/><Setter TargetName='ItemBorder' Property='Background' Value='#F0F6FF'/></Trigger>
          <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.48'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
                }
                return _modernMenuItemStyle;
            }
        }

        private static Style ParseStyle(string xaml)
        {
            try { return XamlReader.Parse(xaml) as Style; }
            catch { return null; }
        }

        public static SolidColorBrush Brush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static Color ParseColor(string value, Color fallback)
        {
            try
            {
                object converted = ColorConverter.ConvertFromString(value);
                if (converted is Color) return (Color)converted;
            }
            catch
            {
            }
            return fallback;
        }

        public static Color ContrastText(Color background)
        {
            double luminance = (background.R * 0.299) + (background.G * 0.587) + (background.B * 0.114);
            return luminance >= 164 ? TextColor : Colors.White;
        }

        public static Color Blend(Color first, Color second, double secondWeight)
        {
            double weight = System.Math.Max(0, System.Math.Min(1, secondWeight));
            return Color.FromRgb(
                (byte)((first.R * (1 - weight)) + (second.R * weight)),
                (byte)((first.G * (1 - weight)) + (second.G * weight)),
                (byte)((first.B * (1 - weight)) + (second.B * weight)));
        }

        public static TextBlock Text(string value, double size, FontWeight weight, Color color)
        {
            return new TextBlock
            {
                Text = value,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = size,
                FontWeight = weight,
                Foreground = Brush(color),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Button Button(string text, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                Height = 36,
                MinWidth = 88,
                Padding = new Thickness(16, 0, 16, 0),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(1),
                Background = Brush(primary ? AccentColor : CardColor),
                Foreground = Brush(primary ? Colors.White : TextColor),
                BorderBrush = Brush(primary ? AccentColor : BorderColor)
                ,Style = ModernButtonStyle
            };
            return button;
        }

        public static Border Card()
        {
            return new Border
            {
                Background = Brush(CardColor),
                BorderBrush = Brush(BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        public static string KindLabel(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Mouse: return "鼠标";
                case DeviceKind.Keyboard: return "键盘";
                case DeviceKind.Headphones: return "耳机";
                case DeviceKind.Gamepad: return "手柄";
                case DeviceKind.Other: return "其他";
                case DeviceKind.Pen: return "触控笔";
                case DeviceKind.Speaker: return "音箱";
                case DeviceKind.Phone: return "手机";
                default: return "蓝牙设备";
            }
        }

        public static string KindGlyph(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.Mouse: return "\uE962";
                case DeviceKind.Keyboard: return "\uE765";
                case DeviceKind.Headphones: return "\uE7F6";
                case DeviceKind.Gamepad: return "\uE7FC";
                case DeviceKind.Other: return "\uE702";
                case DeviceKind.Pen: return "\uEDFB";
                case DeviceKind.Speaker: return "\uE7F5";
                case DeviceKind.Phone: return "\uE8EA";
                default: return "\uE702";
            }
        }
    }
}
