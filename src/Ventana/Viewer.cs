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
    // Visor: foto actual, iconos y animación de la marca, zoom, lupa y pantalla completa.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Visor

        void ShowCurrent()
        {
            var it = Current;
            if (it == null)
            {
                mainImage.Source = null;
                UpdateMarkBadge();
                if (string.IsNullOrEmpty(settings.CarpetaFotos))
                    emptyText.Text = "Elige una carpeta de fotos para empezar\n(botón «Carpeta de fotos…» arriba a la izquierda)";
                else if (all.Count == 0)
                    emptyText.Text = "No hay fotos en esta carpeta";
                else
                    emptyText.Text = "Ninguna foto coincide con el filtro «Mostrar»";
                emptyText.Visibility = Visibility.Visible;
                favPosText.Visibility = Visibility.Collapsed;
                SetStatusParts(all.Count > 0 ? new List<string[]> { new[] { "0 / 0", null } } : new List<string[]>());
                UpdateStrip();
                return;
            }

            if (it != lastShown)
            {
                lastShown = it;
                hiResItem = null;
                loupeActive = false; // al cambiar de foto la lupa se suelta (vuelve con Z)
                ResetZoom();
                // Si «Mostrar» deja ver un solo tipo (p. ej. solo favoritas), todas tienen la misma marca:
                // animarla en cada foto no aporta nada. Al marcar con ↑ / ↓ sí se sigue animando.
                int kinds = (settings.VerFavoritas ? 1 : 0) + (settings.VerSinMarca ? 1 : 0) + (settings.VerNoDeseadas ? 1 : 0);
                if (kinds > 1) AnimateMark(it.Mark);
            }
            else if (hiResItem == it)
            {
                // ya se muestra en resolución completa
                UpdateMarkBadge();
                UpdateStatus();
                UpdateStrip();
                return;
            }

            BitmapSource b;
            bool loaded = fulls.TryGetValue(it.Path, out b);
            if (loaded && b != null)
            {
                mainImage.Source = b;
                emptyText.Visibility = Visibility.Collapsed;
            }
            else if (loaded)
            {
                mainImage.Source = null;
                emptyText.Text = "No se pudo abrir esta imagen\n" + it.Name;
                emptyText.Visibility = Visibility.Visible;
            }
            else
            {
                BitmapSource t;
                mainImage.Source = thumbs.TryGetValue(it.Path, out t) ? t : null;
                emptyText.Visibility = Visibility.Collapsed;
            }

            UpdateMarkBadge();
            UpdateStatus();
            UpdateStrip();
            RequestFulls();
            UpdatePregenCenter();

            if (folderData != null && folderData.ultima != it.Name)
            {
                folderData.ultima = it.Name;
                ScheduleSave();
            }
        }

        void RequestFulls()
        {
            var want = new List<PhotoItem>();
            foreach (int d in new[] { 0, lastDir, -lastDir, 2 * lastDir })
            {
                int i = idx + d;
                if (i >= 0 && i < view.Count) want.Add(view[i]);
            }
            var keep = new HashSet<string>(want.Select(x => x.Path));
            foreach (var key in fulls.Keys.ToList())
            {
                BitmapSource tmp;
                if (!keep.Contains(key)) fulls.TryRemove(key, out tmp);
            }
            fullQueue.Request(want);
        }

        void OnFullReady(PhotoItem it)
        {
            if (it == prevCmp.Item || it == nextCmp.Item) UpdateCompare();
            if (it == Current)
            {
                ShowCurrentImageOnly(it);
                UpdateMarkBadge();
                UpdateStatus(); // ya se conocen las dimensiones
            }
        }

        void ShowCurrentImageOnly(PhotoItem it)
        {
            BitmapSource b;
            if (hiResItem == it || !fulls.TryGetValue(it.Path, out b)) return;
            if (b != null)
            {
                mainImage.Source = b;
                emptyText.Visibility = Visibility.Collapsed;
            }
            else
            {
                mainImage.Source = null;
                emptyText.Text = "No se pudo abrir esta imagen\n" + it.Name;
                emptyText.Visibility = Visibility.Visible;
            }
        }

        void UpdateMarkBadge()
        {
            var it = Current;
            // La barra inferior indica la marca con color intenso
            statusBar.Background = it == null || it.Mark == Mark.None ? PanelBrush
                : it.Mark == Mark.Fav ? StatusFavBrush : RejBrush;
            // Sobre el verde vivo el texto blanco no se lee bien: se usa texto oscuro.
            var statusFg = it != null && it.Mark == Mark.Fav ? StatusDarkText : Brushes.White;
            statusFgBrush = statusFg;
            foreach (var tb in statusParts.Children.OfType<TextBlock>()) tb.Foreground = statusFg;
            countsText.Foreground = statusFg;
            favPosText.Foreground = statusFg;
            SetMarkIcon(markBadge, it == null || !settings.MostrarIconoMarca ? Mark.None : it.Mark);
            SetMosaic(mainMosaic, it == null ? Mark.None : it.Mark);
            SyncBurstView();
            UpdateCompare();
            PaintAllMarkBars();
            MarkBarIntro();
            UpdateInfoPanel();
        }

        // ---------------------------------------------------------------- Iconos de marca
        // Favorita: check verde. No deseada: cruz roja. Ambas con un contorno fino del color del fondo
        // para que se separen de la foto.
        // Tamaños del check y de la cruz. En el visor el check es más grande para que se vea bien.
        const double MainCheckSize = 64, MainCrossSize = 56, ThumbCheckSize = 22, ThumbCrossSize = 18;
        static readonly Brush CheckBrush = B("#00E64D"); // verde bien vivo
        static readonly Geometry CheckGeometry = MakeCheckGeometry();
        static readonly Geometry CrossGeometry = MakeCrossGeometry();

        static System.Windows.Shapes.Path MakeMarkIcon(double starSize, double crossSize, Brush outline, Thickness margin)
        {
            return new System.Windows.Shapes.Path
            {
                Width = starSize, Height = starSize, Stretch = Stretch.Uniform, Tag = Tuple.Create(starSize, crossSize, margin),
                Stroke = outline, StrokeThickness = starSize >= 30 ? 3 : 2, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = margin, Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
        }

        static void SetMarkIcon(System.Windows.Shapes.Path icon, Mark m)
        {
            if (m == Mark.None) { icon.Visibility = Visibility.Collapsed; return; }
            icon.Visibility = Visibility.Visible;
            icon.Data = m == Mark.Fav ? CheckGeometry : CrossGeometry;
            // Tamaño según la marca; las dos quedan centradas en el mismo punto de la esquina
            var bas = (Tuple<double, double, Thickness>)icon.Tag;
            double d = m == Mark.Fav ? bas.Item1 : bas.Item2, off = (bas.Item1 - d) / 2;
            icon.Width = icon.Height = d;
            icon.Margin = new Thickness(0, bas.Item3.Top + off, bas.Item3.Right + off, 0);
            icon.Fill = m == Mark.Fav ? CheckBrush : RejBrush;
        }

        // Opción «Animar la marca»: al llegar a una foto marcada, su icono grande aparece en el centro
        // y crece mientras se desvanece (medio segundo). No bloquea nada: es solo una capa encima.
        System.Windows.Shapes.Path markAnim;

        // removed: se quitó la marca m. Se muestra solo su contorno (vacío) y se encoge en vez de crecer.
        // target: la foto marcada (por defecto la actual). En la vista de ráfaga la animación se ve sobre su foto,
        // más pequeña según el tamaño del recuadro.
        void AnimateMark(Mark m, bool removed = false, PhotoItem target = null, bool retry = true)
        {
            if (!settings.AnimarMarca || m == Mark.None || gridMode || mainPane == null) return;
            System.Windows.Shapes.Path anim;
            double scale = 1;
            var who = target ?? Current;
            // Al llegar a otra ráfaga, la vista se arma justo después de mostrar la foto: si su recuadro todavía
            // no existe o no tiene tamaño, se reintenta una vez cuando termina el diseño
            bool burstNext = settings.ModoRafaga && who != null && who.Burst != null && who.Burst != openBurst;
            var tile = burstMode ? burstTileList.FirstOrDefault(x => x.Item == who) : null;
            if (retry && (burstNext || (burstMode && (tile == null || tile.Root.ActualWidth <= 0))))
            {
                Dispatcher.BeginInvoke(new Action(() => { if (Current == who || target != null) AnimateMark(m, removed, who, false); }),
                                       DispatcherPriority.Loaded);
                return;
            }
            if (burstMode)
            {
                var t = tile;
                if (t == null) return;
                if (t.Anim == null)
                {
                    t.Anim = NewMarkAnimPath();
                    t.Root.Children.Add(t.Anim);
                }
                anim = t.Anim;
                scale = Math.Max(0.35, Math.Min(1, Math.Min(t.Root.ActualWidth, t.Root.ActualHeight) / 420));
            }
            else
            {
                // Comparar: si la foto marcada es la de un panel (anterior / siguiente), la animación va en ese panel
                ComparePane pane = null;
                if (target != null && target != Current)
                {
                    foreach (var p in new[] { prevCmp, nextCmp })
                        if (p.Pane.IsVisible && p.Item == target) pane = p;
                    if (pane == null) return; // no se ve: nada que animar
                }
                if (pane != null)
                {
                    if (pane.Anim == null)
                    {
                        pane.Anim = NewMarkAnimPath();
                        pane.Pane.Children.Add(pane.Anim);
                    }
                    anim = pane.Anim;
                    scale = Math.Max(0.5, Math.Min(1, Math.Min(pane.Pane.ActualWidth, pane.Pane.ActualHeight) / 500));
                }
                else
                {
                    if (markAnim == null)
                    {
                        markAnim = NewMarkAnimPath();
                        mainPane.Children.Add(markAnim);
                    }
                    anim = markAnim;
                }
            }
            RunMarkAnimation(anim, m, removed, scale);
        }

        static System.Windows.Shapes.Path NewMarkAnimPath()
        {
            var p = new System.Windows.Shapes.Path
            {
                Stretch = Stretch.Uniform, Stroke = BgBrush, StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
                IsHitTestVisible = false, Opacity = 0
            };
            Panel.SetZIndex(p, 20);
            return p;
        }

        static void RunMarkAnimation(System.Windows.Shapes.Path markAnim, Mark m, bool removed, double scale)
        {
            bool fav = m == Mark.Fav;
            markAnim.Data = fav ? CheckGeometry : CrossGeometry;
            var color = fav ? CheckBrush : RejBrush;
            markAnim.Fill = removed ? Brushes.Transparent : color;
            markAnim.Stroke = BgBrush; // al quitar la marca, el contorno vacío también va en el color del fondo
            markAnim.StrokeThickness = (removed ? 4 : 5) * Math.Max(0.6, scale);
            markAnim.Width = markAnim.Height = (fav ? 160 : 120) * scale; // misma proporción que los iconos de la esquina
            var dur = new Duration(TimeSpan.FromMilliseconds(550));
            var ease = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0.95, 0, dur) { EasingFunction = ease };
            // Al marcar el icono crece mientras se desvanece; al quitar la marca, el contorno vacío se encoge
            // Marcar tiene algo más de impacto (crece de 0,7× a 1,2×) que quitar la marca (se encoge de 1× a 0,65×)
            var shrink = removed
                ? new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.65, dur) { EasingFunction = ease }
                : new System.Windows.Media.Animation.DoubleAnimation(0.7, 1.2, dur) { EasingFunction = ease };
            var st = (ScaleTransform)markAnim.RenderTransform;
            markAnim.BeginAnimation(OpacityProperty, fade);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }

        // Check: un solo polígono de 6 vértices (trazo grueso A → J → B) con la esquina inferior en inglete exacto.
        static Geometry MakeCheckGeometry()
        {
            double t = 3.1; // medio grosor
            var a = new Point(2, 12.5); var j = new Point(9, 19.5); var b = new Point(22, 5);
            var d1 = j - a; d1.Normalize();
            var d2 = b - j; d2.Normalize();
            var n1 = new Vector(-d1.Y, d1.X) * t; // lado de abajo / fuera de cada trazo
            var n2 = new Vector(-d2.Y, d2.X) * t;
            var outer = Intersect(a + n1, d1, j + n2, d2);
            var inner = Intersect(a - n1, d1, j - n2, d2);
            return Polygon(new List<Point> { a + n1, outer, b + n2, b - n2, inner, a - n1 });
        }

        // Punto donde se cruzan la recta p1 + s·d1 y la recta p2 + u·d2
        static Point Intersect(Point p1, Vector d1, Point p2, Vector d2)
        {
            double s = Vector.CrossProduct(p2 - p1, d2) / Vector.CrossProduct(d1, d2);
            return p1 + d1 * s;
        }

        // Cruz en X: un «+» de brazos gruesos girado 45°
        static Geometry MakeCrossGeometry()
        {
            double w = 3.4, l = 11; // medio grosor y largo de cada brazo
            var plus = new[]
            {
                new Point(-w, -l), new Point(w, -l), new Point(w, -w), new Point(l, -w), new Point(l, w), new Point(w, w),
                new Point(w, l), new Point(-w, l), new Point(-w, w), new Point(-l, w), new Point(-l, -w), new Point(-w, -w)
            };
            double c = Math.Cos(Math.PI / 4), sn = Math.Sin(Math.PI / 4);
            return Polygon(plus.Select(p => new Point(12 + p.X * c - p.Y * sn, 12 + p.X * sn + p.Y * c)).ToList());
        }

        static Geometry Polygon(List<Point> pts)
        {
            var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
            fig.Segments.Add(new PolyLineSegment(pts.Skip(1), true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        TextBlock MakePaneName()
        {
            return new TextBlock
            {
                Foreground = Brushes.White, Background = B("#A0000000"), Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(14, 18, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
        }


        // ---------------------------------------------------------------- Zoom

        bool IsZoomed { get { return zoom.Matrix.M11 > 1.0001; } }

        void OnImageWheel(object sender, MouseWheelEventArgs e)
        {
            // El pellizco del touchpad llega como Ctrl + rueda: aquí siempre es zoom de la foto.
            ZoomAt(ZoomPoint(e), e.Delta > 0 ? 1.25 : 0.8);
            e.Handled = true;
        }

        void ZoomCenter(double factor)
        {
            ZoomAt(mainPane.TranslatePoint(new Point(mainPane.ActualWidth / 2, mainPane.ActualHeight / 2), mainImage), factor);
        }

        // Con comparar activo, el zoom de las dos fotos va sincronizado: se usa el punto de la foto bajo el ratón.
        Point ZoomPoint(MouseEventArgs e)
        {
            foreach (var p in new[] { prevCmp, nextCmp })
                if (p.Pane.IsVisible && p.Pane.IsMouseOver && p.Img.ActualWidth > 0) return e.GetPosition(p.Img);
            return e.GetPosition(mainImage);
        }

        void ZoomAt(Point p, double factor)
        {
            if (Current == null || mainImage.Source == null || mainImage.ActualWidth <= 0) return;
            var m = zoom.Matrix;
            double s = m.M11;
            double ns = Math.Max(1, Math.Min(40, s * factor));
            if (ns <= 1.0001) { ResetZoom(); return; }
            m.ScaleAtPrepend(ns / s, ns / s, p.X, p.Y);
            zoom.Matrix = m;
            ClampPan();
            UpdateZoomUi();
            EnsureHiRes();
            HideDisallowedMarkBars(); // con la lupa la barra de marcar no se muestra
        }

        // Evita que la foto se aleje del recuadro donde se muestra.
        void ClampPan()
        {
            double w = mainImage.ActualWidth, h = mainImage.ActualHeight;
            var m = zoom.Matrix;
            var r = new Rect(0, 0, w, h);
            r.Transform(m);
            double dx = 0, dy = 0;
            if (r.Left > 0) dx = -r.Left; else if (r.Right < w) dx = w - r.Right;
            if (r.Top > 0) dy = -r.Top; else if (r.Bottom < h) dy = h - r.Bottom;
            m.Translate(dx, dy);
            zoom.Matrix = m;
        }

        void ResetZoom()
        {
            if (zoom == null) return;
            zoom.Matrix = Matrix.Identity;
            dragging = false;
            if (center != null && center.IsMouseCaptured) center.ReleaseMouseCapture();
            UpdateZoomUi();
        }

        void UpdateZoomUi()
        {
            if (zoomText == null) return;
            if (!IsZoomed)
            {
                zoomText.Visibility = Visibility.Collapsed;
                center.Cursor = null;
                return;
            }
            var it = Current;
            double s = zoom.Matrix.M11;
            string pct = (s * 100).ToString("N0") + "% del tamaño en pantalla";
            if (it != null && it.Width > 0)
            {
                bool rot = it.Orientation >= 5 && it.Orientation <= 8;
                double origW = rot ? it.Height : it.Width;
                pct = (s * mainImage.ActualWidth * dpiScale / origW * 100).ToString("N0") + "%";
            }
            zoomText.Text = loupeActive
                ? "Lupa " + pct + (HiResPending ? "   ·   cargando alta resolución…" : "") + "   ·   mueve el ratón   ·   suelta para volver"
                : "Zoom " + pct + (HiResPending ? "   ·   cargando alta resolución…" : "") + "   ·   arrastra para mover   ·   doble clic o Esc para restaurar";
            zoomText.Visibility = Visibility.Visible;
            center.Cursor = Cursors.SizeAll;
        }

        void OnImageMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (IsZoomed) ResetZoom();
                else
                {
                    // Doble clic: tamaño real (100%), o x2 si la foto es pequeña.
                    double target = 2;
                    var it = Current;
                    if (it != null && it.Width > 0 && mainImage.ActualWidth > 0)
                    {
                        bool rot = it.Orientation >= 5 && it.Orientation <= 8;
                        double natural = (rot ? it.Height : it.Width) / (mainImage.ActualWidth * dpiScale);
                        if (natural >= 1.5) target = natural;
                    }
                    ZoomAt(ZoomPoint(e), target);
                }
                e.Handled = true;
                return;
            }
            if (IsZoomed)
            {
                dragging = true;
                dragStart = e.GetPosition(center);
                center.CaptureMouse();
                HideDisallowedMarkBars(); // mientras se arrastra la foto, la barra de marcar no estorba
                e.Handled = true;
            }
        }

        // ---------------------------------------------------------------- Lupa (mantener Z)
        // Muestra la foto al 100 % (tamaño real) alrededor del ratón y la sigue mientras se mueve; al soltar Z
        // vuelve el zoom que había. Carga la resolución completa como el zoom normal.
        bool loupeActive;
        Matrix loupeSaved;

        void StartLoupe()
        {
            if (loupeActive || gridMode || burstMode || Current == null || mainImage.Source == null || mainImage.ActualWidth <= 0) return;
            loupeSaved = zoom.Matrix;
            loupeActive = true;
            HideDisallowedMarkBars();
            ApplyLoupe(Mouse.GetPosition(mainPane));
        }

        void EndLoupe()
        {
            if (!loupeActive) return;
            loupeActive = false;
            zoom.Matrix = loupeSaved;
            ClampPan();
            UpdateZoomUi();
            RefreshMarkBarsAtMouse();
        }

        // p: punto del ratón en el panel; se amplía de modo que el punto de la foto que había ahí quede bajo el ratón
        void ApplyLoupe(Point p)
        {
            var it = Current;
            if (it == null || mainImage.ActualWidth <= 0) return;
            double target = 2;
            if (it.Width > 0)
            {
                bool rot = it.Orientation >= 5 && it.Orientation <= 8;
                target = Math.Max(1.5, (rot ? it.Height : it.Width) / (mainImage.ActualWidth * dpiScale));
            }
            // Si el ratón no está sobre la foto, el centro
            if (p.X < 0 || p.Y < 0 || p.X > mainPane.ActualWidth || p.Y > mainPane.ActualHeight)
                p = new Point(mainPane.ActualWidth / 2, mainPane.ActualHeight / 2);
            var off = VisualTreeHelper.GetOffset(mainImage); // posición de la foto sin zoom
            var q = new Point(Math.Max(0, Math.Min(mainImage.ActualWidth, p.X - off.X)), Math.Max(0, Math.Min(mainImage.ActualHeight, p.Y - off.Y)));
            var m = Matrix.Identity;
            m.ScaleAtPrepend(target, target, q.X, q.Y);
            zoom.Matrix = m;
            ClampPan();
            UpdateZoomUi();
            EnsureHiRes();
        }

        void OnImageMouseMove(object sender, MouseEventArgs e)
        {
            if (loupeActive) { ApplyLoupe(e.GetPosition(mainPane)); return; }
            if (!dragging) return;
            var p = e.GetPosition(center);
            var m = zoom.Matrix;
            m.Translate(p.X - dragStart.X, p.Y - dragStart.Y);
            zoom.Matrix = m;
            dragStart = p;
            ClampPan();
        }

        void OnImageMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            center.ReleaseMouseCapture();
            RefreshMarkBarsAtMouse();
        }

        // Al hacer zoom se carga la foto a resolución completa (la normal está reducida al tamaño de pantalla).
        // También para las fotos de los paneles de comparar, así con zoom todas se ven nítidas.
        void EnsureHiRes()
        {
            var it = Current;
            if (it != null && hiResItem != it && hiResLoading != it && NeedsHiRes(it))
            {
                hiResLoading = it;
                LoadHiRes(it, b =>
                {
                    if (hiResLoading == it) hiResLoading = null;
                    if (b != null && Current == it)
                    {
                        mainImage.Source = b;
                        hiResItem = it;
                    }
                });
            }
            foreach (var p in new[] { prevCmp, nextCmp })
            {
                var pane = p;
                var other = pane.Item;
                if (!pane.Pane.IsVisible || other == null || pane.HiResItem == other || pane.HiResLoading == other || !NeedsHiRes(other)) continue;
                pane.HiResLoading = other;
                LoadHiRes(other, b =>
                {
                    if (pane.HiResLoading == other) pane.HiResLoading = null;
                    if (b != null && pane.Item == other)
                    {
                        pane.Img.Source = b;
                        pane.HiResItem = other;
                    }
                });
            }
            UpdateZoomUi();
        }

        bool NeedsHiRes(PhotoItem it)
        {
            return !(it.Width > 0 && Math.Max(it.Width, it.Height) <= fullDim);
        }

        bool HiResPending
        {
            get { return hiResLoading != null || prevCmp.HiResLoading != null || nextCmp.HiResLoading != null; }
        }

        void LoadHiRes(PhotoItem it, Action<BitmapSource> apply)
        {
            var t = new Thread(() =>
            {
                BitmapSource b = null;
                try { b = ImageLoader.Load(it, 0); } catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    apply(b);
                    UpdateZoomUi();
                }));
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }


        // ---------------------------------------------------------------- Pantalla completa

        void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                prevState = WindowState;
                toolbar.Visibility = Visibility.Collapsed;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal; // necesario para cubrir la barra de tareas
                WindowState = WindowState.Maximized;
                fullscreen = true;
            }
            else
            {
                toolbar.Visibility = Visibility.Visible;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = prevState;
                fullscreen = false;
            }
        }
    }
}
