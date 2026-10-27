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
    // Pegar o abrir una lista de nombres de fotos para marcarlas.
    public class ImportDialog : Window
    {
        readonly TextBox box;
        public string ListText;

        public ImportDialog(Window owner)
        {
            Owner = owner;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Importar lista de fotos";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            var sp = new StackPanel { Margin = new Thickness(18), Width = 460 };
            sp.Children.Add(new TextBlock
            {
                Text = "Pega la lista de fotos (separadas por comas, espacios o una por línea), o abre un archivo .txt.\n" +
                       "Valen nombres completos (IMG_8410.jpg), sin extensión (IMG_8410) o solo el número (8410).\n" +
                       "Las fotos encontradas se marcarán como favoritas.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
            box = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 200, FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4)
            };
            // Si el portapapeles ya tiene una lista (por ejemplo, copiada de WhatsApp), se propone directamente.
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText();
                    if (clip.Length < 200000 && Regex.IsMatch(clip, @"\d{3,}")) box.Text = clip;
                }
            }
            catch { }
            sp.Children.Add(box);
            var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
            var open = new Button { Content = "Abrir archivo .txt…", Padding = new Thickness(10, 3, 10, 3) };
            open.Click += (s, e) =>
            {
                var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "Texto (*.txt;*.csv)|*.txt;*.csv|Todos los archivos|*.*", Title = "Abrir lista de fotos" };
                if (ofd.ShowDialog(this) == true)
                {
                    try { box.Text = File.ReadAllText(ofd.FileName); }
                    catch (Exception ex) { MessageBox.Show(this, "No se pudo leer el archivo:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
                }
            };
            DockPanel.SetDock(open, Dock.Left);
            buttons.Children.Add(open);
            var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3) };
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            var ok = new Button { Content = "Importar", IsDefault = false, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (s, e) => { ListText = box.Text; DialogResult = true; };
            DockPanel.SetDock(ok, Dock.Right);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);
            Content = sp;
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }
    }
}
