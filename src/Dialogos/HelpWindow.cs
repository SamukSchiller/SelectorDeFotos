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
    // Atajos de teclado, agrupados por tema (Navegar, Marcar, Ver, Zoom, Herramientas).
    public class HelpWindow : Window
    {
        // Cada grupo: título y filas { tecla, qué hace }
        static readonly Tuple<string, string[,]>[] Groups =
        {
            Tuple.Create("Navegar", new[,]
            {
                { "← / →  o  A / D", "Foto anterior / siguiente" },
                { "Inicio / Fin", "Primera / última foto" },
                { "RePág / AvPág", "Saltar 10 fotos  (en la vista de ráfaga: página anterior / siguiente)" },
                { "F", "Siguiente favorita  (Shift + F: anterior)" },
                { "N", "Siguiente sin marca  (Shift + N: anterior)" },
                { "X", "Siguiente no deseada  (Shift + X: anterior)" },
                { "Ctrl + G", "Ir a una foto por nombre (IMG_8410, 8410) o posición (#1156)" },
            }),
            Tuple.Create("Marcar", new[,]
            {
                { "↑  o  W", "Favorita  (en una no deseada: quitar la marca)" },
                { "↓  o  S", "No deseada  (en una favorita: quitar la marca)" },
                { "U  o  Retroceso", "Quitar la marca" },
                { "Ctrl + Z", "Deshacer la última marca" },
                { "Shift + ↑  o  Shift + W", "En la vista de ráfaga: quedarme con esta y descartar las demás" },
            }),
            Tuple.Create("Ver", new[,]
            {
                { "C", "Comparar fotos  (doble C: cambiar el formato; Esc para salir)" },
                { "B", "Modo ráfaga: las fotos de una ráfaga se ven lado a lado  (Esc para salir)" },
                { "G", "Cuadrícula a pantalla completa  (+ − columnas; Enter, doble clic o G para volver)" },
                { "M", "Miniaturas en lateral / en horizontal" },
                { "I", "Panel de información (histograma, cámara, objetivo, ISO…)" },
                { "Alt + F / N / X", "Mostrar u ocultar favoritas / sin marca / no deseadas" },
                { "Espacio", "Vista completa: solo la foto en toda la pantalla  (Espacio o Esc para salir)" },
                { "F11", "Pantalla completa (sin la barra superior)" },
            }),
            Tuple.Create("Zoom", new[,]
            {
                { "Rueda,  + / −", "Ampliar o reducir la foto" },
                { "Z o clic de rueda (mantener)", "Lupa al 100 % donde está el ratón (suelta para volver)" },
                { "Doble clic", "Tamaño real / restaurar" },
                { "Esc  o  0", "Restaurar el zoom" },
            }),
            Tuple.Create("Herramientas", new[,]
            {
                { "P", "Presentación de favoritas  (Espacio pausa, ↑ ↓ tiempo por foto, Esc salir)" },
                { "E  /  O", "Mostrar en el Explorador  /  Abrir con el programa predeterminado" },
                { "Ctrl + / Ctrl −", "Ampliar / reducir la interfaz  (Ctrl + 0: al 100 %)" },
                { "F5", "Volver a leer la carpeta" },
                { "Alt", "Abrir el menú de opciones ⚙ (luego flechas y Enter)" },
            }),
        };

        public HelpWindow(Window owner)
        {
            Owner = owner;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Atajos de teclado";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // En pantallas bajas no cabe en una columna: los grupos se reparten en dos (con las descripciones en
            // varias líneas) y, si aun así no cabe, la lista se desplaza con la rueda. El botón Cerrar queda visible.
            var area = SystemParameters.WorkArea;
            MaxHeight = area.Height;
            MaxWidth = area.Width;
            FrameworkElement keysPanel = BuildGroups(Groups, 0);
            keysPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (keysPanel.DesiredSize.Height + 160 > area.Height) // título, botón y márgenes
            {
                var cols = new StackPanel { Orientation = Orientation.Horizontal };
                var left = BuildGroups(Groups.Take(3).ToArray(), 330);   // Navegar, Marcar, Ver
                left.Margin = new Thickness(0, 0, 32, 0);
                cols.Children.Add(left);
                cols.Children.Add(BuildGroups(Groups.Skip(3).ToArray(), 330)); // Zoom, Herramientas
                keysPanel = cols;
            }

            var root = new DockPanel { Margin = new Thickness(22, 12, 22, 16) };
            var ok = new Button
            {
                Content = "Cerrar", IsDefault = true, IsCancel = true, MinWidth = 90, Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0)
            };
            ok.Click += (s, e) => Close();
            DockPanel.SetDock(ok, Dock.Bottom);
            root.Children.Add(ok);
            root.Children.Add(new ScrollViewer
            {
                Content = keysPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
            });
            Content = root;
        }

        // Grupos en una sola tabla (así las teclas y las descripciones quedan alineadas entre grupos).
        // descWidth > 0: las descripciones se parten en líneas de ese ancho.
        static FrameworkElement BuildGroups(Tuple<string, string[,]>[] groups, double descWidth)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            foreach (var g in groups)
            {
                // Título del grupo con una línea fina debajo
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var title = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, row == 0 ? 4 : 16, 0, 4), Padding = new Thickness(0, 0, 0, 3),
                    Child = new TextBlock
                    {
                        Text = g.Item1.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF))
                    }
                };
                Grid.SetRow(title, row);
                Grid.SetColumnSpan(title, 2);
                grid.Children.Add(title);
                row++;
                var keys = g.Item2;
                for (int i = 0; i < keys.GetLength(0); i++, row++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var key = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 3, 18, 3),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = new TextBlock { Text = keys[i, 0], FontWeight = FontWeights.SemiBold, FontSize = 13 }
                    };
                    Grid.SetRow(key, row);
                    grid.Children.Add(key);
                    var desc = new TextBlock { Text = keys[i, 1], FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                    if (descWidth > 0) { desc.MaxWidth = descWidth; desc.TextWrapping = TextWrapping.Wrap; desc.Margin = new Thickness(0, 3, 0, 3); }
                    Grid.SetRow(desc, row);
                    Grid.SetColumn(desc, 1);
                    grid.Children.Add(desc);
                }
            }
            return grid;
        }
    }
}
