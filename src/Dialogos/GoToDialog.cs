using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Pide el nombre o la posición de la foto a la que ir (Ctrl + G).
    public class GoToDialog : Window
    {
        readonly TextBox box;
        public string Value;

        public GoToDialog(Window owner, int count)
        {
            Owner = owner;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Ir a una foto";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            var sp = new StackPanel { Margin = new Thickness(18), MinWidth = 340 };
            sp.Children.Add(new TextBlock
            {
                Text = "Nombre de la foto (por ejemplo IMG_8410 o solo 8410)\no su posición con # (por ejemplo #1156, de 1 a " + count + ")",
                Margin = new Thickness(0, 0, 0, 10)
            });
            box = new TextBox { FontSize = 15, Padding = new Thickness(4) };
            sp.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Ir", IsDefault = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) => { Value = box.Text; DialogResult = true; };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            Content = sp;
            Loaded += (s, e) => box.Focus();
        }
    }
}
