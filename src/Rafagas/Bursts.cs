using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Ráfagas: fotos tomadas una detrás de otra (como mucho BurstGapSeconds entre cada una, según la fecha de
    // captura EXIF). Se detectan al terminar de leer las fechas; agruparlas es instantáneo incluso con miles de fotos.
    //  · En la tira, cada foto de una ráfaga lleva una etiqueta pequeña «2/5».
    //  · B activa o desactiva el «modo ráfaga»: mientras está activo, al llegar a una foto de una ráfaga se ven
    //    todas sus fotos lado a lado (hasta 12 por página), con zoom sincronizado (rueda) y arrastre. ← → elige
    //    (y al pasar del borde sigue a la foto de al lado), ↑ ↓ marca, Shift + ↑ se queda con esta y descarta las demás.
    //  · Las imágenes se cargan al tamaño de cada recuadro (no a resolución completa) en segundo plano.
    public partial class MainWindow
    {
        // Tiempo máximo entre una foto y la siguiente para que sean ráfaga (⚙ → Ráfagas; por defecto 2 s).
        // La fecha EXIF tiene precisión de 1 s; 2 s cubre también disparos manuales seguidos.
        double BurstGapSeconds { get { return Math.Max(1, Math.Min(5, settings.SegundosRafaga)); } }

        void SetBurstGap(double seconds)
        {
            settings.SegundosRafaga = seconds;
            SaveSettings();
            ComputeBursts();
            UpdateStrip();
            int n = all.Count(x => x.Burst != null);
            Toast("Ráfagas: hasta " + seconds + " s entre fotos  ·  " + n + " de " + all.Count + " fotos en ráfagas");
        }
        // Fotos por página: hasta 12, menos si el espacio no alcanza para que cada foto mida al menos ~250 px
        // (pantallas chicas, ventana pequeña o interfaz ampliada). Se recalcula al cambiar el tamaño.
        const int BurstPageMax = 12, BurstPageMin = 2;
        const double BurstTileMin = 250;
        int BurstPageSize = BurstPageMax;

        int ComputeBurstPageSize(List<PhotoItem> members)
        {
            double W = burstTiles == null ? 0 : burstTiles.ActualWidth, H = burstTiles == null ? 0 : burstTiles.ActualHeight;
            if (W <= 0 || H <= 0 || members.Count == 0) return BurstPageMax;
            double aspect = members.Average(x => Aspect(x));
            // Si el espacio es tan bajo que ni con 2 fotos se llega al mínimo, se acepta un 80 % de lo que cabe con 2
            double need = Math.Min(BurstTileMin, TileSizeFor(BurstPageMin, W, H, aspect) * 0.8);
            for (int n = Math.Min(BurstPageMax, members.Count); n > BurstPageMin; n--)
                if (TileSizeFor(n, W, H, aspect) >= need) return n;
            return BurstPageMin;
        }

        // Lado mayor que tendría cada foto con n por página, con el mejor reparto en columnas (como BuildBurstTiles)
        static double TileSizeFor(int n, double W, double H, double aspect)
        {
            double best = 0;
            for (int c = 1; c <= n; c++)
            {
                int r = (n + c - 1) / c;
                double tw = W / c - 16, th = H / r - 16; // 16: márgenes de cada recuadro
                double w = Math.Min(tw, th * aspect), h = Math.Min(th, tw / aspect);
                best = Math.Max(best, Math.Max(w, h));
            }
            return best;
        }

        bool burstMode;
        bool burstPanning; // se está arrastrando una foto ampliada de la ráfaga (las barras de marcar se ocultan)
        BurstGroup openBurst;
        Grid burstView, burstTiles;
        Border burstIndicator; // abajo al centro: «Modo ráfaga · B desactivar» o «Comparar… · C desactivar»
        TextBlock burstTitle;
        string burstLayoutKey;
        readonly MatrixTransform burstZoom = new MatrixTransform();
        readonly ConcurrentDictionary<string, BitmapSource> burstImgs = new ConcurrentDictionary<string, BitmapSource>();
        LoadQueue burstQueue;
        volatile int burstDim = 1600;
        readonly List<BurstTile> burstTileList = new List<BurstTile>();

        class BurstTile
        {
            public PhotoItem Item;
            public Grid Root;
            public Image Img;
            public Grid Clip, PhotoBox;
            public Grid HoverBox;            // capa con la barra de marcar (del tamaño de la parte visible de la foto)
            public Border Frame, FavFrame;
            public System.Windows.Shapes.Path Anim; // animación de la marca sobre esta foto (se crea al usarla)
            public Border SharpTag;                  // «Más nítida» (con el análisis de las miniaturas en disco)
            public System.Windows.Shapes.Rectangle Dash;
            public System.Windows.Shapes.Path Badge;
            public TextBlock Label;
        }

        // ---------------------------------------------------------------- Detección

        void ComputeBursts()
        {
            foreach (var it in all) { it.Burst = null; it.BurstPos = 0; }
            var dated = all.Where(x => x.Taken.HasValue)
                           .OrderBy(x => x.Taken.Value).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            int start = 0;
            for (int i = 1; i <= dated.Count; i++)
            {
                bool cut = i == dated.Count;
                if (!cut)
                {
                    double gap = (dated[i].Taken.Value - dated[i - 1].Taken.Value).TotalSeconds;
                    // Ráfaga por tiempo, o (con miniaturas en disco) foto casi igual a la anterior y poco tiempo después
                    cut = gap > BurstGapSeconds && !(i - start < SimilarMaxGroup && SimilarPhotos(dated[i - 1], dated[i], gap));
                }
                if (!cut) continue;
                if (i - start >= 2)
                {
                    var g = new BurstGroup { Start = dated[start].Taken.Value, End = dated[i - 1].Taken.Value };
                    for (int k = start; k < i; k++)
                    {
                        dated[k].Burst = g;
                        dated[k].BurstPos = k - start;
                        g.Items.Add(dated[k]);
                    }
                }
                start = i;
            }
            NumberBursts();
            SyncBurstView(); // con el modo activo, la foto actual puede ser ahora parte de una ráfaga
        }

        // La posición «2/5» de cada foto sigue el orden de la tira (el mismo de la vista de ráfaga): fotos del mismo
        // segundo se desempatan como en la tira (nombre natural) y con orden descendente la 1 es la primera que se ve.
        // Se llama al detectar las ráfagas y al cambiar el orden.
        void NumberBursts()
        {
            var at = new Dictionary<PhotoItem, int>(all.Count);
            for (int i = 0; i < all.Count; i++) at[all[i]] = i;
            var done = new HashSet<BurstGroup>();
            foreach (var it in all)
            {
                var g = it.Burst;
                if (g == null || !done.Add(g)) continue;
                g.Items.Sort((a, b) => at[a].CompareTo(at[b]));
                for (int k = 0; k < g.Items.Count; k++) g.Items[k].BurstPos = k;
            }
        }

        // ---------------------------------------------------------------- Abrir / cerrar

        // B: activa o desactiva el «modo ráfaga» (se recuerda). Con el modo activo, cada vez que la foto actual es
        // parte de una ráfaga se muestra la vista lado a lado; con una foto suelta, el visor normal.
        void ToggleBurst()
        {
            settings.ModoRafaga = !settings.ModoRafaga;
            // Comparar y el modo ráfaga no van juntos: activar uno desactiva el otro
            bool compareOff = settings.ModoRafaga && settings.Comparar;
            if (compareOff) settings.Comparar = false;
            SaveSettings();
            if (settings.ModoRafaga && gridMode) ExitGrid();
            if (compareOff) { ResetZoom(); UpdateStrip(); UpdateCompare(); }
            SyncBurstView();
            if (settings.ModoRafaga)
            {
                var it = Current;
                bool inBurst = it != null && it.Burst != null && BurstMembers(it.Burst).Count >= 2;
                if (compareOff) Toast("Modo ráfaga activado  ·  comparar desactivado");
                else Toast(inBurst ? "Modo ráfaga activado" :
                      metaTotal > 0 && metaDone < metaTotal ? "Modo ráfaga activado: las ráfagas estarán listas al terminar de leer las fechas" :
                      "Modo ráfaga activado: las ráfagas se verán lado a lado al llegar a ellas");
            }
            else Toast("Modo ráfaga desactivado");
        }

        // Muestra u oculta la vista según el modo y la foto actual. Se llama en cada cambio de foto o de marca.
        void SyncBurstView()
        {
            if (settings.ModoRafaga) EnsureBurstView();
            UpdateModeIndicator();
            var it = Current;
            bool show = settings.ModoRafaga && !gridMode && it != null && it.Burst != null && BurstMembers(it.Burst).Count >= 2;
            if (!show) { CloseBurst(); return; }
            // Las ráfagas se recalculan (p. ej. al avanzar el análisis): si es el mismo grupo de fotos, la vista sigue
            // igual (sin perder el zoom)
            if (burstMode && openBurst != null && openBurst != it.Burst && openBurst.Items.SequenceEqual(it.Burst.Items)) openBurst = it.Burst;
            if (!burstMode || openBurst != it.Burst)
            {
                ResetZoom();
                burstMode = true;
                openBurst = it.Burst;
                burstZoom.Matrix = Matrix.Identity;
                burstLayoutKey = null;
                burstView.Visibility = Visibility.Visible;
            }
            RefreshBurstView();
        }

        void CloseBurst()
        {
            if (!burstMode) return;
            burstMode = false;
            openBurst = null;
            burstView.Visibility = Visibility.Collapsed;
            burstTiles.Children.Clear();
            burstTileList.Clear();
            markBars.RemoveAll(b => b.InBurst); // las barras de marcar de los recuadros que se quitan
            burstLayoutKey = null;
            if (burstQueue != null) burstQueue.Request(new List<PhotoItem>());
            burstImgs.Clear(); // libera memoria
        }

        // Fotos de la ráfaga que el filtro deja ver, en el orden de la tira
        List<PhotoItem> BurstMembers(BurstGroup g)
        {
            var pos = new List<KeyValuePair<int, PhotoItem>>();
            foreach (var it in g.Items)
            {
                int i = view.IndexOf(it);
                if (i >= 0) pos.Add(new KeyValuePair<int, PhotoItem>(i, it));
            }
            return pos.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        }

        // ---------------------------------------------------------------- Teclas (con la vista abierta)

        bool HandleBurstKey(Key key)
        {
            key = WasdToArrow(key);
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var members = BurstMembers(openBurst);
            int p = members.IndexOf(Current);
            switch (key)
            {
                case Key.Left:
                    if (p > 0) GoToItem(members[p - 1]); else LeaveBurst(-1);
                    return true;
                case Key.Right:
                    if (p >= 0 && p < members.Count - 1) GoToItem(members[p + 1]); else LeaveBurst(1);
                    return true;
                case Key.PageDown: BurstPage(members, p, 1); return true;
                case Key.PageUp: BurstPage(members, p, -1); return true;
                case Key.Home: if (members.Count > 0) GoToItem(members[0]); return true;
                case Key.End: if (members.Count > 0) GoToItem(members[members.Count - 1]); return true;
                case Key.Up:
                    if (shift) { KeepOnlyThis(members); return true; }
                    return false; // marcar como siempre
                case Key.Down: return false;
                case Key.B: ToggleBurst(); return true; // desactiva el modo
                case Key.Escape: // con zoom: primero vuelve al tamaño normal; luego sale de la vista completa; luego del modo
                    if (burstZoom.Matrix.M11 > 1.01) burstZoom.Matrix = Matrix.Identity;
                    else if (fullView) ExitFullView();
                    else ToggleBurst();
                    return true;
                case Key.Space: return false; // vista completa, como siempre
                case Key.D0: case Key.NumPad0:
                    if (CtrlDown) return false;
                    burstZoom.Matrix = Matrix.Identity;
                    return true;
                case Key.Z: case Key.F11: case Key.I: return false;
                case Key.C: return false; // pasa a comparar (desactiva el modo ráfaga)
                case Key.M: return false; // mover la tira de miniaturas (lateral / horizontal), como siempre
                case Key.U: case Key.Back: return false; // quitar la marca, como siempre
                case Key.OemPlus: case Key.Add: case Key.OemMinus: case Key.Subtract: return !CtrlDown; // Ctrl: tamaño de la interfaz
                default: return true; // el resto no hace nada aquí (evita cambiar de vista sin querer)
            }
        }

        // ← en la primera o → en la última: pasar a la foto de al lado en la tira. Si es de otra ráfaga, se ve esa
        // ráfaga (de ráfaga en ráfaga); si es una foto suelta, el visor normal (el modo sigue activo).
        void LeaveBurst(int dir)
        {
            int target = idx + dir;
            if (target < 0 || target >= view.Count)
            {
                Toast(dir > 0 ? "Es la última foto" : "Es la primera foto");
                return;
            }
            var next = view[target];
            GoTo(target); // SyncBurstView abre la otra ráfaga o vuelve al visor
            if (burstMode && openBurst == next.Burst)
                Toast((dir > 0 ? "Siguiente ráfaga" : "Ráfaga anterior") + "  ·  " + BurstMembers(next.Burst).Count + " fotos");
        }

        // RePág / AvPág: página anterior / siguiente de la ráfaga (12 fotos por página), en la misma posición de la
        // cuadrícula (o la más cercana). Desde la primera / última página se pasa a la ráfaga (o foto) de al lado.
        void BurstPage(List<PhotoItem> members, int p, int dir)
        {
            if (members.Count == 0) return;
            if (p < 0) p = 0;
            BurstPageSize = ComputeBurstPageSize(members);
            int page = p / BurstPageSize, pages = (members.Count + BurstPageSize - 1) / BurstPageSize;
            int target = page + dir;
            if (target < 0 || target >= pages)
            {
                // Fuera de la ráfaga: la foto de justo antes (o después) en la tira; si es de otra ráfaga, se ve esa
                int edge = view.IndexOf(dir > 0 ? members[members.Count - 1] : members[0]) + dir;
                if (edge < 0 || edge >= view.Count) { Toast(dir > 0 ? "Es la última página" : "Es la primera página"); return; }
                var next = view[edge];
                GoTo(edge);
                if (burstMode && openBurst == next.Burst)
                    Toast((dir > 0 ? "Siguiente ráfaga" : "Ráfaga anterior") + "  ·  " + BurstMembers(next.Burst).Count + " fotos");
                return;
            }
            int slot = p % BurstPageSize;
            int i = Math.Min(members.Count - 1, target * BurstPageSize + slot);
            GoToItem(members[i]);
            Toast("Página " + (target + 1) + " de " + pages);
        }

        void GoToItem(PhotoItem it)
        {
            int i = view.IndexOf(it);
            if (i >= 0) GoTo(i);
        }

        // Shift + ↑: la actual queda como la ÚNICA favorita de la ráfaga; todas las demás (sin marca o favoritas)
        // pasan a no deseadas. Se deshace de una vez con Ctrl + Z.
        void KeepOnlyThis(List<PhotoItem> members)
        {
            var cur = Current;
            if (cur == null) return;
            var group = new List<KeyValuePair<PhotoItem, Mark>>();
            if (cur.Mark != Mark.Fav) group.Add(new KeyValuePair<PhotoItem, Mark>(cur, cur.Mark));
            var others = members.Where(x => x != cur && x.Mark != Mark.Reject).ToList();
            int wasFav = others.Count(x => x.Mark == Mark.Fav);
            foreach (var o in others) group.Add(new KeyValuePair<PhotoItem, Mark>(o, o.Mark));
            if (group.Count == 0) { Toast("Ya es la única favorita de la ráfaga"); return; }
            PushUndo(group);
            foreach (var o in others) ApplyMark(o, Mark.Reject, false);
            if (cur.Mark != Mark.Fav) ApplyMark(cur, Mark.Fav, false);
            AnimateMark(Mark.Fav, false, cur);
            // Con «Avanzar al marcar»: ya elegida la de esta ráfaga, pasar a la primera foto después de ella
            if (settings.AvanzarAlMarcar && members.Count > 0)
            {
                int after = view.IndexOf(members[members.Count - 1]) + 1;
                if (after > 0 && after < view.Count) GoTo(after);
            }
            Toast("Elegida " + cur.Name +
                  (others.Count > 0 ? "  ·  " + others.Count + (others.Count == 1 ? " no deseada" : " no deseadas") : "") +
                  (wasFav > 0 ? " (" + wasFav + (wasFav == 1 ? " era favorita" : " eran favoritas") + ")" : "") +
                  "   (Ctrl + Z deshace)");
        }

        // ---------------------------------------------------------------- Vista

        void EnsureBurstView()
        {
            if (burstView != null) return;
            burstZoom.Changed += (s, e) =>
            {
                foreach (var t in burstTileList) PlaceBurstOverlay(t);
                HideDisallowedMarkBars(); // al arrastrar una foto ampliada, las barras de marcar se ocultan
            };
            burstQueue = new LoadQueue(2, ThreadPriority.Normal, burstImgs,
                it => ImageLoader.Load(it, burstDim),
                it => Dispatcher.BeginInvoke(new Action(() => OnBurstImageReady(it))));

            burstView = new Grid { Background = BgBrush, Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(burstView, 3);
            Panel.SetZIndex(burstView, 50);
            Panel.SetZIndex(toast, 100); // los avisos quedan por encima
            burstView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            burstView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Cabecera: título a la izquierda y atajos a la derecha
            var head = new DockPanel { Margin = new Thickness(12, 8, 12, 6), LastChildFill = false };
            burstTitle = new TextBlock { Foreground = TextBrush, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(burstTitle, Dock.Left);
            head.Children.Add(burstTitle);
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            AddToolbarKey(keys, "← →", "elegir");
            AddToolbarKey(keys, "↑ ↓", "marcar");
            AddToolbarKey(keys, "Shift + ↑", "quedarme con esta");
            AddToolbarKey(keys, "Rueda", "zoom");
            AddToolbarKey(keys, "Doble clic", "ver sola");
            var keysBox = new Border { Child = keys, ClipToBounds = true, Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(keysBox, Dock.Right);
            head.Children.Add(keysBox);
            burstView.Children.Add(head);

            burstTiles = new Grid { Margin = new Thickness(4, 0, 4, 48) }; // abajo queda sitio para el indicador del modo
            Grid.SetRow(burstTiles, 1);
            burstTiles.SizeChanged += (s, e) => { burstLayoutKey = null; RefreshBurstView(); };
            burstView.Children.Add(burstTiles);
            // Que la rueda y los clics no lleguen al visor de abajo
            burstView.MouseWheel += (s, e) => e.Handled = true;
            burstView.MouseLeftButtonDown += (s, e) => e.Handled = true;
            center.Children.Add(burstView);
        }

        // ---------------------------------------------------------------- Indicador de modo (abajo al centro)
        // Se ve mientras está activo el modo ráfaga o comparar (son excluyentes), con la tecla para desactivarlo.
        // Clic sobre el indicador también lo desactiva.

        void UpdateModeIndicator()
        {
            bool burst = settings.ModoRafaga, cmp = settings.Comparar && !burst;
            bool show = (burst || cmp) && !gridMode;
            if (burstIndicator == null)
            {
                if (!show) return;
                burstIndicator = new Border
                {
                    Background = B("#E0262626"), BorderBrush = B("#4A4A4A"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 5, 12, 6),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 12), Cursor = Cursors.Hand
                };
                burstIndicator.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (settings.ModoRafaga) ToggleBurst(); else if (settings.Comparar) ToggleCompare();
                };
                Grid.SetColumnSpan(burstIndicator, 3);
                Panel.SetZIndex(burstIndicator, 90);
                center.Children.Add(burstIndicator);
            }
            burstIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // el porcentaje de zoom (también abajo al centro) sube para no taparse con el indicador
            zoomText.Margin = new Thickness(14, 14, 14, show ? 52 : 14);
            if (!show) return;

            string key = burst ? "burst" : "cmp" + settings.ModoComparar;
            if ((string)burstIndicator.Tag == key) return; // ya muestra este modo
            burstIndicator.Tag = key;
            var ind = new StackPanel { Orientation = Orientation.Horizontal };
            ind.Children.Add(burst ? MakeBurstIcon(B("#E6FFFFFF"), 14, 11) : MakeCompareIcon(B("#E6FFFFFF")));
            string title = burst ? "Modo ráfaga"
                         : settings.ModoComparar == 1 ? "Comparar con la anterior"
                         : settings.ModoComparar == 2 ? "Comparar con la anterior y la siguiente"
                         : "Comparar con la siguiente";
            ind.Children.Add(new TextBlock
            {
                Text = title, Foreground = TextBrush, FontWeight = FontWeights.SemiBold, FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 12, 0)
            });
            if (burst) AddToolbarKey(ind, "B", "desactivar");
            else
            {
                AddToolbarKey(ind, "C", "desactivar");
                AddToolbarKey(ind, "C C", "cambiar formato");
            }
            ((FrameworkElement)ind.Children[ind.Children.Count - 1]).Margin = new Thickness(5, 0, 0, 0);
            burstIndicator.Child = ind;
            burstIndicator.ToolTip = burst
                ? "Con el modo ráfaga activo, las fotos de una ráfaga se ven lado a lado. Clic o B para desactivarlo."
                : "Comparar está activo. Clic o C para desactivarlo; doble C cambia el formato.";
        }

        // Icono de comparar: dos fotos lado a lado
        static FrameworkElement MakeCompareIcon(Brush brush)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Border { Width = 6, Height = 10, Background = brush, CornerRadius = new CornerRadius(1) });
            sp.Children.Add(new Border { Width = 6, Height = 10, BorderBrush = brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1), Margin = new Thickness(2, 0, 0, 0) });
            return sp;
        }

        // Se llama al cambiar de foto o de marca (desde UpdateMarkBadge) y al cambiar el tamaño
        void RefreshBurstView()
        {
            if (!burstMode || burstView == null) return;
            var cur = Current;
            if (cur == null || cur.Burst != openBurst) { CloseBurst(); return; }
            var members = BurstMembers(openBurst);
            if (members.Count < 2) { CloseBurst(); return; }
            int p = Math.Max(0, members.IndexOf(cur));
            BurstPageSize = ComputeBurstPageSize(members);
            int page = p / BurstPageSize, pages = (members.Count + BurstPageSize - 1) / BurstPageSize;
            var shown = members.Skip(page * BurstPageSize).Take(BurstPageSize).ToList();

            double secs = (openBurst.End - openBurst.Start).TotalSeconds;
            bool hasSimilar = members.Count > 1 && (openBurst.End - openBurst.Start).TotalSeconds > BurstGapSeconds * (members.Count - 1);
            burstTitle.Text = (hasSimilar ? "Ráfaga y fotos parecidas" : "Ráfaga") + "  ·  " + members.Count + " fotos" +
                              (members.Count < openBurst.Items.Count ? " (de " + openBurst.Items.Count + ")" : "") +
                              "  ·  " + openBurst.Start.ToString("HH:mm:ss") + (secs >= 1 ? "  ·  " + secs.ToString("0") + " s" : "") +
                              (pages > 1 ? "  ·  página " + (page + 1) + " de " + pages : "");

            string key = string.Join("|", shown.Select(x => x.Path)) + "#" + burstTiles.ActualWidth + "x" + burstTiles.ActualHeight;
            if (key != burstLayoutKey) { burstLayoutKey = key; BuildBurstTiles(shown); }
            var sharpest = SharpestOf(members);
            foreach (var t in burstTileList)
            {
                UpdateBurstTile(t, members);
                t.SharpTag.Visibility = t.Item == sharpest ? Visibility.Visible : Visibility.Collapsed;
            }
            burstQueue.Request(new List<PhotoItem> { cur }.Concat(shown.Where(x => x != cur)).ToList());
        }

        void BuildBurstTiles(List<PhotoItem> shown)
        {
            burstTiles.Children.Clear();
            burstTiles.RowDefinitions.Clear();
            burstTiles.ColumnDefinitions.Clear();
            burstTileList.Clear();
            markBars.RemoveAll(b => b.InBurst); // las barras de marcar de los recuadros que se quitan
            int n = shown.Count;
            double W = Math.Max(1, burstTiles.ActualWidth), H = Math.Max(1, burstTiles.ActualHeight);

            // Columnas que hacen las fotos lo más grandes posible (según la proporción media)
            double aspect = shown.Average(x => Aspect(x));
            int bestCols = 1;
            double bestSize = 0;
            for (int c = 1; c <= n; c++)
            {
                int r = (n + c - 1) / c;
                double tw = W / c, th = H / r;
                double size = Math.Min(tw, th * aspect) * Math.Min(th, tw / aspect); // área de la foto en el recuadro
                if (size > bestSize) { bestSize = size; bestCols = c; }
            }
            int cols = bestCols, rows = (n + cols - 1) / cols;
            for (int c = 0; c < cols; c++) burstTiles.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < rows; r++) burstTiles.RowDefinitions.Add(new RowDefinition());

            // Resolución de carga: unas 3 veces el recuadro (para poder ampliar), entre 1200 y 4000 px
            double dpi = 1;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) dpi = src.CompositionTarget.TransformToDevice.M11;
            int dim = (int)Math.Max(1200, Math.Min(4000, Math.Max(W / cols, H / rows) * dpi * 3));
            if (Math.Abs(dim - burstDim) > burstDim / 4) { burstDim = dim; burstImgs.Clear(); }

            for (int i = 0; i < n; i++)
            {
                var t = MakeBurstTile(shown[i]);
                Grid.SetRow(t.Root, i / cols);
                Grid.SetColumn(t.Root, i % cols);
                burstTiles.Children.Add(t.Root);
                burstTileList.Add(t);
            }
        }

        BurstTile MakeBurstTile(PhotoItem it)
        {
            var t = new BurstTile { Item = it };
            t.Img = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(t.Img, BitmapScalingMode.HighQuality);
            // Todas comparten la misma transformación: el zoom y el arrastre se ven igual en todas
            var zoomHost = new Grid { RenderTransform = burstZoom };
            zoomHost.Children.Add(t.Img);
            var clip = new Grid { ClipToBounds = true, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            clip.Children.Add(zoomHost);

            // Capa con el tamaño de la parte visible de la foto (con el zoom aplicado): marcos, icono y etiqueta
            // se pegan a la foto, no al recuadro, y la siguen al ampliar o mover (ver PlaceBurstOverlay)
            var photoBox = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            t.Clip = clip;
            t.PhotoBox = photoBox;
            t.Img.SizeChanged += (s, e) => PlaceBurstOverlay(t);
            clip.SizeChanged += (s, e) => PlaceBurstOverlay(t);
            // Favorita: borde verde sobre la foto. Actual: borde azul por fuera (como en la tira)
            t.FavFrame = new Border { BorderBrush = FavBrush, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(2) };
            t.Frame = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6), Margin = new Thickness(-6) };
            t.Badge = MakeMarkIcon(34, 28, BgBrush, new Thickness(0, 10, 10, 0));
            t.Label = new TextBlock
            {
                Foreground = Brushes.White, Background = B("#A0000000"), Padding = new Thickness(8, 2, 8, 3), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10)
            };
            photoBox.Children.Add(t.FavFrame);
            t.SharpTag = new Border
            {
                Background = B("#CC0F2A18"), BorderBrush = CheckBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 10, 3), Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "◎ Más nítida", Foreground = B("#E6FFFFFF"), FontSize = 12, FontWeight = FontWeights.SemiBold },
                ToolTip = "La foto más nítida de esta ráfaga (según el análisis de las miniaturas guardadas)"
            };
            photoBox.Children.Add(t.SharpTag);
            photoBox.Children.Add(t.Badge);
            photoBox.Children.Add(t.Label);
            photoBox.Children.Add(t.Frame);
            t.Dash = MakeFocusDash(new Thickness(-6), 6, 3);
            photoBox.Children.Add(t.Dash);
            t.Root = new Grid { Margin = new Thickness(8) }; // deja sitio para el borde azul exterior
            t.Root.Children.Add(clip);
            t.Root.Children.Add(photoBox);

            // Barra de marcar abajo de la foto (aparece al acercar el ratón a la parte de abajo); va en una capa del
            // tamaño de la parte visible de la foto (fondo nulo: solo la barra recibe clics)
            t.HoverBox = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            t.Root.Children.Add(t.HoverBox);
            // (con zoom también aparece; solo se oculta mientras se arrastra la foto)
            var bar = CreateMarkBar(t.HoverBox, t.HoverBox, t.Root, () => t.Item,
                                    () => burstMode && burstTileList.Contains(t) && !burstPanning && !loupeActive, false);
            bar.InBurst = true;
            bar.Root.Margin = new Thickness(0, 0, 0, 10);

            // Ratón: clic elige, doble clic abre en el visor, rueda amplía (en todas a la vez), arrastrar mueve
            Point down = new Point();
            bool dragging = false, moved = false;
            t.Root.MouseWheel += (s, e) => // en el recuadro entero: también funciona con el ratón sobre los botones
            {
                var m = burstZoom.Matrix;
                double f = e.Delta > 0 ? 1.25 : 0.8;
                if (m.M11 * f > 6) f = 6 / m.M11;
                var pt = e.GetPosition(clip);
                m.ScaleAt(f, f, pt.X, pt.Y);
                burstZoom.Matrix = m.M11 <= 1.01 ? Matrix.Identity : ClampBurstZoom(m, t);
                e.Handled = true;
            };
            clip.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { GoToItem(it); ToggleBurst(); e.Handled = true; return; } // ver esa foto sola
                down = e.GetPosition(burstTiles);
                dragging = true; moved = false;
                clip.CaptureMouse();
                e.Handled = true;
            };
            clip.MouseMove += (s, e) =>
            {
                if (!dragging) return;
                var p = e.GetPosition(burstTiles);
                var d = p - down;
                // Sin zoom no hay nada que arrastrar: el movimiento no cuenta (un clic con la mano algo movida
                // seguía siendo un clic). Con zoom, a partir de 8 px es arrastrar.
                if (burstZoom.Matrix.M11 <= 1.01) return;
                if (!moved && Math.Abs(d.X) + Math.Abs(d.Y) < 8) return;
                if (!moved) { burstPanning = true; HideDisallowedMarkBars(); }
                moved = true;
                if (burstZoom.Matrix.M11 > 1.01)
                {
                    var m = burstZoom.Matrix;
                    m.Translate(d.X, d.Y);
                    burstZoom.Matrix = ClampBurstZoom(m, t);
                }
                down = p;
            };
            clip.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                dragging = false;
                clip.ReleaseMouseCapture();
                if (burstPanning) { burstPanning = false; RefreshMarkBarsAtMouse(); }
                if (!moved && Current != it) GoToItem(it);
            };
            return t;
        }

        // Límites del zoom de la ráfaga (como en el visor): la foto ampliada no puede separarse de los bordes de su
        // recuadro. Si ya es más grande que el recuadro, lo cubre entero; si aún es más pequeña en un sentido, queda
        // dentro. Se calcula con la foto sobre la que se usa la rueda o se arrastra (todas comparten el zoom).
        static Matrix ClampBurstZoom(Matrix m, BurstTile t)
        {
            double cw = t.Clip.ActualWidth, ch = t.Clip.ActualHeight, iw = t.Img.ActualWidth, ih = t.Img.ActualHeight;
            if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) return m;
            var r = new Rect((cw - iw) / 2, (ch - ih) / 2, iw, ih);
            r.Transform(m);
            double dx = 0, dy = 0;
            if (r.Width >= cw) { if (r.Left > 0) dx = -r.Left; else if (r.Right < cw) dx = cw - r.Right; }
            else { if (r.Left < 0) dx = -r.Left; else if (r.Right > cw) dx = cw - r.Right; }
            if (r.Height >= ch) { if (r.Top > 0) dy = -r.Top; else if (r.Bottom < ch) dy = ch - r.Bottom; }
            else { if (r.Top < 0) dy = -r.Top; else if (r.Bottom > ch) dy = ch - r.Bottom; }
            m.Translate(dx, dy);
            return m;
        }

        // Ajusta la capa de marcos a la foto tal como se ve: su rectángulo con el zoom, recortado al recuadro
        void PlaceBurstOverlay(BurstTile t)
        {
            // Sin zoom: la capa toma el tamaño de la foto por enlace y se centra sobre ella (lo resuelve el propio
            // diseño, como en las miniaturas). Calcularlo a mano fallaba en algunas pantallas: en la fila de arriba
            // los marcos quedaban fuera de sitio y no se veían.
            if (burstZoom.Matrix.IsIdentity)
            {
                foreach (var box in new[] { t.PhotoBox, t.HoverBox })
                {
                    if (box.HorizontalAlignment == HorizontalAlignment.Center) continue; // ya enlazada
                    box.HorizontalAlignment = HorizontalAlignment.Center;
                    box.VerticalAlignment = VerticalAlignment.Center;
                    box.Margin = new Thickness(0);
                    box.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = t.Img });
                    box.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = t.Img });
                }
                t.PhotoBox.Visibility = Visibility.Visible;
                return;
            }
            // Con zoom: la parte visible de la foto, recortada al recuadro
            foreach (var box in new[] { t.PhotoBox, t.HoverBox })
            {
                System.Windows.Data.BindingOperations.ClearBinding(box, FrameworkElement.WidthProperty);
                System.Windows.Data.BindingOperations.ClearBinding(box, FrameworkElement.HeightProperty);
                box.HorizontalAlignment = HorizontalAlignment.Left;
                box.VerticalAlignment = VerticalAlignment.Top;
            }
            double cw = t.Clip.ActualWidth, ch = t.Clip.ActualHeight, iw = t.Img.ActualWidth, ih = t.Img.ActualHeight;
            if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) { t.PhotoBox.Visibility = Visibility.Hidden; return; }
            var r = new Rect((cw - iw) / 2, (ch - ih) / 2, iw, ih);
            r.Transform(burstZoom.Matrix);
            r.Intersect(new Rect(0, 0, cw, ch));
            if (r.IsEmpty || r.Width < 1 || r.Height < 1) { t.PhotoBox.Visibility = Visibility.Hidden; return; }
            t.PhotoBox.Visibility = Visibility.Visible;
            t.PhotoBox.Margin = t.HoverBox.Margin = new Thickness(r.X, r.Y, 0, 0);
            t.PhotoBox.Width = t.HoverBox.Width = r.Width;
            t.PhotoBox.Height = t.HoverBox.Height = r.Height;
        }

        // La barra de marcar de cada foto se pinta sola (ver MarkBar.cs); aquí solo el icono de la marca
        void UpdateBurstHover(BurstTile t)
        {
            t.Badge.Visibility = t.Item.Mark == Mark.None ? Visibility.Collapsed : Visibility.Visible;
        }

        void UpdateBurstTile(BurstTile t, List<PhotoItem> members)
        {
            var it = t.Item;
            bool cur = it == Current;
            t.Frame.BorderBrush = cur ? FocusBrush : Brushes.Transparent;
            ApplyFocusStyle(t.Frame, t.Dash, cur);
            t.FavFrame.Visibility = it.Mark == Mark.Fav ? Visibility.Visible : Visibility.Collapsed;
            SetMarkIcon(t.Badge, it.Mark);
            t.Img.Opacity = it.Mark == Mark.Reject ? 0.45 : 1; // atenuada, pero en color para poder compararla
            t.Label.Text = (members.IndexOf(it) + 1) + " / " + members.Count + "   " + it.SortDate.ToString("HH:mm:ss");
            t.Img.Source = BurstImage(it);
            UpdateBurstHover(t);
        }

        // La mejor imagen que ya esté cargada: la de este tamaño, la del visor o, mientras tanto, la miniatura
        BitmapSource BurstImage(PhotoItem it)
        {
            BitmapSource b;
            if (burstImgs.TryGetValue(it.Path, out b) && b != null) return b;
            if (fulls.TryGetValue(it.Path, out b) && b != null) return b;
            if (thumbs.TryGetValue(it.Path, out b) && b != null) return b;
            return null;
        }

        void OnBurstImageReady(PhotoItem it)
        {
            if (!burstMode) return;
            foreach (var t in burstTileList)
                if (t.Item == it) t.Img.Source = BurstImage(it);
        }

        // ---------------------------------------------------------------- Etiqueta en la tira

        // Pastilla «2/5» abajo a la derecha de la miniatura, con un pequeño icono de fotos apiladas
        // Icono de fotos apiladas: una con contorno detrás y otra rellena delante
        static FrameworkElement MakeBurstIcon(Brush brush, double w, double h)
        {
            double pw = Math.Round(w * 0.72), ph = Math.Round(h * 0.68);
            var icon = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center };
            icon.Children.Add(new Border
            {
                Width = pw, Height = ph, BorderBrush = brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
            });
            icon.Children.Add(new Border
            {
                Width = pw, Height = ph, Background = brush, CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom
            });
            return icon;
        }

        static Border MakeBurstMark(out TextBlock text)
        {
            var icon = MakeBurstIcon(B("#C8FFFFFF"), 11, 9);
            icon.Margin = new Thickness(0, 0, 4, 0);
            text = new TextBlock { Foreground = B("#E6FFFFFF"), FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(icon);
            sp.Children.Add(text);
            return new Border
            {
                Child = sp, Background = B("#99000000"), CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 1, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 7, 7), IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
        }
    }
}
