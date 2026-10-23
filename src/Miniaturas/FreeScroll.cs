using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace SelectorDeFotos
{
    // Opción «Miniatura actual siempre centrada» (activada por defecto).
    // Desactivada = desplazamiento libre, como en Lightroom:
    //  · Clic en otra miniatura: se selecciona sin que la tira se mueva.
    //  · Con las flechas, la tira solo se desplaza cuando la foto actual se saldría de la vista, y lo justo:
    //    hacia atrás queda pegada al borde del principio; hacia delante, pegada al borde del final.
    //  · La rueda y la barra fina mueven la vista sin cambiar la foto seleccionada.
    // La vista se guarda como «primera línea» (freeFirst) y la posición donde empieza (freeStartPos, puede quedar
    // algo fuera si la vista está alineada al final). Línea = fila en el lateral y en la cuadrícula, foto (o columna
    // de fotos) en la tira de abajo. Línea de una foto = índice / carriles.
    public partial class MainWindow
    {
        const double FreePad = 8;   // margen al principio y al final de la tira
        int freeFirst;              // primera línea dibujada
        double freeStartPos = FreePad; // dónde empieza esa línea (a lo largo de la tira)
        bool freeAlignEnd;          // la vista termina en freeEndLine (pegada al final) en vez de empezar en freeFirst
        int freeEndLine;
        int freeMaxFirst;           // para la barra: la mayor primera línea (la última queda al final de la vista)
        int freeLastIdx = -1;       // última foto actual para la que se ajustó la vista
        string freeLayoutKey;       // si cambia el tipo de tira (lateral, abajo, cuadrícula, columnas…) se recoloca

        bool FreeScroll { get { return !settings.CentrarMiniaturaActual; } }

        int LineOf(int i) { return Math.Max(0, i) / Math.Max(1, Lanes); }
        int LineCount { get { return (view.Count + Lanes - 1) / Math.Max(1, Lanes); } }
        double FreeLen { get { return sideStrip ? strip.ActualHeight : strip.ActualWidth; } }
        double FreeViewLen { get { return FreeLen - 2 * FreePad; } }

        // Largo de una línea en la dirección de la tira, con su separación
        double LineSize(int line)
        {
            if (Justified && sideStrip) return JustifiedRowHeight(line, Cols, strip.ActualWidth - 20) + SlotGap;
            if (Justified)
            {
                double h = Math.Floor(strip.ActualHeight - BottomPadTop - BottomPadBottom);
                return line < view.Count ? Aspect(view[line]) * h + SlotGap : h + SlotGap;
            }
            return (sideStrip ? SlotH : SlotW) + SlotGap;
        }

        void UpdateFreeScroll()
        {
            if (!FreeScroll || view.Count == 0 || FreeViewLen <= 0) return;
            string key = (gridMode ? "g" : sideStrip ? "s" : "b") + Lanes + (Justified ? "j" : "") + "|" + view.Count;
            if (key != freeLayoutKey)
            {
                // Otro tipo de tira: empezar con la foto actual a la vista (centrada)
                freeLayoutKey = key;
                CenterFreeOnCurrent();
            }
            else if (idx >= 0 && idx != freeLastIdx) EnsureCurrentVisible();
            freeLastIdx = idx;
            ResolveFreeView();
        }

        void AlignStart(int line) { freeAlignEnd = false; freeFirst = line; freeStartPos = FreePad; }
        void AlignEnd(int line) { freeAlignEnd = true; freeEndLine = line; }

        void CenterFreeOnCurrent()
        {
            int cur = LineOf(idx);
            double half = (FreeViewLen - LineSize(cur)) / 2, acc = 0;
            int first = cur;
            while (first > 0 && acc + LineSize(first - 1) <= half) { acc += LineSize(first - 1); first--; }
            AlignStart(first);
        }

        // Como Lightroom: si la actual queda antes de lo que se ve entero, pasa a ser la primera (pegada al
        // principio); si queda después, la última (pegada al final)
        void EnsureCurrentVisible()
        {
            int cur = LineOf(idx);
            int fullFirst, fullLast;
            FullyVisibleLines(out fullFirst, out fullLast);
            if (cur < fullFirst) AlignStart(cur);
            else if (cur > fullLast) AlignEnd(cur);
        }

        // Primera y última línea que se ven enteras con la vista actual
        void FullyVisibleLines(out int first, out int last)
        {
            first = -1; last = -1;
            double x = freeStartPos, end = FreeLen - FreePad + 0.5;
            for (int l = freeFirst; l < LineCount && x < end; l++)
            {
                double size = LineSize(l);
                if (x >= FreePad - 0.5 && x + size - SlotGap <= end)
                {
                    if (first < 0) first = l;
                    last = l;
                }
                x += size;
            }
            if (first < 0) { first = freeFirst; last = freeFirst - 1; } // nada entero a la vista
        }

        // Convierte la alineación elegida en (primera línea, posición) y respeta los extremos de la lista
        void ResolveFreeView()
        {
            int n = LineCount;
            // Para la barra: la mayor primera línea con la vista alineada al principio
            int maxFirst = 0;
            double acc = 0;
            for (int l = n - 1; l >= 0; l--)
            {
                acc += LineSize(l);
                if (acc - SlotGap > FreeViewLen) { maxFirst = l + 1; break; }
            }
            freeMaxFirst = Math.Max(0, Math.Min(maxFirst, n - 1));

            if (!freeAlignEnd && freeFirst >= freeMaxFirst && freeMaxFirst > 0) AlignEnd(n - 1); // final de la lista: pegada al final
            if (freeAlignEnd)
            {
                // Desde la última línea hacia atrás hasta llenar la vista
                int line = Math.Max(0, Math.Min(n - 1, freeEndLine));
                double x = FreeLen - FreePad - (LineSize(line) - SlotGap);
                while (line > 0 && x > FreePad) { line--; x -= LineSize(line); }
                if (line == 0 && x >= FreePad) { AlignStart(0); return; } // todo cabe: pegada al principio
                freeFirst = line;
                freeStartPos = x;
                return;
            }
            freeFirst = Math.Max(0, Math.Min(freeMaxFirst, freeFirst));
            freeStartPos = FreePad;
        }

        // Casillas fijas (sin cuadrícula justificada): colocarlas según la vista (puede empezar algo fuera)
        void PlaceFreeFixedSlots()
        {
            double step = (sideStrip ? SlotH : SlotW) + SlotGap;
            foreach (var s in slots)
            {
                double pos = freeStartPos + s.Row * step - RingPad;
                if (sideStrip) Canvas.SetTop(s.Root, pos); else Canvas.SetLeft(s.Root, pos);
            }
        }

        // Rueda en modo libre: mueve la vista (no la selección), alineada al principio
        void FreeWheel(MouseWheelEventArgs e)
        {
            int step = !sideStrip && Justified ? 2 : 1; // abajo con 1 fila: 2 fotos por paso
            int fullFirst, fullLast;
            FullyVisibleLines(out fullFirst, out fullLast);
            int target = (e.Delta > 0 ? fullFirst : freeFirst) + (e.Delta > 0 ? -step : step);
            AlignStart(Math.Max(0, target));
            ResolveFreeView();
            UpdateStrip();
        }

        void FreeScrollTo(double fraction)
        {
            AlignStart((int)Math.Round(fraction * freeMaxFirst));
            UpdateStrip();
        }

        void SetCenteredThumb(bool centered)
        {
            ExitFullView(); // cambia la distribución de la ventana: primero sale de la vista completa
            if (settings.CentrarMiniaturaActual == centered) return;
            settings.CentrarMiniaturaActual = centered;
            SaveSettings();
            freeLayoutKey = null; // al pasar a libre: empezar con la actual centrada (sin saltos)
            freeLastIdx = -1;
            strip.Children.Clear();
            slots.Clear();
            Dispatcher.BeginInvoke(new Action(LayoutStrip), System.Windows.Threading.DispatcherPriority.Loaded);
            Toast(centered ? "Miniatura actual siempre centrada" : "Desplazamiento libre: la tira solo se mueve si la foto actual sale de la vista");
        }

        // Fotos que se ven ahora en la tira (en modo libre pueden estar lejos de la actual)
        IEnumerable<PhotoItem> VisibleStripItems()
        {
            return slots.Where(s => s.Item != null).Select(s => s.Item);
        }
    }
}
