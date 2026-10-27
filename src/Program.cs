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
    // Punto de entrada: tema oscuro, modo de prueba (--autotest) y registro de errores no previstos.
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application();
            Theme.Apply(app);
            if (args.Length == 3 && args[0] == "--autotest")
            {
                MainWindow.Autotest = true;
                var w = new MainWindow
                {
                    WindowState = WindowState.Normal, WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -5000, Top = 0, Width = 1600, Height = 1000, ShowActivated = false, ShowInTaskbar = false
                };
                w.RunAutotest(args[1], args[2]);
                app.Run(w);
                return;
            }
            app.DispatcherUnhandledException += (s, e) =>
            {
                // Se guarda en el registro de errores (data\errores.log) para poder diagnosticarlo después
                MainWindow.LogError(e.Exception, "interfaz");
                MessageBox.Show("Error inesperado:\n" + e.Exception.Message + "\n\nLas marcas están a salvo. El detalle quedó guardado en el registro de errores (⚙ → Acerca de).",
                                "Selector de Fotos", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            app.Run(new MainWindow());
        }
    }
}
