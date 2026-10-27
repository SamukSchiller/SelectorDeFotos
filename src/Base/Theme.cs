using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace SelectorDeFotos
{
    // Tema oscuro para los controles estándar de Windows (desplegable, menús, casillas, campos de texto,
    // tooltips y ventanas), para que todo combine con la interfaz.
    public static class Theme
    {
        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- Recuadros de ayuda (tooltips) -->
  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='#2B2B2B' BorderBrush='#4A4A4A' BorderThickness='1' CornerRadius='4' Padding='8,4'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Menús (⚙ y clic derecho) -->
  <Style TargetType='ContextMenu'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='#2B2B2B' BorderBrush='#444444' BorderThickness='1' CornerRadius='6' Padding='4'>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='8,4' Background='#444444'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='6,5,8,5'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='22'/>
                <ColumnDefinition Width='24'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='16'/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name='Check' Text='✓' Visibility='Collapsed' Foreground='#4FA3FF' FontWeight='Bold' VerticalAlignment='Center' HorizontalAlignment='Center'/>
              <!-- Icono (opcional): siempre en la misma columna, así los textos quedan alineados -->
              <ContentPresenter Grid.Column='1' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Left'/>
              <ContentPresenter Grid.Column='2' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='3' Text='{TemplateBinding InputGestureText}' Foreground='#9A9A9A' Margin='28,0,0,0' VerticalAlignment='Center'/>
              <TextBlock x:Name='Arrow' Grid.Column='4' Text='›' FontSize='15' Visibility='Collapsed' HorizontalAlignment='Right' VerticalAlignment='Center'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='6' VerticalOffset='-5' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border Background='#2B2B2B' BorderBrush='#444444' BorderThickness='1' CornerRadius='6' Padding='4'>
                  <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='Role' Value='SubmenuHeader'>
              <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='#3D4A5C'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Desplegable «Ordenar» -->
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='3' Padding='8,5'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='#33475E'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='#3D4A5C'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid TextElement.Foreground='#EDEDED'>
            <ToggleButton Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border x:Name='B' Background='#383838' BorderBrush='#484848' BorderThickness='1' CornerRadius='4'>
                    <Path Data='M0,0 L4,4 L8,0' Stroke='#BBBBBB' StrokeThickness='1.5' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,9,0'/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='B' Property='Background' Value='#4A4A4A'/>
                      <Setter TargetName='B' Property='BorderBrush' Value='#6A6A6A'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter IsHitTestVisible='False' Margin='9,4,26,4' VerticalAlignment='Center' HorizontalAlignment='Left'
                              Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <Popup Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
                   IsOpen='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}'>
              <Border Background='#2B2B2B' BorderBrush='#444444' BorderThickness='1' CornerRadius='4' Padding='3' Margin='0,2,0,0'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Contained'/>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Casillas («Mostrar» y diálogos) -->
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='Box' Width='16' Height='16' CornerRadius='3' Background='#2F2F2F' BorderBrush='#6A6A6A' BorderThickness='1' VerticalAlignment='Center'>
              <Path x:Name='Mark' Data='M3,8 L6.5,11.5 L13,4.5' Stroke='White' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Margin='6,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Mark' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Box' Property='Background' Value='#4FA3FF'/>
              <Setter TargetName='Box' Property='BorderBrush' Value='#4FA3FF'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Box' Property='BorderBrush' Value='#9AC8FF'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Campos de texto de los diálogos -->
  <!-- Barras de desplazamiento: finas y oscuras, sin flechas (ventana de atajos, listas, cuadros de texto) -->
  <Style x:Key='DarkScrollThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='T' Background='#5A5A5A' CornerRadius='3'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='T' Property='Background' Value='#787878'/></Trigger>
            <Trigger Property='IsDragging' Value='True'><Setter TargetName='T' Property='Background' Value='#8C8C8C'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='DarkScrollPage' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <ControlTemplate x:Key='DarkVScroll' TargetType='ScrollBar'>
    <Border Background='#1F1F1F' CornerRadius='5'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource DarkScrollPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb Style='{StaticResource DarkScrollThumb}' Margin='2,2'/></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource DarkScrollPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
      </Track>
    </Border>
  </ControlTemplate>
  <ControlTemplate x:Key='DarkHScroll' TargetType='ScrollBar'>
    <Border Background='#1F1F1F' CornerRadius='5'>
      <Track x:Name='PART_Track' IsDirectionReversed='False'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource DarkScrollPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb Style='{StaticResource DarkScrollThumb}' Margin='2,2'/></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource DarkScrollPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
      </Track>
    </Border>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='Template' Value='{StaticResource DarkVScroll}'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Margin' Value='4,0,0,0'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Template' Value='{StaticResource DarkHScroll}'/>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='10'/>
        <Setter Property='MinHeight' Value='10'/>
        <Setter Property='Margin' Value='0,4,0,0'/>
      </Trigger>
    </Style.Triggers>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='#1F1F1F'/>
    <Setter Property='Foreground' Value='#EDEDED'/>
    <Setter Property='BorderBrush' Value='#555555'/>
    <Setter Property='CaretBrush' Value='White'/>
    <Setter Property='SelectionBrush' Value='#4FA3FF'/>
  </Style>
</ResourceDictionary>";

        public static void Apply(Application app)
        {
            try
            {
                var dict = (ResourceDictionary)XamlReader.Parse(Xaml);
                app.Resources.MergedDictionaries.Add(dict);
                // Botones de los diálogos con el mismo estilo oscuro que la barra superior
                app.Resources[typeof(System.Windows.Controls.Button)] = MainWindow.DarkButtonStylePublic();
            }
            catch { /* si algo falla, la app sigue con el aspecto estándar */ }
        }

        // Ventanas secundarias: fondo oscuro y texto claro
        public static void DarkDialog(Window w)
        {
            w.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));
            w.Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));
            DarkTitleBar(w);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Barra de título oscura (Windows 10 2004+ / Windows 11)
        public static void DarkTitleBar(Window w)
        {
            w.SourceInitialized += (s, e) =>
            {
                try
                {
                    var h = new WindowInteropHelper(w).Handle;
                    int on = 1;
                    if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
                }
                catch { }
            };
        }
    }
}
