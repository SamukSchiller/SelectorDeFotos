using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SelectorDeFotos
{
    // Mosaico de la marca: en el fondo del visor (y de cada panel de comparar), alrededor de la foto, un patrón de
    // checks (favorita) o cruces (no deseada) apenas más claro que el fondo. Sin marca, el fondo queda liso.
    public partial class MainWindow
    {
        const double MosaicTile = 76;      // lado de la baldosa: dos iconos en diagonal (filas desplazadas)
        const double MosaicIcon = 18;      // tamaño de cada icono
        const double MosaicAngle = -10;    // las filas van levemente en diagonal (los iconos quedan derechos)
        static Brush favMosaic, rejMosaic;

        static Brush FavMosaic { get { return favMosaic ?? (favMosaic = MakeMosaic(CheckGeometry, Color.FromArgb(26, 0x00, 0xE6, 0x4D))); } }
        static Brush RejMosaic { get { return rejMosaic ?? (rejMosaic = MakeMosaic(CrossGeometry, Color.FromArgb(28, 0xD9, 0x44, 0x3A))); } }

        // Más tenue en el centro (alrededor de la foto) y más presente hacia los bordes
        static readonly Brush MosaicMask = MakeMosaicMask();

        static Brush MakeMosaicMask()
        {
            var m = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.75 };
            m.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0, 0, 0), 0));
            m.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
            m.Freeze();
            return m;
        }

        static Brush MakeMosaic(Geometry icon, Color color)
        {
            var fill = new SolidColorBrush(color);
            var group = new DrawingGroup();
            // Rectángulo invisible: fija el tamaño de la baldosa
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, MosaicTile, MosaicTile))));
            double s = MosaicIcon / 24.0; // los iconos se dibujan en un cuadro de 24 × 24
            foreach (var at in new[] { new Point(MosaicTile * 0.25, MosaicTile * 0.25), new Point(MosaicTile * 0.75, MosaicTile * 0.75) })
            {
                var g = icon.Clone();
                var t = new TransformGroup();
                t.Children.Add(new ScaleTransform(s, s));
                t.Children.Add(new RotateTransform(-MosaicAngle, MosaicIcon / 2, MosaicIcon / 2)); // compensa el giro del patrón
                t.Children.Add(new TranslateTransform(at.X - MosaicIcon / 2, at.Y - MosaicIcon / 2));
                g.Transform = t;
                group.Children.Add(new GeometryDrawing(fill, null, g));
            }
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile, Stretch = Stretch.None,
                Viewport = new Rect(0, 0, MosaicTile, MosaicTile), ViewportUnits = BrushMappingMode.Absolute,
                Transform = new RotateTransform(MosaicAngle)
            };
            brush.Freeze();
            return brush;
        }

        // Capa del mosaico: va primera (debajo de la foto) y no recibe clics
        static Rectangle MakeMosaicLayer()
        {
            return new Rectangle { IsHitTestVisible = false, Opacity = 0, OpacityMask = MosaicMask };
        }

        // Cambia el mosaico con un fundido corto; al pasar de una marca a otra, el nuevo aparece desde cero
        void SetMosaic(Rectangle layer, Mark m)
        {
            if (layer == null) return;
            var brush = !settings.MosaicoMarca || m == Mark.None ? null : m == Mark.Fav ? FavMosaic : RejMosaic;
            if (brush == null)
            {
                if (layer.Fill != null) FadeMosaic(layer, 0, () => layer.Fill = null);
                return;
            }
            if (layer.Fill == brush) { FadeMosaic(layer, 1, null); return; }
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = 0;
            layer.Fill = brush;
            FadeMosaic(layer, 1, null);
        }

        static void FadeMosaic(Rectangle layer, double to, Action done)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(to > 0 ? 120 : 90))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            if (done != null) a.Completed += (s, e) => { if (layer.Opacity == 0) done(); };
            layer.BeginAnimation(OpacityProperty, a);
        }

        void ToggleMarkMosaic()
        {
            settings.MosaicoMarca = !settings.MosaicoMarca;
            SaveSettings();
            UpdateMarkBadge();
        }
    }
}
