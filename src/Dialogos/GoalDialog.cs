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
    // Pide el objetivo de favoritas (cuántas fotos quieres elegir; 0 = sin objetivo).
    public class GoalDialog : Window
    {
        readonly TextBox box;
        public int Value;

        public GoalDialog(Window owner, int current)
        {
            Owner = owner;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Objetivo de favoritas";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var sp = new StackPanel { Margin = new Thickness(18), MinWidth = 320 };
            sp.Children.Add(new TextBlock
            {
                Text = "¿Cuántas fotos quieres elegir?\n(por ejemplo, las que caben en el álbum; 0 = sin objetivo)",
                Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap
            });
            box = new TextBox { Text = current > 0 ? current.ToString() : "", FontSize = 15, Padding = new Thickness(4) };
            sp.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Aceptar", IsDefault = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) =>
            {
                int v;
                string t = box.Text.Trim();
                if (t.Length == 0) v = 0;
                else if (!int.TryParse(t, out v) || v < 0)
                {
                    MessageBox.Show(this, "Escribe un número entero (0 para quitar el objetivo).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Value = v;
                DialogResult = true;
            };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            Content = sp;
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }
}
