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
    // Comparar: paneles de la foto anterior / siguiente.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Comparar

        void BuildComparePane(ComparePane p, int column, Thickness margin)
        {
            p.Pane = new Grid { Background = BgBrush, ClipToBounds = true, Margin = margin, Visibility = Visibility.Collapsed };
            Grid.SetColumn(p.Pane, column);
            p.Mosaic = MakeMosaicLayer();
            p.Pane.Children.Add(p.Mosaic);
            p.Img = new Image { Stretch = Stretch.Uniform, RenderTransform = zoom }; // zoom sincronizado con la actual
            RenderOptions.SetBitmapScalingMode(p.Img, BitmapScalingMode.HighQuality);
            p.Pane.Children.Add(p.Img);
            p.Empty = new TextBlock
            {
                Foreground = DimTextBrush, FontSize = 18, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            p.Pane.Children.Add(p.Empty);
            p.Badge = MakeMarkIcon(MainCheckSize, MainCrossSize, BgBrush, new Thickness(0, 18, 18, 0));
            p.Pane.Children.Add(p.Badge);
            p.Name = MakePaneName();
            p.Pane.Children.Add(p.Name);
            center.Children.Add(p.Pane);
        }

        void UpdateCompare()
        {
            var it = Current;
            mainName.Visibility = settings.Comparar && it != null ? Visibility.Visible : Visibility.Collapsed;
            mainFrame.Visibility = settings.Comparar && it != null ? Visibility.Visible : Visibility.Collapsed;
            ApplyFocusStyle(mainFrame, mainDash, settings.Comparar && it != null);
            if (it != null) mainName.Text = "ACTUAL  ·  " + it.Name;
            UpdateComparePane(prevCmp, CompareShowsPrev);
            UpdateComparePane(nextCmp, CompareShowsNext);
        }

        void UpdateComparePane(ComparePane p, bool on)
        {
            p.Col.Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            p.Pane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on)
            {
                p.Item = null;
                p.HiResItem = null;
                p.Img.Source = null;
                return;
            }
            int i = idx + p.Offset;
            var other = idx >= 0 && i >= 0 && i < view.Count ? view[i] : null;
            p.Item = other;
            if (p.HiResItem != other) p.HiResItem = null;
            SetMarkIcon(p.Badge, other == null || !settings.MostrarIconoMarca ? Mark.None : other.Mark);
            SetMosaic(p.Mosaic, other == null ? Mark.None : other.Mark);
            if (other == null)
            {
                p.Img.Source = null;
                p.Empty.Text = Current == null ? "" : (p.Offset < 0 ? "No hay foto anterior" : "No hay foto siguiente");
                p.Empty.Visibility = Visibility.Visible;
                p.Name.Visibility = Visibility.Collapsed;
                return;
            }
            p.Empty.Visibility = Visibility.Collapsed;
            p.Name.Text = p.Label + "  ·  " + other.Name;
            p.Name.Visibility = Visibility.Visible;
            if (p.HiResItem == other) return; // ya se muestra a resolución completa
            BitmapSource b;
            if (fulls.TryGetValue(other.Path, out b) && b != null) p.Img.Source = b;
            else if (thumbs.TryGetValue(other.Path, out b) && b != null) p.Img.Source = b;
            else p.Img.Source = null;
        }

        // C: activa/desactiva. Doble C rápida: pasa al siguiente formato (siguiente → anterior → ambas).
        DateTime lastCompareKey = DateTime.MinValue;
        bool compareBeforeKey;
        void OnCompareKey()
        {
            var now = DateTime.Now;
            bool doublePress = (now - lastCompareKey).TotalMilliseconds < 500;
            lastCompareKey = doublePress ? DateTime.MinValue : now;
            if (!doublePress)
            {
                compareBeforeKey = settings.Comparar;
                ToggleCompare();
                return;
            }
            // La primera pulsación ya cambió el estado; la doble deja la comparación activa y cambia el formato.
            // Si estaba apagada, se activa con el formato que ya tenía; si estaba encendida, pasa al siguiente.
            int mode = compareBeforeKey ? (settings.ModoComparar + 1) % 3 : settings.ModoComparar;
            SetCompareMode(mode);
        }

        void ToggleCompare()
        {
            settings.Comparar = !settings.Comparar;
            ApplyCompareChange();
        }

        void SetCompareMode(int mode)
        {
            settings.ModoComparar = mode;
            settings.Comparar = true;
            ApplyCompareChange();
        }

        void ApplyCompareChange()
        {
            // Comparar y el modo ráfaga no van juntos: activar uno desactiva el otro
            bool burstOff = settings.Comparar && settings.ModoRafaga;
            if (burstOff) settings.ModoRafaga = false;
            SaveSettings();
            ResetZoom();
            UpdateMarkBadge();
            UpdateStrip();
            string how = settings.ModoComparar == 1 ? "anterior a la izquierda, actual a la derecha"
                       : settings.ModoComparar == 2 ? "anterior, actual en el centro y siguiente"
                       : "actual a la izquierda, siguiente a la derecha";
            Toast(settings.Comparar ? "Comparar: " + how + (burstOff ? "  ·  modo ráfaga desactivado" : " (las marcas se aplican a la actual)") : "Comparar desactivado");
        }
    }
}
