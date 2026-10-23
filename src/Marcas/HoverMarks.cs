using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SelectorDeFotos
{
    // Marcar con el ratón en las miniaturas (tira lateral, tira inferior y cuadrícula):
    // al pasar el ratón por una foto aparecen dos botones redondos sobre ella.
    //  · Arriba a la derecha, ✓: favorita (otro clic la quita).
    //  · Arriba a la izquierda, ✕: no deseada (otro clic la quita).
    // El botón se ve relleno de color si la foto ya tiene esa marca. Marcar así no cambia la foto seleccionada,
    // no cuenta como clic en la miniatura y se deshace con Ctrl + Z como cualquier marca.
    public partial class MainWindow
    {
        static readonly Brush HoverBtnBg = B("#A6000000"), HoverBtnBgOver = B("#E0101010");
        static readonly Brush HoverIconIdle = B("#E6FFFFFF");
        Slot hoverForced; // solo para las pruebas automáticas (simula el ratón encima)

        void AttachHoverMarks(Slot s, Grid host)
        {
            // Capa del tamaño de la foto: solo los botones reciben el ratón (fondo nulo = transparente al clic)
            var box = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            box.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = s.Img });
            box.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = s.Img });
            s.HoverFav = MakeHoverButton(CheckGeometry, HorizontalAlignment.Right);
            s.HoverRej = MakeHoverButton(CrossGeometry, HorizontalAlignment.Left);
            box.Children.Add(s.HoverFav);
            box.Children.Add(s.HoverRej);
            s.HoverBox = box;
            host.Children.Add(box);

            WireHoverButton(s, s.HoverFav, Mark.Fav);
            WireHoverButton(s, s.HoverRej, Mark.Reject);
            s.Root.MouseEnter += (a, e) => UpdateHoverMarks(s);
            s.Root.MouseLeave += (a, e) => UpdateHoverMarks(s);
        }

        static Border MakeHoverButton(Geometry icon, HorizontalAlignment side)
        {
            var path = new Path
            {
                Data = icon, Stretch = Stretch.Uniform, Fill = HoverIconIdle,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Border
            {
                Background = HoverBtnBg, BorderBrush = B("#59FFFFFF"), BorderThickness = new Thickness(1),
                HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6), Child = path, Cursor = Cursors.Hand
            };
            SizeHoverButton(b, 28);
            return b;
        }

        // Tamaño del botón y de su icono en píxeles enteros: el hueco a cada lado del icono queda igual
        // (con márgenes fraccionarios el redondeo desplazaba la ✕ medio píxel hacia un lado)
        static void SizeHoverButton(Border b, double size)
        {
            b.Width = b.Height = size;
            b.CornerRadius = new CornerRadius(size / 2);
            double pad = Math.Round(size * 0.22);
            double inner = size - 2 * b.BorderThickness.Left - 2 * pad; // mismo par/impar que el botón: centrado exacto
            var path = (Path)b.Child;
            path.Width = path.Height = inner;
            path.Margin = new Thickness(0);
        }

        void WireHoverButton(Slot s, Border btn, Mark mark)
        {
            btn.MouseEnter += (a, e) => PaintHoverButton(s.Item, btn, mark, true);
            btn.MouseLeave += (a, e) => PaintHoverButton(s.Item, btn, mark, false);
            // Se marca al pulsar (respuesta inmediata) y se «consume» el clic para que la miniatura no lo reciba
            btn.MouseLeftButtonDown += (a, e) =>
            {
                e.Handled = true;
                var it = s.Item;
                if (it == null) return;
                var m = it.Mark == mark ? Mark.None : mark;
                var old = it.Mark;
                ApplyMark(it, m, true);
                // Sin cambiar el foco: si la tira se moviera, el botón ya no quedaría bajo el ratón para desmarcar
                if (it == Current) { if (m == Mark.None) AnimateMark(old, true); else AnimateMark(m); }
                if (s.Item == it) PaintHoverButton(it, btn, mark, true);
            };
            btn.MouseLeftButtonUp += (a, e) => e.Handled = true;
            btn.MouseRightButtonUp += (a, e) => e.Handled = true;
        }

        // Muestra u oculta los botones según el ratón, el tamaño de la miniatura y la marca de la foto
        void UpdateHoverMarks(Slot s)
        {
            if (s.HoverBox == null) return;
            double w = s.Img.ActualWidth, h = s.Img.ActualHeight;
            bool show = s.Item != null && (s.Root.IsMouseOver || s == hoverForced) && w >= 64 && h >= 40;
            s.HoverBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // Mientras se ven los botones, el icono de la marca de la esquina se oculta (el botón ya la indica)
            if (s.Item != null) s.Badge.Visibility = show || s.Item.Mark == Mark.None ? Visibility.Collapsed : Visibility.Visible;
            if (!show) return;
            // Tamaño según la miniatura: más grandes en la cuadrícula
            double size = Math.Min(w, h) >= 160 ? 32 : Math.Min(w, h) >= 90 ? 28 : 24;
            SizeHoverButton(s.HoverFav, size);
            SizeHoverButton(s.HoverRej, size);
            PaintHoverButton(s.Item, s.HoverFav, Mark.Fav, s.HoverFav.IsMouseOver);
            PaintHoverButton(s.Item, s.HoverRej, Mark.Reject, s.HoverRej.IsMouseOver);
        }

        void PaintHoverButton(PhotoItem it, Border btn, Mark mark, bool over)
        {
            if (it == null) return;
            bool active = it.Mark == mark;
            var color = mark == Mark.Fav ? CheckBrush : RejBrush;
            var path = (Path)btn.Child;
            // Activo: relleno con el color de la marca. Encima del botón: se adelanta el color (o se atenúa si quitará la marca)
            path.Fill = active ? color : over ? color : HoverIconIdle;
            path.Opacity = active && over ? 0.6 : 1;
            btn.Background = over ? HoverBtnBgOver : HoverBtnBg;
            // Solo el botón de la marca activa se ve pleno; los demás quedan atenuados (también sin marca)
            // y recuperan su opacidad con el ratón encima
            btn.Opacity = !active && !over ? 0.45 : 1;
            btn.BorderBrush = active ? color : over ? B("#99FFFFFF") : B("#59FFFFFF");
            btn.ToolTip = mark == Mark.Fav ? (active ? "Quitar de favoritas" : "Marcar como favorita")
                                           : (active ? "Quitar «no deseada»" : "Marcar como no deseada");
        }
    }
}
