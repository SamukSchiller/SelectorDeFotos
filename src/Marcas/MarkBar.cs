using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Barra para marcar sobre las fotos grandes: [✕ No deseada ↓] [✓ Favorita ↑], abajo al centro de cada foto.
    // Está en el visor principal, en cada panel de comparar y en cada foto de la vista de ráfaga (en las
    // miniaturas se usan los botones de las esquinas, ver HoverMarks.cs).
    //  · Aparece al instante cuando el ratón llega a la parte de abajo de esa foto; al alejarse se oculta con un
    //    fundido. Así no tapa la foto mientras se mira.
    //  · La marca de esa foto se ve resaltada; otro clic la quita. En la foto actual funciona como ↑ / ↓
    //    (animación, avanzar al marcar, Ctrl + Z); en las demás (comparar, ráfaga) marca sin cambiar el foco.
    //  · En fotos pequeñas (ráfagas con muchas fotos) se muestra compacta: solo los iconos.
    //  · En el visor principal se muestra unos segundos al abrir la app, para que se sepa que existe.
    public partial class MainWindow
    {
        const double MarkBarZone = 90; // alto de la franja de abajo de la foto que hace aparecer la barra

        class MarkBarUi
        {
            public Border Root, Fav, Rej;
            public Func<PhotoItem> Item;
            public Func<bool> Allowed;
            public FrameworkElement Area;  // la foto (o el panel) sobre la que va la barra
            public bool AvoidIndicator;    // subir si se ve el indicador de modo (abajo al centro)
            public bool InBurst;           // barra de una foto de la vista de ráfaga (se quita al rehacer la vista)
            public bool Visible, Compact;
            public DispatcherTimer Hide;
        }

        readonly List<MarkBarUi> markBars = new List<MarkBarUi>();
        MarkBarUi mainMarkBar;
        bool markBarIntroDone;

        void BuildMarkBar()
        {
            // Clic derecho: el menú actúa sobre la foto en la que se hizo clic (comparar, ráfaga)
            center.PreviewMouseRightButtonDown += (s, e) => contextTarget = PhotoUnderMouse();
            // Visor principal (también la foto actual al comparar)
            mainMarkBar = CreateMarkBar(mainPane, mainPane, mainPane, () => Current,
                () => Current != null && !gridMode && !burstMode && !IsZoomed && !loupeActive && !dragging, true);
            // Paneles de comparar (anterior / siguiente)
            foreach (var p in new[] { prevCmp, nextCmp })
            {
                var pane = p;
                CreateMarkBar(pane.Pane, pane.Pane, pane.Pane, () => pane.Item,
                    () => pane.Item != null && pane.Pane.IsVisible && settings.Comparar && !IsZoomed && !dragging, true);
            }
        }

        // host: donde se agrega la barra; area: la foto sobre la que va (y cuya parte de abajo la hace aparecer);
        // mouse: el elemento que recibe el movimiento del ratón
        MarkBarUi CreateMarkBar(Panel host, FrameworkElement area, UIElement mouse, Func<PhotoItem> item, Func<bool> allowed, bool avoidIndicator)
        {
            var bar = new MarkBarUi { Item = item, Allowed = allowed, Area = area, AvoidIndicator = avoidIndicator };
            bar.Rej = MakeMarkBarButton(bar, Mark.Reject, "No deseada", "↓");
            bar.Fav = MakeMarkBarButton(bar, Mark.Fav, "Favorita", "↑");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(bar.Rej);
            sp.Children.Add(bar.Fav);
            bar.Root = new Border
            {
                Child = sp, Background = B("#E6262626"), BorderBrush = B("#4A4A4A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20), Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 12), Opacity = 0, IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(bar.Root, 95);
            host.Children.Add(bar.Root);
            bar.Hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            bar.Hide.Tick += (s, e) => { bar.Hide.Stop(); ShowMarkBar(bar, false); };
            mouse.MouseMove += (s, e) => OnMarkBarMouse(bar, e.GetPosition(area));
            mouse.MouseLeave += (s, e) => { if (bar.Visible) bar.Hide.Start(); };
            markBars.Add(bar);
            return bar;
        }

        Border MakeMarkBarButton(MarkBarUi bar, Mark mark, string text, string key)
        {
            var icon = new Path
            {
                Data = mark == Mark.Fav ? CheckGeometry : CrossGeometry, Stretch = Stretch.Uniform,
                Width = mark == Mark.Fav ? 17 : 13, Height = mark == Mark.Fav ? 17 : 13, Fill = TextBrush,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock { Text = text, Foreground = TextBrush, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new Border
            {
                Background = B("#3A3A3A"), BorderBrush = B("#4A4A4A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, Foreground = DimTextBrush, FontSize = 11.5, FontWeight = FontWeights.SemiBold }
            });
            var b = new Border
            {
                Child = sp, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 7, 12, 7), Margin = new Thickness(2, 0, 2, 0),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Cursor = Cursors.Hand
            };
            b.MouseEnter += (s, e) => PaintMarkBar(bar);
            b.MouseLeave += (s, e) => PaintMarkBar(bar);
            b.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; // que la foto no lo tome como clic (arrastrar, elegir en la ráfaga…)
                var it = bar.Item();
                if (it == null) return;
                MarkPhoto(it, it.Mark == mark ? Mark.None : mark);
                PaintMarkBar(bar);
            };
            b.MouseLeftButtonUp += (s, e) => e.Handled = true;
            return b;
        }

        // Marca una foto: la actual como ↑ / ↓ (animación, avanzar al marcar); otra, sin cambiar el foco
        void MarkPhoto(PhotoItem it, Mark m)
        {
            if (it == null) return;
            if (it == Current) { SetMark(m); return; }
            var old = it.Mark;
            if (old == m) return;
            ApplyMark(it, m, true);
            if (m == Mark.None) AnimateMark(old, true, it); else AnimateMark(m, false, it);
        }

        void OnMarkBarMouse(MarkBarUi bar, Point p)
        {
            double extra = bar.Root.Margin.Bottom > 12 ? bar.Root.Margin.Bottom - 12 : 0; // si va más arriba, la franja crece
            bool inZone = bar.Allowed() && p.X >= 0 && p.X <= bar.Area.ActualWidth
                          && p.Y <= bar.Area.ActualHeight && p.Y >= bar.Area.ActualHeight - MarkBarZone - extra;
            if (inZone) { bar.Hide.Stop(); if (!bar.Visible) ShowMarkBar(bar, true); } // al instante
            else if (bar.Visible && !bar.Hide.IsEnabled) bar.Hide.Start();             // fundido al alejarse
        }

        void ShowMarkBar(MarkBarUi bar, bool show)
        {
            if (show && !bar.Allowed()) show = false;
            if (show == bar.Visible) return;
            bar.Visible = show;
            if (show)
            {
                // Encima del indicador de modo (ráfaga / comparar) si se está viendo
                bool indicator = bar.AvoidIndicator && burstIndicator != null && burstIndicator.Visibility == Visibility.Visible;
                bar.Root.Margin = new Thickness(0, 0, 0, indicator ? 58 : 12);
                // Compacta (solo iconos) si la foto es estrecha
                bool compact = bar.Area.ActualWidth < 330;
                if (compact != bar.Compact) SetMarkBarCompact(bar, compact);
                bar.Root.Visibility = Visibility.Visible;
            }
            PaintMarkBar(bar);
            bar.Root.IsHitTestVisible = show;
            var anim = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 120 : 250));
            if (!show) anim.Completed += (s, e) => { if (!bar.Visible) bar.Root.Visibility = Visibility.Collapsed; };
            bar.Root.BeginAnimation(OpacityProperty, anim);
        }

        void SetMarkBarCompact(MarkBarUi bar, bool compact)
        {
            bar.Compact = compact;
            foreach (var b in new[] { bar.Fav, bar.Rej })
            {
                var sp = (StackPanel)b.Child;
                for (int i = 1; i < sp.Children.Count; i++) sp.Children[i].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                ((FrameworkElement)sp.Children[0]).Margin = new Thickness(0);
                if (!compact) ((FrameworkElement)sp.Children[0]).Margin = new Thickness(0, 0, 8, 0);
                b.Padding = compact ? new Thickness(10, 7, 10, 7) : new Thickness(14, 7, 12, 7);
            }
        }

        // Resalta la marca de esa foto (verde / rojo) y el botón bajo el ratón
        void PaintMarkBar(MarkBarUi bar)
        {
            var it = bar.Item();
            if (it == null || (bar.Visible && !bar.Allowed())) { if (bar.Visible) ShowMarkBar(bar, false); return; }
            foreach (var pair in new[] { Tuple.Create(bar.Fav, Mark.Fav), Tuple.Create(bar.Rej, Mark.Reject) })
            {
                var b = pair.Item1;
                bool active = it.Mark == pair.Item2;
                var color = pair.Item2 == Mark.Fav ? CheckBrush : RejBrush;
                var icon = (Path)((StackPanel)b.Child).Children[0];
                icon.Fill = active ? color : TextBrush;
                b.BorderBrush = active ? color : Brushes.Transparent;
                b.Background = b.IsMouseOver ? B("#3D4A5C") : active ? B(pair.Item2 == Mark.Fav ? "#2600E64D" : "#33D9443A") : Brushes.Transparent;
                bool cur = it == Current;
                b.ToolTip = active ? (pair.Item2 == Mark.Fav ? "Quitar de favoritas" : "Quitar «no deseada»")
                                   : (pair.Item2 == Mark.Fav ? "Marcar como favorita" : "Marcar como no deseada") + (cur ? (pair.Item2 == Mark.Fav ? " (↑)" : " (↓)") : "");
            }
        }

        // Oculta las barras visibles que ya no deben verse (p. ej. al empezar a hacer zoom)
        void HideDisallowedMarkBars()
        {
            foreach (var bar in markBars)
                if (bar.Visible && !bar.Allowed()) { bar.Hide.Stop(); ShowMarkBar(bar, false); }
        }

        void PaintAllMarkBars()
        {
            foreach (var bar in markBars) if (bar.Visible) PaintMarkBar(bar);
        }

        // Al abrir la app: la barra del visor se muestra unos segundos para que se sepa que existe
        void MarkBarIntro()
        {
            if (markBarIntroDone || mainMarkBar == null || Autotest || Current == null) return;
            markBarIntroDone = true;
            ShowMarkBar(mainMarkBar, true);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var p = Mouse.GetPosition(mainPane);
                if (!(mainPane.IsMouseOver && p.Y >= mainPane.ActualHeight - MarkBarZone)) ShowMarkBar(mainMarkBar, false);
            };
            t.Start();
        }

        // ---------------------------------------------------------------- Clic derecho: la foto bajo el ratón

        PhotoItem contextTarget; // foto sobre la que se abrió el menú (en comparar o en la ráfaga puede no ser la actual)

        PhotoItem PhotoUnderMouse()
        {
            if (burstMode)
            {
                var t = burstTileList.FirstOrDefault(x => x.Root.IsMouseOver);
                if (t != null) return t.Item;
            }
            else if (settings.Comparar)
            {
                foreach (var p in new[] { prevCmp, nextCmp })
                    if (p.Pane.IsVisible && p.Pane.IsMouseOver && p.Item != null) return p.Item;
            }
            return Current;
        }
    }
}
