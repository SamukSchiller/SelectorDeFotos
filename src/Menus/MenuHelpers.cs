using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Ayudas para construir los menús: iconos dibujados y elementos con atajo y explicación.
    public partial class MainWindow
    {
        static readonly Brush MenuIconBrush = B("#CFCFCF");

        // Iconos dibujados (para lo que no tiene un símbolo claro en la fuente de Windows)

        // Tira con la foto del medio resaltada: «miniatura actual siempre centrada»
        static FrameworkElement CenteredIcon()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 3; i++)
                sp.Children.Add(new Border
                {
                    Width = i == 1 ? 5 : 3.5, Height = i == 1 ? 11 : 8, CornerRadius = new CornerRadius(1),
                    Margin = new Thickness(i == 0 ? 0 : 1.5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = i == 1 ? MenuIconBrush : Brushes.Transparent,
                    BorderBrush = MenuIconBrush, BorderThickness = new Thickness(i == 1 ? 0 : 1)
                });
            return sp;
        }

        // Carriles (columnas o filas), como el botón de la barra superior; count = cuántos se dibujan
        static FrameworkElement LanesMenuIcon(bool columns, int count = 3)
        {
            var sp = new StackPanel { Orientation = columns ? Orientation.Horizontal : Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < count; i++)
                sp.Children.Add(new Border
                {
                    Width = columns ? 3.5 : 13, Height = columns ? 12 : 3, CornerRadius = new CornerRadius(1), Background = MenuIconBrush,
                    Margin = columns ? new Thickness(i == 0 ? 0 : 1.5, 0, 0, 0) : new Thickness(0, i == 0 ? 0 : 1.5, 0, 0)
                });
            return sp;
        }

        // Ventana con la tira arriba (la fuente de Windows no tiene «acoplar arriba»)
        static FrameworkElement TopDockIcon()
        {
            var g = new Grid { Width = 15, Height = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            g.Children.Add(new Border { BorderBrush = MenuIconBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1.5) });
            g.Children.Add(new Border { Height = 4.5, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2.5, 2.5, 2.5, 0), Background = MenuIconBrush });
            return g;
        }

        // Foto con borde de foco: «borde de la foto actual»
        static FrameworkElement FocusFrameIcon()
        {
            return new Border
            {
                Width = 15, Height = 12, BorderBrush = MenuIconBrush, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(2.5),
                Padding = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Border { Background = B("#7A7A7A"), CornerRadius = new CornerRadius(1) }
            };
        }

        // Ventana con una barra fina arriba: «barra superior compacta»
        static FrameworkElement CompactBarIcon()
        {
            var g = new Grid { Width = 15, Height = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            g.Children.Add(new Border { BorderBrush = MenuIconBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) });
            g.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Background = MenuIconBrush, CornerRadius = new CornerRadius(2, 2, 0, 0) });
            return g;
        }

        // Icono de la fuente de símbolos de Windows (la misma del botón ⚙)
        static TextBlock Glyph(string g)
        {
            return new TextBlock { Text = g, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = MenuIconBrush, VerticalAlignment = VerticalAlignment.Center };
        }

        static MenuItem MenuCheck(string header, bool isChecked, Action toggle, string keys = "", object icon = null)
        {
            var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, InputGestureText = keys ?? "", Icon = icon };
            mi.Click += (s, e) => toggle();
            return mi;
        }

        static MenuItem MenuAction(string header, string keys, object icon, Action act)
        {
            var mi = new MenuItem { Header = header, InputGestureText = keys ?? "", Icon = icon };
            mi.Click += (s, e) => act();
            return mi;
        }

        static MenuItem MenuGroup(string header, object icon)
        {
            return new MenuItem { Header = header, Icon = icon };
        }

        static MenuItem Tip(MenuItem mi, string tip)
        {
            mi.ToolTip = tip;
            return mi;
        }
    }
}
