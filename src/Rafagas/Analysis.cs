using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Análisis de las fotos a partir de las miniaturas guardadas en disco (solo con «Guardar miniaturas en disco»):
    //  · Nitidez: contraste fino (varianza del laplaciano) en la zona más nítida de una cuadrícula de 4 × 4, para
    //    que un fondo desenfocado (bokeh) no cuente como foto movida. Se usa de forma RELATIVA dentro de cada
    //    ráfaga: «la más nítida del grupo».
    //  · Huella visual (dHash de 64 bits): permite unir a una ráfaga fotos casi iguales tomadas con algo más de
    //    tiempo entre sí (la misma pose repetida), opción «Agrupar también fotos parecidas».
    // Se calcula en el mismo proceso de fondo que genera las miniaturas (baja prioridad, en pausa mientras se carga
    // lo que se está viendo) y se guarda en data\analisis.json, así se calcula una sola vez por foto.
    public partial class MainWindow
    {
        const string AnalysisFileName = "analisis.json";
        const int SimilarMaxBits = 8;          // huellas que difieren en 8 bits o menos (de 64): fotos casi iguales
        const double SimilarMaxSeconds = 90;   // y como mucho 90 s entre una y la siguiente
        const int SimilarMaxGroup = 40;        // tope de fotos por grupo (evita cadenas de una escena fija)
        // clave de la miniatura (ruta, tamaño y fecha) -> "nitidez|huella"
        readonly ConcurrentDictionary<string, string> analysis = new ConcurrentDictionary<string, string>();
        string analysisPath;
        int analysisDirty;
        DispatcherTimer analysisTimer;

        bool AnalysisOn { get { return thumbCacheDir != null; } }

        // Al activar las miniaturas en disco (o cambiar de carpeta de datos): leer lo ya calculado
        void LoadAnalysis()
        {
            analysis.Clear();
            analysisPath = thumbCacheDir == null ? null : Path.Combine(Path.GetDirectoryName(thumbCacheDir), AnalysisFileName);
            if (analysisPath != null)
            {
                Dictionary<string, string> d;
                if (Json.TryRead(analysisPath, out d) == Json.ReadResult.Ok && d != null)
                    foreach (var kv in d) analysis[kv.Key] = kv.Value;
            }
            foreach (var it in all) ApplyAnalysis(it);
            if (analysisTimer == null)
            {
                // Guarda y actualiza las ráfagas cada pocos segundos mientras se va calculando
                analysisTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                analysisTimer.Tick += (s, e) => FlushAnalysis(false);
            }
            if (AnalysisOn) analysisTimer.Start(); else analysisTimer.Stop();
        }

        void ApplyAnalysis(PhotoItem it)
        {
            string v;
            it.Sharpness = -1;
            it.HasHash = false;
            if (!AnalysisOn || !analysis.TryGetValue(CacheKey(it), out v)) return;
            var p = v.Split('|');
            double s;
            ulong h;
            if (p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)
                && ulong.TryParse(p[1], System.Globalization.NumberStyles.HexNumber, null, out h))
            {
                it.Sharpness = s;
                it.Hash = h;
                it.HasHash = true;
            }
        }

        // Lo llama el proceso de fondo de las miniaturas, con el archivo ya guardado
        void AnalyzeIfNeeded(PhotoItem it, string cacheFile)
        {
            string key = CacheKey(it);
            if (analysis.ContainsKey(key) || !File.Exists(cacheFile)) return;
            try
            {
                double sharp;
                ulong hash;
                using (var fs = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    var gray = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
                    int w = gray.PixelWidth, h = gray.PixelHeight;
                    var px = new byte[w * h];
                    gray.CopyPixels(px, w, 0);
                    sharp = Sharpness(px, w, h);
                    hash = DHash(px, w, h);
                }
                analysis[key] = sharp.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "|" + hash.ToString("x16");
                System.Threading.Interlocked.Increment(ref analysisDirty);
            }
            catch { }
        }

        // Varianza del laplaciano en la mejor de 4 × 4 zonas (la zona enfocada manda)
        static double Sharpness(byte[] px, int w, int h)
        {
            if (w < 16 || h < 16) return 0;
            const int G = 4;
            double best = 0;
            for (int gy = 0; gy < G; gy++)
                for (int gx = 0; gx < G; gx++)
                {
                    int x0 = Math.Max(1, gx * w / G), x1 = Math.Min(w - 1, (gx + 1) * w / G);
                    int y0 = Math.Max(1, gy * h / G), y1 = Math.Min(h - 1, (gy + 1) * h / G);
                    double sum = 0, sum2 = 0;
                    int n = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * w;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x;
                            int lap = 4 * px[i] - px[i - 1] - px[i + 1] - px[i - w] - px[i + w];
                            sum += lap; sum2 += (double)lap * lap; n++;
                        }
                    }
                    if (n == 0) continue;
                    double mean = sum / n, v = sum2 / n - mean * mean;
                    if (v > best) best = v;
                }
            return best;
        }

        // Huella dHash: la foto reducida a 9 × 8 en gris; cada bit dice si un punto es más claro que el de su derecha
        static ulong DHash(byte[] px, int w, int h)
        {
            var small = new double[9 * 8];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 9; x++)
                {
                    int xa = x * w / 9, xb = Math.Max(xa + 1, (x + 1) * w / 9);
                    int ya = y * h / 8, yb = Math.Max(ya + 1, (y + 1) * h / 8);
                    double s = 0; int n = 0;
                    for (int yy = ya; yy < yb; yy += 2)
                        for (int xx = xa; xx < xb; xx += 2) { s += px[yy * w + xx]; n++; }
                    small[y * 9 + x] = n > 0 ? s / n : 0;
                }
            ulong hash = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (small[y * 9 + x] > small[y * 9 + x + 1]) hash |= 1UL << (y * 8 + x);
            return hash;
        }

        static int HashDistance(ulong a, ulong b)
        {
            ulong v = a ^ b;
            int c = 0;
            while (v != 0) { v &= v - 1; c++; }
            return c;
        }

        // ¿Van en el mismo grupo dos fotos seguidas (por fecha) que no son ráfaga por tiempo?
        bool SimilarPhotos(PhotoItem a, PhotoItem b, double seconds)
        {
            return settings.AgruparParecidas && AnalysisOn && a.HasHash && b.HasHash
                   && seconds <= SimilarMaxSeconds && HashDistance(a.Hash, b.Hash) <= SimilarMaxBits;
        }

        // Guarda lo nuevo y refresca las ráfagas (agrupar parecidas, «más nítida»)
        void FlushAnalysis(bool force)
        {
            if (analysisPath == null) return;
            if (!force && analysisDirty == 0) return;
            analysisDirty = 0;
            try { Json.Save(analysisPath, analysis.ToDictionary(kv => kv.Key, kv => kv.Value)); } catch { }
            foreach (var it in all) ApplyAnalysis(it);
            ComputeBursts();
            UpdateStrip();
            RefreshBurstView();
        }

        void ToggleGroupSimilar()
        {
            settings.AgruparParecidas = !settings.AgruparParecidas;
            SaveSettings();
            ComputeBursts();
            UpdateStrip();
            Toast(settings.AgruparParecidas ? "Las ráfagas incluyen también fotos casi iguales" : "Ráfagas: solo fotos seguidas (por tiempo)");
        }

        // La más nítida de las fotos que se ven de la ráfaga (null si aún no hay datos de al menos 2)
        static PhotoItem SharpestOf(List<PhotoItem> members)
        {
            var known = members.Where(x => x.Sharpness >= 0).ToList();
            if (known.Count < 2) return null;
            return known.OrderByDescending(x => x.Sharpness).First();
        }
    }
}
