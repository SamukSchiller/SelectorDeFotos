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
    // Elegir qué fotos copiar (favoritas, sin marcar, no deseadas) a la carpeta de datos.
    public class CopyDialog : Window
    {
        public CheckBox Fav, None, Rej;

        public CopyDialog(Window owner, int nf, int nn, int nr, string dest)
        {
            Owner = owner;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Copiar fotos";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var sp = new StackPanel { Margin = new Thickness(18), MinWidth = 380 };
            sp.Children.Add(new TextBlock { Text = "¿Qué fotos quieres copiar?", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
            Fav = Check("Favoritas (" + nf + ")   →   carpeta «seleccionadas»", true);
            None = Check("Sin marca (" + nn + ")   →   carpeta «sin_marca»", false);
            Rej = Check("No deseadas (" + nr + ")   →   carpeta «no_deseadas»", false);
            sp.Children.Add(Fav);
            sp.Children.Add(None);
            sp.Children.Add(Rej);
            sp.Children.Add(new TextBlock
            {
                Text = "Destino: " + dest + "\nSe copian con el mismo nombre, fechas y metadatos (EXIF) que el original.\nLas fotos originales no se modifican. Si ya existe una copia igual, se omite.",
                Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 460
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "Copiar", IsDefault = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) => DialogResult = true;
            var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            Content = sp;
        }

        static CheckBox Check(string text, bool value)
        {
            return new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        }
    }
}
