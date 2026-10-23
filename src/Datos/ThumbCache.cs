using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Miniaturas guardadas en disco (opción en ⚙, desactivada por defecto).
    //  · Carpeta «data\miniaturas» dentro de la carpeta de datos, con dos tamaños: 400 y 800 px (lado mayor).
    //  · Cada vista usa el tamaño guardado más pequeño que le basta; la cuadrícula con 3 – 4 columnas
    //    (más de 800 px) sigue leyendo el original.
    //  · Un proceso de baja prioridad las va creando, empezando por las cercanas a la foto actual, y se pausa
    //    mientras la app carga lo que se está viendo (eso tiene prioridad y se guarda al momento).
    //  · El nombre de cada archivo depende de la ruta, el tamaño y la fecha de la foto: si la foto cambia,
    //    se genera de nuevo sola.
    //  · Al desactivar la opción se borran estos archivos para liberar espacio.
    public partial class MainWindow
    {
        const string ThumbFolderName = "miniaturas";
        static readonly int[] CacheLevels = { 400, 800 };
        volatile string thumbCacheDir;   // null = desactivado (lo leen los hilos de fondo)
        volatile int pregenCenter;       // posición de la foto actual, para empezar por las cercanas
        volatile int pregenGen;
        int pregenDone, pregenTotal;
        Dictionary<PhotoItem, int> pregenPos;
        DispatcherTimer pregenTimer;

        static int CacheLevel(int dim)
        {
            foreach (int l in CacheLevels) if (dim <= l) return l;
            return 0; // más grande que 800: se lee del original
        }

        static string CacheKey(PhotoItem it)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(it.Path.ToLowerInvariant() + "|" + it.Size + "|" + it.Modified.Ticks));
                var sb = new StringBuilder();
                for (int i = 0; i < 12; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static string CacheFile(string dir, int level, PhotoItem it)
        {
            return Path.Combine(dir, level.ToString(), CacheKey(it) + ".jpg");
        }

        // Lo usa la cola de miniaturas (hilo de fondo)
        BitmapSource LoadThumb(PhotoItem it)
        {
            int dim = thumbDim;
            string dir = thumbCacheDir;
            int level = CacheLevel(dim);
            if (dir == null || level == 0) return ImageLoader.Load(it, dim);
            MetaReader.Read(it); // la clave usa tamaño y fecha, ya conocidos
            string file = CacheFile(dir, level, it);
            var cached = TryLoadCached(file, dim);
            if (cached != null) return cached;
            var b = ImageLoader.Load(it, level);
            SaveCached(file, b);
            return b;
        }

        static BitmapSource TryLoadCached(string file, int dim)
        {
            if (!File.Exists(file)) return null;
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    int w = dec.Frames[0].PixelWidth, h = dec.Frames[0].PixelHeight;
                    fs.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = fs;
                    // ya está girada: no se vuelve a aplicar la orientación EXIF
                    if (Math.Max(w, h) > dim)
                    {
                        if (w >= h) bi.DecodePixelWidth = dim; else bi.DecodePixelHeight = dim;
                    }
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch
            {
                try { File.Delete(file); } catch { } // dañada: se vuelve a generar
                return null;
            }
        }

        static void SaveCached(string file, BitmapSource b)
        {
            if (b == null) return;
            try
            {
                if (File.Exists(file)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var enc = new JpegBitmapEncoder { QualityLevel = 85 };
                enc.Frames.Add(BitmapFrame.Create(b));
                using (var fs = File.Create(tmp)) enc.Save(fs);
                try { File.Move(tmp, file); }
                catch { try { File.Delete(tmp); } catch { } }
            }
            catch { }
        }

        // ---------------------------------------------------------------- Activar / desactivar

        void ToggleThumbCache()
        {
            if (!settings.GuardarMiniaturas && (string.IsNullOrEmpty(settings.CarpetaDatos) || !Directory.Exists(settings.CarpetaDatos)))
            {
                Toast("Primero elige la carpeta de datos");
                return;
            }
            settings.GuardarMiniaturas = !settings.GuardarMiniaturas;
            SaveSettings();
            if (settings.GuardarMiniaturas)
            {
                RefreshThumbCache();
                Toast("Guardando miniaturas en «data\\" + ThumbFolderName + "» de la carpeta de datos (en segundo plano)");
            }
            else
            {
                string dir = Path.Combine(settings.CarpetaDatos ?? "", AppDataFolderName, ThumbFolderName);
                RefreshThumbCache();
                DeleteThumbCache(dir);
            }
        }

        // Aplica la opción: carpeta actual, y arranca o detiene la generación de fondo
        void RefreshThumbCache()
        {
            pregenGen++;
            if (settings.GuardarMiniaturas && !string.IsNullOrEmpty(settings.CarpetaDatos) && Directory.Exists(settings.CarpetaDatos))
            {
                string dir = Path.Combine(settings.CarpetaDatos, AppDataFolderName, ThumbFolderName);
                try { Directory.CreateDirectory(dir); thumbCacheDir = dir; }
                catch { thumbCacheDir = null; }
            }
            else thumbCacheDir = null;
            LoadAnalysis();
            StartPregen();
        }

        void StartPregen()
        {
            int gen = ++pregenGen;
            string dir = thumbCacheDir;
            if (pregenTimer == null)
            {
                pregenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                pregenTimer.Tick += (s, e) => UpdatePregenProgress();
            }
            if (dir == null || all.Count == 0)
            {
                pregenTimer.Stop();
                pregenTotal = 0;
                UpdatePregenProgress();
                return;
            }
            var items = all.ToArray();
            pregenPos = new Dictionary<PhotoItem, int>();
            for (int i = 0; i < items.Length; i++) pregenPos[items[i]] = i;
            var cur = Current;
            int c;
            pregenCenter = cur != null && pregenPos.TryGetValue(cur, out c) ? c : 0;
            int level = CacheLevel(thumbDim);
            if (level == 0) level = CacheLevels[CacheLevels.Length - 1];
            pregenTotal = items.Length;
            pregenDone = 0;
            pregenTimer.Start();
            var t = new Thread(() =>
            {
                var done = new bool[items.Length];
                int count = 0;
                while (gen == pregenGen && count < items.Length)
                {
                    // Lo que se está viendo tiene prioridad: esperar a que la cola de miniaturas termine
                    while (thumbQueue.Active > 0 && gen == pregenGen) Thread.Sleep(40);
                    if (gen != pregenGen) break;
                    // la pendiente más cercana a la foto actual
                    int center = Math.Max(0, Math.Min(items.Length - 1, pregenCenter));
                    int pick = -1;
                    for (int d = 0; d < items.Length && pick < 0; d++)
                    {
                        if (center + d < items.Length && !done[center + d]) pick = center + d;
                        else if (center - d >= 0 && !done[center - d]) pick = center - d;
                    }
                    if (pick < 0) break;
                    done[pick] = true;
                    count++;
                    var it = items[pick];
                    try
                    {
                        MetaReader.Read(it);
                        string file = CacheFile(dir, level, it);
                        if (!File.Exists(file)) SaveCached(file, ImageLoader.Load(it, level));
                        AnalyzeIfNeeded(it, file); // nitidez y huella visual (ver Analysis.cs)
                    }
                    catch { }
                    Interlocked.Increment(ref pregenDone);
                }
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.Lowest;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // Llamado al mostrar una foto: la generación de fondo sigue desde ahí
        void UpdatePregenCenter()
        {
            int p;
            var cur = Current;
            if (pregenPos != null && cur != null && pregenPos.TryGetValue(cur, out p)) pregenCenter = p;
        }

        void UpdatePregenProgress()
        {
            if (copying || (metaTotal > 0 && metaDone < metaTotal)) return; // otros avisos tienen preferencia
            if (thumbCacheDir == null || pregenTotal == 0)
            {
                if (progressText.Text.StartsWith("Miniaturas")) progressText.Text = "";
                return;
            }
            if (pregenDone >= pregenTotal)
            {
                pregenTimer.Stop();
                if (progressText.Text.StartsWith("Miniaturas")) progressText.Text = "";
                return;
            }
            progressText.Text = "Miniaturas " + pregenDone + " / " + pregenTotal;
        }

        // Borra solo los archivos de miniaturas (.jpg / .tmp de las subcarpetas 400 y 800)
        void DeleteThumbCache(string dir)
        {
            if (!Directory.Exists(dir) || !string.Equals(Path.GetFileName(dir.TrimEnd('\\')), ThumbFolderName, StringComparison.OrdinalIgnoreCase))
                return;
            var t = new Thread(() =>
            {
                long freed = 0;
                int files = 0;
                foreach (int level in CacheLevels)
                {
                    string sub = Path.Combine(dir, level.ToString());
                    if (!Directory.Exists(sub)) continue;
                    foreach (var f in Directory.EnumerateFiles(sub).ToList())
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".jpg" && ext != ".tmp") continue;
                        try { long len = new FileInfo(f).Length; File.Delete(f); freed += len; files++; } catch { }
                    }
                    try { if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub); } catch { }
                }
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                    Toast("Miniaturas guardadas borradas: " + files + " archivos, " + (freed / (1024.0 * 1024.0)).ToString("N0") + " MB liberados")));
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
