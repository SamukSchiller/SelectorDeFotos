using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SelectorDeFotos
{
    // Borde de la foto con foco (la actual) en la tira, la cuadrícula, comparar y la vista de ráfaga.
    // Opción en ⚙ → «Borde de la foto actual»:
    //   0 = blanco fijo
    //   1 = degradado que gira (un brillo recorre el borde)  ← por defecto
    //   2 = líneas que avanzan lentamente alrededor de la foto
    // Solo se anima el borde de la foto actual, así que el costo es mínimo.
    public partial class MainWindow
    {
        static LinearGradientBrush focusGradient;

        // Un solo pincel animado, compartido por todos los bordes (todos giran sincronizados)
        static Brush FocusGradient
        {
            get
            {
                if (focusGradient != null) return focusGradient;
                focusGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                focusGradient.GradientStops.Add(new GradientStop(Colors.White, 0));
                focusGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x9F, 0xCF, 0xFF), 0.35));
                focusGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0.5));
                focusGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x9F, 0xCF, 0xFF), 0.65));
                focusGradient.GradientStops.Add(new GradientStop(Colors.White, 1));
                var rot = new RotateTransform(0, 0.5, 0.5);
                focusGradient.RelativeTransform = rot;
                rot.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3.2))) { RepeatBehavior = RepeatBehavior.Forever });
                return focusGradient;
            }
        }

        // Rectángulo de líneas para el estilo 2. Se dibuja sobre el borde: mismo tamaño exterior que el Border.
        static Rectangle MakeFocusDash(Thickness borderMargin, double radius, double thickness)
        {
            // El trazo de un Rectangle va centrado en su borde: se mete medio grosor para que coincida con el Border
            double h = thickness / 2;
            return new Rectangle
            {
                Stroke = Brushes.White, StrokeThickness = thickness, StrokeDashArray = new DoubleCollection { 2, 1.4 },
                RadiusX = Math.Max(0, radius - h), RadiusY = Math.Max(0, radius - h),
                Margin = new Thickness(borderMargin.Left + h, borderMargin.Top + h, borderMargin.Right + h, borderMargin.Bottom + h),
                IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
        }

        // Aplica el estilo elegido al borde de la foto con foco (on = esta es la actual)
        void ApplyFocusStyle(Border frame, Rectangle dash, bool on)
        {
            int style = settings.BordeFoco;
            bool lines = on && style == 2;
            if (on) frame.BorderBrush = style == 1 ? FocusGradient : style == 2 ? Brushes.Transparent : FocusBrush;
            if (dash == null) return;
            if (lines && dash.Visibility != Visibility.Visible)
            {
                dash.Visibility = Visibility.Visible;
                // Avanza una «línea + hueco» por segundo; RepeatBehavior hace que gire sin fin
                dash.BeginAnimation(Shape.StrokeDashOffsetProperty,
                    new DoubleAnimation(0, -3.4, new Duration(TimeSpan.FromSeconds(1.1))) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else if (!lines && dash.Visibility == Visibility.Visible)
            {
                dash.BeginAnimation(Shape.StrokeDashOffsetProperty, null); // detener: no gasta nada mientras no se ve
                dash.Visibility = Visibility.Collapsed;
            }
        }

        void SetFocusStyle(int style)
        {
            settings.BordeFoco = style;
            SaveSettings();
            UpdateStrip();
            UpdateCompare();
            RefreshBurstView();
        }

        MenuItem FocusStyleMenu()
        {
            var menu = new MenuItem { Header = "Borde de la foto actual", Icon = FocusFrameIcon() };
            string[] names = { "Blanco fijo", "Degradado que gira", "Líneas que avanzan" };
            for (int i = 0; i < names.Length; i++)
            {
                int style = i;
                var item = new MenuItem { Header = names[i], IsCheckable = true, IsChecked = settings.BordeFoco == i };
                item.Click += (s, e) => SetFocusStyle(style);
                menu.Items.Add(item);
            }
            return menu;
        }
    }
}
