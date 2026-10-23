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
    // Tira de miniaturas: colocación (centrada, lateral, justificada), barra de desplazamiento, rueda y tamaño.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Tira de miniaturas

        // Coloca las miniaturas con la actual siempre en el centro (horizontal abajo, vertical en el lateral).
        void LayoutStrip()
        {
            strip.Children.Clear();
            slots.Clear();
            if (Justified)
            {
                // Cuadrícula justificada: las casillas se crean y colocan en cada actualización.
                UpdateStrip();
                return;
            }
            double len = sideStrip ? strip.ActualHeight : strip.ActualWidth;
            if (len <= 0) return;
            int cols = Lanes;
            double slotLen = sideStrip ? SlotH : SlotW;
            double step = slotLen + SlotGap;
            int k = (int)Math.Ceiling((len / 2 - slotLen / 2) / step);
            double crossStart = sideStrip
                ? (stripSize - (cols * SlotW + (cols - 1) * SlotGap)) / 2
                : BottomPadTop;
            // Centrada: líneas -k..k alrededor del centro. Libre: desde el principio de la tira (línea 0 = freeFirst).
            int from = FreeScroll ? 0 : -k, to = FreeScroll ? (int)Math.Ceiling(len / step) + 1 : k; // libre: una más (la vista puede empezar algo fuera)
            for (int o = from; o <= to; o++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var s = CreateSlot(o, c);
                    double pos = FreeScroll ? FreePad + o * step : len / 2 - slotLen / 2 + o * step;
                    double cross = crossStart + c * ((sideStrip ? SlotW : SlotH) + SlotGap);
                    Canvas.SetLeft(s.Root, (sideStrip ? cross : pos) - RingPad);
                    Canvas.SetTop(s.Root, (sideStrip ? pos : cross) - RingPad);
                    strip.Children.Add(s.Root);
                    slots.Add(s);
                }
            }
            UpdateStrip();
        }

        // Dónde va la tira: a un lado (derecha o izquierda) o en horizontal (abajo o arriba). Por defecto derecha / abajo.
        Dock StripDock
        {
            get
            {
                if (sideStrip) return settings.MiniaturasIzquierda ? Dock.Left : Dock.Right;
                return settings.MiniaturasArriba ? Dock.Top : Dock.Bottom;
            }
        }

        // Elige posición: side = lateral u horizontal; alt = izquierda (lateral) o arriba (horizontal)
        void SetStripPlace(bool side, bool alt)
        {
            ExitFullView(); // cambia la distribución de la ventana: primero sale de la vista completa
            if (side) settings.MiniaturasIzquierda = alt; else settings.MiniaturasArriba = alt;
            SaveSettings();
            if (gridMode) return; // se aplica al salir de la cuadrícula
            if (sideStrip != side) { ToggleStripSide(); return; }
            ApplyStripMode();
            OnStripResized();
            Dispatcher.BeginInvoke(new Action(LayoutStrip), DispatcherPriority.Loaded);
        }

        void ApplyStripMode()
        {
            int saved = sideStrip ? settings.AnchoMiniaturas : settings.AlturaMiniaturas;
            stripSize = saved >= StripMin && saved <= StripMax ? saved : (sideStrip ? 220 : 128);

            var dock = StripDock;
            DockPanel.SetDock(stripHost, dock);
            DockPanel.SetDock(splitter, dock);
            ApplyScrollbarOrientation();

            // Divisor entre la tira y el visor: la línea va del lado del visor
            var tpl = new ControlTemplate(typeof(Thumb));
            var fe = new FrameworkElementFactory(typeof(Border));
            fe.SetValue(Border.BackgroundProperty, B("#333333"));
            fe.SetValue(Border.BorderBrushProperty, B("#4A4A4A"));
            fe.SetValue(Border.BorderThicknessProperty,
                dock == Dock.Right ? new Thickness(1, 0, 0, 0) : dock == Dock.Left ? new Thickness(0, 0, 1, 0)
                : dock == Dock.Bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1));
            tpl.VisualTree = fe;
            splitter.Template = tpl;
            splitter.Width = sideStrip ? 7 : double.NaN;
            splitter.Height = sideStrip ? double.NaN : 7;
            splitter.Cursor = sideStrip ? Cursors.SizeWE : Cursors.SizeNS;

            ApplyStripSize();
            UpdateSideButton();
        }

        // Barra de desplazamiento: vertical a la derecha (lateral y cuadrícula) u horizontal abajo
        void ApplyScrollbarOrientation()
        {
            if (sideStrip)
            {
                scrollBar.HorizontalAlignment = HorizontalAlignment.Right;
                scrollBar.VerticalAlignment = VerticalAlignment.Stretch;
                scrollBar.Width = 12;
                scrollBar.Height = double.NaN;
                scrollBar.Margin = new Thickness(0, 6, 0, 6);
            }
            else
            {
                scrollBar.HorizontalAlignment = HorizontalAlignment.Stretch;
                scrollBar.VerticalAlignment = VerticalAlignment.Bottom;
                scrollBar.Width = double.NaN;
                scrollBar.Height = 12;
                scrollBar.Margin = new Thickness(6, 0, 6, 0);
            }
        }

        void ApplyStripSize()
        {
            stripHost.Width = sideStrip ? stripSize : double.NaN;
            stripHost.Height = sideStrip ? double.NaN : stripSize;
        }

        // Grosor y posición de la barra: fina en reposo, más gruesa al pasar el ratón o arrastrar.
        void UpdateScrollbar()
        {
            if (scrollBar == null) return;
            double len = sideStrip ? scrollBar.ActualHeight : scrollBar.ActualWidth;
            int visible = Math.Max(1, slots.Count);
            if (view.Count <= visible || idx < 0 || len <= 0)
            {
                bool show = view.Count > visible && idx >= 0;
                scrollBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                // Al volver a mostrarse aún no tiene medidas: recalcular cuando Windows se las dé
                // (si no, el tirador se quedaba ocupando todo el ancho hasta pasar el ratón).
                if (show && !scrollbarRetryPending)
                {
                    scrollbarRetryPending = true;
                    Dispatcher.BeginInvoke(new Action(() => { scrollbarRetryPending = false; UpdateScrollbar(); }), DispatcherPriority.Loaded);
                }
                return;
            }
            scrollBar.Visibility = Visibility.Visible;
            bool active = scrollBar.IsMouseOver || scrollDragging;
            double thick = active ? 9 : 4;
            double thumbLen = ScrollThumbLen(len, visible);
            // Centrada: el tirador indica la foto actual. Libre: la parte de la lista que se está viendo.
            double frac = FreeScroll ? (freeMaxFirst > 0 ? Math.Min(1, (double)freeFirst / freeMaxFirst) : 0) : (double)idx / Math.Max(1, view.Count - 1);
            double pos = (len - thumbLen) * frac;
            scrollThumb.Background = active ? ScrollActiveBrush : ScrollBrush;
            if (sideStrip)
            {
                scrollTrack.Width = thick; scrollTrack.Height = double.NaN;
                scrollTrack.HorizontalAlignment = HorizontalAlignment.Center; scrollTrack.VerticalAlignment = VerticalAlignment.Stretch;
                scrollThumb.Width = thick; scrollThumb.Height = thumbLen;
                scrollThumb.HorizontalAlignment = HorizontalAlignment.Center; scrollThumb.VerticalAlignment = VerticalAlignment.Top;
                scrollThumb.Margin = new Thickness(0, pos, 0, 0);
            }
            else
            {
                scrollTrack.Height = thick; scrollTrack.Width = double.NaN;
                scrollTrack.VerticalAlignment = VerticalAlignment.Center; scrollTrack.HorizontalAlignment = HorizontalAlignment.Stretch;
                scrollThumb.Height = thick; scrollThumb.Width = thumbLen;
                scrollThumb.VerticalAlignment = VerticalAlignment.Center; scrollThumb.HorizontalAlignment = HorizontalAlignment.Left;
                scrollThumb.Margin = new Thickness(pos, 0, 0, 0);
            }
        }

        double ScrollThumbLen(double len, int visible)
        {
            return Math.Max(30, Math.Min(len, len * visible / Math.Max(1, view.Count)));
        }

        void ScrollTo(Point p)
        {
            if (view.Count == 0) return;
            double len = sideStrip ? scrollBar.ActualHeight : scrollBar.ActualWidth;
            int visible = Math.Max(1, slots.Count);
            double thumbLen = ScrollThumbLen(len, visible);
            double v = sideStrip ? p.Y : p.X;
            double f = Math.Max(0, Math.Min(1, (v - thumbLen / 2) / Math.Max(1, len - thumbLen)));
            if (FreeScroll) { FreeScrollTo(f); return; } // libre: la barra mueve la vista, sin cambiar la foto seleccionada
            int target = (int)Math.Round(f * (view.Count - 1));
            if (gridMode) target = MiddleOfRow(target);
            if (target != idx) GoTo(target);
        }

        void ToggleStripSide()
        {
            ExitFullView(); // cambia la distribución de la ventana: primero sale de la vista completa
            if (gridMode) return;
            sideStrip = !sideStrip;
            settings.MiniaturasDerecha = sideStrip;
            ApplyStripMode();
            OnStripResized();
            Dispatcher.BeginInvoke(new Action(LayoutStrip), DispatcherPriority.Loaded);
        }

        // Icono: tantos rectángulos como columnas (en el lateral) o filas (abajo)
        UIElement LanesIcon()
        {
            // Todas las opciones posibles: las elegidas rellenas y el resto solo con contorno
            var p = new StackPanel { Orientation = sideStrip ? Orientation.Horizontal : Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            int n = Lanes;
            int total = gridMode ? GridMaxCols : 3;
            for (int i = 0; i < total; i++)
                p.Children.Add(new Border
                {
                    Background = i < n ? TextBrush : Brushes.Transparent,
                    BorderBrush = i < n ? TextBrush : DimTextBrush, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(1),
                    // en la cuadrícula (hasta 10) las barras son más finas para que el botón no crezca
                    Width = sideStrip ? (gridMode ? 3 : 4) : 16, Height = sideStrip ? 13 : 4,
                    Margin = sideStrip ? new Thickness(i > 0 ? (gridMode ? 1.5 : 2) : 0, 0, 0, 0) : new Thickness(0, i > 0 ? 2 : 0, 0, 0)
                });
            return p;
        }

        void UpdateSideButton()
        {
            if (btnSide == null) return;
            // Iconos de Segoe MDL2: E90D = acoplar a la derecha, E90E = acoplar abajo
            btnSide.Content = new TextBlock
            {
                Text = sideStrip ? (settings.MiniaturasIzquierda ? "\uE90C" : "\uE90D") : "\uE90E", // acoplar izquierda / derecha / abajo
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16
            };
            btnSide.ToolTip = sideStrip ? "Mover miniaturas " + (settings.MiniaturasArriba ? "arriba" : "abajo") + " (M)"
                                        : "Mover miniaturas a la " + (settings.MiniaturasIzquierda ? "izquierda" : "derecha") + " (M)";
            btnCols.Visibility = Visibility.Visible;
            btnCols.Content = LanesIcon();
            btnCols.ToolTip = gridMode ? GridCols + " columnas en la cuadrícula (clic: más · clic derecho: menos · + −)" : sideStrip
                ? Cols + (Cols == 1 ? " columna" : " columnas") + " de miniaturas (clic: siguiente · clic derecho: anterior)"
                : Rows + (Rows == 1 ? " fila" : " filas") + " de miniaturas (clic: siguiente · clic derecho: anterior)";
        }

        // 1 → 2 → 3 → 1 columnas (lateral) o filas (abajo). El panel mantiene su tamaño; las miniaturas se reparten el espacio.
        // dir = 1: siguiente (clic izquierdo); dir = -1: anterior (clic derecho). Da la vuelta en los extremos.
        void CycleColumns(int dir = 1)
        {
            ExitFullView(); // cambia la distribución de la ventana: primero sale de la vista completa
            if (gridMode)
            {
                int next = GridCols + dir;
                if (next > GridMaxCols) next = GridMinCols;
                if (next < GridMinCols) next = GridMaxCols;
                StepGridCols(next - GridCols);
                return;
            }
            if (sideStrip) settings.ColumnasMiniaturas = (Cols - 1 + dir + 3) % 3 + 1;
            else settings.FilasMiniaturas = (Rows - 1 + dir + 3) % 3 + 1;
            strip.Children.Clear();
            slots.Clear();
            UpdateSideButton();
            OnStripResized();
            Dispatcher.BeginInvoke(new Action(LayoutStrip), DispatcherPriority.Loaded);
        }

        Slot CreateSlot(int row, int col)
        {
            var s = new Slot { Row = row, Col = col, Index = -1 };
            s.Img = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(s.Img, BitmapScalingMode.HighQuality);
            s.ErrText = new TextBlock
            {
                Text = "No se puede abrir", Foreground = DimTextBrush, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            s.Badge = MakeMarkIcon(ThumbCheckSize, ThumbCrossSize, StripBrush, new Thickness(0, 5, 5, 0));
            s.FavFrame = new Border
            {
                // Mismo estilo que el borde de la actual: 3 px por fuera de la foto
                BorderBrush = FavBrush, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6), Margin = new Thickness(-6),
                Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
            // Inicio de un nuevo momento: etiqueta con la hora abajo a la izquierda (no choca con el ✓ ni se confunde con los marcos)
            s.GapText = new TextBlock { Foreground = B("#2A1A00"), FontSize = 11, FontWeight = FontWeights.Bold, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center };
            s.GapMark = new Border
            {
                Background = MomentBrush, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), MinHeight = 20,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(7, 0, 0, 7), Child = s.GapText,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            // Capa con el tamaño exacto de la foto: el marco verde, el ✓, la hora y el borde de la actual
            // se pegan a la foto (no a la casilla), así las verticales no quedan dentro de una caja gris.
            var photoBox = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            photoBox.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = s.Img });
            photoBox.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = s.Img });
            // Foto actual: borde azul por FUERA de la foto (en el hueco), así no la encoge ni se monta con el verde
            s.CurFrame = new Border
            {
                BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6), Margin = new Thickness(-6),
                Visibility = Visibility.Collapsed
            };
            s.PhotoBox = photoBox;
            photoBox.Children.Add(s.FavFrame);
            photoBox.Children.Add(s.CurFrame);
            s.CurDash = MakeFocusDash(new Thickness(-6), 6, 3);
            photoBox.Children.Add(s.CurDash);
            photoBox.Children.Add(s.Badge);
            photoBox.Children.Add(s.GapMark);
            s.BurstMark = MakeBurstMark(out s.BurstText);
            photoBox.Children.Add(s.BurstMark);

            var g = new Grid { Margin = new Thickness(RingPad) };
            g.Children.Add(s.Img);
            g.Children.Add(s.ErrText);
            g.Children.Add(photoBox);
            s.Root = new Border
            {
                Width = SlotW + 2 * RingPad, Height = SlotH + 2 * RingPad, Child = g,
                Background = Brushes.Transparent, // sin caja gris; transparente para seguir recibiendo clics
                Cursor = Cursors.Hand
            };
            AttachHoverMarks(s, g);
            s.Root.MouseLeftButtonDown += (a, e) =>
            {
                if (s.Index < 0) return;
                var pos = e.GetPosition(this);
                int now = Environment.TickCount;
                // Doble clic en la cuadrícula. El primer clic centra su fila y la cuadrícula se mueve, así que el
                // segundo cae sobre OTRA miniatura y WPF ya no lo cuenta como doble clic (ClickCount vuelve a 1).
                // Por eso se detecta aquí: mismo punto de la pantalla y dentro del tiempo de doble clic de Windows.
                // Se abre la foto del primer clic.
                bool dbl = gridMode && gridClickItem != null && unchecked(now - gridClickTime) <= GetDoubleClickTime()
                           && Math.Abs(pos.X - gridClickPos.X) <= 6 && Math.Abs(pos.Y - gridClickPos.Y) <= 6;
                if (dbl)
                {
                    int target = view.IndexOf(gridClickItem);
                    gridClickItem = null;
                    gridSuppressUp = true; // el botón se suelta ya en el visor: que no elija otra miniatura
                    if (target >= 0 && target != idx) GoTo(target);
                    ExitGrid();
                    e.Handled = true;
                    return;
                }
                gridClickItem = view[s.Index];
                gridClickTime = now;
                gridClickPos = pos;
                gridSuppressUp = false;
            };
            s.Root.MouseLeftButtonUp += (a, e) =>
            {
                if (gridSuppressUp) { gridSuppressUp = false; return; }
                if (s.Index < 0) return;
                // Solo si se suelta sobre la misma foto que se pulsó (la tira pudo moverse entre medio)
                if (view[s.Index] != gridClickItem) return;
                if (s.Index != idx) GoTo(s.Index);
            };
            return s;
        }

        // Proporción ancho/alto de la foto tal como se ve (con la rotación EXIF). Si aún no se conoce, 3:2.
        static double Aspect(PhotoItem it)
        {
            if (it.Width <= 0 || it.Height <= 0) return 1.5;
            bool rot = it.Orientation >= 5 && it.Orientation <= 8;
            double a = rot ? (double)it.Height / it.Width : (double)it.Width / it.Height;
            return Math.Max(0.4, Math.Min(3, a));
        }

        // Altura de una fila para que sus fotos llenen el ancho: dos horizontales → fila baja,
        // dos verticales → fila alta, mezcla → las dos a la misma altura.
        double JustifiedRowHeight(int row, int cols, double width)
        {
            double sum = 0;
            for (int c = 0; c < cols; c++)
            {
                int i = row * cols + c;
                sum += i < view.Count ? Aspect(view[i]) : 1.5; // última fila incompleta: como si siguieran fotos 3:2
            }
            double h = (width - (cols - 1) * SlotGap) / sum;
            return Math.Min(h, width); // tope: una sola vertical no ocupa más alto que el ancho de la tira
        }

        // Lateral: coloca las filas visibles con la fila de la foto actual centrada en vertical.
        void LayoutJustified()
        {
            const double pad = 10;
            double gap = SlotGap;
            double width = strip.ActualWidth - 2 * pad, height = strip.ActualHeight;
            int used = 0;
            if (width > 20 && height > 0 && idx >= 0 && view.Count > 0)
            {
                int cols = Cols, n = view.Count;
                // Centrada: la fila de la actual en el medio. Libre: la primera fila visible arriba del todo.
                int curRow = FreeScroll ? freeFirst : idx / cols;
                var rows = new List<KeyValuePair<int, double>>(); // fila, posición y
                double hc = JustifiedRowHeight(curRow, cols, width);
                double y0 = FreeScroll ? freeStartPos : height / 2 - hc / 2;
                rows.Add(new KeyValuePair<int, double>(curRow, y0));
                double up = y0;
                for (int r = curRow - 1; r >= 0 && up > 0; r--)
                {
                    up -= gap + JustifiedRowHeight(r, cols, width);
                    rows.Add(new KeyValuePair<int, double>(r, up));
                }
                double down = y0 + hc;
                for (int r = curRow + 1; r * cols < n && down < height; r++)
                {
                    rows.Add(new KeyValuePair<int, double>(r, down + gap));
                    down += gap + JustifiedRowHeight(r, cols, width);
                }
                foreach (var row in rows)
                {
                    double h = JustifiedRowHeight(row.Key, cols, width);
                    int first = row.Key * cols, count = Math.Min(cols, n - first);
                    double total = (count - 1) * gap;
                    for (int c = 0; c < count; c++) total += Aspect(view[first + c]) * h;
                    double x = pad + (width - total) / 2;
                    for (int c = 0; c < count; c++)
                    {
                        double w = Aspect(view[first + c]) * h;
                        Slot s;
                        if (used < slots.Count) s = slots[used];
                        else
                        {
                            s = CreateSlot(0, 0);
                            strip.Children.Add(s.Root);
                            slots.Add(s);
                        }
                        used++;
                        s.Index = first + c;
                        s.Root.Width = Math.Floor(w) + 2 * RingPad;
                        s.Root.Height = Math.Floor(h) + 2 * RingPad;
                        Canvas.SetLeft(s.Root, Math.Round(x) - RingPad);
                        Canvas.SetTop(s.Root, Math.Round(row.Value) - RingPad);
                        x += w + gap;
                    }
                }
            }
            for (int k = used; k < slots.Count; k++) slots[k].Index = -1;
        }

        Slot PoolSlot(ref int used)
        {
            Slot s;
            if (used < slots.Count) s = slots[used];
            else
            {
                s = CreateSlot(0, 0);
                strip.Children.Add(s.Root);
                slots.Add(s);
            }
            used++;
            return s;
        }

        // Abajo con 1 fila: cada foto ocupa su ancho natural (sin huecos) y la actual queda en el centro.
        void LayoutJustifiedRow()
        {
            double gap = SlotGap;
            double h = Math.Floor(strip.ActualHeight - BottomPadTop - BottomPadBottom), width = strip.ActualWidth;
            int used = 0;
            if (h > 20 && width > 0 && idx >= 0 && view.Count > 0)
            {
                // Hacia la derecha desde la actual (centrada) y hacia la izquierda, hasta salir de la vista.
                // Libre: desde la primera visible, pegada al borde izquierdo.
                int start = FreeScroll ? Math.Min(freeFirst, view.Count - 1) : idx;
                double wCur = Aspect(view[start]) * h;
                double x = FreeScroll ? freeStartPos : width / 2 - wCur / 2;
                PlaceRowSlot(ref used, start, x, wCur, BottomPadTop, h);
                double right = x + wCur + gap;
                for (int i = start + 1; i < view.Count && right < width + 20; i++)
                {
                    double w = Aspect(view[i]) * h;
                    PlaceRowSlot(ref used, i, right, w, BottomPadTop, h);
                    right += w + gap;
                }
                double left = x - gap;
                for (int i = start - 1; i >= 0 && left > -20; i--)
                {
                    double w = Aspect(view[i]) * h;
                    left -= w;
                    PlaceRowSlot(ref used, i, left, w, BottomPadTop, h);
                    left -= gap;
                }
            }
            for (int k = used; k < slots.Count; k++) slots[k].Index = -1;
        }

        void PlaceRowSlot(ref int used, int i, double x, double w, double y, double h)
        {
            var s = PoolSlot(ref used);
            s.Index = i;
            s.Root.Width = Math.Floor(w) + 2 * RingPad;
            s.Root.Height = h + 2 * RingPad;
            Canvas.SetLeft(s.Root, Math.Round(x) - RingPad);
            Canvas.SetTop(s.Root, Math.Round(y) - RingPad);
        }

        // Al conocerse las medidas reales de una foto, se recoloca la cuadrícula (agrupando varias a la vez).
        bool relayoutPending;
        void RequestRelayout()
        {
            if (relayoutPending) return;
            relayoutPending = true;
            Dispatcher.BeginInvoke(new Action(() => { relayoutPending = false; UpdateStrip(); }), DispatcherPriority.Background);
        }

        int ThumbDimFor()
        {
            // En el lateral una vertical puede ser bastante más alta que el ancho de columna.
            // Tope de 600 px: suficiente para miniaturas grandes y limita la memoria en monitores grandes.
            // Tope: 600 px en la tira; en la cuadrícula hasta 1600 px para que con pocas columnas se vea nítido.
            return Math.Min(gridMode ? 1600 : 600, (int)((Justified ? Math.Max(SlotW, SlotH) * 1.6 : SlotW) * dpiScale * UiScale));
        }

        void UpdateStrip()
        {
            // Abajo: casillas fijas con la actual en el centro. Lateral: cuadrícula justificada (LayoutJustified).
            int cols = Lanes;
            UpdateFreeScroll(); // modo libre: ajusta la primera línea visible
            if (FreeScroll && !Justified) PlaceFreeFixedSlots();
            int curRow = FreeScroll ? freeFirst : idx >= 0 ? idx / cols : 0;
            if (Justified) { if (sideStrip) LayoutJustified(); else LayoutJustifiedRow(); }
            foreach (var s in slots)
            {
                int i = Justified ? s.Index : (curRow + s.Row) * cols + s.Col;
                s.Index = i;
                if (idx < 0 || i < 0 || i >= view.Count)
                {
                    s.Item = null;
                    s.Root.Visibility = Visibility.Hidden;
                    continue;
                }
                var it = view[i];
                s.Item = it;
                s.Root.Visibility = Visibility.Visible;
                bool cur = i == idx;
                bool cmp = (CompareShowsNext && i == idx + 1) || (CompareShowsPrev && i == idx - 1);
                s.CurFrame.Visibility = cur || cmp ? Visibility.Visible : Visibility.Collapsed;
                s.CurFrame.BorderBrush = cur ? FocusBrush : CompareBrush;
                ApplyFocusStyle(s.CurFrame, s.CurDash, cur);
                s.CurFrame.BorderThickness = new Thickness(cur ? 3 : 2);
                // Actual y favorita: el azul queda por fuera y el verde pasa al borde de la foto (mismo grosor)
                s.FavFrame.Margin = new Thickness(cur || cmp ? 0 : -6);
                s.FavFrame.CornerRadius = new CornerRadius(cur || cmp ? 2 : 6);
                s.Root.ToolTip = it.Name;
                // La etiqueta de la hora solo si la miniatura tiene sitio para ella
                bool moment = IsMomentStart(i) && s.Root.Width - 2 * RingPad >= 62;
                s.GapMark.Visibility = moment ? Visibility.Visible : Visibility.Collapsed;
                if (moment) s.GapText.Text = (settings.Orden == 0 ? it.SortDate : it.Modified).ToString("HH:mm");
                // Ráfaga: «2/5» abajo a la derecha (si hay sitio también para la hora de la izquierda)
                bool burst = it.Burst != null && s.Root.Width - 2 * RingPad >= (moment ? 120 : 56);
                s.BurstMark.Visibility = burst ? Visibility.Visible : Visibility.Collapsed;
                if (burst) s.BurstText.Text = (it.BurstPos + 1) + "/" + it.Burst.Items.Count;
                SetSlotImage(s);
                SetSlotMark(s);
            }
            UpdateScrollbar();
            RequestThumbs();
        }

        // Nuevo «momento»: entre esta foto y la anterior pasaron más de MomentSeconds (solo al ordenar por fecha).
        bool IsMomentStart(int i)
        {
            if (!settings.SepararMomentos || i <= 0 || i >= view.Count || settings.Orden > 1) return false;
            DateTime a = settings.Orden == 0 ? view[i - 1].SortDate : view[i - 1].Modified;
            DateTime b = settings.Orden == 0 ? view[i].SortDate : view[i].Modified;
            return Math.Abs((b - a).TotalSeconds) > MomentSeconds;
        }

        void SetSlotImage(Slot s)
        {
            BitmapSource b;
            bool has = thumbs.TryGetValue(s.Item.Path, out b);
            s.Img.Source = b != null && s.Item.Mark == Mark.Reject ? Grayscale(b) : b;
            s.ErrText.Visibility = has && b == null ? Visibility.Visible : Visibility.Collapsed;
        }

        // No deseadas en la tira: en blanco y negro y atenuadas, para que «desaparezcan» al mirar de reojo
        // (como las rechazadas de Lightroom). La versión gris se crea una vez por miniatura y se libera con ella.
        const double RejectThumbOpacity = 0.3;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, BitmapSource> grayThumbs =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, BitmapSource>();

        static BitmapSource Grayscale(BitmapSource b)
        {
            return grayThumbs.GetValue(b, src =>
            {
                try
                {
                    var g = new CachedBitmap(new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0),
                                             BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    g.Freeze();
                    return g;
                }
                catch { return src; }
            });
        }

        void SetSlotMark(Slot s)
        {
            var m = s.Item.Mark;
            s.FavFrame.Visibility = m == Mark.Fav ? Visibility.Visible : Visibility.Collapsed;
            if (m == Mark.None)
            {
                s.Badge.Visibility = Visibility.Collapsed;
                s.Img.Opacity = 1;
            }
            else
            {
                SetMarkIcon(s.Badge, m);
                s.Img.Opacity = m == Mark.Reject ? RejectThumbOpacity : 1;
            }
            s.GapMark.Opacity = m == Mark.Reject ? 0.5 : 1; // la hora naranja tampoco debe llamar la atención
            s.BurstMark.Opacity = m == Mark.Reject ? 0.5 : 1;
            if (s.Item != null) SetSlotImage(s); // color o blanco y negro según la marca
            UpdateHoverMarks(s); // botones ✓ / ✕ si el ratón está encima
        }

        void RequestThumbs()
        {
            var want = new List<PhotoItem>();
            // Modo libre: primero las que se ven (pueden estar lejos de la actual)
            if (FreeScroll) want.AddRange(VisibleStripItems());
            if (idx >= 0)
            {
                int k = Math.Min(slots.Count / 2 + 25, ThumbCacheLimit / 2 - 2); // las visibles y algunas más para avanzar rápido
                for (int d = 0; d <= k; d++)
                {
                    int a = idx + d * lastDir, b = idx - d * lastDir;
                    if (a >= 0 && a < view.Count) want.Add(view[a]);
                    if (d > 0 && b >= 0 && b < view.Count) want.Add(view[b]);
                }
            }
            thumbQueue.Request(want);
        }

        void OnThumbReady(PhotoItem it)
        {
            foreach (var s in slots)
            {
                if (s.Item != it) continue;
                SetSlotImage(s);
                if (Justified && s.Root.Height > 2 * RingPad && Math.Abs((s.Root.Width - 2 * RingPad) / (s.Root.Height - 2 * RingPad) - Aspect(it)) > 0.02) RequestRelayout();
            }
            if (it == Current && mainImage.Source == null)
            {
                BitmapSource b;
                if (thumbs.TryGetValue(it.Path, out b) && b != null) mainImage.Source = b;
                UpdateMarkBadge();
            }
            if ((it == prevCmp.Item && prevCmp.Img.Source == null) || (it == nextCmp.Item && nextCmp.Img.Source == null)) UpdateCompare();
            if (thumbs.Count > ThumbCacheLimit) TrimThumbs();
        }

        // Cuántas miniaturas se guardan en memoria: menos cuanto más grandes son (unos 350 MB como máximo)
        int ThumbCacheLimit
        {
            get
            {
                double bytes = Math.Max(1, (double)thumbDim * thumbDim * 0.67 * 4);
                return Math.Max(40, Math.Min(ThumbCacheMax, (int)(350e6 / bytes)));
            }
        }

        void TrimThumbs()
        {
            var keep = new HashSet<string>();
            int half = ThumbCacheLimit / 2;
            for (int i = Math.Max(0, idx - half); i < Math.Min(view.Count, idx + half); i++) keep.Add(view[i].Path);
            foreach (var it in VisibleStripItems()) keep.Add(it.Path); // las que se ven (modo libre)
            foreach (var key in thumbs.Keys.ToList())
            {
                BitmapSource tmp;
                if (!keep.Contains(key)) thumbs.TryRemove(key, out tmp);
            }
        }


        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (FreeScroll && view.Count > 0) { FreeWheel(e); e.Handled = true; return; } // solo mueve la vista
            if (gridMode && view.Count > 0 && idx >= 0)
            {
                // Cuadrícula: una fila por paso, seleccionando la foto del centro de la fila
                int row = idx / Cols + (e.Delta > 0 ? -1 : 1);
                int rows = (view.Count + Cols - 1) / Cols;
                row = Math.Max(0, Math.Min(rows - 1, row));
                GoTo(MiddleOfRow(row * Cols));
            }
            else Go(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        }


        // ---------------------------------------------------------------- Altura de miniaturas

        void OnStripResized()
        {
            if (!gridMode)
            {
                if (sideStrip) settings.AnchoMiniaturas = (int)stripSize;
                else settings.AlturaMiniaturas = (int)stripSize;
                SaveSettings();
            }
            int newDim = ThumbDimFor();
            if (newDim > thumbDim * 1.15 || newDim < thumbDim * 0.6)
            {
                // Recargar miniaturas al nuevo tamaño para que no se vean borrosas.
                bool levelChanged = CacheLevel(newDim) != CacheLevel(thumbDim);
                thumbDim = newDim;
                if (levelChanged && thumbCacheDir != null) StartPregen();
                thumbs.Clear();
                UpdateStrip();
            }
        }
    }
}
