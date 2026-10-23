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
    // Menús de clic derecho y abrir la foto fuera de la app (E / O).
    public partial class MainWindow
    {
        ContextMenu BuildPhotoMenu()
        {
            var m = new ContextMenu();
            // Se rehace cada vez que se abre, para mostrar la marca y el estado de las opciones
            m.Opened += (s, e) =>
            {
                m.Items.Clear();
                // La foto sobre la que se hizo clic derecho (en comparar o en la ráfaga puede no ser la actual)
                var cur = contextTarget != null && view.Contains(contextTarget) ? contextTarget : Current;
                if (cur != null && cur != Current)
                {
                    // Título con el nombre, para que quede claro sobre qué foto actúa el menú
                    // (el «_» se duplica: en un menú, un «_» suelto marca la tecla de acceso y no se ve)
                    m.Items.Add(new MenuItem { Header = cur.Name.Replace("_", "__"), IsEnabled = false, FontWeight = FontWeights.SemiBold });
                    m.Items.Add(new Separator());
                }
                // Marcar la foto actual (como ↑ / ↓); la marca que tiene lleva ✓
                Func<Mark, System.Windows.Shapes.Path> markIcon = mk => new System.Windows.Shapes.Path
                {
                    Data = mk == Mark.Fav ? CheckGeometry : CrossGeometry, Stretch = Stretch.Uniform, Width = mk == Mark.Fav ? 14 : 11, Height = mk == Mark.Fav ? 14 : 11,
                    Fill = mk == Mark.Fav ? CheckBrush : RejBrush, HorizontalAlignment = HorizontalAlignment.Left
                };
                bool isCur = cur == Current;
                var fav = MenuCheck("Favorita", cur != null && cur.Mark == Mark.Fav, () => MarkPhoto(cur, Mark.Fav), isCur ? "↑" : "", markIcon(Mark.Fav));
                var rej = MenuCheck("No deseada", cur != null && cur.Mark == Mark.Reject, () => MarkPhoto(cur, Mark.Reject), isCur ? "↓" : "", markIcon(Mark.Reject));
                var none = MenuAction("Quitar marca", "", null, () => MarkPhoto(cur, Mark.None));
                fav.IsEnabled = rej.IsEnabled = cur != null;
                none.IsEnabled = cur != null && cur.Mark != Mark.None;
                m.Items.Add(fav);
                m.Items.Add(rej);
                m.Items.Add(none);
                m.Items.Add(new Separator());
                m.Items.Add(MenuAction("Abrir con el programa predeterminado", isCur ? "O" : "", Glyph(""), () => OpenExternal(cur)));
                m.Items.Add(MenuAction("Abrir con…", "", Glyph(""), () => OpenWith(cur)));
                m.Items.Add(MenuAction("Mostrar en el Explorador", isCur ? "E" : "", Glyph(""), () => ShowInExplorer(cur)));
                m.Items.Add(new Separator());
                m.Items.Add(MenuAction("Copiar nombre", "", Glyph(""), () => { if (cur != null) CopyToClipboard(cur.Name); }));
                m.Items.Add(MenuAction("Copiar ruta completa", "", null, () => { if (cur != null) CopyToClipboard(cur.Path); }));
                foreach (var x in ViewerMenuItems()) m.Items.Add(x);
            };
            m.Items.Add(new MenuItem()); // para que el menú se abra (el contenido real se pone al abrirse)
            return m;
        }

        // Clic derecho sobre las miniaturas (tira lateral, tira inferior y cuadrícula): todo lo que cambia cómo se
        // ven, con su estado (✓) y su atajo. Se rehace cada vez que se abre.
        ContextMenu BuildStripMenu()
        {
            var m = new ContextMenu();
            m.Opened += (s, e) =>
            {
                m.Items.Clear();
                foreach (var x in ThumbsMenuItems()) m.Items.Add(x); // lo mismo que ⚙ → Miniaturas
                m.Items.Add(new Separator());
                m.Items.Add(BurstsMenu());
                m.Items.Add(FocusStyleMenu());
            };
            // Un elemento vacío para que el menú se abra (el contenido real se pone al abrirse)
            m.Items.Add(new MenuItem());
            return m;
        }

        // Todo lo que cambia cómo se ve el visor, con su estado (✓) y su atajo
        List<object> ViewerMenuItems()
        {
            var list = new List<object>();
            list.Add(new Separator());
            list.Add(MenuCheck("Comparar fotos", settings.Comparar, ToggleCompare, "C", MakeCompareIcon(MenuIconBrush)));
            list.Add(CompareModesMenu());
            list.Add(MenuCheck("Cuadrícula de miniaturas", gridMode, ToggleGrid, "G", GridIcon()));
            list.Add(MenuCheck("Panel de información", settings.MostrarInfo, ToggleInfo, "I", Glyph("")));
            list.Add(MenuCheck("Vista completa (solo la foto)", fullView, ToggleFullView, "Espacio", Glyph("")));
            list.Add(MenuCheck("Pantalla completa", fullscreen && !fullView, () => { if (fullView) ExitFullView(); else ToggleFullscreen(); }, "F11"));
            list.Add(new Separator());
            list.Add(BurstsMenu());
            list.Add(MarksMenu());
            list.Add(new Separator());
            list.Add(MenuAction("Presentación de favoritas", "P", Glyph(""), StartSlideshow));
            return list;
        }

        void OpenExternal(PhotoItem target = null) // por defecto la foto actual
        {
            var it = target ?? Current;
            if (it == null) return;
            try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true }); }
            catch (Exception ex) { Toast("No se pudo abrir: " + ex.Message); }
        }

        void OpenWith(PhotoItem target = null) // por defecto la foto actual
        {
            var it = target ?? Current;
            if (it == null) return;
            try { Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + it.Path); }
            catch (Exception ex) { Toast("No se pudo abrir: " + ex.Message); }
        }

        void ShowInExplorer(PhotoItem target = null) // por defecto la foto actual
        {
            var it = target ?? Current;
            if (it == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + it.Path + "\""); }
            catch (Exception ex) { Toast("No se pudo abrir el Explorador: " + ex.Message); }
        }
    }
}
