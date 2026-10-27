using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SelectorDeFotos
{
    // Histograma en el panel de información (I), encima de los datos de la foto.
    //  · Rojo, verde y azul superpuestos y el brillo en gris; a la izquierda los negros, a la derecha los blancos.
    //  · Debajo, aviso de zonas quemadas (blanco puro) o de sombras recortadas (negro puro) si pasan del 0,5 %.
    //  · Se calcula en segundo plano sobre la imagen ya cargada reducida a 320 px (unos pocos ms) y se guarda
    //    por foto; mientras llega la imagen del visor se usa la miniatura.
    public partial class MainWindow
    {
        const int HistW = 256, HistH = 72;
        const double ClipWarnPercent = 0.5;
        Grid histBox;
        Path histR, histG, histB, histL;
        TextBlock histClip;
        class HistData { public int[] R, G, B, L; public double Hi, Lo; public bool Full; }
        readonly Dictionary<string, HistData> histCache = new Dictionary<string, HistData>(StringComparer.OrdinalIgnoreCase);
        PhotoItem histLoading;

        // Panel: [histograma] [aviso de recorte] [datos de la foto]
        UIElement BuildHistogramUi(UIElement details)
        {
            histBox = new Grid { Width = HistW, Height = HistH, Background = B("#40000000"), ClipToBounds = true, Margin = new Thickness(0, 2, 0, 4) };
            // guías en cuartos, muy suaves
            for (int i = 1; i < 4; i++)
                histBox.Children.Add(new Rectangle { Width = 1, Fill = B("#22FFFFFF"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(HistW * i / 4.0, 0, 0, 0) });
            histL = MakeHistPath("#55FFFFFF");
            histR = MakeHistPath("#70FF4A3D");
            histG = MakeHistPath("#6038D160");
            histB = MakeHistPath("#702F8CFF");
            histBox.Children.Add(histL);
            histBox.Children.Add(histR);
            histBox.Children.Add(histG);
            histBox.Children.Add(histB);
            histClip = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
            var sp = new StackPanel();
            sp.Children.Add(histBox);
            sp.Children.Add(histClip);
            sp.Children.Add(details);
            return sp;
        }

        static Path MakeHistPath(string color)
        {
            // Mezcla «aditiva» aproximada: colores semitransparentes que al superponerse se aclaran
            return new Path { Fill = B(color), Stretch = Stretch.None, IsHitTestVisible = false };
        }

        void UpdateHistogram(PhotoItem it)
        {
            if (histBox == null || it == null) return;
            HistData h;
            if (histCache.TryGetValue(it.Path, out h)) ShowHistogram(h);
            if (h != null && h.Full) return; // ya calculado con la imagen del visor
            BitmapSource src;
            bool full = fulls.TryGetValue(it.Path, out src) && src != null;
            if (!full && !(thumbs.TryGetValue(it.Path, out src) && src != null))
            {
                if (h == null) ClearHistogram();
                return; // se calculará cuando llegue la imagen (UpdateInfoPanel se vuelve a llamar)
            }
            if (histLoading == it) return;
            histLoading = it;
            var item = it;
            var t = new Thread(() =>
            {
                HistData d = null;
                try { d = ComputeHistogram(src); d.Full = full; } catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (histLoading == item) histLoading = null;
                    if (d == null) return;
                    histCache[item.Path] = d;
                    if (histCache.Count > 400) histCache.Clear(); // límite de memoria (cada uno ocupa ~4 KB)
                    if (Current == item && settings.MostrarInfo) ShowHistogram(d);
                    if (!full && Current == item) UpdateHistogram(item); // por si ya llegó la imagen grande
                }));
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        static HistData ComputeHistogram(BitmapSource src)
        {
            // Reducir a 320 px como máximo: suficiente para el histograma y muy rápido
            double scale = Math.Min(1.0, 320.0 / Math.Max(src.PixelWidth, src.PixelHeight));
            BitmapSource s = scale < 1 ? (BitmapSource)new TransformedBitmap(src, new ScaleTransform(scale, scale)) : src;
            var bgr = new FormatConvertedBitmap(s, PixelFormats.Bgr32, null, 0);
            int w = bgr.PixelWidth, hgt = bgr.PixelHeight, stride = w * 4;
            var px = new byte[stride * hgt];
            bgr.CopyPixels(px, stride, 0);
            var d = new HistData { R = new int[256], G = new int[256], B = new int[256], L = new int[256] };
            int hi = 0, lo = 0, n = w * hgt;
            for (int i = 0; i < px.Length; i += 4)
            {
                int b = px[i], g = px[i + 1], r = px[i + 2];
                d.R[r]++; d.G[g]++; d.B[b]++;
                int l = (r * 54 + g * 183 + b * 19) >> 8; // luminancia (Rec. 709)
                d.L[l]++;
                if (r >= 250 && g >= 250 && b >= 250) hi++; // quemado = blanco sin detalle (un color saturado no cuenta)
                if (l <= 2) lo++;
            }
            d.Hi = n > 0 ? hi * 100.0 / n : 0;
            d.Lo = n > 0 ? lo * 100.0 / n : 0;
            return d;
        }

        void ShowHistogram(HistData d)
        {
            // Escala: el pico de los tonos intermedios llena el alto; los extremos (0 y 255) pueden salirse por arriba
            int max = 1;
            foreach (var ch in new[] { d.R, d.G, d.B, d.L })
                for (int i = 1; i < 255; i++) if (ch[i] > max) max = ch[i];
            histR.Data = HistGeometry(d.R, max);
            histG.Data = HistGeometry(d.G, max);
            histB.Data = HistGeometry(d.B, max);
            histL.Data = HistGeometry(d.L, max);
            // Foto en blanco y negro: los tres canales coinciden y superpuestos se verían azulados; solo el brillo
            long diff = 0, n = 0;
            for (int i = 0; i < 256; i++) { diff += Math.Abs(d.R[i] - d.G[i]) + Math.Abs(d.G[i] - d.B[i]); n += d.L[i]; }
            bool mono = n > 0 && diff < n * 0.06;
            var rgbVis = mono ? Visibility.Collapsed : Visibility.Visible;
            histR.Visibility = histG.Visibility = histB.Visibility = rgbVis;
            histL.Fill = mono ? B("#B0D8D8D8") : B("#55FFFFFF");

            var parts = new List<string>();
            if (d.Hi >= ClipWarnPercent) parts.Add("Quemado: " + d.Hi.ToString("0.0") + " %");
            if (d.Lo >= ClipWarnPercent) parts.Add("Sombras recortadas: " + d.Lo.ToString("0.0") + " %");
            histClip.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            histClip.Text = "⚠ " + string.Join("   ·   ", parts);
            histClip.Foreground = d.Hi >= ClipWarnPercent ? B("#FF8A80") : B("#9FC5FF");
        }

        void ClearHistogram()
        {
            histR.Data = histG.Data = histB.Data = histL.Data = null;
            histClip.Visibility = Visibility.Collapsed;
        }

        static Geometry HistGeometry(int[] bins, int max)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, HistH), true, true);
                for (int i = 0; i < 256; i++)
                {
                    double y = HistH - Math.Min(HistH, bins[i] * (double)HistH / max);
                    ctx.LineTo(new Point(i * HistW / 255.0, y), false, false);
                }
                ctx.LineTo(new Point(HistW, HistH), false, false);
            }
            g.Freeze();
            return g;
        }
    }
}
