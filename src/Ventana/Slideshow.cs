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
    // Presentación de favoritas (P): abrirla y, al salir, volver a la foto mostrada.
    public partial class MainWindow
    {
        void StartSlideshow()
        {
            var favs = all.Where(x => x.Mark == Mark.Fav).ToList();
            if (favs.Count == 0)
            {
                Toast("No hay favoritas para la presentación");
                return;
            }
            int start = Current != null ? Math.Max(0, favs.IndexOf(Current)) : 0;
            // Un texto de ayuda abierto (p. ej. el nombre de una miniatura bajo el ratón) quedaba encima de la
            // presentación: se cierra antes de empezar
            CloseOpenToolTips();
            var w = new SlideshowWindow(favs, start, Math.Max(0.5, Math.Min(30, settings.SegundosPresentacion)), fullDim) { Owner = this };
            w.ShowDialog();
            settings.SegundosPresentacion = w.Seconds;
            SaveSettings();
            FocusNearest(w.CurrentItem); // al salir: la foto que se estaba viendo (o la más cercana que se vea)
        }

        // Cierra los textos de ayuda (ToolTip) abiertos: cada uno vive en su propia ventanita emergente
        static void CloseOpenToolTips()
        {
            foreach (PresentationSource src in PresentationSource.CurrentSources.OfType<PresentationSource>().ToList())
            {
                var root = src.RootVisual;
                if (root == null) continue;
                var stack = new Stack<DependencyObject>();
                stack.Push(root);
                int guard = 0;
                while (stack.Count > 0 && guard++ < 50) // la ventanita del texto tiene muy pocos elementos
                {
                    var d = stack.Pop();
                    var tt = d as ToolTip;
                    if (tt != null) { tt.IsOpen = false; break; }
                    if (!(d is Visual)) continue;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
                }
            }
        }

        // Ir a esa foto si el filtro «Mostrar» la deja ver; si no, a la más cercana en el orden actual que sí se vea
        void FocusNearest(PhotoItem it)
        {
            if (it == null || view.Count == 0) return;
            int i = view.IndexOf(it);
            if (i < 0)
            {
                int p = all.IndexOf(it);
                if (p < 0) return;
                for (int d = 1; d < all.Count && i < 0; d++)
                {
                    if (p + d < all.Count && Pass(all[p + d])) i = view.IndexOf(all[p + d]);
                    else if (p - d >= 0 && Pass(all[p - d])) i = view.IndexOf(all[p - d]);
                }
            }
            if (i >= 0 && i != idx) GoTo(i);
        }
    }
}
