using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Vista de cuadrícula a pantalla completa (G): el panel de miniaturas ocupa toda la ventana con el modelo
    // del lateral. Entre 3 y 10 columnas como máximo, para que nunca se muestren cientos de fotos a la vez.
    public partial class MainWindow
    {
        bool gridMode;
        bool gridPrevSide;
        Button btnGrid;
        const int GridMinCols = 3, GridMaxCols = 10;
        const int GridAutoMaxCols = 8; // el cálculo automático no pasa de 8; 9 y 10 solo si se eligen

        // Columnas: las elegidas con + / −, o automáticas según el ancho (miniaturas de unos 280 px)
        int GridCols
        {
            get
            {
                int c = settings.ColumnasCuadricula;
                if (c >= GridMinCols && c <= GridMaxCols) return c;
                double w = uiRoot != null && uiRoot.ActualWidth > 0 ? uiRoot.ActualWidth : 1600;
                return Math.Max(GridMinCols, Math.Min(GridAutoMaxCols, (int)Math.Round(w / 280.0)));
            }
        }

        void ToggleGrid()
        {
            if (gridMode) ExitGrid(); else EnterGrid();
        }

        void EnterGrid()
        {
            ExitFullView(); // cambia la distribución de la ventana: primero sale de la vista completa
            if (gridMode || all.Count == 0) return;
            gridMode = true;
            gridPrevSide = sideStrip;
            ResetZoom();
            sideStrip = true;
            center.Visibility = Visibility.Collapsed;
            splitter.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(stripHost, Dock.Right);
            stripSize = uiRoot.ActualWidth;
            ApplyStripSize();
            ApplyScrollbarOrientation();
            RefreshGridLayout();
            SyncBurstView(); // oculta el indicador del modo ráfaga mientras dure la cuadrícula
            Toast("Cuadrícula: doble clic, Enter o G para volver a la foto   ·   + − columnas");
        }

        void ExitGrid()
        {
            if (!gridMode) return;
            gridMode = false;
            sideStrip = gridPrevSide;
            center.Visibility = Visibility.Visible;
            splitter.Visibility = Visibility.Visible;
            ApplyStripMode(); // recupera la posición y el tamaño guardados del panel
            RefreshGridLayout();
            ShowCurrent();
        }

        // Cambiar columnas: + → menos columnas (miniaturas más grandes), − → más columnas
        void StepGridCols(int delta)
        {
            int cols = Math.Max(GridMinCols, Math.Min(GridMaxCols, GridCols + delta));
            settings.ColumnasCuadricula = cols;
            SaveSettings();
            RefreshGridLayout();
            Toast(cols + " columnas" + (cols == GridMinCols || cols == GridMaxCols ? "  (límite)" : ""));
        }

        void RefreshGridLayout()
        {
            int nd = ThumbDimFor();
            if (nd != thumbDim) { thumbDim = nd; thumbs.Clear(); }
            strip.Children.Clear();
            slots.Clear();
            UpdateSideButton();
            UpdateGridButton();
            Dispatcher.BeginInvoke(new Action(LayoutStrip), DispatcherPriority.Loaded);
        }

        void UpdateGridButton()
        {
            if (btnGrid == null) return;
            if (gridMode) btnGrid.Background = AccentBrush; else btnGrid.ClearValue(BackgroundProperty);
            btnGrid.ToolTip = gridMode ? "Volver a la foto (G)" : "Ver las miniaturas a pantalla completa (G)";
            btnSide.IsEnabled = !gridMode;
        }

        // La foto de la fila (de la foto i) que queda visualmente más cerca del centro.
        // Con la cuadrícula justificada se tienen en cuenta los anchos distintos de cada foto.
        int MiddleOfRow(int i)
        {
            int cols = Cols;
            int first = Math.Max(0, i) / cols * cols;
            int count = Math.Min(cols, view.Count - first);
            if (count <= 1) return first;
            if (!Justified) return first + (count - 1) / 2;
            double total = 0;
            for (int k = 0; k < count; k++) total += Aspect(view[first + k]);
            double x = 0, best = double.MaxValue;
            int pick = first;
            for (int k = 0; k < count; k++)
            {
                double a = Aspect(view[first + k]);
                double dist = Math.Abs(x + a / 2 - total / 2);
                if (dist < best) { best = dist; pick = first + k; }
                x += a;
            }
            return pick;
        }

        // Icono: cuadrícula de 2 × 2
        static UIElement GridIcon()
        {
            var g = new Grid { Width = 14, Height = 14 };
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                {
                    var b = new Border
                    {
                        Width = 6, Height = 6, CornerRadius = new CornerRadius(1), Background = TextBrush,
                        HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                        VerticalAlignment = r == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom
                    };
                    g.Children.Add(b);
                }
            return g;
        }
    }
}
