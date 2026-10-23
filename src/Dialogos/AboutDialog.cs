using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SelectorDeFotos
{
    // Ventana «Acerca de»: versión, carpetas en uso y dónde se guardan los errores
    public class AboutDialog : Window
    {
        public AboutDialog(MainWindow owner)
        {
            Owner = owner;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Acerca de Selector de Fotos";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var sp = new StackPanel { Margin = new Thickness(26, 22, 26, 18), MinWidth = 420 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            var icon = MainWindow.AppIcon();
            if (icon != null) head.Children.Add(new Image { Source = icon, Width = 48, Height = 48, Margin = new Thickness(0, 0, 14, 0) });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = "Selector de Fotos", FontSize = 20, FontWeight = FontWeights.SemiBold });
            names.Children.Add(new TextBlock { Text = "Versión " + MainWindow.AppVersion + "  ·  " + MainWindow.AppVersionDate, Foreground = Dim, FontSize = 13, Margin = new Thickness(0, 2, 0, 0) });
            head.Children.Add(names);
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock
            {
                Text = "Visor portable para elegir fotos: marca favoritas y no deseadas, compara, revisa ráfagas y copia la selección.\nLas fotos originales nunca se modifican.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 460, FontSize = 13, Margin = new Thickness(0, 0, 0, 14)
            });
            var credits = new TextBlock { FontSize = 13, Margin = new Thickness(0, 0, 0, 14) };
            credits.Inlines.Add(new Run(MainWindow.AppCopyright + "  ·  "));
            credits.Inlines.Add(Link("Licencia " + MainWindow.AppLicense, LicenseTarget()));
            credits.Inlines.Add(new Run("  ·  "));
            credits.Inlines.Add(Link("Proyecto en GitHub", MainWindow.AppRepoUrl));
            sp.Children.Add(credits);
            AddRow(sp, "Carpeta de fotos", owner.AboutPhotosFolder);
            AddRow(sp, "Carpeta de datos", owner.AboutDataFolder);
            AddRow(sp, "Registro de errores", owner.AboutErrorLog);
            AddRow(sp, "Manual", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LEEME.md"));

            var close = new Button { Content = "Cerrar", IsDefault = true, IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (s, e) => Close();
            sp.Children.Add(close);
            Content = sp;
        }

        static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        static readonly Brush LinkBrush = new SolidColorBrush(Color.FromRgb(0x9F, 0xCF, 0xFF));

        // La licencia junto al programa (viene en el zip); si no está, la del repositorio
        static string LicenseTarget()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE.txt");
            return File.Exists(local) ? local : MainWindow.AppRepoUrl + "/blob/main/LICENSE";
        }

        // Enlace que se abre con un clic (archivo o página web)
        static Hyperlink Link(string text, string target)
        {
            var link = new Hyperlink(new Run(text)) { Foreground = LinkBrush, ToolTip = target };
            link.Click += (s, e) => { try { Process.Start(target); } catch { } };
            return link;
        }

        // Fila «nombre: ruta»; si existe, la ruta se abre con un clic en el Explorador
        static void AddRow(StackPanel sp, string label, string path)
        {
            var row = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Foreground = Dim, FontSize = 12 });
            bool exists = path != null && (File.Exists(path) || Directory.Exists(path));
            var value = new TextBlock
            {
                Text = path ?? "(sin elegir)", FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 460,
                Foreground = exists ? new SolidColorBrush(Color.FromRgb(0x9F, 0xCF, 0xFF)) : Brushes.White,
                Cursor = exists ? System.Windows.Input.Cursors.Hand : null,
                ToolTip = path == null ? null : path + (exists ? "\nClic para mostrarlo en el Explorador" : "\n(todavía no existe)")
            };
            if (exists)
                value.MouseLeftButtonUp += (s, e) =>
                {
                    try { Process.Start("explorer.exe", File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + path + "\""); } catch { }
                };
            row.Children.Add(value);
            sp.Children.Add(row);
        }
    }
}
