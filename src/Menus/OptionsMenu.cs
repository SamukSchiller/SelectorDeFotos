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
    // Menú de opciones ⚙ y sus submenús, objetivo de favoritas y ayuda.
    public partial class MainWindow
    {
        // Menú ⚙: agrupado en submenús por tema; las acciones más usadas quedan a la vista.
        // Los atajos van a la derecha y las explicaciones en el texto de ayuda (al pasar el ratón).
        void ShowOptionsMenu(Button anchor)
        {
            var m = BuildOptionsMenu();
            m.PlacementTarget = anchor;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        }

        ContextMenu BuildOptionsMenu()
        {
            var m = new ContextMenu();

            // Visor
            var viewer = MenuGroup("Visor", Glyph(""));
            viewer.Items.Add(MenuCheck("Comparar fotos", settings.Comparar, ToggleCompare, "C", MakeCompareIcon(MenuIconBrush)));
            viewer.Items.Add(CompareModesMenu());
            viewer.Items.Add(MenuCheck("Panel de información", settings.MostrarInfo, ToggleInfo, "I", Glyph("")));
            viewer.Items.Add(MenuCheck("Pantalla completa", fullscreen, ToggleFullscreen, "F11", Glyph("")));
            m.Items.Add(viewer);

            // Miniaturas
            m.Items.Add(ThumbsMenu());

            m.Items.Add(MarksMenu());
            m.Items.Add(BurstsMenu());

            m.Items.Add(new Separator());
            // Acciones más usadas, a la vista
            m.Items.Add(MenuAction("Presentación de favoritas", "P", Glyph(""), StartSlideshow));
            m.Items.Add(MenuAction("Ir a una foto…", "Ctrl + G", Glyph(""), ShowGoTo));
            m.Items.Add(MenuAction("Importar lista de favoritas…", "", Glyph(""), ImportList));
            m.Items.Add(MenuAction(settings.ObjetivoFavoritas > 0 ? "Objetivo de favoritas: " + settings.ObjetivoFavoritas + "…" : "Objetivo de favoritas…",
                                   "", Glyph(""), AskGoal));

            m.Items.Add(new Separator());
            // Interfaz
            var ui = MenuGroup("Interfaz", Glyph("")); // tamaño de texto (la lupa ya es «Ir a una foto»)
            var size = MenuGroup("Tamaño: " + settings.EscalaInterfaz + " %", null);
            size.Items.Add(MenuAction("Aumentar", "Ctrl + +", null, () => StepUiScale(1)));
            size.Items.Add(MenuAction("Reducir", "Ctrl + −", null, () => StepUiScale(-1)));
            size.Items.Add(MenuAction("Restablecer (100 %)", "Ctrl + 0", null, () => SetUiScale(100)));
            ui.Items.Add(size);
            ui.Items.Add(MenuCheck("Barra superior compacta", settings.BarraCompacta, () =>
            {
                settings.BarraCompacta = !settings.BarraCompacta;
                SaveSettings();
                UpdateLabels();
            }, "", CompactBarIcon()));
            m.Items.Add(ui);
            m.Items.Add(MenuAction("Restablecer preferencias…", "", Glyph(""), () => ResetPreferences(true)));
            m.Items.Add(new Separator());
            m.Items.Add(MenuAction("Acerca de Selector de Fotos", "", Glyph(""), ShowAbout));
            return m;
        }

        // «Comparar con»: la siguiente / la anterior / las dos
        MenuItem CompareModesMenu()
        {
            var modes = MenuGroup("Comparar con", null);
            string[] modeNames = { "La siguiente", "La anterior", "La anterior y la siguiente" };
            for (int i = 0; i < 3; i++)
            {
                int mode = i;
                // El formato elegido se ve siempre (aunque comparar esté apagado); elegir uno activa comparar
                modes.Items.Add(MenuCheck(modeNames[i], settings.ModoComparar == i, () => SetCompareMode(mode)));
            }
            return modes;
        }

        // Submenú «Marcas» (en ⚙ y en el clic derecho sobre la foto)
        MenuItem MarksMenu()
        {
            var marks = MenuGroup("Marcas", Glyph("")); // etiqueta (un check se confundiría con «activado»)
            marks.Items.Add(Tip(MenuCheck("Avanzar al marcar", settings.AvanzarAlMarcar, ToggleAutoAdvance, "", Glyph("")),
                                "Al marcar con ↑ / ↓ se pasa sola a la siguiente foto (quitar una marca no avanza)"));
            marks.Items.Add(MenuCheck("Mostrar el icono en el visor", settings.MostrarIconoMarca, ToggleMarkIcon));
            marks.Items.Add(Tip(MenuCheck("Animar la marca", settings.AnimarMarca, ToggleMarkAnimation, "", Glyph("")),
                                "Al cambiar de foto y al marcar, el icono de la marca aparece grande en el centro y se desvanece"));
            marks.Items.Add(Tip(MenuCheck("Mosaico de la marca en el fondo", settings.MosaicoMarca, ToggleMarkMosaic),
                                "Alrededor de la foto, el fondo del visor se llena de checks (favorita) o cruces (no deseada) muy suaves"));
            marks.Items.Add(FocusStyleMenu());
            return marks;
        }

        // Submenú «Ráfagas» (en ⚙ y en los dos menús de clic derecho)
        MenuItem BurstsMenu()
        {
            var bursts = MenuGroup("Ráfagas", MakeBurstIcon(MenuIconBrush, 14, 11));
            bursts.Items.Add(Tip(MenuCheck("Modo ráfaga", settings.ModoRafaga, ToggleBurst, "B"),
                                 "Las fotos de una ráfaga se ven lado a lado al llegar a ellas"));
            var similar = Tip(MenuCheck("Agrupar también fotos parecidas", settings.AgruparParecidas, ToggleGroupSimilar),
                              settings.GuardarMiniaturas
                                  ? "Une a la ráfaga fotos casi iguales tomadas poco después (la misma pose repetida)"
                                  : "Necesita «Guardar miniaturas en disco» (en Miniaturas): se calcula a partir de ellas");
            similar.IsEnabled = settings.GuardarMiniaturas;
            ToolTipService.SetShowOnDisabled(similar, true);
            bursts.Items.Add(similar);
            // Cuánto tiempo puede pasar entre una foto y la siguiente para contar como ráfaga
            var gap = MenuGroup("Tiempo entre fotos", Glyph(""));
            gap.InputGestureText = BurstGapSeconds + " s";
            foreach (var sec in new[] { 1.0, 2.0, 3.0 })
            {
                double v = sec;
                gap.Items.Add(Tip(MenuCheck(v + " s" + (v == 2 ? " (predeterminado)" : ""), Math.Abs(BurstGapSeconds - v) < 0.01, () => SetBurstGap(v)),
                                  v == 1 ? "Solo las ráfagas muy rápidas" : v == 3 ? "También disparos más espaciados" : "Ráfagas y disparos seguidos"));
            }
            bursts.Items.Add(gap);
            return bursts;
        }

        // Submenú «Miniaturas» (también se usa en el clic derecho sobre las miniaturas)
        MenuItem ThumbsMenu()
        {
            var t = MenuGroup("Miniaturas", Glyph(""));
            foreach (var x in ThumbsMenuItems()) t.Items.Add(x);
            return t;
        }

        List<object> ThumbsMenuItems()
        {
            var list = new List<object>();
            if (gridMode)
            {
                list.Add(MenuCheck("Cuadrícula a pantalla completa", true, ToggleGrid, "G", GridIcon()));
                var cols = MenuGroup("Columnas de la cuadrícula", LanesMenuIcon(true));
                cols.InputGestureText = "+ −";
                bool auto = settings.ColumnasCuadricula < GridMinCols || settings.ColumnasCuadricula > GridMaxCols;
                cols.Items.Add(MenuCheck("Automático (según el ancho)", auto, () =>
                {
                    settings.ColumnasCuadricula = 0;
                    SaveSettings();
                    RefreshGridLayout();
                    Toast(GridCols + " columnas (automático)");
                }));
                for (int c = GridMinCols; c <= GridMaxCols; c++)
                {
                    int n = c;
                    cols.Items.Add(MenuCheck(n + " columnas", !auto && GridCols == n, () => StepGridCols(n - GridCols)));
                }
                list.Add(cols);
            }
            else
            {
                // «A la derecha» y «Abajo»: cada uno con su cantidad de columnas / filas (se puede cambiar sin cambiar
                // la posición activa). La ✓ marca la posición en uso y a la derecha se ve la cantidad elegida.
                list.Add(StripPositionMenu(true));
                list.Add(StripPositionMenu(false));
                list.Add(MenuCheck("Cuadrícula a pantalla completa", false, ToggleGrid, "G", GridIcon()));
            }
            list.Add(new Separator());
            list.Add(Tip(MenuCheck("Miniatura actual siempre centrada", settings.CentrarMiniaturaActual, () => SetCenteredThumb(!settings.CentrarMiniaturaActual), "", CenteredIcon()),
                         "Desactivada: la tira solo se mueve cuando la foto actual sale de la vista (como en Lightroom)"));
            list.Add(Tip(MenuCheck("Cuadrícula justificada", settings.CuadriculaJustificada, () =>
            {
                settings.CuadriculaJustificada = !settings.CuadriculaJustificada;
                SaveSettings();
                OnStripResized();
                LayoutStrip();
            }), "Cada foto ocupa su ancho natural, sin huecos (en el lateral y abajo con 1 fila)"));
            list.Add(Tip(MenuCheck("Separar momentos", settings.SepararMomentos, () =>
            {
                settings.SepararMomentos = !settings.SepararMomentos;
                SaveSettings();
                UpdateStrip();
            }, "", Glyph("")), "Etiqueta naranja con la hora cuando pasan más de " + MomentSeconds + " s entre una foto y la siguiente"));
            list.Add(Tip(MenuCheck("Guardar miniaturas en disco", settings.GuardarMiniaturas, ToggleThumbCache, "", Glyph("")),
                         "Carga más rápida de las carpetas grandes; permite también «más nítida» y agrupar fotos parecidas en las ráfagas"));
            return list;
        }

        MenuItem StripPositionMenu(bool side)
        {
            int count = side ? Math.Max(1, Math.Min(3, settings.ColumnasMiniaturas)) : Math.Max(1, Math.Min(3, settings.FilasMiniaturas));
            string unit1 = side ? " columna" : " fila", unitN = side ? " columnas" : " filas";
            bool alt = side ? settings.MiniaturasIzquierda : settings.MiniaturasArriba;
            // Lateral (derecha o izquierda) / Horizontal (abajo o arriba); a la derecha se ve el lado y la cantidad elegidos
            var menu = MenuGroup(side ? "Lateral" : "Horizontal", Glyph(side ? (alt ? "" : "") : ""));
            menu.IsChecked = sideStrip == side;                       // solo para mostrar la ✓ (es un submenú)
            menu.InputGestureText = (side ? (alt ? "izquierda" : "derecha") : (alt ? "arriba" : "abajo")) + " · " + count + (count == 1 ? unit1 : unitN);
            menu.ToolTip = sideStrip == side ? "En uso (M cambia entre lateral y horizontal)" : "M cambia entre lateral y horizontal";
            // Elegir un lado también pasa a usar esta posición
            menu.Items.Add(MenuCheck(side ? "A la derecha" : "Abajo", !alt, () => SetStripPlace(side, false), "", Glyph(side ? "" : "")));
            menu.Items.Add(MenuCheck(side ? "A la izquierda" : "Arriba", alt, () => SetStripPlace(side, true), "", side ? Glyph("") : (object)TopDockIcon()));
            menu.Items.Add(new Separator());
            for (int c = 1; c <= 3; c++)
            {
                int n = c;
                menu.Items.Add(MenuCheck(n + (n == 1 ? unit1 : unitN), count == n, () => SetStripLanes(side, n), "", LanesMenuIcon(side, n)));
            }
            return menu;
        }

        // Cambia las columnas (lateral) o filas (abajo); si esa posición no está en uso, solo se guarda para cuando se use
        void SetStripLanes(bool side, int n)
        {
            ExitFullView(); // cambia la distribución de la ventana: primero sale de la vista completa
            if (sideStrip == side && !gridMode) { if (Lanes != n) CycleColumns(n - Lanes); return; }
            if (side) settings.ColumnasMiniaturas = n; else settings.FilasMiniaturas = n;
            SaveSettings();
            Toast((side ? "A la derecha: " : "Abajo: ") + n + (side ? (n == 1 ? " columna" : " columnas") : (n == 1 ? " fila" : " filas")));
        }

        void ToggleMarkIcon()
        {
            settings.MostrarIconoMarca = !settings.MostrarIconoMarca;
            SaveSettings();
            UpdateMarkBadge();
        }

        void ToggleMarkAnimation()
        {
            settings.AnimarMarca = !settings.AnimarMarca;
            SaveSettings();
            var cur = Current;
            if (settings.AnimarMarca && cur != null) AnimateMark(cur.Mark); // vista previa
        }


        void AskGoal()
        {
            var dlg = new GoalDialog(this, settings.ObjetivoFavoritas);
            if (dlg.ShowDialog() != true) return;
            SetGoal(dlg.Value);
        }

        void SetGoal(int value)
        {
            settings.ObjetivoFavoritas = Math.Max(0, value);
            SaveSettings();
            UpdateCounts();
        }

        void ShowHelp()
        {
            new HelpWindow(this).ShowDialog();
        }
    }
}
